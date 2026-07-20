using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Documents;
using Smartnet.Domain.MasterData;
using Smartnet.Domain.Settings;
using Smartnet.Infrastructure.Auditing;
using Smartnet.Infrastructure.Documents;
using Smartnet.Infrastructure.Numbering;
using Smartnet.Infrastructure.Settings;
using Smartnet.Tests.Auditing;

namespace Smartnet.Tests.Documents;

/// <summary>
/// Purchase orders (Phase 6, slice 1): the quotation engine addressed to a supplier — a document that
/// <b>charges nothing and issues nothing</b>. It proves the Phase 6 adoption template (a new document type
/// on <c>po_h</c>/<c>po_l</c>) and that a PO records the order without moving stock or posting a payable:
/// the stock receipt is the deferred GRN, the payable is the supplier invoice.
/// </summary>
[Collection(nameof(AuditCollection))]
public sealed class PurchaseOrderTests
{
    private readonly AuditFixture _fixture;
    private static readonly TimeProvider Clock = TimeProvider.System;

    public PurchaseOrderTests(AuditFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_purchase_order_totals_correctly_snapshots_itself_and_touches_no_stock()
    {
        var (companyId, supplierId, itemId) = await SeedCompanySupplierItemAndSeries(vatRegistered: true);
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };

        PurchaseOrderCreated created;
        await using (var db = _fixture.CreateContext(change))
        {
            // The same lines as the invoice/quotation acceptance tests: an item 2 × 100 less 10% (net 180)
            // and a service 1 × 50 (net 50). Total 271.40 at 18% VAT — but nothing is received or owed yet.
            created = await CreatorFor(db, change).CreateAsync(new NewPurchaseOrder(
                companyId, supplierId, new DateOnly(2026, 7, 15),
                Lines:
                [
                    new NewPurchaseOrderLine(itemId, "I-1", "Widget", 2m, 100m, 10m, Cost: 120m),
                    new NewPurchaseOrderLine(null, null, "Labour", 1m, 50m, 0m, Cost: null),
                ]));
        }

        created.Number.Should().Be($"PO{companyId}-700");
        created.Total.Should().Be(271.40m);

        await using (var db = _fixture.CreateContext(change))
        {
            var order = await db.PurchaseOrders.Include(p => p.Lines).FirstAsync(p => p.Id == created.Id);

            order.Subtotal.Should().Be(250m);
            order.NetTotal.Should().Be(230m);
            order.TaxAmount.Should().Be(41.40m);
            order.Total.Should().Be(271.40m);
            order.TaxRatePercentage.Should().Be(18m);
            // 240: the item line's UNIT cost of 120, times its quantity of 2. The service line carries no
            // cost and contributes nothing.
            //
            // This asserted 120 until 2026-07-20 — the quantity was missing from the cost basis in all seven
            // places that computed it (see DocumentCostBasis), so every document understated its cost by a
            // factor of the quantity and overstated its margin to match.
            order.Cost.Should().Be(240m);
            order.Lines.Should().HaveCount(2);

            // The item line carries its item linkage (so the future GRN can receive against it); the
            // service line carries neither item nor cost.
            var itemLine = order.Lines.Single(l => l.ItemId != null);
            itemLine.ItemId.Should().Be(itemId);
            itemLine.ItemCode.Should().Be("I-1");
            itemLine.Cost.Should().Be(120m);
            order.Lines.Single(l => l.ItemId == null).Description.Should().Be("Labour");

            // The one thing a PO must NOT do: move stock. (There is no supplier ledger in slice 1 either.)
            (await db.StockMovements.CountAsync(m => m.ItemId == itemId)).Should().Be(0);

            // A version-1 snapshot exists, and the audit log recorded the creation.
            var versions = await db.DocumentVersions
                .Where(v => v.DocType == DocumentTypes.PurchaseOrder && v.DocId == created.Id)
                .ToListAsync();
            versions.Should().ContainSingle().Which.VersionNo.Should().Be(1);

            // The legacy shadow was written — the totals for the surviving legacy readers (SearchPO), and
            // the line total column is `total` (as on quotation_l).
            var legacyTotal = await db.Database
                .SqlQuery<string>($"SELECT totamount AS Value FROM po_h WHERE id = {created.Id}")
                .SingleAsync();
            legacyTotal.Should().Be("271.40");

            var legacyLineTotal = await db.Database
                .SqlQuery<string>($"SELECT total AS Value FROM po_l WHERE pono = {created.Number} ORDER BY id")
                .FirstAsync();
            legacyLineTotal.Should().Be("200"); // the item line gross, 2 × 100
        }
    }

    [Fact]
    public async Task A_service_only_purchase_order_carries_no_item_and_no_stock()
    {
        var (companyId, supplierId, itemId) = await SeedCompanySupplierItemAndSeries(vatRegistered: false);
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };

        PurchaseOrderCreated created;
        await using (var db = _fixture.CreateContext(change))
        {
            created = await CreatorFor(db, change).CreateAsync(new NewPurchaseOrder(
                companyId, supplierId, new DateOnly(2026, 7, 15),
                [new NewPurchaseOrderLine(null, null, "Consultancy", 3m, 100m, 0m, Cost: null)]));
        }

        // Not VAT-registered → zero VAT; a service-only PO moves no stock and carries no item.
        created.Total.Should().Be(300m);

        await using (var db = _fixture.CreateContext(change))
        {
            var order = await db.PurchaseOrders.Include(p => p.Lines).FirstAsync(p => p.Id == created.Id);
            order.TaxAmount.Should().Be(0m);
            order.TaxRatePercentage.Should().Be(0m);
            order.Lines.Should().ContainSingle().Which.ItemId.Should().BeNull();
            (await db.StockMovements.CountAsync(m => m.ItemId == itemId)).Should().Be(0);
        }
    }

    // --- Seeding ---------------------------------------------------------------------------------

    private static PurchaseOrderCreator CreatorFor(TestDbContext db, FakeChangeContext change) => new(
        db,
        new TaxEngine(),
        new DocumentNumberAllocator(db),
        new DocumentVersionWriter(db, change, Clock),
        new BusinessRuleReader(db),
        change,
        Clock);

    private async Task<(long CompanyId, long SupplierId, long ItemId)> SeedCompanySupplierItemAndSeries(bool vatRegistered)
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

        var supplier = new Supplier { Code = $"S-{company.Id}", Name = "Widgets Ltd" };
        db.Suppliers.Add(supplier);

        var item = new Item { Code = $"I-{company.Id}", Name = "Widget", Cost = 60m, SellingPrice = 100m };
        db.Items.Add(item);

        db.DocumentSeries.Add(new DocumentSeries
        {
            CompanyId = company.Id, DocType = DocumentTypes.PurchaseOrder, Prefix = $"PO{company.Id}-", NextNumber = 700, Padding = 0,
        });

        await db.SaveChangesAsync();
        return (company.Id, supplier.Id, item.Id);
    }
}
