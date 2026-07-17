using System.Globalization;
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
using Smartnet.Infrastructure.Persistence;
using Smartnet.Infrastructure.Settings;
using Smartnet.Tests.Auditing;

namespace Smartnet.Tests.Documents;

/// <summary>
/// Quotations (Phase 5, slice 3): the same engine as invoices, given a document that charges nothing and
/// issues nothing, plus a conversion that is done correctly — once, through the real invoice pipeline,
/// with the two documents linked. The bugs closed here are the re-runnable, unlinked legacy conversion
/// (plan §6).
/// </summary>
[Collection(nameof(AuditCollection))]
public sealed class QuotationTests
{
    private readonly AuditFixture _fixture;
    private static readonly TimeProvider Clock = TimeProvider.System;

    public QuotationTests(AuditFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_quotation_totals_correctly_snapshots_itself_and_touches_no_ledger_or_stock()
    {
        var (companyId, customerId, itemId) = await SeedCompanyCustomerItemAndSeries(vatRegistered: true);
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };

        QuotationCreated created;
        await using (var db = _fixture.CreateContext(change))
        {
            // The same lines as the invoice acceptance test: an item 2 × 100 less 10% (net 180) and a
            // service 1 × 50 (net 50). Total 271.40 at 18% VAT — but nothing is charged or issued.
            created = await CreatorFor(db, change).CreateAsync(new NewQuotation(
                companyId, customerId, new DateOnly(2026, 7, 15), ContactPerson: "Mr Khan", Validity: "30 Days",
                Lines:
                [
                    new NewQuotationLine(itemId, "I-1", "Widget", 2m, 100m, 10m, Cost: 120m),
                    new NewQuotationLine(null, null, "Labour", 1m, 50m, 0m, Cost: null),
                ]));
        }

        created.Number.Should().Be($"QTN{companyId}-500");
        created.Total.Should().Be(271.40m);

        await using (var db = _fixture.CreateContext(change))
        {
            var quotation = await db.Quotations.Include(q => q.Lines).FirstAsync(q => q.Id == created.Id);

            quotation.Subtotal.Should().Be(250m);
            quotation.NetTotal.Should().Be(230m);
            quotation.TaxAmount.Should().Be(41.40m);
            quotation.Total.Should().Be(271.40m);
            quotation.TaxRatePercentage.Should().Be(18m);
            quotation.Validity.Should().Be("30 Days");
            quotation.IsConverted.Should().BeFalse();
            quotation.Lines.Should().HaveCount(2);

            // The two things a quotation must NOT do: no ledger entry, no stock movement.
            (await new ReceivablesLedger(db).BalanceForCustomerAsync(customerId)).Should().Be(0m);
            (await db.StockMovements.CountAsync(m => m.ItemId == itemId)).Should().Be(0);

            // A version-1 snapshot exists, and the audit log recorded the creation.
            var versions = await db.DocumentVersions
                .Where(v => v.DocType == DocumentTypes.Quotation && v.DocId == created.Id)
                .ToListAsync();
            versions.Should().ContainSingle().Which.VersionNo.Should().Be(1);

            // The legacy shadow was written — the NOT NULL columns prove the insert set them, and the
            // line total column is `total` (not the invoice's `tot`).
            var legacyTotal = await db.Database
                .SqlQuery<string>($"SELECT totamount AS Value FROM quotation_h WHERE id = {created.Id}")
                .SingleAsync();
            legacyTotal.Should().Be("271.40");

            var legacyLineTotal = await db.Database
                .SqlQuery<string>($"SELECT total AS Value FROM quotation_l WHERE qno = {created.Number} ORDER BY id")
                .FirstAsync();
            legacyLineTotal.Should().Be("200"); // the item line gross, 2 × 100
        }
    }

    [Fact]
    public async Task Converting_a_quotation_raises_an_invoice_through_the_real_pipeline_and_links_both()
    {
        var (companyId, customerId, itemId) = await SeedCompanyCustomerItemAndSeries(vatRegistered: true);
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };

        long quotationId;
        await using (var db = _fixture.CreateContext(change))
        {
            var quote = await CreatorFor(db, change).CreateAsync(new NewQuotation(
                companyId, customerId, new DateOnly(2026, 7, 15), null, "30 Days",
                [new NewQuotationLine(itemId, "I-1", "Widget", 2m, 100m, 10m, Cost: 120m)]));
            quotationId = quote.Id;
        }

