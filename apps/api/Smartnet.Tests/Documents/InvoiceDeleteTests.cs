using System.Globalization;
using Microsoft.EntityFrameworkCore;
using FluentAssertions;
using Smartnet.Domain.Auditing;
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
/// The soft, recoverable, attributable invoice delete (Phase 5, slice 5): the void reverses the invoice's
/// ledger and stock through new entries and soft-deletes the row — it never erases history, never resets a
/// balance in place, and is rejected on a stale row_version.
/// </summary>
[Collection(nameof(AuditCollection))]
public sealed class InvoiceDeleteTests
{
    private readonly AuditFixture _fixture;
    private static readonly TimeProvider Clock = TimeProvider.System;

    public InvoiceDeleteTests(AuditFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Deleting_reverses_the_ledger_and_stock_soft_deletes_and_is_audited_with_a_reason()
    {
        var (companyId, customerId, itemId) = await SeedCompanyCustomerItemAndSeries();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId, Reason = "Raised against the wrong customer" };

        var created = await CreateInvoice(change, companyId, customerId, itemId); // credit, total 271.40, stock issued −2

        int rowVersion;
        await using (var db = _fixture.CreateContext(change))
        {
            rowVersion = (await db.Invoices.FirstAsync(i => i.Id == created.Id)).RowVersion;
        }

        await using (var db = _fixture.CreateContext(change))
        {
            await new InvoiceDeleter(db, AdopterFor(db, change), Clock).DeleteAsync(created.Id, rowVersion);
        }

        await using (var db = _fixture.CreateContext(change))
        {
            // The invoice is gone from normal queries (soft-deleted) but the row survives under the filter.
            (await db.Invoices.AnyAsync(i => i.Id == created.Id)).Should().BeFalse();
            var row = await db.Invoices.IgnoreQueryFilters().FirstAsync(i => i.Id == created.Id);
            row.DeletedAt.Should().NotBeNull();
            row.DeletedBy.Should().Be(1);

            // The receivable is back to zero — through a compensating Credit, never a reset.
            (await new ReceivablesLedger(db).BalanceForCustomerAsync(customerId)).Should().Be(0m);
            var reversal = await db.ReceivablesLedger
                .Where(e => e.InvoiceId == created.Id && e.Type == LedgerEntryType.Credit)
                .SingleAsync();
            reversal.Amount.Should().Be(-271.40m);

            // The stock the item line issued came back: an Issue(−2) and a Receipt(+2), netting to zero.
            var movements = await db.StockMovements.Where(m => m.ItemId == itemId).ToListAsync();
            movements.Should().HaveCount(2);
            movements.Sum(m => m.Quantity).Should().Be(0m);
            movements.Should().ContainSingle(m => m.Type == StockMovementType.Receipt && m.Quantity == 2m);

            // The delete is on the audit trail, with the reason — the register reads it from there.
            var key = created.Id.ToString(CultureInfo.InvariantCulture);
            var audit = await db.AuditLog
                .Where(a => a.EntityType == "Invoice" && a.EntityId == key && a.Action == AuditAction.Delete)
                .SingleAsync();
            audit.Reason.Should().Be("Raised against the wrong customer");
        }
    }

    [Fact]
    public async Task A_deleted_invoice_can_be_restored()
    {
        var (companyId, customerId, itemId) = await SeedCompanyCustomerItemAndSeries();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId, Reason = "Voided in error, bringing it back" };

        var created = await CreateInvoice(change, companyId, customerId, itemId);

        await using (var db = _fixture.CreateContext(change))
        {
            var rv = (await db.Invoices.FirstAsync(i => i.Id == created.Id)).RowVersion;
            await new InvoiceDeleter(db, AdopterFor(db, change), Clock).DeleteAsync(created.Id, rv);
        }

        // Restore is the interceptor's Restore path: clear DeletedAt and save. Nothing was erased, so it
        // is simply un-hidden — the row, its number and its history were there the whole time.
        await using (var db = _fixture.CreateContext(change))
        {
            var row = await db.Invoices.IgnoreQueryFilters().FirstAsync(i => i.Id == created.Id);
            row.DeletedAt = null;
            row.DeletedBy = null;
            await db.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateContext(change))
        {
            (await db.Invoices.AnyAsync(i => i.Id == created.Id)).Should().BeTrue();
        }
    }

    [Fact]
    public async Task Deleting_a_stale_copy_is_rejected()
    {
        var (companyId, customerId, itemId) = await SeedCompanyCustomerItemAndSeries();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId, Reason = "Attempting to void a stale copy" };

        var created = await CreateInvoice(change, companyId, customerId, itemId);

        // Someone else edits it first (row_version 1 → 2).
        long lineId;
        await using (var db = _fixture.CreateContext(change))
        {
            lineId = (await db.Invoices.Include(i => i.Lines).FirstAsync(i => i.Id == created.Id))
                .Lines.Single(l => l.ItemId == itemId).Id;
        }
        await using (var db = _fixture.CreateContext(change))
        {
            await new InvoiceEditor(db, LegacyContext(), new TaxEngine(), new DocumentVersionWriter(db, change, Clock),
                AdopterFor(db, change), new BusinessRuleReader(db), change, Clock)
                .EditAsync(created.Id, new EditInvoice(1, "PO-99", null, 0m,
                    [new EditInvoiceLine(lineId, itemId, "I-1", "Widget", 3m, 100m, 0m, 180m)]));
        }

        // A delete still holding row_version 1 is refused — it cannot void what it has not seen.
        await using (var db = _fixture.CreateContext(change))
        {
            var act = () => new InvoiceDeleter(db, AdopterFor(db, change), Clock).DeleteAsync(created.Id, expectedRowVersion: 1);
            await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
        }
    }

    private static LegacyInvoiceAdopter AdopterFor(TestDbContext db, FakeChangeContext change) => new(
        db, new TaxEngine(), new DocumentVersionWriter(db, change, Clock), new BusinessRuleReader(db));

    private SmartnetLegacyDbContext LegacyContext() => new(
        new DbContextOptionsBuilder<SmartnetLegacyDbContext>()
            .UseMySql(_fixture.ConnectionString, SmartnetServerVersion.Value)
            .Options);

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
