using System.Globalization;
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
/// The suspended account: an invoice's outstanding moved off it — out of the ledger-derived figure, the
/// legacy balance shadow and the collectable receivable — while what the customer owes stays the same,
/// then brought back on its own or with the payment that settles it.
/// </summary>
[Collection(nameof(AuditCollection))]
public sealed class SuspendedReceivableTests
{
    private readonly AuditFixture _fixture;
    private static readonly TimeProvider Clock = TimeProvider.System;

    public SuspendedReceivableTests(AuditFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Suspending_takes_the_amount_off_the_invoice_but_not_off_what_the_customer_owes_and_reinstating_puts_it_back()
    {
        var (companyId, customerId, code) = await SeedCompanyAndCustomer();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };
        var invoiceId = await SeedInvoice(companyId, customerId, code, $"SUS-{Guid.NewGuid():N}"[..14], 100m);

        SuspensionRecorded suspended;
        await using (var db = _fixture.CreateContext(change))
        await using (var legacy = CreateLegacy())
        {
            suspended = await ServiceFor(db, legacy, change).SuspendAsync(
                new SuspendReceivable(invoiceId, 70m, new DateOnly(2026, 11, 1), "Customer disputing delivery"));
        }

        int rowVersion;
        await using (var db = _fixture.CreateContext(change))
        {
            (await OutstandingFor(db, invoiceId)).Should().Be(30m);
            (await LegacyBalance(db, invoiceId)).Should().Be(30m);
            (await new ReceivablesLedger(db).BalanceForCustomerAsync(customerId))
                .Should().Be(100m, "the customer still owes it — the credit limit is measured on this");

            var entry = await db.GlEntries.SingleAsync(e => e.SourceType == GlSources.ReceivableSuspension && e.SourceId == suspended.Id);
            (await Debit(db, entry.Id, GlAccountCodes.SuspendedReceivables)).Should().Be(70m);
            (await Credit(db, entry.Id, GlAccountCodes.AccountsReceivable)).Should().Be(70m);

            var row = await db.SuspendedReceivables.SingleAsync(s => s.Id == suspended.Id);
            row.Status.Should().Be(SuspendedReceivableStatus.Suspended);
            rowVersion = row.RowVersion;
        }

        await using (var db = _fixture.CreateContext(change))
        await using (var legacy = CreateLegacy())
        {
            await ServiceFor(db, legacy, change).ReinstateAsync(suspended.Id, rowVersion, "Dispute resolved");
        }

        await using (var db = _fixture.CreateContext(change))
        {
            (await OutstandingFor(db, invoiceId)).Should().Be(100m);
            (await LegacyBalance(db, invoiceId)).Should().Be(100m);
            (await db.SuspendedReceivables.SingleAsync(s => s.Id == suspended.Id)).Status
                .Should().Be(SuspendedReceivableStatus.Reinstated);
            (await db.GlEntries.CountAsync(e => e.SourceType == GlSources.ReceivableReinstatement && e.SourceId == suspended.Id))
                .Should().Be(1);
        }
    }

    [Fact]
    public async Task Settling_reinstates_and_takes_the_receipt_in_one_act()
    {
        var (companyId, customerId, code) = await SeedCompanyAndCustomer();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };
        var invoiceId = await SeedInvoice(companyId, customerId, code, $"SUS-{Guid.NewGuid():N}"[..14], 100m);

        SuspensionRecorded suspended;
        await using (var db = _fixture.CreateContext(change))
        await using (var legacy = CreateLegacy())
        {
            suspended = await ServiceFor(db, legacy, change).SuspendAsync(
                new SuspendReceivable(invoiceId, 100m, new DateOnly(2026, 11, 1), "Awaiting customer funds"));
        }

        int rowVersion;
        await using (var db = _fixture.CreateContext(change))
        {
            (await OutstandingFor(db, invoiceId)).Should().Be(0m);
            rowVersion = (await db.SuspendedReceivables.SingleAsync(s => s.Id == suspended.Id)).RowVersion;
        }

        CustomerReceiptCreated receipt;
        await using (var db = _fixture.CreateContext(change))
        await using (var legacy = CreateLegacy())
        {
            receipt = await ServiceFor(db, legacy, change).SettleAsync(
                suspended.Id, rowVersion, new SettleSuspension(60m, new DateOnly(2026, 11, 4), "BANK", "TT-9", "settle-1"));
        }

