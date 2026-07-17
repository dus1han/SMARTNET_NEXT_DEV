using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Documents;
using Smartnet.Domain.Ledger;
using Smartnet.Domain.MasterData;
using Smartnet.Domain.Settings;
using Smartnet.Infrastructure.Auditing;
using Smartnet.Infrastructure.Documents;
using Smartnet.Infrastructure.Ledger;
using Smartnet.Infrastructure.Numbering;
using Smartnet.Infrastructure.Settings;
using Smartnet.Tests.Auditing;

namespace Smartnet.Tests.Documents;

/// <summary>
/// Credit notes (Phase 5, slice 4): the mirror of an invoice — raised against a parent invoice, posting the
/// opposite ledger sign and, where it returns goods, a stock receipt back into stock. Its rate is inherited
/// from the parent invoice (the engine is given no rate table of its own), so a full credit nets exactly
/// against the invoice it reverses. The bug closed here is the legacy <c>UPDATE invoice_h SET balance =
/// balance - x</c> (B3): a credit is a ledger entry, never a mutated column.
/// </summary>
[Collection(nameof(AuditCollection))]
public sealed class CreditNoteTests
{
    private readonly AuditFixture _fixture;
    private static readonly TimeProvider Clock = TimeProvider.System;

    public CreditNoteTests(AuditFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_credit_note_credits_the_ledger_returns_stock_and_snapshots_itself()
    {
        var (companyId, customerId, itemId) = await SeedCompanyCustomerItemAndSeries(vatRegistered: true);
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };

        // A parent invoice on credit: item 2 × 100 less 10% (net 180) + service 1 × 50 (net 50). Total
        // 271.40 at 18% VAT, charged to the ledger, stock issued for the item line.
        InvoiceCreated invoice = await CreateParentInvoice(change, companyId, customerId, itemId);

        // Now credit the whole thing back — returning the goods to stock. The rate is inherited from the
        // invoice (18%); the engine is handed no rate table of its own.
        CreditNoteCreated note;
        await using (var db = _fixture.CreateContext(change))
        {
            note = await CreatorFor(db, change).CreateAsync(new NewCreditNote(
                companyId, customerId, invoice.Id, invoice.Number, new DateOnly(2026, 7, 20),
                ReturnsStock: true, TaxRateId: null, TaxRatePercentage: 18m,
                Lines:
                [
                    new NewCreditNoteLine(itemId, "I-1", "Widget", 2m, 100m, 10m, Cost: 120m),
                    new NewCreditNoteLine(null, null, "Labour", 1m, 50m, 0m, Cost: null),
                ]));
        }

        note.Number.Should().Be($"CN{companyId}-800");
        note.Total.Should().Be(271.40m);

        await using (var db = _fixture.CreateContext(change))
        {
            var saved = await db.CreditNotes.Include(c => c.Lines).FirstAsync(c => c.Id == note.Id);

            saved.Subtotal.Should().Be(250m);
            saved.NetTotal.Should().Be(230m);
            saved.TaxAmount.Should().Be(41.40m);
            saved.Total.Should().Be(271.40m);
            saved.TaxRatePercentage.Should().Be(18m); // inherited — the engine had no rate table
            saved.InvoiceId.Should().Be(invoice.Id);
            saved.ReturnsStock.Should().BeTrue();
            saved.DataOrigin.Should().Be("new");
            saved.Lines.Should().HaveCount(2);

            // The credit nets the invoice: 271.40 charged, 271.40 credited → the customer owes nothing.
            (await new ReceivablesLedger(db).BalanceForCustomerAsync(customerId)).Should().Be(0m);

            // The credit entry is the opposite sign to the invoice's charge, and it names the parent invoice.
            var credit = await db.ReceivablesLedger.SingleAsync(e => e.Type == LedgerEntryType.Credit && e.CustomerId == customerId);
            credit.Amount.Should().Be(-271.40m);
            credit.InvoiceId.Should().Be(invoice.Id);

            // Stock returned: the item line's issue (−2) and the note's receipt (+2) net to nothing.
            var movements = await db.StockMovements.Where(m => m.ItemId == itemId).ToListAsync();
            movements.Should().HaveCount(2);
            movements.Sum(m => m.Quantity).Should().Be(0m);
            movements.Should().ContainSingle(m => m.Type == StockMovementType.Receipt && m.Quantity == 2m);

            // A version-1 snapshot exists.
            var versions = await db.DocumentVersions
                .Where(v => v.DocType == DocumentTypes.CreditNote && v.DocId == note.Id)
                .ToListAsync();
            versions.Should().ContainSingle().Which.VersionNo.Should().Be(1);

            // The legacy shadow was written — the NOT NULL columns prove the insert set them.
            var (legacyTotal, legacyStock, legacyInvoiceNo) = await db.Database
                .SqlQuery<LegacyCn>($"SELECT totamount AS Total, stockposting AS Stock, invoiceno AS InvoiceNo FROM cn_h WHERE id = {note.Id}")
                .SingleAsync();
            legacyTotal.Should().Be("271.40");
            legacyStock.Should().Be("1");
            legacyInvoiceNo.Should().Be(invoice.Number);
        }
    }

