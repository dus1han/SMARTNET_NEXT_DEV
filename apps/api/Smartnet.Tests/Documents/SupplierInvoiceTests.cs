using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Documents;
using Smartnet.Domain.MasterData;
using Smartnet.Domain.Settings;
using Smartnet.Infrastructure.Auditing;
using Smartnet.Infrastructure.Documents;
using Smartnet.Infrastructure.Ledger;
using Smartnet.Infrastructure.Persistence;
using Smartnet.Tests.Auditing;

namespace Smartnet.Tests.Documents;

/// <summary>
/// Supplier invoices &amp; the payables ledger (Phase 6, slice 2): the accounts-payable side. A header-only
/// AP record whose payable and payments are ledger entries, so the outstanding is derived (never a stored,
/// mutated column) and — unlike the legacy binary <c>paymentstat</c> flag — <b>partial payments work</b>
/// and "paid" is a computed fact (<c>Σ = 0</c>).
/// </summary>
[Collection(nameof(AuditCollection))]
public sealed class SupplierInvoiceTests
{
    private readonly AuditFixture _fixture;
    private static readonly TimeProvider Clock = TimeProvider.System;

    public SupplierInvoiceTests(AuditFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_supplier_invoice_posts_a_payable_that_two_partial_payments_settle_to_zero()
    {
        var (companyId, supplierId) = await SeedCompanyAndSupplier();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };

        SupplierInvoiceCreated created;
        await using (var db = _fixture.CreateContext(change))
        {
            created = await ServiceFor(db, change).CreateAsync(new NewSupplierInvoice(
                companyId, supplierId, SupplierReference: "SUP-INV-9",
                Date: new DateOnly(2026, 7, 16), NetTotal: 100m, TaxRatePercentage: 18m, Amount: 118m));
        }

        created.Amount.Should().Be(118m);

        // The payable is on the ledger — the supplier balance and the invoice's outstanding are both 118.
        await using (var db = _fixture.CreateContext(change))
        {
            var ledger = new PayablesLedger(db);
            (await ledger.BalanceForSupplierAsync(supplierId)).Should().Be(118m);
            (await ledger.OutstandingForInvoiceAsync(created.Id)).Should().Be(118m);

            // Pending, dual-written for the legacy supplier report.
            (await LegacyPaymentStat(db, created.Id)).Should().Be("Pending");

            // A version-1 snapshot exists.
            var versions = await db.DocumentVersions
                .Where(v => v.DocType == DocumentTypes.SupplierInvoice && v.DocId == created.Id)
                .ToListAsync();
            versions.Should().ContainSingle().Which.VersionNo.Should().Be(1);
        }

        // First partial payment: 50 → 68 outstanding, still Pending.
        await using (var db = _fixture.CreateContext(change))
        {
            var result = await ServiceFor(db, change).RecordPaymentAsync(
                created.Id, new RecordSupplierPayment(50m, new DateOnly(2026, 7, 17), "CASH", "R-1"));
            result.Outstanding.Should().Be(68m);
        }

        await using (var db = _fixture.CreateContext(change))
        {
            (await new PayablesLedger(db).OutstandingForInvoiceAsync(created.Id)).Should().Be(68m);
            (await LegacyPaymentStat(db, created.Id)).Should().Be("Pending");
        }

        // Second payment settles it: 68 → 0, flips Paid.
        await using (var db = _fixture.CreateContext(change))
        {
            var result = await ServiceFor(db, change).RecordPaymentAsync(
                created.Id, new RecordSupplierPayment(68m, new DateOnly(2026, 7, 18), "BANK", "R-2"));
            result.Outstanding.Should().Be(0m);
        }

        await using (var db = _fixture.CreateContext(change))
        {
            (await new PayablesLedger(db).BalanceForSupplierAsync(supplierId)).Should().Be(0m);
            (await LegacyPaymentStat(db, created.Id)).Should().Be("Paid"); // derived Σ=0, dual-written

            // Two legacy supplier_inv_pay rows were dual-written for the legacy report.
            var payRows = await db.Database
                .SqlQuery<int>($"SELECT COUNT(*) AS Value FROM supplier_inv_pay WHERE supinvid = {created.Id}")
                .SingleAsync();
            payRows.Should().Be(2);
        }
    }