        await using (var db = _fixture.CreateContext(change))
        {
            receipt.Amount.Should().Be(60m);

            // 100 back on the invoice, 60 received: the remaining 40 is ordinary outstanding again.
            (await OutstandingFor(db, invoiceId)).Should().Be(40m);
            (await LegacyBalance(db, invoiceId)).Should().Be(40m);
            (await new ReceivablesLedger(db).BalanceForCustomerAsync(customerId)).Should().Be(40m);

            var row = await db.SuspendedReceivables.SingleAsync(s => s.Id == suspended.Id);
            row.Status.Should().Be(SuspendedReceivableStatus.Settled);
            row.ReceiptId.Should().Be(receipt.Id);
        }
    }

    [Fact]
    public async Task More_than_is_outstanding_cannot_be_suspended_and_a_suspended_invoice_cannot_be_suspended_again()
    {
        var (companyId, customerId, code) = await SeedCompanyAndCustomer();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };
        var invoiceId = await SeedInvoice(companyId, customerId, code, $"SUS-{Guid.NewGuid():N}"[..14], 100m);

        await using (var db = _fixture.CreateContext(change))
        await using (var legacy = CreateLegacy())
        {
            var tooMuch = () => ServiceFor(db, legacy, change).SuspendAsync(
                new SuspendReceivable(invoiceId, 100.01m, new DateOnly(2026, 11, 1), "Too much"));
            await tooMuch.Should().ThrowAsync<SuspensionExceedsOutstandingException>();
        }

        await using (var db = _fixture.CreateContext(change))
        await using (var legacy = CreateLegacy())
        {
            await ServiceFor(db, legacy, change).SuspendAsync(
                new SuspendReceivable(invoiceId, 20m, new DateOnly(2026, 11, 1), "First part"));
        }

        await using (var db = _fixture.CreateContext(change))
        await using (var legacy = CreateLegacy())
        {
            var again = () => ServiceFor(db, legacy, change).SuspendAsync(
                new SuspendReceivable(invoiceId, 10m, new DateOnly(2026, 11, 2), "Second part"));
            await again.Should().ThrowAsync<InvoiceAlreadySuspendedException>();
        }
    }

    // --- Helpers ---------------------------------------------------------------------------------

    private static ReceivableSuspensionService ServiceFor(TestDbContext db, SmartnetLegacyDbContext legacy, FakeChangeContext change) =>
        new(db, legacy, new GeneralLedger(db), new CustomerReceiptService(db, legacy, new GeneralLedger(db), change, Clock), change, Clock);

    private SmartnetLegacyDbContext CreateLegacy() =>
        new(new DbContextOptionsBuilder<SmartnetLegacyDbContext>()
            .UseMySql(_fixture.ConnectionString, SmartnetServerVersion.Value)
            .Options);

    private static async Task<decimal> OutstandingFor(TestDbContext db, long invoiceId) =>
        await db.ReceivablesLedger.Where(e => e.InvoiceId == invoiceId).SumAsync(e => e.Amount);

    private static async Task<decimal> LegacyBalance(TestDbContext db, long invoiceId) =>
        await db.Database.SqlQuery<decimal>($"SELECT CAST(balance AS DECIMAL(18,4)) AS Value FROM invoice_h WHERE id = {invoiceId}").SingleAsync();

    private static async Task<decimal> Debit(SmartnetDbContext db, long entryId, string code) =>
        await db.GlLines
            .Where(l => l.GlEntryId == entryId && db.GlAccounts.Any(a => a.Id == l.AccountId && a.Code == code))
            .Select(l => l.Debit)
            .SingleAsync();

    private static async Task<decimal> Credit(SmartnetDbContext db, long entryId, string code) =>
        await db.GlLines
            .Where(l => l.GlEntryId == entryId && db.GlAccounts.Any(a => a.Id == l.AccountId && a.Code == code))
            .Select(l => l.Credit)
            .SingleAsync();

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

    /// <summary>Seeds an invoice_h row and its receivables-ledger charge, so there is real outstanding to suspend.</summary>
    private async Task<long> SeedInvoice(long companyId, long customerId, string code, string invoiceNo, decimal total)
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext { UserId = 1 });

        var amount = total.ToString(CultureInfo.InvariantCulture);
        var company = companyId.ToString(CultureInfo.InvariantCulture);
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO `invoice_h`
              (`invoiceno`, `invtype`, `indate`, `customer`, `totamount`, `balance`, `company`, `data_origin`,
               `discountper`, `beforedisctot`, `contactperson`, `row_version`)
            VALUES ({invoiceNo}, 'item', '2026-07-10', {code}, {amount}, {amount}, {company}, 'new', '0', '0', '', 0)
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
