using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Documents;
using Smartnet.Domain.Ledger;
using Smartnet.Domain.MasterData;
using Smartnet.Domain.Settings;
using Smartnet.Infrastructure.Documents;
using Smartnet.Infrastructure.Ledger;
using Smartnet.Infrastructure.Persistence;
using Smartnet.Tests.Auditing;

namespace Smartnet.Tests.Documents;

/// <summary>
/// Customer receipts &amp; their allocation (Phase 7, slice 1): the money-in side. A receipt is
/// <b>allocated across several open invoices</b> — each a receivables-ledger <c>Payment</c> entry (the truth,
/// from which the outstanding is derived) that also dual-writes the legacy <c>payments</c> row and
/// <c>invoice_h.balance</c> for the surviving legacy report. Idempotent (Finding 1), over-allocation refused,
/// voidable through a compensating entry — never by rewriting a balance (B2/B3).
/// </summary>
[Collection(nameof(AuditCollection))]
public sealed class CustomerReceiptTests
{
    private readonly AuditFixture _fixture;
    private static readonly TimeProvider Clock = TimeProvider.System;

    public CustomerReceiptTests(AuditFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_receipt_allocated_across_two_invoices_settles_both_and_dual_writes_the_legacy_shadow()
    {
        var (companyId, customerId, code) = await SeedCompanyAndCustomer();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };

        var inv1 = await SeedInvoice(companyId, customerId, code, "INV-A1", 100m);
        var inv2 = await SeedInvoice(companyId, customerId, code, "INV-A2", 60m);

        CustomerReceiptCreated created;
        await using (var db = _fixture.CreateContext(change))
        await using (var legacy = CreateLegacy())
        {
            created = await ServiceFor(db, legacy, change).CreateAsync(new NewCustomerReceipt(
                companyId, customerId, new DateOnly(2026, 7, 17), "CASH", "R-100", "idem-A",
                new[] { new NewReceiptAllocation(inv1, 50m), new NewReceiptAllocation(inv2, 30m) }));
        }

        created.Amount.Should().Be(80m);
        created.AlreadyExisted.Should().BeFalse();

        await using (var db = _fixture.CreateContext(change))
        {
            // The ledger is the truth: each invoice's derived outstanding dropped by its allocation, and the
            // customer's balance is 160 charged − 80 received = 80.
            (await OutstandingFor(db, inv1)).Should().Be(50m);
            (await OutstandingFor(db, inv2)).Should().Be(30m);
            (await new ReceivablesLedger(db).BalanceForCustomerAsync(customerId)).Should().Be(80m);

            // The legacy shadow was dual-written: one payments row per invoice, and invoice_h.balance set to
            // the freshly derived outstanding (absolute, off the ledger — not a drifting decrement).
            (await PaymentRowCount(db, "INV-A1")).Should().Be(1);
            (await PaymentRowCount(db, "INV-A2")).Should().Be(1);
            (await LegacyBalance(db, inv1)).Should().Be(50m);
            (await LegacyBalance(db, inv2)).Should().Be(30m);
        }
    }

    [Fact]
    public async Task A_resubmit_with_the_same_idempotency_key_returns_the_first_receipt_and_does_not_pay_twice()
    {
        var (companyId, customerId, code) = await SeedCompanyAndCustomer();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };
        var inv = await SeedInvoice(companyId, customerId, code, "INV-B1", 100m);

        CustomerReceiptCreated first, second;
        await using (var db = _fixture.CreateContext(change))
        await using (var legacy = CreateLegacy())
        {
            first = await ServiceFor(db, legacy, change).CreateAsync(new NewCustomerReceipt(
                companyId, customerId, new DateOnly(2026, 7, 17), "CASH", "R-1", "idem-B",
                new[] { new NewReceiptAllocation(inv, 40m) }));
        }

        await using (var db = _fixture.CreateContext(change))
        await using (var legacy = CreateLegacy())
        {
            second = await ServiceFor(db, legacy, change).CreateAsync(new NewCustomerReceipt(
                companyId, customerId, new DateOnly(2026, 7, 17), "CASH", "R-1", "idem-B",
                new[] { new NewReceiptAllocation(inv, 40m) }));
        }

        second.Id.Should().Be(first.Id);
        second.AlreadyExisted.Should().BeTrue();

