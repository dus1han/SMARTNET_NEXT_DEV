using FluentAssertions;
using Microsoft.EntityFrameworkCore;
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
/// Where a document's cost basis comes from, per kind.
/// </summary>
/// <remarks>
/// <para>
/// <b>Item</b> documents derive it: Σ (unit cost × quantity) over the lines, the unit cost coming off the
/// item master. <b>Service</b> documents cannot derive it, so it is entered — on a service invoice when the
/// invoice is raised, and on a service quotation <b>when it is converted</b>, not when it is quoted.
/// </para>
/// <para>
/// <b>This reverses the 2026-07-16 decision</b> recorded in the previous version of this file, which held
/// that a service quote's cost was captured up front and carried through with "no re-entry — the legacy
/// convert box is gone". The reasoning was sound but the premise was not: nothing ever <i>required</i> the
/// up-front figure, and <c>quotation_h.cost_amount</c> is <c>NOT NULL DEFAULT 0</c>, so "carried through"
/// in practice meant carrying a zero. An invoice with no cost does not report as incomplete, it reports as
/// 100% margin. The cost is now asked for at conversion and required there, which is both the point at
/// which it is knowable and the point at which refusing to proceed is cheap.
/// </para>
/// </remarks>
[Collection(nameof(AuditCollection))]
public sealed class ServiceCostCaptureTests
{
    private readonly AuditFixture _fixture;
    private static readonly TimeProvider Clock = TimeProvider.System;

    public ServiceCostCaptureTests(AuditFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_service_invoice_captures_the_document_cost_the_user_entered()
    {
        var (companyId, customerId, _) = await Seed();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };

        InvoiceCreated created;
        await using (var db = _fixture.CreateContext(change))
        {
            // A service invoice — a free-typed line (no item, so no per-line cost) with a document cost of 40.
            created = await InvoiceCreatorFor(db, change).CreateAsync(new NewInvoice(
                companyId, customerId, InvoiceType.Credit, new DateOnly(2026, 7, 16), null, null,
                [new NewInvoiceLine(null, null, "Consulting", 1m, 100m, 0m, Cost: null)],
                DocumentCost: 40m));
        }

        await using (var db = _fixture.CreateContext(change))
        {
            var invoice = await db.Invoices.FirstAsync(i => i.Id == created.Id);
            invoice.Cost.Should().Be(40m); // captured, not 0

            var legacyCost = await db.Database
                .SqlQuery<string>($"SELECT cost AS Value FROM invoice_h WHERE id = {created.Id}")
                .SingleAsync();
            legacyCost.Should().Be("40"); // dual-written for the legacy reports
        }
    }

    [Fact]
    public async Task An_item_invoice_multiplies_each_line_cost_by_its_quantity()
    {
        var (companyId, customerId, itemId) = await Seed();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };

        InvoiceCreated created;
        await using (var db = _fixture.CreateContext(change))
        {
            // Two widgets, each costing 60.
            created = await InvoiceCreatorFor(db, change).CreateAsync(new NewInvoice(
                companyId, customerId, InvoiceType.Credit, new DateOnly(2026, 7, 16), null, null,
                [new NewInvoiceLine(itemId, "I-1", "Widget", 2m, 100m, 0m, Cost: 60m)]));
        }

