using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Documents;
using Smartnet.Domain.Ledger;
using Smartnet.Domain.MasterData;
using Smartnet.Domain.Settings;
using Smartnet.Infrastructure.Documents;
using Smartnet.Infrastructure.Ledger;
using Smartnet.Tests.Auditing;

namespace Smartnet.Tests.Documents;

/// <summary>
/// Supplier payments &amp; their allocation (Phase 7): the money-out side, the payables mirror of customer
/// receipts. A payment is <b>allocated across several open supplier invoices</b> — each a payables-ledger
/// <c>Payment</c> entry (the truth) that dual-writes the legacy <c>supplier_inv_pay</c> row and flips
/// <c>paymentstat</c>. Idempotent, over-allocation refused, voidable through a compensating entry. It settles
/// adopted-legacy supplier invoices (their payable is a seeded opening balance) as well as new ones.
/// </summary>
[Collection(nameof(AuditCollection))]
public sealed class SupplierPaymentTests
{
    private readonly AuditFixture _fixture;
    private static readonly TimeProvider Clock = TimeProvider.System;

    public SupplierPaymentTests(AuditFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_payment_allocated_across_two_legacy_invoices_settles_both_and_dual_writes_the_shadow()
    {
        var (companyId, supplierId, code) = await SeedCompanyAndSupplier();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };

        var inv1 = await SeedSupplierInvoice(companyId, supplierId, code, "SINV-A1", 100m);
        var inv2 = await SeedSupplierInvoice(companyId, supplierId, code, "SINV-A2", 60m);

        SupplierPaymentCreated created;
        await using (var db = _fixture.CreateContext(change))
        {
            created = await new SupplierPaymentService(db, change, Clock).CreateAsync(new NewSupplierPayment(
                companyId, supplierId, new DateOnly(2026, 7, 17), "BANK", "TT-100", "sidem-A",
                new[] { new NewSupplierPaymentAllocation(inv1, 100m), new NewSupplierPaymentAllocation(inv2, 25m) }));
        }

        created.Amount.Should().Be(125m);
        created.AlreadyExisted.Should().BeFalse();

        await using (var db = _fixture.CreateContext(change))
        {
            // The ledger is the truth: inv1 fully settled (0), inv2 partly (35 left); the supplier owes 35.
            (await OutstandingFor(db, inv1)).Should().Be(0m);
            (await OutstandingFor(db, inv2)).Should().Be(35m);
            (await new PayablesLedger(db).BalanceForSupplierAsync(supplierId)).Should().Be(35m);

            // Dual-written: a supplier_inv_pay row per invoice, and paymentstat flipped only on the fully-paid one.
            (await PayRowCount(db, inv1)).Should().Be(1);
            (await PayRowCount(db, inv2)).Should().Be(1);
            (await PaymentStat(db, inv1)).Should().Be("Paid");
            (await PaymentStat(db, inv2)).Should().Be("Pending");
        }
    }

    [Fact]
    public async Task A_resubmit_with_the_same_idempotency_key_returns_the_first_payment_and_does_not_pay_twice()
    {
        var (companyId, supplierId, code) = await SeedCompanyAndSupplier();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };
        var inv = await SeedSupplierInvoice(companyId, supplierId, code, "SINV-B1", 100m);

        SupplierPaymentCreated first, second;
        await using (var db = _fixture.CreateContext(change))
        {
            first = await new SupplierPaymentService(db, change, Clock).CreateAsync(new NewSupplierPayment(
                companyId, supplierId, new DateOnly(2026, 7, 17), "CASH", "R-1", "sidem-B",
                new[] { new NewSupplierPaymentAllocation(inv, 40m) }));
        }
        await using (var db = _fixture.CreateContext(change))
        {
            second = await new SupplierPaymentService(db, change, Clock).CreateAsync(new NewSupplierPayment(
                companyId, supplierId, new DateOnly(2026, 7, 17), "CASH", "R-1", "sidem-B",
                new[] { new NewSupplierPaymentAllocation(inv, 40m) }));
        }

        second.Id.Should().Be(first.Id);
        second.AlreadyExisted.Should().BeTrue();

