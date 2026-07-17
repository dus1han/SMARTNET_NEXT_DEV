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
/// The versioned, reason-gated, concurrency-guarded invoice edit (Phase 5, slice 5). This is where the
/// legacy edit's three bugs are proven closed: lines reconciled in place (not delete-and-reinsert), the
/// balance adjusted by a compensating ledger entry (not reset), and a concurrent edit rejected (not
/// last-write-wins). The prior version still reprints exactly as issued.
/// </summary>
[Collection(nameof(AuditCollection))]
public sealed class InvoiceEditTests
{
    private readonly AuditFixture _fixture;
    private static readonly TimeProvider Clock = TimeProvider.System;

    public InvoiceEditTests(AuditFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Editing_writes_a_new_version_updates_figures_and_adjusts_the_ledger_by_a_delta()
    {
        var (companyId, customerId, itemId) = await SeedCompanyCustomerItemAndSeries();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId, Reason = "Customer ordered one more unit" };

        var created = await CreateInvoice(change, companyId, customerId, itemId);

        // Read the invoice back for its line ids and row_version — what the edit screen would load.
        long itemLineId, serviceLineId;
        int rowVersion;
        await using (var db = _fixture.CreateContext(change))
        {
            var invoice = await db.Invoices.Include(i => i.Lines).FirstAsync(i => i.Id == created.Id);
            rowVersion = invoice.RowVersion;
            itemLineId = invoice.Lines.Single(l => l.ItemId == itemId).Id;
            serviceLineId = invoice.Lines.Single(l => l.ItemId == null).Id;
        }

        // Bump the item line 2 → 3: gross 300, less 10% = net 270; service 50; net 320, VAT 57.60, total 377.60.
        InvoiceEdited edited;
        await using (var db = _fixture.CreateContext(change))
        {
            edited = await EditorFor(db, change).EditAsync(created.Id, new EditInvoice(
                rowVersion, PurchaseOrderNo: "PO-99", ContactPerson: null, DocumentDiscountPercent: 0m,
                Lines:
                [
                    new EditInvoiceLine(itemLineId, itemId, "I-1", "Widget", 3m, 100m, 10m, Cost: 180m),
                    new EditInvoiceLine(serviceLineId, null, null, "Labour", 1m, 50m, 0m, Cost: null),
                ]));
        }

        edited.Total.Should().Be(377.60m);
        edited.VersionNo.Should().Be(2);
        edited.Outstanding.Should().Be(377.60m); // 271.40 original charge + 106.20 delta

        await using (var db = _fixture.CreateContext(change))
        {
            var invoice = await db.Invoices.Include(i => i.Lines).FirstAsync(i => i.Id == created.Id);
            invoice.Total.Should().Be(377.60m);
            invoice.RowVersion.Should().Be(2);

            // The item line was updated *in place* — same id, new quantity — not deleted and re-inserted.
            invoice.Lines.Where(l => l.DeletedAt == null).Should().HaveCount(2);
            invoice.Lines.Single(l => l.Id == itemLineId).Quantity.Should().Be(3m);

            // The balance moved by a single compensating charge, never a reset: two Charge entries.
            var ledger = await db.ReceivablesLedger.Where(e => e.InvoiceId == created.Id).ToListAsync();
            ledger.Where(e => e.Type == LedgerEntryType.Charge).Should().HaveCount(2);
            ledger.Sum(e => e.Amount).Should().Be(377.60m);

            // Stock followed the item line automatically: the original issue (−2) plus the edit's extra
            // unit issued (−1) leaves 3 units out of stock — the quantity now on the line.
            var movements = await db.StockMovements.Where(m => m.ItemId == itemId).ToListAsync();
            movements.Sum(m => m.Quantity).Should().Be(-3m);
            movements.Should().Contain(m => m.Type == StockMovementType.Issue && m.Quantity == -1m);

            // The prior version still prints as issued: v1 holds the original total, v2 the new — with reason.
            var versions = await db.DocumentVersions
                .Where(v => v.DocType == DocumentTypes.Invoice && v.DocId == created.Id)
                .OrderBy(v => v.VersionNo)
                .ToListAsync();
            versions.Should().HaveCount(2);
            versions[0].Snapshot.Should().Contain("271.40");
            versions[1].Snapshot.Should().Contain("377.60");
            versions[1].Reason.Should().Be("Customer ordered one more unit");
        }
    }

    [Fact]
    public async Task A_concurrent_edit_is_rejected()
    {
        var (companyId, customerId, itemId) = await SeedCompanyCustomerItemAndSeries();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId, Reason = "Correcting the quantity" };

        var created = await CreateInvoice(change, companyId, customerId, itemId);

        long lineId;
        await using (var db = _fixture.CreateContext(change))
        {
            lineId = (await db.Invoices.Include(i => i.Lines).FirstAsync(i => i.Id == created.Id))
                .Lines.Single(l => l.ItemId == itemId).Id;
        }

