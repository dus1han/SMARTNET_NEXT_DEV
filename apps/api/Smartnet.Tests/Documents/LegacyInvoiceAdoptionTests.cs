using System.Globalization;
using Microsoft.EntityFrameworkCore;
using FluentAssertions;
using Smartnet.Domain.Documents;
using Smartnet.Domain.Ledger;
using Smartnet.Domain.MasterData;
using Smartnet.Domain.Settings;
using Smartnet.Infrastructure.Auditing;
using Smartnet.Infrastructure.Documents;
using Smartnet.Infrastructure.Ledger;
using Smartnet.Infrastructure.Numbering;
using Smartnet.Infrastructure.Persistence;
using Smartnet.Infrastructure.Settings;
using Smartnet.Tests.Auditing;

namespace Smartnet.Tests.Documents;

/// <summary>
/// Legacy parity (Phase 5, slice 5b): a legacy invoice — one the old app raised, whose real figures live in
/// varchar columns — is adopted into the new model the first time the new app edits or voids it. Adoption
/// materialises the typed columns and lines, recomputes the money through the decimal engine, and writes a
/// version-1 "as imported" snapshot; then the normal edit/void runs. This is the same mechanism the go-live
/// migration will run in bulk.
/// </summary>
[Collection(nameof(AuditCollection))]
public sealed class LegacyInvoiceAdoptionTests
{
    private readonly AuditFixture _fixture;
    private static readonly TimeProvider Clock = TimeProvider.System;

    public LegacyInvoiceAdoptionTests(AuditFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Editing_a_legacy_invoice_adopts_it_then_applies_the_edit()
    {
        var (companyId, customerId, itemId, number, itemCode) = await SeedLegacyInvoice(withPayment: false);
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId, Reason = "Correcting the quantity on an old invoice" };

        long invoiceId, lineId;
        int rowVersion;
        await using (var db = _fixture.CreateContext(change))
        {
            invoiceId = await db.Database.SqlQuery<long>($"SELECT id AS Value FROM invoice_h WHERE invoiceno = {number}").SingleAsync();
            lineId = await db.Database.SqlQuery<long>($"SELECT id AS Value FROM invoice_l WHERE inno = {number} ORDER BY id").FirstAsync();
            rowVersion = await db.Database.SqlQuery<int>($"SELECT row_version AS Value FROM invoice_h WHERE id = {invoiceId}").SingleAsync();
        }

        // Edit the legacy invoice: bump the item line 2 → 3. Adoption values it at 236 (2×100 net 200, VAT
        // 36); the edit takes it to 354 (3×100 net 300, VAT 54). Both through the decimal engine.
        InvoiceEdited edited;
        await using (var db = _fixture.CreateContext(change))
        {
            edited = await EditorFor(db, change).EditAsync(invoiceId, new EditInvoice(
                rowVersion, PurchaseOrderNo: "PO-L", ContactPerson: null, DocumentDiscountPercent: 0m,
                [new EditInvoiceLine(lineId, itemId, itemCode, "Widget", 3m, 100m, 0m, Cost: 180m)]));
        }

        edited.Total.Should().Be(354m);

        await using (var db = _fixture.CreateContext(change))
        {
            var invoice = await db.Invoices.Include(i => i.Lines).FirstAsync(i => i.Id == invoiceId);
            invoice.DataOrigin.Should().Be("new"); // adopted
            invoice.Total.Should().Be(354m);
            invoice.CustomerId.Should().Be(customerId);
            invoice.Lines.Single(l => l.DeletedAt == null).Quantity.Should().Be(3m);

            // Two versions: v1 "as imported" (adopted), v2 the edit.
            var versions = await db.DocumentVersions
                .Where(v => v.DocType == DocumentTypes.Invoice && v.DocId == invoiceId)
                .OrderBy(v => v.VersionNo).ToListAsync();
            versions.Should().HaveCount(2);
            versions[0].Reason.Should().Be("Adopted from the legacy system");
            versions[0].Snapshot.Should().Contain("236");

            // The balance: the imported opening balance (236) plus the edit's charge delta (+118) = 354.
            var ledger = await db.ReceivablesLedger.Where(e => e.InvoiceId == invoiceId).ToListAsync();
            ledger.Sum(e => e.Amount).Should().Be(354m);
            ledger.Should().Contain(e => e.Type == LedgerEntryType.OpeningBalance && e.Amount == 236m);
            ledger.Should().Contain(e => e.Type == LedgerEntryType.Charge && e.Amount == 118m);
        }
    }