        await using (var db = _fixture.CreateContext(change))
        {
            (await OutstandingFor(db, inv)).Should().Be(60m);
            (await PayRowCount(db, inv)).Should().Be(1);
            (await db.SupplierPayments.CountAsync(p => p.SupplierId == supplierId)).Should().Be(1);
        }
    }

    [Fact]
    public async Task An_allocation_over_the_outstanding_is_refused()
    {
        var (companyId, supplierId, code) = await SeedCompanyAndSupplier();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };
        var inv = await SeedSupplierInvoice(companyId, supplierId, code, "SINV-C1", 100m);

        await using var db = _fixture.CreateContext(change);
        var act = () => new SupplierPaymentService(db, change, Clock).CreateAsync(new NewSupplierPayment(
            companyId, supplierId, new DateOnly(2026, 7, 17), "CASH", null, "sidem-C",
            new[] { new NewSupplierPaymentAllocation(inv, 150m) }));

        await act.Should().ThrowAsync<SupplierPaymentAllocationExceedsOutstandingException>();
    }

    [Fact]
    public async Task Voiding_a_payment_reverses_its_allocations_and_reopens_the_invoice()
    {
        var (companyId, supplierId, code) = await SeedCompanyAndSupplier();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };
        var inv = await SeedSupplierInvoice(companyId, supplierId, code, "SINV-D1", 100m);

        long paymentId;
        int rowVersion;
        await using (var db = _fixture.CreateContext(change))
        {
            paymentId = (await new SupplierPaymentService(db, change, Clock).CreateAsync(new NewSupplierPayment(
                companyId, supplierId, new DateOnly(2026, 7, 17), "CASH", "R-1", "sidem-D",
                new[] { new NewSupplierPaymentAllocation(inv, 100m) }))).Id;
            rowVersion = await db.SupplierPayments.Where(p => p.Id == paymentId).Select(p => p.RowVersion).SingleAsync();
        }

        // Fully paid, then voided — the payable comes back and the legacy flag reopens.
        await using (var db = _fixture.CreateContext(change))
        {
            (await PaymentStat(db, inv)).Should().Be("Paid");
            await new SupplierPaymentService(db, change, Clock).VoidAsync(paymentId, rowVersion);
        }

        await using (var db = _fixture.CreateContext(change))
        {
            (await OutstandingFor(db, inv)).Should().Be(100m);
            (await new PayablesLedger(db).BalanceForSupplierAsync(supplierId)).Should().Be(100m);
            (await PaymentStat(db, inv)).Should().Be("Pending");
            (await db.SupplierPayments.CountAsync(p => p.Id == paymentId)).Should().Be(0);
            (await db.SupplierPayments.IgnoreQueryFilters().CountAsync(p => p.Id == paymentId && p.DeletedAt != null))
                .Should().Be(1);
        }
    }

    // --- Helpers ---------------------------------------------------------------------------------

    private static async Task<decimal> OutstandingFor(TestDbContext db, long invoiceId) =>
        await db.PayablesLedger.Where(e => e.SupplierInvoiceId == invoiceId).SumAsync(e => e.Amount);

    private static async Task<int> PayRowCount(TestDbContext db, long invoiceId) =>
        await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM supplier_inv_pay WHERE supinvid = {invoiceId}").SingleAsync();

    private static async Task<string?> PaymentStat(TestDbContext db, long invoiceId) =>
        await db.Database.SqlQuery<string?>($"SELECT paymentstat AS Value FROM supplier_invoice WHERE id = {invoiceId}").SingleAsync();

    private async Task<(long CompanyId, long SupplierId, string Code)> SeedCompanyAndSupplier()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext { UserId = 1 });

        var company = new Company { Name = "Smart Net (test)", VatCode = "1", IsVatRegistered = true };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        var code = $"S-{Guid.NewGuid():N}"[..12];
        var supplier = new Supplier { Code = code, Name = "Widgets Ltd" };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();

        return (company.Id, supplier.Id, code);
    }

    /// <summary>Seeds a legacy supplier_invoice row and its seeded payable, so there is real outstanding to settle.</summary>
    private async Task<long> SeedSupplierInvoice(long companyId, long supplierId, string code, string invno, decimal amount)
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext { UserId = 1 });

        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO `supplier_invoice` (`invno`, `supcode`, `amount`, `paymentstat`, `invdate`, `company`, `data_origin`)
            VALUES ({invno}, {code}, {amount.ToString(System.Globalization.CultureInfo.InvariantCulture)}, 'Pending',
                    '2026-07-10', {companyId.ToString(System.Globalization.CultureInfo.InvariantCulture)}, 'legacy')
            """);

        var invoiceId = await db.Database
            .SqlQuery<long>($"SELECT id AS Value FROM supplier_invoice WHERE invno = {invno}")
            .SingleAsync();

        db.PayablesLedger.Add(new PayablesLedgerEntry
        {
            SupplierId = supplierId,
            Type = PayablesLedgerEntryType.OpeningBalance,
            Amount = amount,
            SupplierInvoiceId = invoiceId,
            OccurredAt = new DateTime(2026, 7, 10),
            Note = "Imported from legacy system; not recalculated",
        });
        await db.SaveChangesAsync();

        return invoiceId;
    }
}