        await using (var db = _fixture.CreateContext(change))
        {
            // Exactly one payment took place — the outstanding fell by 40 once, not 80.
            (await OutstandingFor(db, inv)).Should().Be(60m);
            (await PaymentRowCount(db, "INV-B1")).Should().Be(1);
            (await db.CustomerReceipts.CountAsync()).Should().Be(1);
        }
    }

    [Fact]
    public async Task An_allocation_over_the_outstanding_is_refused()
    {
        var (companyId, customerId, code) = await SeedCompanyAndCustomer();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };
        var inv = await SeedInvoice(companyId, customerId, code, "INV-C1", 100m);

        await using var db = _fixture.CreateContext(change);
        await using var legacy = CreateLegacy();
        var act = () => ServiceFor(db, legacy, change).CreateAsync(new NewCustomerReceipt(
            companyId, customerId, new DateOnly(2026, 7, 17), "CASH", null, "idem-C",
            new[] { new NewReceiptAllocation(inv, 150m) }));

        await act.Should().ThrowAsync<ReceiptAllocationExceedsOutstandingException>();
    }

    [Fact]
    public async Task Voiding_a_receipt_reverses_its_allocations_and_restores_the_balance()
    {
        var (companyId, customerId, code) = await SeedCompanyAndCustomer();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };
        var inv = await SeedInvoice(companyId, customerId, code, "INV-D1", 100m);

        long receiptId;
        int rowVersion;
        await using (var db = _fixture.CreateContext(change))
        await using (var legacy = CreateLegacy())
        {
            receiptId = (await ServiceFor(db, legacy, change).CreateAsync(new NewCustomerReceipt(
                companyId, customerId, new DateOnly(2026, 7, 17), "CASH", "R-1", "idem-D",
                new[] { new NewReceiptAllocation(inv, 40m) }))).Id;
            rowVersion = await db.CustomerReceipts.Where(r => r.Id == receiptId).Select(r => r.RowVersion).SingleAsync();
        }

        await using (var db = _fixture.CreateContext(change))
        await using (var legacy = CreateLegacy())
        {
            await ServiceFor(db, legacy, change).VoidAsync(receiptId, rowVersion);
        }

        await using (var db = _fixture.CreateContext(change))
        {
            // The payment is undone: outstanding back to 100, customer balance back to 100, the receipt
            // soft-deleted (its history intact), and the legacy detail nets to nothing (40 in, 40 out).
            (await OutstandingFor(db, inv)).Should().Be(100m);
            (await new ReceivablesLedger(db).BalanceForCustomerAsync(customerId)).Should().Be(100m);
            (await LegacyBalance(db, inv)).Should().Be(100m);
            (await db.CustomerReceipts.CountAsync(r => r.Id == receiptId)).Should().Be(0);
            (await db.CustomerReceipts.IgnoreQueryFilters().CountAsync(r => r.Id == receiptId && r.DeletedAt != null))
                .Should().Be(1);
        }
    }

    // --- Helpers ---------------------------------------------------------------------------------

    private static CustomerReceiptService ServiceFor(TestDbContext db, SmartnetLegacyDbContext legacy, FakeChangeContext change) =>
        new(db, legacy, change, Clock);

    private SmartnetLegacyDbContext CreateLegacy() =>
        new(new DbContextOptionsBuilder<SmartnetLegacyDbContext>()
            .UseMySql(_fixture.ConnectionString, SmartnetServerVersion.Value)
            .Options);

    private static async Task<decimal> OutstandingFor(TestDbContext db, long invoiceId) =>
        await db.ReceivablesLedger.Where(e => e.InvoiceId == invoiceId).SumAsync(e => e.Amount);

    private static async Task<int> PaymentRowCount(TestDbContext db, string invoiceNo) =>
        await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM payments WHERE invoiceno = {invoiceNo}").SingleAsync();

    private static async Task<decimal> LegacyBalance(TestDbContext db, long invoiceId) =>
        await db.Database.SqlQuery<decimal>($"SELECT CAST(balance AS DECIMAL(18,4)) AS Value FROM invoice_h WHERE id = {invoiceId}").SingleAsync();

    private async Task<(long CompanyId, long CustomerId, string Code)> SeedCompanyAndCustomer()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext { UserId = 1 });

        var company = new Company { Name = "Smart Net (test)", VatCode = "1", IsVatRegistered = true };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        var code = $"C-{Guid.NewGuid():N}"[..12];
        var customer = new Customer { Code = code, Name = "Acme Co" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        return (company.Id, customer.Id, code);
    }

    /// <summary>Seeds an invoice_h row and its receivables-ledger charge, so there is real outstanding to settle.</summary>
    private async Task<long> SeedInvoice(long companyId, long customerId, string code, string invoiceNo, decimal total)
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext { UserId = 1 });

        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO `invoice_h`
              (`invoiceno`, `invtype`, `indate`, `customer`, `totamount`, `balance`, `company`, `data_origin`,
               `discountper`, `beforedisctot`, `contactperson`, `row_version`)
            VALUES ({invoiceNo}, 'item', '2026-07-10', {code}, {total.ToString(System.Globalization.CultureInfo.InvariantCulture)},
                    {total.ToString(System.Globalization.CultureInfo.InvariantCulture)},
                    {companyId.ToString(System.Globalization.CultureInfo.InvariantCulture)}, 'new', '0', '0', '', 0)
            """);

        var invoiceId = await db.Database
            .SqlQuery<long>($"SELECT id AS Value FROM invoice_h WHERE invoiceno = {invoiceNo}")
            .SingleAsync();

        db.ReceivablesLedger.Add(new LedgerEntry
        {
            CustomerId = customerId,
            Type = LedgerEntryType.Charge,
            Amount = total,
            InvoiceId = invoiceId,
            OccurredAt = new DateTime(2026, 7, 10),
        });
        await db.SaveChangesAsync();

        return invoiceId;
    }
}