        // Two editors both loaded row_version 1. The first edit succeeds (→ 2).
        await using (var db = _fixture.CreateContext(change))
        {
            await EditorFor(db, change).EditAsync(created.Id, new EditInvoice(
                ExpectedRowVersion: 1, "PO-99", null, 0m,
                [new EditInvoiceLine(lineId, itemId, "I-1", "Widget", 3m, 100m, 0m, Cost: 180m)]));
        }

        // The second, still holding row_version 1, is refused — not silently applied over the first.
        await using (var db = _fixture.CreateContext(change))
        {
            var act = () => EditorFor(db, change).EditAsync(created.Id, new EditInvoice(
                ExpectedRowVersion: 1, "PO-77", null, 0m,
                [new EditInvoiceLine(lineId, itemId, "I-1", "Widget", 5m, 100m, 0m, Cost: 300m)]));

            await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
        }

        // And the first edit stands — the loser did not overwrite it.
        await using (var db = _fixture.CreateContext(change))
        {
            (await db.Invoices.FirstAsync(i => i.Id == created.Id)).RowVersion.Should().Be(2);
        }
    }

    [Fact]
    public async Task Editing_an_invoice_with_a_payment_is_refused()
    {
        var (companyId, customerId, itemId) = await SeedCompanyCustomerItemAndSeries();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId, Reason = "Trying to edit a paid invoice" };

        var created = await CreateInvoice(change, companyId, customerId, itemId);

        long lineId;
        int rowVersion;
        await using (var db = _fixture.CreateContext(change))
        {
            var invoice = await db.Invoices.Include(i => i.Lines).FirstAsync(i => i.Id == created.Id);
            rowVersion = invoice.RowVersion;
            lineId = invoice.Lines.Single(l => l.ItemId == itemId).Id;

            // A partial payment arrives against the invoice — what Phase 7 records.
            db.ReceivablesLedger.Add(new LedgerEntry
            {
                CustomerId = customerId, Type = LedgerEntryType.Payment, Amount = -100m,
                InvoiceId = created.Id, OccurredAt = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc),
            });
            await db.SaveChangesAsync();
        }

        // The edit is refused: the payment must be deleted first.
        await using (var db = _fixture.CreateContext(change))
        {
            var act = () => EditorFor(db, change).EditAsync(created.Id, new EditInvoice(
                rowVersion, "PO-99", null, 0m,
                [new EditInvoiceLine(lineId, itemId, "I-1", "Widget", 5m, 100m, 0m, 300m)]));

            await act.Should().ThrowAsync<InvoiceHasPaymentsException>();
        }

        // Nothing changed — no new version, no adjustment.
        await using (var db = _fixture.CreateContext(change))
        {
            (await db.DocumentVersions.CountAsync(v => v.DocType == DocumentTypes.Invoice && v.DocId == created.Id))
                .Should().Be(1);
        }
    }

    [Fact]
    public async Task Reducing_a_quantity_returns_stock()
    {
        var (companyId, customerId, itemId) = await SeedCompanyCustomerItemAndSeries();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId, Reason = "Customer took fewer units" };

        var created = await CreateInvoice(change, companyId, customerId, itemId); // item qty 2 issued (−2)

        long itemLineId, serviceLineId;
        int rowVersion;
        await using (var db = _fixture.CreateContext(change))
        {
            var invoice = await db.Invoices.Include(i => i.Lines).FirstAsync(i => i.Id == created.Id);
            rowVersion = invoice.RowVersion;
            itemLineId = invoice.Lines.Single(l => l.ItemId == itemId).Id;
            serviceLineId = invoice.Lines.Single(l => l.ItemId == null).Id;
        }

        // Drop the item quantity 2 → 1: one unit should come back into stock as a receipt.
        await using (var db = _fixture.CreateContext(change))
        {
            await EditorFor(db, change).EditAsync(created.Id, new EditInvoice(
                rowVersion, null, null, 0m,
                [
                    new EditInvoiceLine(itemLineId, itemId, "I-1", "Widget", 1m, 100m, 10m, Cost: 60m),
                    new EditInvoiceLine(serviceLineId, null, null, "Labour", 1m, 50m, 0m, null),
                ]));
        }

        await using (var db = _fixture.CreateContext(change))
        {
            var movements = await db.StockMovements.Where(m => m.ItemId == itemId).ToListAsync();
            movements.Sum(m => m.Quantity).Should().Be(-1m); // 2 out, 1 back
            movements.Should().Contain(m => m.Type == StockMovementType.Receipt && m.Quantity == 1m);
        }
    }

    [Fact]
    public async Task Editing_reconciles_lines_in_place_updating_adding_and_soft_deleting()
    {
        var (companyId, customerId, itemId) = await SeedCompanyCustomerItemAndSeries();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId, Reason = "Reworking the line items" };

        var created = await CreateInvoice(change, companyId, customerId, itemId);

        long itemLineId, serviceLineId;
        int rowVersion;
        await using (var db = _fixture.CreateContext(change))
        {
            var invoice = await db.Invoices.Include(i => i.Lines).FirstAsync(i => i.Id == created.Id);
            rowVersion = invoice.RowVersion;
            itemLineId = invoice.Lines.Single(l => l.ItemId == itemId).Id;
            serviceLineId = invoice.Lines.Single(l => l.ItemId == null).Id;
        }

        // Keep and update the item line; drop the service line; add a brand-new service line (null id).
        await using (var db = _fixture.CreateContext(change))
        {
            await EditorFor(db, change).EditAsync(created.Id, new EditInvoice(
                rowVersion, null, null, 0m,
                Lines:
                [
                    new EditInvoiceLine(itemLineId, itemId, "I-1", "Widget", 4m, 100m, 0m, Cost: 240m),
                    new EditInvoiceLine(null, null, null, "Installation", 1m, 200m, 0m, Cost: null),
                ]));
        }

        await using (var db = _fixture.CreateContext(change))
        {
            var lines = await db.InvoiceLines.Where(l => l.InvoiceId == created.Id).ToListAsync();

            // The item line survived with its id (updated, not replaced); the dropped service line is
            // soft-deleted (recoverable, attributable); the new line was added.
            lines.Single(l => l.Id == itemLineId).Should().Match<InvoiceLine>(l => l.DeletedAt == null && l.Quantity == 4m);
            lines.Single(l => l.Id == serviceLineId).DeletedAt.Should().NotBeNull();
            lines.Where(l => l.DeletedAt == null).Should().HaveCount(2);
            lines.Should().Contain(l => l.Description == "Installation" && l.DeletedAt == null);
        }
    }

    // --- Seeding ---------------------------------------------------------------------------------

    private async Task<InvoiceCreated> CreateInvoice(FakeChangeContext change, long companyId, long customerId, long itemId)
    {
        await using var db = _fixture.CreateContext(change);
        return await new InvoiceCreator(
            db, new TaxEngine(), new DocumentNumberAllocator(db),
            new DocumentVersionWriter(db, change, Clock), new ReceivablesLedger(db), new GeneralLedger(db),
            new BusinessRuleReader(db), change, Clock)
            .CreateAsync(new NewInvoice(
                companyId, customerId, InvoiceType.Credit, new DateOnly(2026, 7, 15), "PO-99", null,
                [
                    new NewInvoiceLine(itemId, "I-1", "Widget", 2m, 100m, 10m, Cost: 120m),
                    new NewInvoiceLine(null, null, "Labour", 1m, 50m, 0m, Cost: null),
                ]));
    }

    private InvoiceEditor EditorFor(TestDbContext db, FakeChangeContext change) => new(
        db,
        LegacyContext(),
        new TaxEngine(),
        new DocumentVersionWriter(db, change, Clock),
        AdopterFor(db, change),
        new BusinessRuleReader(db),
        change,
        Clock);

    private static LegacyInvoiceAdopter AdopterFor(TestDbContext db, FakeChangeContext change) => new(
        db, new TaxEngine(), new DocumentVersionWriter(db, change, Clock), new BusinessRuleReader(db));

    /// <summary>A legacy read-model context on the same throwaway database — the editor's legacy-payment check.</summary>
    private SmartnetLegacyDbContext LegacyContext() => new(
        new DbContextOptionsBuilder<SmartnetLegacyDbContext>()
            .UseMySql(_fixture.ConnectionString, SmartnetServerVersion.Value)
            .Options);

    private async Task<(long CompanyId, long CustomerId, long ItemId)> SeedCompanyCustomerItemAndSeries()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext { UserId = 1 });

        var company = new Company { Name = "Smart Net (test)", VatCode = "1", IsVatRegistered = true };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        db.TaxRates.Add(new TaxRate
        {
            CompanyId = company.Id, Name = "VAT 18%", Percentage = 18m,
            EffectiveFrom = new DateOnly(2024, 1, 1), IsDefault = true,
        });

        var customer = new Customer { Code = $"C-{company.Id}", Name = "Acme" };
        db.Customers.Add(customer);

        var item = new Item { Code = $"I-{company.Id}", Name = "Widget", Cost = 60m, SellingPrice = 100m };
        db.Items.Add(item);

        db.DocumentSeries.Add(new DocumentSeries
        {
            CompanyId = company.Id, DocType = DocumentTypes.Invoice, Prefix = $"INV{company.Id}-", NextNumber = 1215, Padding = 0,
        });

        await db.SaveChangesAsync();
        return (company.Id, customer.Id, item.Id);
    }
}