    [Fact]
    public async Task A_payment_over_the_outstanding_is_refused()
    {
        var (companyId, supplierId) = await SeedCompanyAndSupplier();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };

        long id;
        await using (var db = _fixture.CreateContext(change))
        {
            id = (await ServiceFor(db, change).CreateAsync(new NewSupplierInvoice(
                companyId, supplierId, "SUP-INV-10", new DateOnly(2026, 7, 16), 100m, 0m, 100m))).Id;
        }

        await using (var db = _fixture.CreateContext(change))
        {
            var act = () => ServiceFor(db, change).RecordPaymentAsync(
                id, new RecordSupplierPayment(150m, new DateOnly(2026, 7, 17), "CASH", null));
            await act.Should().ThrowAsync<SupplierPaymentExceedsOutstandingException>();
        }
    }

    [Fact]
    public async Task Voiding_a_supplier_invoice_reverses_its_payable_to_zero()
    {
        var (companyId, supplierId) = await SeedCompanyAndSupplier();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };

        long id;
        int rowVersion;
        await using (var db = _fixture.CreateContext(change))
        {
            id = (await ServiceFor(db, change).CreateAsync(new NewSupplierInvoice(
                companyId, supplierId, "SUP-INV-11", new DateOnly(2026, 7, 16), 100m, 0m, 100m))).Id;
        }

        // Pay part of it, then void — the remaining outstanding reverses through a compensating entry.
        await using (var db = _fixture.CreateContext(change))
        {
            await ServiceFor(db, change).RecordPaymentAsync(
                id, new RecordSupplierPayment(30m, new DateOnly(2026, 7, 17), "CASH", null));
            rowVersion = await db.SupplierInvoices.Where(s => s.Id == id).Select(s => s.RowVersion).SingleAsync();
        }

        await using (var db = _fixture.CreateContext(change))
        {
            await ServiceFor(db, change).DeleteAsync(id, rowVersion);
        }

        await using (var db = _fixture.CreateContext(change))
        {
            // The supplier owes nothing now (100 purchase − 30 payment − 70 void reversal = 0), and the
            // invoice is soft-deleted (excluded by the query filter) — its history intact, not erased.
            (await new PayablesLedger(db).BalanceForSupplierAsync(supplierId)).Should().Be(0m);
            (await db.SupplierInvoices.CountAsync(s => s.Id == id)).Should().Be(0);
            (await db.SupplierInvoices.IgnoreQueryFilters().CountAsync(s => s.Id == id && s.DeletedAt != null))
                .Should().Be(1);
        }
    }

    // --- Seeding ---------------------------------------------------------------------------------

    private static SupplierInvoiceService ServiceFor(TestDbContext db, FakeChangeContext change) => new(
        db,
        new PayablesLedger(db),
        new GeneralLedger(db),
        new DocumentVersionWriter(db, change, Clock),
        change,
        Clock);

    private static async Task<string?> LegacyPaymentStat(TestDbContext db, long id) =>
        await db.Database
            .SqlQuery<string?>($"SELECT paymentstat AS Value FROM supplier_invoice WHERE id = {id}")
            .SingleAsync();

    private async Task<(long CompanyId, long SupplierId)> SeedCompanyAndSupplier()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext { UserId = 1 });

        var company = new Company { Name = "Smart Net (test)", VatCode = "1", IsVatRegistered = true };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        var supplier = new Supplier { Code = $"S-{company.Id}", Name = "Widgets Ltd" };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();

        return (company.Id, supplier.Id);
    }
}