    [Fact]
    public async Task Voiding_a_legacy_invoice_adopts_it_reverses_the_opening_balance_and_returns_stock()
    {
        var (companyId, customerId, itemId, number, itemCode) = await SeedLegacyInvoice(withPayment: false);
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId, Reason = "Voiding a legacy invoice raised in error" };

        long invoiceId;
        int rowVersion;
        await using (var db = _fixture.CreateContext(change))
        {
            invoiceId = await db.Database.SqlQuery<long>($"SELECT id AS Value FROM invoice_h WHERE invoiceno = {number}").SingleAsync();
            rowVersion = await db.Database.SqlQuery<int>($"SELECT row_version AS Value FROM invoice_h WHERE id = {invoiceId}").SingleAsync();
        }

        await using (var db = _fixture.CreateContext(change))
        {
            await new InvoiceDeleter(db, LegacyContext(), AdopterFor(db, change), Clock).DeleteAsync(invoiceId, rowVersion);
        }

        await using (var db = _fixture.CreateContext(change))
        {
            // Adopted, then soft-deleted.
            (await db.Invoices.AnyAsync(i => i.Id == invoiceId)).Should().BeFalse();
            var row = await db.Invoices.IgnoreQueryFilters().FirstAsync(i => i.Id == invoiceId);
            row.DataOrigin.Should().Be("new");
            row.DeletedAt.Should().NotBeNull();

            // The opening balance (236) is reversed to zero through a compensating entry — never wiped.
            (await new ReceivablesLedger(db).BalanceForCustomerAsync(customerId)).Should().Be(0m);

            // The goods on the item line are returned to stock (a receipt of 2).
            (await db.StockMovements.CountAsync(m => m.ItemId == itemId && m.Type == StockMovementType.Receipt && m.Quantity == 2m))
                .Should().Be(1);
        }
    }

    [Fact]
    public async Task A_legacy_invoice_with_a_legacy_payment_cannot_be_edited()
    {
        var (companyId, customerId, itemId, number, itemCode) = await SeedLegacyInvoice(withPayment: true);
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId, Reason = "Trying to edit a paid legacy invoice" };

        long invoiceId, lineId;
        int rowVersion;
        await using (var db = _fixture.CreateContext(change))
        {
            invoiceId = await db.Database.SqlQuery<long>($"SELECT id AS Value FROM invoice_h WHERE invoiceno = {number}").SingleAsync();
            lineId = await db.Database.SqlQuery<long>($"SELECT id AS Value FROM invoice_l WHERE inno = {number} ORDER BY id").FirstAsync();
            rowVersion = await db.Database.SqlQuery<int>($"SELECT row_version AS Value FROM invoice_h WHERE id = {invoiceId}").SingleAsync();
        }

        // Its payment is in the old `payments` table, not the new ledger — the check must still find it.
        await using (var db = _fixture.CreateContext(change))
        {
            var act = () => EditorFor(db, change).EditAsync(invoiceId, new EditInvoice(
                rowVersion, "PO-L", null, 0m,
                [new EditInvoiceLine(lineId, itemId, itemCode, "Widget", 3m, 100m, 0m, 180m)]));

            await act.Should().ThrowAsync<InvoiceHasPaymentsException>();
        }

        // Untouched — still legacy, no version written.
        await using (var db = _fixture.CreateContext(change))
        {
            var row = await db.Invoices.IgnoreQueryFilters().FirstAsync(i => i.Id == invoiceId);
            row.DataOrigin.Should().Be("legacy");
            (await db.DocumentVersions.CountAsync(v => v.DocType == DocumentTypes.Invoice && v.DocId == invoiceId)).Should().Be(0);
        }
    }

    // --- Seeding ---------------------------------------------------------------------------------

    private InvoiceEditor EditorFor(TestDbContext db, FakeChangeContext change) => new(
        db, LegacyContext(), new TaxEngine(), new DocumentVersionWriter(db, change, Clock),
        AdopterFor(db, change), new BusinessRuleReader(db), change, Clock);

    private static LegacyInvoiceAdopter AdopterFor(TestDbContext db, FakeChangeContext change) => new(
        db, new TaxEngine(), new DocumentVersionWriter(db, change, Clock), new BusinessRuleReader(db));

    private SmartnetLegacyDbContext LegacyContext() => new(
        new DbContextOptionsBuilder<SmartnetLegacyDbContext>()
            .UseMySql(_fixture.ConnectionString, SmartnetServerVersion.Value)
            .Options);

    /// <summary>
    /// Seeds a legacy invoice as the old app wrote it — money in varchar columns, customer and item as
    /// codes, data_origin 'legacy' — plus the OPENING_BALANCE ledger entry cutover imported for it, and
    /// optionally a row in the old `payments` table.
    /// </summary>
    private async Task<(long CompanyId, long CustomerId, long ItemId, string Number, string ItemCode)> SeedLegacyInvoice(bool withPayment)
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext { UserId = 1 });

        var company = new Company { Name = "Smart Net (test)", VatCode = "1", IsVatRegistered = true };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        var itemCode = $"IL{company.Id}";
        var customer = new Customer { Code = $"CL-{company.Id}", Name = "Legacy Co" };
        var item = new Item { Code = itemCode, Name = "Widget", Cost = 60m, SellingPrice = 100m };
        db.Customers.Add(customer);
        db.Items.Add(item);
        await db.SaveChangesAsync();

        var number = $"SNI-L{company.Id}";

        // The legacy invoice_h row: total 236 (net 200 + 18% VAT), balance 236 (unpaid). 2 × 100 item line.
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO invoice_h
              (it, invoiceno, invtype, indate, customer, pono, totamount, balance, preparedby, cdatetime,
               cost, company, novattotal, vtype, vper, discountper, beforedisctot, contactperson,
               company_id, data_origin)
            VALUES
              ('ITEM', {number}, 'CREDIT', '2024-05-01', {customer.Code}, 'PO-L', '236', '236', 'Old User',
               '2024-05-01 10:00:00', '120', {company.Id.ToString(CultureInfo.InvariantCulture)}, '200', '1',
               '18', '0', '200', 'Mr Legacy', {company.Id}, 'legacy')
            """);

        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO invoice_l (inno, itemcode, `desc`, qty, rate, tot)
            VALUES ({number}, {item.Code}, 'Widget', '2', '100', '200')
            """);

        var invoiceId = await db.Database
            .SqlQuery<long>($"SELECT id AS Value FROM invoice_h WHERE invoiceno = {number}")
            .SingleAsync();

        // The opening balance cutover imported for this legacy invoice (LEGACY-DATA-POLICY §2).
        db.ReceivablesLedger.Add(new LedgerEntry
        {
            CustomerId = customer.Id,
            Type = LedgerEntryType.OpeningBalance,
            Amount = 236m,
            InvoiceId = invoiceId,
            OccurredAt = new DateTime(2024, 5, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        await db.SaveChangesAsync();

        if (withPayment)
        {
            await db.Database.ExecuteSqlAsync($"INSERT INTO payments (invoiceno) VALUES ({number})");
        }

        return (company.Id, customer.Id, item.Id, number, itemCode);
    }
}