        await using (var db = _fixture.CreateContext(change))
        {
            // 120, not 60. Line Cost is a UNIT cost; the basis is Σ (unit cost × quantity).
            //
            // This asserted 60 until 2026-07-20, which is the bug it now guards: every creator and editor
            // summed the bare unit costs, so the recorded cost was short by a factor of the quantity and
            // margin read too generous everywhere it is shown. Nothing caught it because cost is never
            // posted to the ledger and never reconciled against anything.
            (await db.Invoices.FirstAsync(i => i.Id == created.Id)).Cost.Should().Be(120m);
        }
    }

    [Fact]
    public async Task Converting_a_service_quotation_without_a_cost_is_refused()
    {
        var (companyId, customerId, _) = await Seed();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };

        var quotationId = await ServiceQuotation(companyId, customerId, change);

        await using (var db = _fixture.CreateContext(change))
        {
            // No DocumentCost. The old behaviour was to fall back to the quote's stored figure — which for
            // a service quote is a NOT NULL DEFAULT 0 column, so the invoice would have been raised
            // reporting 100% margin, silently.
            var convert = async () => await ConverterFor(db, change).ConvertAsync(
                quotationId, new ConvertQuotation(InvoiceType.Credit, new DateOnly(2026, 8, 1), null, null));

            await convert.Should().ThrowAsync<ServiceCostRequiredException>();
        }

        await using (var db = _fixture.CreateContext(change))
        {
            // And the refusal is total: no invoice, and the quote is still unspent. The conversion runs in
            // one transaction, so a failure part-way cannot leave a quote marked converted with nothing to
            // show for it.
            var quotation = await db.Quotations.IgnoreQueryFilters().FirstAsync(q => q.Id == quotationId);
            quotation.ConvertedToInvoiceId.Should().BeNull();
        }
    }

    [Fact]
    public async Task Converting_a_service_quotation_uses_the_cost_entered_at_conversion()
    {
        var (companyId, customerId, _) = await Seed();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };

        var quotationId = await ServiceQuotation(companyId, customerId, change);

        InvoiceCreated invoice;
        await using (var db = _fixture.CreateContext(change))
        {
            invoice = await ConverterFor(db, change).ConvertAsync(
                quotationId,
                new ConvertQuotation(InvoiceType.Credit, new DateOnly(2026, 8, 1), null, null, DocumentCost: 55m));
        }

        await using (var db = _fixture.CreateContext(change))
        {
            (await db.Invoices.FirstAsync(i => i.Id == invoice.Id)).Cost.Should().Be(55m);
        }
    }

    [Fact]
    public async Task A_typed_zero_is_accepted_where_a_blank_is_not()
    {
        var (companyId, customerId, _) = await Seed();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };

        var quotationId = await ServiceQuotation(companyId, customerId, change);

        InvoiceCreated invoice;
        await using (var db = _fixture.CreateContext(change))
        {
            // Zero and null are the same number to a database and entirely different claims to a person: one
            // says "this cost nothing", the other says "nobody said". Only the first may pass.
            invoice = await ConverterFor(db, change).ConvertAsync(
                quotationId,
                new ConvertQuotation(InvoiceType.Credit, new DateOnly(2026, 8, 1), null, null, DocumentCost: 0m));
        }

        await using (var db = _fixture.CreateContext(change))
        {
            (await db.Invoices.FirstAsync(i => i.Id == invoice.Id)).Cost.Should().Be(0m);
        }
    }

    [Fact]
    public async Task Converting_an_item_quotation_needs_no_cost_and_derives_it_from_the_lines()
    {
        var (companyId, customerId, itemId) = await Seed();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };

        long quotationId;
        await using (var db = _fixture.CreateContext(change))
        {
            var quote = await QuotationCreatorFor(db, change).CreateAsync(new NewQuotation(
                companyId, customerId, new DateOnly(2026, 7, 15), null, "30 Days",
                [new NewQuotationLine(itemId, "I-1", "Widget", 3m, 100m, 0m, Cost: 60m)]));
            quotationId = quote.Id;
        }

        InvoiceCreated invoice;
        await using (var db = _fixture.CreateContext(change))
        {
            // No DocumentCost, and none needed: the item master is the source.
            invoice = await ConverterFor(db, change).ConvertAsync(
                quotationId, new ConvertQuotation(InvoiceType.Credit, new DateOnly(2026, 8, 1), null, null));
        }

        await using (var db = _fixture.CreateContext(change))
        {
            // 3 × 60.
            (await db.Invoices.FirstAsync(i => i.Id == invoice.Id)).Cost.Should().Be(180m);
        }
    }

    /// <summary>An all-service quotation, raised with no cost — which is now the only way to raise one.</summary>
    private async Task<long> ServiceQuotation(long companyId, long customerId, FakeChangeContext change)
    {
        await using var db = _fixture.CreateContext(change);
        var quote = await QuotationCreatorFor(db, change).CreateAsync(new NewQuotation(
            companyId, customerId, new DateOnly(2026, 7, 15), null, "30 Days",
            [new NewQuotationLine(null, null, "Consulting", 1m, 100m, 0m, Cost: null)]));
        return quote.Id;
    }

    // --- Seeding ---------------------------------------------------------------------------------

    private static InvoiceCreator InvoiceCreatorFor(TestDbContext db, FakeChangeContext change) => new(
        db,
        new TaxEngine(),
        new DocumentNumberAllocator(db),
        new DocumentVersionWriter(db, change, Clock),
        new ReceivablesLedger(db), new GeneralLedger(db),
        new BusinessRuleReader(db),
        change,
        Clock);

    private static QuotationCreator QuotationCreatorFor(TestDbContext db, FakeChangeContext change) => new(
        db,
        new TaxEngine(),
        new DocumentNumberAllocator(db),
        new DocumentVersionWriter(db, change, Clock),
        new BusinessRuleReader(db),
        change,
        Clock);

    private QuotationConverter ConverterFor(TestDbContext db, FakeChangeContext change) => new(
        db,
        new SmartnetLegacyDbContext(new DbContextOptionsBuilder<SmartnetLegacyDbContext>()
            .UseMySql(_fixture.ConnectionString, SmartnetServerVersion.Value).Options),
        InvoiceCreatorFor(db, change),
        change,
        Clock);

    private async Task<(long CompanyId, long CustomerId, long ItemId)> Seed()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext { UserId = 1 });

        var company = new Company { Name = "Smart Net (test)", VatCode = "1", IsVatRegistered = false };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        var customer = new Customer { Code = $"C-{company.Id}", Name = "Acme" };
        db.Customers.Add(customer);
        var item = new Item { Code = $"I-{company.Id}", Name = "Widget", Cost = 60m, SellingPrice = 100m };
        db.Items.Add(item);

        db.DocumentSeries.Add(new DocumentSeries
        {
            CompanyId = company.Id, DocType = DocumentTypes.Invoice, Prefix = $"INV{company.Id}-", NextNumber = 1, Padding = 0,
        });
        db.DocumentSeries.Add(new DocumentSeries
        {
            CompanyId = company.Id, DocType = DocumentTypes.Quotation, Prefix = $"QTN{company.Id}-", NextNumber = 1, Padding = 0,
        });

        await db.SaveChangesAsync();
        return (company.Id, customer.Id, item.Id);
    }
}
