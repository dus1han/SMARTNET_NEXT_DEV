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
/// Quotation → proforma → invoice: a proforma charges nothing, numbers per company, is one per quotation,
/// blocks the quotation's direct conversion while it lives, converts once, and frees the quotation when voided.
/// </summary>
[Collection(nameof(AuditCollection))]
public sealed class ProformaInvoiceTests
{
    private readonly AuditFixture _fixture;
    private static readonly TimeProvider Clock = TimeProvider.System;

    public ProformaInvoiceTests(AuditFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_proforma_carries_the_quotation_at_its_own_number_and_charges_nothing()
    {
        var (companyId, customerId, itemId) = await Seed();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };
        var quotationId = await RaiseQuotation(companyId, customerId, itemId, change);

        ProformaCreated created;
        await using (var db = _fixture.CreateContext(change))
        {
            created = await ServiceFor(db, change).CreateFromQuotationAsync(quotationId, new DateOnly(2026, 8, 1));
        }

        created.Number.Should().Be($"PI{companyId}-1");
        created.Total.Should().Be(212.40m); // 2 × 100 less 10% = 180, + 18% VAT

        await using (var db = _fixture.CreateContext(change))
        {
            var proforma = await db.ProformaInvoices.Include(p => p.Lines).SingleAsync(p => p.Id == created.Id);
            proforma.QuotationId.Should().Be(quotationId);
            proforma.Lines.Should().ContainSingle().Which.ItemId.Should().Be(itemId);

            // Nothing is charged or issued until it becomes an invoice.
            (await new ReceivablesLedger(db).BalanceForCustomerAsync(customerId)).Should().Be(0m);
            (await db.StockMovements.CountAsync(m => m.ItemId == itemId)).Should().Be(0);

            var quotation = await db.Quotations.SingleAsync(q => q.Id == quotationId);
            quotation.IsConverted.Should().BeFalse();
        }
    }

    [Fact]
    public async Task Each_company_numbers_its_proformas_separately()
    {
        var (first, firstCustomer, firstItem) = await Seed();
        var (second, secondCustomer, secondItem) = await Seed();

        var changeA = new FakeChangeContext { UserId = 1, CompanyId = first };
        var changeB = new FakeChangeContext { UserId = 1, CompanyId = second };
        var quoteA = await RaiseQuotation(first, firstCustomer, firstItem, changeA);
        var quoteB = await RaiseQuotation(second, secondCustomer, secondItem, changeB);

        await using var db = _fixture.CreateContext(changeA);
        var a = await ServiceFor(db, changeA).CreateFromQuotationAsync(quoteA, new DateOnly(2026, 8, 1));
        var b = await ServiceFor(db, changeB).CreateFromQuotationAsync(quoteB, new DateOnly(2026, 8, 1));

        // Both start at 1, each under its own company's prefix.
        a.Number.Should().Be($"PI{first}-1");
        b.Number.Should().Be($"PI{second}-1");
    }

    [Fact]
    public async Task A_live_proforma_blocks_a_second_one_and_the_quotations_direct_conversion()
    {
        var (companyId, customerId, itemId) = await Seed();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };
        var quotationId = await RaiseQuotation(companyId, customerId, itemId, change);

        await using (var db = _fixture.CreateContext(change))
        {
            await ServiceFor(db, change).CreateFromQuotationAsync(quotationId, new DateOnly(2026, 8, 1));
        }

        await using (var db = _fixture.CreateContext(change))
        {
            var second = () => ServiceFor(db, change).CreateFromQuotationAsync(quotationId, new DateOnly(2026, 8, 2));
            await second.Should().ThrowAsync<QuotationHasProformaException>();
        }

        await using (var db = _fixture.CreateContext(change))
        {
            var direct = () => ConverterFor(db, change).ConvertAsync(
                quotationId, new ConvertQuotation(InvoiceType.Credit, new DateOnly(2026, 8, 2), null, null));
            await direct.Should().ThrowAsync<QuotationHasProformaException>();
        }

