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
/// Service-cost capture (2026-07-16): the legacy app captured a document-level cost for service invoices
/// and service-quote conversions (item cost is derived from the item master). The new per-line model lost
/// it for the service flow; this restores it as a document-level <c>Cost</c> — entered on a service
/// document, summed from the item lines otherwise, and carried through conversion (no re-entry).
/// </summary>
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
    public async Task An_item_invoice_still_sums_its_line_costs_when_no_document_cost_is_given()
    {
        var (companyId, customerId, itemId) = await Seed();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };

        InvoiceCreated created;
        await using (var db = _fixture.CreateContext(change))
        {
            created = await InvoiceCreatorFor(db, change).CreateAsync(new NewInvoice(
                companyId, customerId, InvoiceType.Credit, new DateOnly(2026, 7, 16), null, null,
                [new NewInvoiceLine(itemId, "I-1", "Widget", 2m, 100m, 0m, Cost: 60m)]));
        }

        await using (var db = _fixture.CreateContext(change))
        {
            // No DocumentCost → the item flow is unchanged: cost = Σ line costs.
            (await db.Invoices.FirstAsync(i => i.Id == created.Id)).Cost.Should().Be(60m);
        }
    }

    [Fact]
    public async Task Converting_a_service_quotation_carries_its_document_cost_to_the_invoice()
    {
        var (companyId, customerId, _) = await Seed();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };

        long quotationId;
        await using (var db = _fixture.CreateContext(change))
        {
            var quote = await QuotationCreatorFor(db, change).CreateAsync(new NewQuotation(
                companyId, customerId, new DateOnly(2026, 7, 15), null, "30 Days",
                [new NewQuotationLine(null, null, "Consulting", 1m, 100m, 0m, Cost: null)],
                DocumentCost: 40m));
            quotationId = quote.Id;
        }

        InvoiceCreated invoice;
        await using (var db = _fixture.CreateContext(change))
        {
            invoice = await ConverterFor(db, change).ConvertAsync(
                quotationId, new ConvertQuotation(InvoiceType.Credit, new DateOnly(2026, 8, 1), null, null));
        }

        await using (var db = _fixture.CreateContext(change))
        {
            // The service cost carried through — no convert-time re-entry (the legacy convert box is gone).
            (await db.Invoices.FirstAsync(i => i.Id == invoice.Id)).Cost.Should().Be(40m);
        }
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