        InvoiceCreated invoice;
        await using (var db = _fixture.CreateContext(change))
        {
            invoice = await ConverterFor(db, change).ConvertAsync(
                quotationId,
                new ConvertQuotation(InvoiceType.Credit, new DateOnly(2026, 8, 1), "PO-7", null));
        }

        await using (var db = _fixture.CreateContext(change))
        {
            // The invoice is a real one: it got its own number from the invoice series (not the quote's),
            // a ledger charge and a stock issue — none of which the legacy copy-paste conversion produced.
            invoice.Number.Should().Be($"INV{companyId}-1215");
            invoice.Total.Should().Be(212.40m); // 180 net × 18%

            (await new ReceivablesLedger(db).BalanceForCustomerAsync(customerId)).Should().Be(212.40m);
            (await db.StockMovements.CountAsync(m => m.ItemId == itemId && m.Type == StockMovementType.Issue))
                .Should().Be(1);

            // The two documents point at each other.
            var savedInvoice = await db.Invoices.FirstAsync(i => i.Id == invoice.Id);
            savedInvoice.SourceQuotationId.Should().Be(quotationId);

            var savedQuote = await db.Quotations.FirstAsync(q => q.Id == quotationId);
            savedQuote.ConvertedToInvoiceId.Should().Be(invoice.Id);
            savedQuote.IsConverted.Should().BeTrue();
            savedQuote.ConvertedBy.Should().Be(1);
        }
    }

    [Fact]
    public async Task A_second_conversion_of_the_same_quotation_is_refused()
    {
        var (companyId, customerId, itemId) = await SeedCompanyCustomerItemAndSeries(vatRegistered: true);
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };

        long quotationId;
        await using (var db = _fixture.CreateContext(change))
        {
            var quote = await CreatorFor(db, change).CreateAsync(new NewQuotation(
                companyId, customerId, new DateOnly(2026, 7, 15), null, "30 Days",
                [new NewQuotationLine(itemId, "I-1", "Widget", 1m, 100m, 0m, Cost: 60m)]));
            quotationId = quote.Id;
        }

        await using (var db = _fixture.CreateContext(change))
        {
            await ConverterFor(db, change).ConvertAsync(
                quotationId, new ConvertQuotation(InvoiceType.Cash, new DateOnly(2026, 8, 1), null, null));
        }

        // The second attempt is refused — the quote is spent.
        await using (var db = _fixture.CreateContext(change))
        {
            var act = () => ConverterFor(db, change).ConvertAsync(
                quotationId, new ConvertQuotation(InvoiceType.Cash, new DateOnly(2026, 8, 2), null, null));

            await act.Should().ThrowAsync<QuotationAlreadyConvertedException>();
        }

        // And only ONE invoice exists for this customer — stock was issued once, not twice (the legacy bug).
        await using (var db = _fixture.CreateContext(change))
        {
            (await db.Invoices.CountAsync(i => i.CustomerId == customerId)).Should().Be(1);
            (await db.StockMovements.CountAsync(m => m.ItemId == itemId && m.Type == StockMovementType.Issue))
                .Should().Be(1);
        }
    }

    [Fact]
    public async Task A_legacy_quotation_converts_to_a_real_invoice_and_is_then_spent()
    {
        var (companyId, customerId, itemId) = await SeedCompanyCustomerItemAndSeries(vatRegistered: true);
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };

        // Seed a legacy quotation directly, as the old app wrote it — its money in varchar columns, its
        // customer and item as codes, data_origin 'legacy', with company_id backfilled (as the
        // multi-company migration does) so the new entity can carry it.
        var qno = $"STQ-{companyId}";
        long legacyQuotationId;
        await using (var db = _fixture.CreateContext(change))
        {
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO quotation_h
                  (q_no, qdate, customer, totamount, novattotal, beforedisctot, discountper, vper, it,
                   company, contactperson, q_valid, quotecost, vtype, preparedby, cdatetime, company_id, data_origin)
                VALUES
                  ({qno}, '2024-05-01', {"C-" + companyId}, '236', '200', '200', '0', '18', 'ITEM',
                   {companyId.ToString(CultureInfo.InvariantCulture)}, 'Mr Legacy', '30 Days', '120', '1',
                   'Old User', '2024-05-01 10:00:00', {companyId}, 'legacy')
                """);

            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO quotation_l (qno, itemcode, `desc`, qty, rate, total)
                VALUES ({qno}, {"I-" + companyId}, 'Widget', '2', '100', '200')
                """);

            legacyQuotationId = await db.Database
                .SqlQuery<long>($"SELECT id AS Value FROM quotation_h WHERE q_no = {qno}")
                .SingleAsync();
        }

        InvoiceCreated invoice;
        await using (var db = _fixture.CreateContext(change))
        {
            invoice = await ConverterFor(db, change).ConvertAsync(
                legacyQuotationId, new ConvertQuotation(InvoiceType.Credit, new DateOnly(2026, 8, 1), null, null));
        }

        await using (var db = _fixture.CreateContext(change))
        {
            // A real invoice, re-valued at its own date: 2 × 100 net 200, VAT 36, total 236 — with a ledger
            // charge and, because the line's item code still exists, a stock issue.
            invoice.Number.Should().Be($"INV{companyId}-1215");
            invoice.Total.Should().Be(236m);
            (await new ReceivablesLedger(db).BalanceForCustomerAsync(customerId)).Should().Be(236m);
            (await db.StockMovements.CountAsync(m => m.ItemId == itemId && m.Type == StockMovementType.Issue))
                .Should().Be(1);

            // The two documents point at each other — the back-link the legacy conversion never had.
            (await db.Invoices.FirstAsync(i => i.Id == invoice.Id)).SourceQuotationId.Should().Be(legacyQuotationId);
            var convertedTo = await db.Database
                .SqlQuery<long?>($"SELECT converted_to_invoice_id AS Value FROM quotation_h WHERE id = {legacyQuotationId}")
                .SingleAsync();
            convertedTo.Should().Be(invoice.Id);
        }

        // A second conversion is refused — the legacy quote is spent, just like a new one.
        await using (var db = _fixture.CreateContext(change))
        {
            var act = () => ConverterFor(db, change).ConvertAsync(
                legacyQuotationId, new ConvertQuotation(InvoiceType.Credit, new DateOnly(2026, 8, 2), null, null));
            await act.Should().ThrowAsync<QuotationAlreadyConvertedException>();
        }
    }

    // --- Seeding ---------------------------------------------------------------------------------

    private static QuotationCreator CreatorFor(TestDbContext db, FakeChangeContext change) => new(
        db,
        new TaxEngine(),
        new DocumentNumberAllocator(db),
        new DocumentVersionWriter(db, change, Clock),
        new BusinessRuleReader(db),
        change,
        Clock);

    private QuotationConverter ConverterFor(TestDbContext db, FakeChangeContext change) => new(
        db,
        LegacyContext(),
        new InvoiceCreator(
            db,
            new TaxEngine(),
            new DocumentNumberAllocator(db),
            new DocumentVersionWriter(db, change, Clock),
            new ReceivablesLedger(db), new GeneralLedger(db),
            new BusinessRuleReader(db),
            change,
            Clock),
        change,
        Clock);

    /// <summary>A legacy read-model context on the same throwaway database — used by the converter's legacy path.</summary>
    private SmartnetLegacyDbContext LegacyContext() => new(
        new DbContextOptionsBuilder<SmartnetLegacyDbContext>()
            .UseMySql(_fixture.ConnectionString, SmartnetServerVersion.Value)
            .Options);

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

        // Two series: a quotation series (the document under test) and an invoice series (for conversion).
        db.DocumentSeries.Add(new DocumentSeries
        {
            CompanyId = company.Id, DocType = DocumentTypes.Quotation, Prefix = $"QTN{company.Id}-", NextNumber = 500, Padding = 0,
        });
        db.DocumentSeries.Add(new DocumentSeries
        {
            CompanyId = company.Id, DocType = DocumentTypes.Invoice, Prefix = $"INV{company.Id}-", NextNumber = 1215, Padding = 0,
        });

        await db.SaveChangesAsync();
        return (company.Id, customer.Id, item.Id);
    }
}