        await using (var db = _fixture.CreateContext(change))
        {
            (await db.Invoices.CountAsync(i => i.CustomerId == customerId)).Should().Be(0);
        }
    }

    [Fact]
    public async Task Converting_a_proforma_raises_one_invoice_and_spends_both_the_proforma_and_the_quotation()
    {
        var (companyId, customerId, itemId) = await Seed();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };
        var quotationId = await RaiseQuotation(companyId, customerId, itemId, change);

        long proformaId;
        await using (var db = _fixture.CreateContext(change))
        {
            proformaId = (await ServiceFor(db, change).CreateFromQuotationAsync(quotationId, new DateOnly(2026, 8, 1))).Id;
        }

        InvoiceCreated invoice;
        await using (var db = _fixture.CreateContext(change))
        {
            invoice = await ServiceFor(db, change).ConvertToInvoiceAsync(
                proformaId, new ConvertQuotation(InvoiceType.Credit, new DateOnly(2026, 8, 5), "PO-1", null));
        }

        await using (var db = _fixture.CreateContext(change))
        {
            // A real invoice: its own number, the ledger charge, the stock issue.
            invoice.Number.Should().Be($"INV{companyId}-1215");
            invoice.Total.Should().Be(212.40m);
            (await new ReceivablesLedger(db).BalanceForCustomerAsync(customerId)).Should().Be(212.40m);
            (await db.StockMovements.CountAsync(m => m.ItemId == itemId && m.Type == StockMovementType.Issue)).Should().Be(1);

            // The chain links every way.
            (await db.ProformaInvoices.SingleAsync(p => p.Id == proformaId)).ConvertedToInvoiceId.Should().Be(invoice.Id);
            (await db.Quotations.SingleAsync(q => q.Id == quotationId)).ConvertedToInvoiceId.Should().Be(invoice.Id);
            (await db.Invoices.SingleAsync(i => i.Id == invoice.Id)).SourceQuotationId.Should().Be(quotationId);
        }

        // Once only — and a converted proforma cannot be voided.
        await using (var db = _fixture.CreateContext(change))
        {
            var again = () => ServiceFor(db, change).ConvertToInvoiceAsync(
                proformaId, new ConvertQuotation(InvoiceType.Credit, new DateOnly(2026, 8, 6), null, null));
            await again.Should().ThrowAsync<ProformaAlreadyConvertedException>();

            var rowVersion = (await db.ProformaInvoices.SingleAsync(p => p.Id == proformaId)).RowVersion;
            var voiding = () => ServiceFor(db, change).VoidAsync(proformaId, rowVersion);
            await voiding.Should().ThrowAsync<ProformaAlreadyConvertedException>();
        }
    }

    [Fact]
    public async Task Voiding_a_proforma_frees_its_quotation()
    {
        var (companyId, customerId, itemId) = await Seed();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };
        var quotationId = await RaiseQuotation(companyId, customerId, itemId, change);

        long proformaId;
        await using (var db = _fixture.CreateContext(change))
        {
            proformaId = (await ServiceFor(db, change).CreateFromQuotationAsync(quotationId, new DateOnly(2026, 8, 1))).Id;
        }

        await using (var db = _fixture.CreateContext(change))
        {
            var rowVersion = (await db.ProformaInvoices.SingleAsync(p => p.Id == proformaId)).RowVersion;
            await ServiceFor(db, change).VoidAsync(proformaId, rowVersion);
        }

        await using (var db = _fixture.CreateContext(change))
        {
            // Gone from ordinary reads, kept on record.
            (await db.ProformaInvoices.AnyAsync(p => p.Id == proformaId)).Should().BeFalse();
            (await db.ProformaInvoices.IgnoreQueryFilters().SingleAsync(p => p.Id == proformaId)).DeletedAt.Should().NotBeNull();

            // The quotation converts directly again.
            var invoice = await ConverterFor(db, change).ConvertAsync(
                quotationId, new ConvertQuotation(InvoiceType.Credit, new DateOnly(2026, 8, 2), null, null));
            invoice.Id.Should().BePositive();
        }
    }

    // --- Seeding ---------------------------------------------------------------------------------

    private ProformaInvoiceService ServiceFor(TestDbContext db, FakeChangeContext change) => new(
        db,
        LegacyContext(),
        new TaxEngine(),
        new DocumentNumberAllocator(db),
        new DocumentVersionWriter(db, change, Clock),
        new BusinessRuleReader(db),
        InvoiceCreatorFor(db, change),
        change,
        Clock);

    private QuotationConverter ConverterFor(TestDbContext db, FakeChangeContext change) =>
        new(db, LegacyContext(), InvoiceCreatorFor(db, change), change, Clock);

    private static InvoiceCreator InvoiceCreatorFor(TestDbContext db, FakeChangeContext change) => new(
        db,
        new TaxEngine(),
        new DocumentNumberAllocator(db),
        new DocumentVersionWriter(db, change, Clock),
        new ReceivablesLedger(db), new GeneralLedger(db),
        new BusinessRuleReader(db),
        change,
        Clock);

    private SmartnetLegacyDbContext LegacyContext() => new(
        new DbContextOptionsBuilder<SmartnetLegacyDbContext>()
            .UseMySql(_fixture.ConnectionString, SmartnetServerVersion.Value)
            .Options);

    /// <summary>A new quotation: an item line 2 × 100 less 10%.</summary>
    private async Task<long> RaiseQuotation(long companyId, long customerId, long itemId, FakeChangeContext change)
    {
        await using var db = _fixture.CreateContext(change);
        var quote = await new QuotationCreator(
                db, new TaxEngine(), new DocumentNumberAllocator(db), new DocumentVersionWriter(db, change, Clock),
                new BusinessRuleReader(db), change, Clock)
            .CreateAsync(new NewQuotation(
                companyId, customerId, new DateOnly(2026, 7, 15), null, "30 Days",
                [new NewQuotationLine(itemId, $"I-{companyId}", "Widget", 2m, 100m, 10m, Cost: 120m)]));
        return quote.Id;
    }

    private async Task<(long CompanyId, long CustomerId, long ItemId)> Seed()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext { UserId = 1 });

        var company = new Company { Name = "Smart Net (test)", VatCode = "1", IsVatRegistered = true };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        db.TaxRates.Add(new TaxRate
        {
            CompanyId = company.Id, Name = "VAT 18%", Percentage = 18m, EffectiveFrom = new DateOnly(2024, 1, 1), IsDefault = true,
        });

        var customer = new Customer { Code = $"C-{company.Id}", Name = "Acme" };
        db.Customers.Add(customer);

        var item = new Item { Code = $"I-{company.Id}", Name = "Widget", Cost = 60m, SellingPrice = 100m };
        db.Items.Add(item);

        db.DocumentSeries.AddRange(
            new DocumentSeries { CompanyId = company.Id, DocType = DocumentTypes.Quotation, Prefix = $"QTN{company.Id}-", NextNumber = 500 },
            new DocumentSeries { CompanyId = company.Id, DocType = DocumentTypes.Invoice, Prefix = $"INV{company.Id}-", NextNumber = 1215 },
            new DocumentSeries { CompanyId = company.Id, DocType = DocumentTypes.Proforma, Prefix = $"PI{company.Id}-", NextNumber = 1 });

        await db.SaveChangesAsync();
        return (company.Id, customer.Id, item.Id);
    }
}