    [Fact]
    public async Task A_credit_note_that_does_not_return_stock_leaves_stock_untouched()
    {
        var (companyId, customerId, itemId) = await SeedCompanyCustomerItemAndSeries(vatRegistered: true);
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };

        var invoice = await CreateParentInvoice(change, companyId, customerId, itemId);

        await using (var db = _fixture.CreateContext(change))
        {
            await CreatorFor(db, change).CreateAsync(new NewCreditNote(
                companyId, customerId, invoice.Id, invoice.Number, new DateOnly(2026, 7, 20),
                ReturnsStock: false, TaxRateId: null, TaxRatePercentage: 18m,
                [new NewCreditNoteLine(itemId, "I-1", "Widget", 2m, 100m, 10m, Cost: 120m)]));
        }

        await using (var db = _fixture.CreateContext(change))
        {
            // The receivable is still credited...
            (await db.ReceivablesLedger.CountAsync(e => e.Type == LedgerEntryType.Credit && e.CustomerId == customerId)).Should().Be(1);

            // ...but no stock came back: only the invoice's original issue exists, no receipt.
            var movements = await db.StockMovements.Where(m => m.ItemId == itemId).ToListAsync();
            movements.Should().ContainSingle().Which.Type.Should().Be(StockMovementType.Issue);
        }
    }

    [Fact]
    public async Task A_credit_note_inherits_a_legacy_parents_rate_without_a_rate_row()
    {
        // A legacy parent invoice has no tax_rate_id, only a stored vper. The controller passes that
        // percentage with a null id; the note must save, carrying the null id and the inherited rate — and
        // the engine, handed no rate table, still applies 18% because the override drives it.
        var (companyId, customerId, itemId) = await SeedCompanyCustomerItemAndSeries(vatRegistered: true);
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };

        // A real parent invoice, so the ledger's invoice_id foreign key is satisfied — the point under test
        // is the null rate id the controller supplies for a legacy parent, not the parent's own origin.
        var invoice = await CreateParentInvoice(change, companyId, customerId, itemId);

        CreditNoteCreated note;
        await using (var db = _fixture.CreateContext(change))
        {
            // A service-only credit for 1 × 200 at the parent's 18%: net 200, VAT 36, total 236.
            note = await CreatorFor(db, change).CreateAsync(new NewCreditNote(
                companyId, customerId, invoice.Id, invoice.Number, new DateOnly(2026, 7, 20),
                ReturnsStock: false, TaxRateId: null, TaxRatePercentage: 18m,
                [new NewCreditNoteLine(null, null, "Overcharge adjustment", 1m, 200m, 0m, Cost: null)]));
        }

        note.Total.Should().Be(236m);

        await using (var db = _fixture.CreateContext(change))
        {
            var saved = await db.CreditNotes.FirstAsync(c => c.Id == note.Id);
            saved.TaxRateId.Should().BeNull(); // a legacy parent has no rate row
            saved.TaxRatePercentage.Should().Be(18m);
            saved.InvoiceId.Should().Be(invoice.Id);

            // The parent invoice charged 271.40; this 236 credit reduces the customer's balance to 35.40.
            (await new ReceivablesLedger(db).BalanceForCustomerAsync(customerId)).Should().Be(35.40m);
        }
    }

    private sealed record LegacyCn(string Total, string Stock, string InvoiceNo);

    // --- Seeding ---------------------------------------------------------------------------------

    private async Task<InvoiceCreated> CreateParentInvoice(FakeChangeContext change, long companyId, long customerId, long itemId)
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

    private static CreditNoteCreator CreatorFor(TestDbContext db, FakeChangeContext change) => new(
        db,
        new TaxEngine(),
        new DocumentNumberAllocator(db),
        new DocumentVersionWriter(db, change, Clock),
        new GeneralLedger(db),
        new BusinessRuleReader(db),
        change,
        Clock);

    private async Task<(long CompanyId, long CustomerId, long ItemId)> SeedCompanyCustomerItemAndSeries(bool vatRegistered)
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext { UserId = 1 });

        var company = new Company { Name = "Smart Net (test)", VatCode = "1", IsVatRegistered = vatRegistered };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        if (vatRegistered)
        {
            db.TaxRates.Add(new TaxRate
            {
                CompanyId = company.Id,
                Name = "VAT 18%",
                Percentage = 18m,
                EffectiveFrom = new DateOnly(2024, 1, 1),
                IsDefault = true,
            });
        }

        var customer = new Customer { Code = $"C-{company.Id}", Name = "Acme" };
        db.Customers.Add(customer);

        var item = new Item { Code = $"I-{company.Id}", Name = "Widget", Cost = 60m, SellingPrice = 100m };
        db.Items.Add(item);

        // Two series: a credit-note series (the document under test) and an invoice series (its parent).
        db.DocumentSeries.Add(new DocumentSeries
        {
            CompanyId = company.Id, DocType = DocumentTypes.CreditNote, Prefix = $"CN{company.Id}-", NextNumber = 800, Padding = 0,
        });
        db.DocumentSeries.Add(new DocumentSeries
        {
            CompanyId = company.Id, DocType = DocumentTypes.Invoice, Prefix = $"INV{company.Id}-", NextNumber = 1215, Padding = 0,
        });

        await db.SaveChangesAsync();
        return (company.Id, customer.Id, item.Id);
    }
}
