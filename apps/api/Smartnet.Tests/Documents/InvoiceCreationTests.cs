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
using Smartnet.Tests.Auditing;

namespace Smartnet.Tests.Documents;

/// <summary>
/// The non-negotiable test (DEVELOPMENT.md §9), end to end: an invoice at the company's VAT rate, with
/// a discount and — through the ledger — a partial payment, produces the correct total, the correct
/// derived balance, a correct audit record and a version-1 snapshot. That single case covers most of
/// what the legacy system got wrong.
/// </summary>
[Collection(nameof(AuditCollection))]
public sealed class InvoiceCreationTests
{
    private readonly AuditFixture _fixture;
    private static readonly TimeProvider Clock = TimeProvider.System;

    public InvoiceCreationTests(AuditFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task An_invoice_totals_correctly_charges_the_ledger_issues_stock_and_snapshots_itself()
    {
        var (companyId, customerId, itemId) = await SeedCompanyCustomerItemAndSeries(vatRegistered: true);
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };

        InvoiceCreated created;
        await using (var db = _fixture.CreateContext(change))
        {
            var creator = CreatorFor(db, change);

            // Line 1 — an item, 2 × 100 less 10%: net 180, VAT 32.40, and stock issued.
            // Line 2 — a service, 1 × 50: net 50, VAT 9.
            created = await creator.CreateAsync(new NewInvoice(
                companyId, customerId, InvoiceType.Credit, new DateOnly(2026, 7, 15),
                PurchaseOrderNo: "PO-99", ContactPerson: null,
                Lines:
                [
                    new NewInvoiceLine(itemId, "I-1", "Widget", 2m, 100m, 10m, Cost: 120m),
                    new NewInvoiceLine(null, null, "Labour", 1m, 50m, 0m, Cost: null),
                ]));
        }

        // The number came from the series, not from 1.
        created.Number.Should().Be($"INV{companyId}-1215");
        created.Total.Should().Be(271.40m);
        created.Outstanding.Should().Be(271.40m); // credit — nothing settled yet

        await using (var db = _fixture.CreateContext(change))
        {
            var invoice = await db.Invoices
                .Include(i => i.Lines)
                .FirstAsync(i => i.Id == created.Id);

            invoice.Subtotal.Should().Be(250m);
            invoice.NetTotal.Should().Be(230m);
            invoice.TaxAmount.Should().Be(41.40m);
            invoice.Total.Should().Be(271.40m);
            invoice.TaxRatePercentage.Should().Be(18m);
            invoice.DataOrigin.Should().Be("new");
            invoice.Lines.Should().HaveCount(2);

            // The receivable is charged, and the derived balance is the charge.
            (await new ReceivablesLedger(db).BalanceForCustomerAsync(customerId)).Should().Be(271.40m);

            // Stock was issued for the item line — one negative movement.
            var movements = await db.StockMovements.Where(m => m.ItemId == itemId).ToListAsync();
            movements.Should().ContainSingle()
                .Which.Should().Match<StockMovement>(m => m.Type == StockMovementType.Issue && m.Quantity == -2m);

            // A version-1 snapshot exists, and the audit log recorded the creation.
            var versions = await db.DocumentVersions
                .Where(v => v.DocType == DocumentTypes.Invoice && v.DocId == created.Id)
                .ToListAsync();
            versions.Should().ContainSingle().Which.VersionNo.Should().Be(1);

            var invoiceKey = created.Id.ToString(CultureInfo.InvariantCulture);
            (await db.AuditLog.AnyAsync(a => a.EntityType == "Invoice" && a.EntityId == invoiceKey))
                .Should().BeTrue();

            // The legacy shadow was written — the NOT NULL columns are proof the insert set them, and
            // the reports read totamount by name.
            var legacyTotal = await db.Database
                .SqlQuery<string>($"SELECT totamount AS Value FROM invoice_h WHERE id = {created.Id}")
                .SingleAsync();
            legacyTotal.Should().Be("271.40");
        }

        // A partial payment arrives — what Phase 7's payments module will record. The derived balance
        // must reflect it without any stored column being mutated (B3).
        await using (var db = _fixture.CreateContext(change))
        {
            db.ReceivablesLedger.Add(new LedgerEntry
            {
                CustomerId = customerId,
                Type = LedgerEntryType.Payment,
                Amount = -100m,
                InvoiceId = created.Id,
                OccurredAt = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc),
            });
            await db.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateContext(change))
        {
            (await new ReceivablesLedger(db).BalanceForCustomerAsync(customerId))
                .Should().Be(171.40m); // 271.40 charged − 100 paid
        }
    }

    [Fact]
    public async Task A_non_vat_registered_company_charges_no_tax()
    {
        var (companyId, customerId, _) = await SeedCompanyCustomerItemAndSeries(vatRegistered: false);
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };

        await using var db = _fixture.CreateContext(change);
        var created = await CreatorFor(db, change).CreateAsync(new NewInvoice(
            companyId, customerId, InvoiceType.Cash, new DateOnly(2026, 7, 15), null, null,
            [new NewInvoiceLine(null, null, "Labour", 1m, 1000m, 0m, null)]));

        // No VAT collected, and cash — settled at issue, so nothing outstanding.
        created.Total.Should().Be(1000m);
        created.Outstanding.Should().Be(0m);

        // Cash: a charge and a settling payment, netting to zero.
        (await new ReceivablesLedger(db).BalanceForCustomerAsync(customerId)).Should().Be(0m);
    }

    // --- Seeding ---------------------------------------------------------------------------------

    private static InvoiceCreator CreatorFor(TestDbContext db, FakeChangeContext change) => new(
        db,
        new TaxEngine(),
        new DocumentNumberAllocator(db),
        new DocumentVersionWriter(db, change, Clock),
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

        // A per-company prefix, because invoiceno has a GLOBAL unique index (UX_invoice_h_invoiceno,
        // Finding 9) — two companies both numbering "INV-1215" would collide, exactly as two real
        // companies do not because their prefixes differ (STI- vs SNI-).
        db.DocumentSeries.Add(new DocumentSeries
        {
            CompanyId = company.Id,
            DocType = DocumentTypes.Invoice,
            Prefix = $"INV{company.Id}-",
            NextNumber = 1215,
            Padding = 0,
        });

        await db.SaveChangesAsync();
        return (company.Id, customer.Id, item.Id);
    }
}
