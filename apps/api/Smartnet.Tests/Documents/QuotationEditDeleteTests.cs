using System.Globalization;
using Microsoft.EntityFrameworkCore;
using FluentAssertions;
using Smartnet.Domain.Documents;
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
/// Quotation edit and void (Phase 5, slice 5, legacy parity): versioned, reason-gated, concurrency-guarded,
/// and — the mirror of the invoice work — no ledger and no stock. A converted quote is spent and cannot be
/// edited; a legacy quote is adopted into the new model on first edit or void.
/// </summary>
[Collection(nameof(AuditCollection))]
public sealed class QuotationEditDeleteTests
{
    private readonly AuditFixture _fixture;
    private static readonly TimeProvider Clock = TimeProvider.System;

    public QuotationEditDeleteTests(AuditFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Editing_a_quotation_writes_a_new_version_and_recomputes()
    {
        var (companyId, customerId, itemId, itemCode) = await Seed();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId, Reason = "Customer wants more units quoted" };

        var created = await CreateQuotation(change, companyId, customerId, itemId, itemCode);

        long lineId;
        int rowVersion;
        await using (var db = _fixture.CreateContext(change))
        {
            var q = await db.Quotations.Include(x => x.Lines).FirstAsync(x => x.Id == created.Id);
            lineId = q.Lines.Single().Id;
            rowVersion = q.RowVersion;
        }

        // 2 × 100 → 4 × 100: net 400, VAT 72, total 472.
        QuotationEdited edited;
        await using (var db = _fixture.CreateContext(change))
        {
            edited = await EditorFor(db, change).EditAsync(created.Id, new EditQuotation(
                rowVersion, ContactPerson: "Mr Khan", Validity: "45 Days", DocumentDiscountPercent: 0m,
                [new EditQuotationLine(lineId, itemId, itemCode, "Widget", 4m, 100m, 0m, Cost: 240m)]));
        }

        edited.Total.Should().Be(472m);
        edited.VersionNo.Should().Be(2);

        await using (var db = _fixture.CreateContext(change))
        {
            var q = await db.Quotations.Include(x => x.Lines).FirstAsync(x => x.Id == created.Id);
            q.Total.Should().Be(472m);
            q.Validity.Should().Be("45 Days");
            q.Lines.Single(l => l.DeletedAt == null).Quantity.Should().Be(4m);
            (await db.DocumentVersions.CountAsync(v => v.DocType == DocumentTypes.Quotation && v.DocId == created.Id))
                .Should().Be(2);
        }
    }

    [Fact]
    public async Task A_converted_quotation_cannot_be_edited()
    {
        var (companyId, customerId, itemId, itemCode) = await Seed();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId, Reason = "Trying to edit a converted quote" };

        var created = await CreateQuotation(change, companyId, customerId, itemId, itemCode);

        long lineId;
        int rowVersion;
        await using (var db = _fixture.CreateContext(change))
        {
            var q = await db.Quotations.Include(x => x.Lines).FirstAsync(x => x.Id == created.Id);
            lineId = q.Lines.Single().Id;
            rowVersion = q.RowVersion;
        }

        await using (var db = _fixture.CreateContext(change))
        {
            await ConverterFor(db, change).ConvertAsync(created.Id, new ConvertQuotation(
                InvoiceType.Credit, new DateOnly(2026, 8, 1), null, null));
        }

