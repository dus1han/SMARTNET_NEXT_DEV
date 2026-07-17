using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Documents;
using Smartnet.Domain.MasterData;
using Smartnet.Domain.Settings;
using Smartnet.Infrastructure.Documents;
using Smartnet.Tests.Auditing;

namespace Smartnet.Tests.Documents;

/// <summary>
/// The cheque register (Phase 7, slice 2): a standalone adopted record. A cheque persists its typed columns
/// and dual-writes the legacy <c>cheques</c> varchars so the surviving <c>ChequeReport</c> reads a whole row.
/// Void is soft, not the legacy hard delete.
/// </summary>
[Collection(nameof(AuditCollection))]
public sealed class ChequeTests
{
    private readonly AuditFixture _fixture;
    private static readonly TimeProvider Clock = TimeProvider.System;

    public ChequeTests(AuditFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Recording_a_manual_cheque_persists_typed_columns_and_dual_writes_the_legacy_shadow()
    {
        var companyId = await SeedCompany();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };

        ChequeCreated created;
        await using (var db = _fixture.CreateContext(change))
        {
            created = await new ChequeService(db, change, Clock).CreateAsync(new NewCheque(
                companyId, "Manual", PayTo: "John Smith", SupplierId: null, Bank: "NTB", ChequeNumber: "600123",
                Amount: 500m, ChequeDate: new DateOnly(2026, 7, 17), DueDate: new DateOnly(2026, 7, 31)));
        }

        created.Amount.Should().Be(500m);

        await using (var db = _fixture.CreateContext(change))
        {
            var cheque = await db.Cheques.FirstAsync(c => c.Id == created.Id);
            cheque.Amount.Should().Be(500m);
            cheque.PayTo.Should().Be("John Smith");
            cheque.EntryType.Should().Be("Manual");
            cheque.ChequeDate.Should().Be(new DateOnly(2026, 7, 17));
            cheque.DataOrigin.Should().Be("new");

            // The legacy shadow was dual-written for the ChequeReport.
            var shadow = await db.Database
                .SqlQuery<ChequeShadow>($"SELECT amount AS Amount, chequedate AS Chequedate, company AS Company, printeddt AS Printeddt FROM cheques WHERE id = {created.Id}")
                .SingleAsync();
            shadow.Amount.Should().Be("500");
            shadow.Chequedate.Should().Be("2026-07-17");
            shadow.Company.Should().Be(companyId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            shadow.Printeddt.Should().Be(""); // not printed here — printing is Phase 8
        }
    }

    [Fact]
    public async Task A_supplier_cheque_links_the_supplier_and_writes_its_code()
    {
        var companyId = await SeedCompany();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };

        long supplierId;
        string code = $"S-{Guid.NewGuid():N}"[..12];
        await using (var db = _fixture.CreateContext(change))
        {
            var supplier = new Supplier { Code = code, Name = "Widgets Ltd" };
            db.Suppliers.Add(supplier);
            await db.SaveChangesAsync();
            supplierId = supplier.Id;
        }

        long id;
        await using (var db = _fixture.CreateContext(change))
        {
            id = (await new ChequeService(db, change, Clock).CreateAsync(new NewCheque(
                companyId, "Supplier", PayTo: "Widgets Ltd", SupplierId: supplierId, Bank: "HNB", ChequeNumber: "700900",
                Amount: 1200m, ChequeDate: new DateOnly(2026, 7, 18), DueDate: new DateOnly(2026, 8, 1)))).Id;
        }

        await using (var db = _fixture.CreateContext(change))
        {
            var cheque = await db.Cheques.FirstAsync(c => c.Id == id);
            cheque.EntryType.Should().Be("Supplier");
            cheque.SupplierId.Should().Be(supplierId);
            cheque.SupplierCode.Should().Be(code);
        }
    }

    [Fact]
    public async Task Voiding_a_cheque_soft_deletes_it()
    {
        var companyId = await SeedCompany();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };

        long id;
        int rowVersion;
        await using (var db = _fixture.CreateContext(change))
        {
            id = (await new ChequeService(db, change, Clock).CreateAsync(new NewCheque(
                companyId, "Manual", "Cash", null, "NTB", "1", 100m, new DateOnly(2026, 7, 17), new DateOnly(2026, 7, 17)))).Id;
            rowVersion = await db.Cheques.Where(c => c.Id == id).Select(c => c.RowVersion).SingleAsync();
        }

        await using (var db = _fixture.CreateContext(change))
        {
            await new ChequeService(db, change, Clock).VoidAsync(id, rowVersion);
        }

        await using (var db = _fixture.CreateContext(change))
        {
            (await db.Cheques.CountAsync(c => c.Id == id)).Should().Be(0); // excluded by the query filter
            (await db.Cheques.IgnoreQueryFilters().CountAsync(c => c.Id == id && c.DeletedAt != null)).Should().Be(1);
        }
    }

    private async Task<long> SeedCompany()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext { UserId = 1 });
        var company = new Company { Name = "Smart Net (test)", VatCode = "1", IsVatRegistered = true };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        return company.Id;
    }

    private sealed record ChequeShadow(string? Amount, string? Chequedate, string? Company, string? Printeddt);
}