        await using (var db = _fixture.CreateContext(change))
        {
            var act = () => EditorFor(db, change).EditAsync(created.Id, new EditQuotation(
                rowVersion, null, "30 Days", 0m,
                [new EditQuotationLine(lineId, itemId, itemCode, "Widget", 5m, 100m, 0m, 300m)]));

            await act.Should().ThrowAsync<QuotationAlreadyConvertedException>();
        }
    }

    [Fact]
    public async Task Voiding_a_quotation_soft_deletes_it()
    {
        var (companyId, customerId, itemId, itemCode) = await Seed();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId, Reason = "Quote raised in error, voiding it" };

        var created = await CreateQuotation(change, companyId, customerId, itemId, itemCode);

        int rowVersion;
        await using (var db = _fixture.CreateContext(change))
        {
            rowVersion = (await db.Quotations.FirstAsync(x => x.Id == created.Id)).RowVersion;
        }

        await using (var db = _fixture.CreateContext(change))
        {
            await new QuotationDeleter(db, AdopterFor(db, change), Clock).DeleteAsync(created.Id, rowVersion);
        }

        await using (var db = _fixture.CreateContext(change))
        {
            (await db.Quotations.AnyAsync(x => x.Id == created.Id)).Should().BeFalse();
            (await db.Quotations.IgnoreQueryFilters().FirstAsync(x => x.Id == created.Id)).DeletedAt.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task Editing_a_legacy_quotation_adopts_it_then_applies_the_edit()
    {
        var (companyId, customerId, itemId, itemCode) = await Seed();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId, Reason = "Correcting an old quotation" };

        var (quotationId, lineId, rowVersion) = await SeedLegacyQuotation(companyId, customerId);

        // Adoption values it at 236 (2×100 net 200, VAT 36); the edit takes it to 354 (3×100 net 300, VAT 54).
        QuotationEdited edited;
        await using (var db = _fixture.CreateContext(change))
        {
            edited = await EditorFor(db, change).EditAsync(quotationId, new EditQuotation(
                rowVersion, ContactPerson: "Mr Legacy", Validity: "30 Days", DocumentDiscountPercent: 0m,
                [new EditQuotationLine(lineId, itemId, itemCode, "Widget", 3m, 100m, 0m, Cost: 180m)]));
        }

        edited.Total.Should().Be(354m);

        await using (var db = _fixture.CreateContext(change))
        {
            var q = await db.Quotations.FirstAsync(x => x.Id == quotationId);
            q.DataOrigin.Should().Be("new"); // adopted
            q.Total.Should().Be(354m);
            q.CustomerId.Should().Be(customerId);

            var versions = await db.DocumentVersions
                .Where(v => v.DocType == DocumentTypes.Quotation && v.DocId == quotationId)
                .OrderBy(v => v.VersionNo).ToListAsync();
            versions.Should().HaveCount(2);
            versions[0].Reason.Should().Be("Adopted from the legacy system");
        }
    }

    // --- Seeding ---------------------------------------------------------------------------------

    private static QuotationEditor EditorFor(TestDbContext db, FakeChangeContext change) => new(
        db, new TaxEngine(), new DocumentVersionWriter(db, change, Clock), AdopterFor(db, change),
        new BusinessRuleReader(db), change);

    private static LegacyQuotationAdopter AdopterFor(TestDbContext db, FakeChangeContext change) => new(
        db, new TaxEngine(), new DocumentVersionWriter(db, change, Clock), new BusinessRuleReader(db));

    private QuotationConverter ConverterFor(TestDbContext db, FakeChangeContext change) => new(
        db, LegacyContext(),
        new InvoiceCreator(db, new TaxEngine(), new DocumentNumberAllocator(db),
            new DocumentVersionWriter(db, change, Clock), new ReceivablesLedger(db), new GeneralLedger(db),
            new BusinessRuleReader(db), change, Clock),
        change, Clock);

    private SmartnetLegacyDbContext LegacyContext() => new(
        new DbContextOptionsBuilder<SmartnetLegacyDbContext>()
            .UseMySql(_fixture.ConnectionString, SmartnetServerVersion.Value)
            .Options);

    private async Task<QuotationCreated> CreateQuotation(FakeChangeContext change, long companyId, long customerId, long itemId, string itemCode)
    {
        await using var db = _fixture.CreateContext(change);
        return await new QuotationCreator(
            db, new TaxEngine(), new DocumentNumberAllocator(db), new DocumentVersionWriter(db, change, Clock),
            new BusinessRuleReader(db), change, Clock)
            .CreateAsync(new NewQuotation(
                companyId, customerId, new DateOnly(2026, 7, 15), "Mr Khan", "30 Days",
                [new NewQuotationLine(itemId, itemCode, "Widget", 2m, 100m, 0m, Cost: 120m)]));
    }

    /// <summary>Seeds a legacy quotation (2 × 100 item line, total 236) and returns its ids and row_version.</summary>
    private async Task<(long QuotationId, long LineId, int RowVersion)> SeedLegacyQuotation(long companyId, long customerId)
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext { UserId = 1 });
        var qno = $"STQ-L{companyId}";

        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO quotation_h
              (q_no, qdate, customer, totamount, novattotal, beforedisctot, discountper, vper, it, company,
               contactperson, q_valid, quotecost, vtype, preparedby, cdatetime, company_id, data_origin)
            VALUES
              ({qno}, '2024-05-01', {"C-" + companyId}, '236', '200', '200', '0', '18', 'ITEM',
               {companyId.ToString(CultureInfo.InvariantCulture)}, 'Mr Legacy', '30 Days', '120', '1',
               'Old User', '2024-05-01 10:00:00', {companyId}, 'legacy')
            """);
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO quotation_l (qno, itemcode, `desc`, qty, rate, total)
            VALUES ({qno}, {"I-" + companyId}, 'Widget', '2', '100', '200')
            """);

        var quotationId = await db.Database.SqlQuery<long>($"SELECT id AS Value FROM quotation_h WHERE q_no = {qno}").SingleAsync();
        var lineId = await db.Database.SqlQuery<long>($"SELECT id AS Value FROM quotation_l WHERE qno = {qno} ORDER BY id").FirstAsync();
        var rowVersion = await db.Database.SqlQuery<int>($"SELECT row_version AS Value FROM quotation_h WHERE id = {quotationId}").SingleAsync();
        return (quotationId, lineId, rowVersion);
    }

    private async Task<(long CompanyId, long CustomerId, long ItemId, string ItemCode)> Seed()
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

        // One customer and one item, referenced by both the new quote and the legacy one (by code "C-{id}"
        // / "I-{id}"), so adoption resolves them.
        var customer = new Customer { Code = $"C-{company.Id}", Name = "Acme" };
        var item = new Item { Code = $"I-{company.Id}", Name = "Widget", Cost = 60m, SellingPrice = 100m };
        db.Customers.Add(customer);
        db.Items.Add(item);

        db.DocumentSeries.Add(new DocumentSeries
        {
            CompanyId = company.Id, DocType = DocumentTypes.Quotation, Prefix = $"QTN{company.Id}-", NextNumber = 500, Padding = 0,
        });
        db.DocumentSeries.Add(new DocumentSeries
        {
            CompanyId = company.Id, DocType = DocumentTypes.Invoice, Prefix = $"INV{company.Id}-", NextNumber = 1215, Padding = 0,
        });

        await db.SaveChangesAsync();
        return (company.Id, customer.Id, item.Id, item.Code);
    }
}
