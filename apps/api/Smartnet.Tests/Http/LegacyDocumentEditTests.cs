using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Ledger;
using Smartnet.Domain.MasterData;
using Smartnet.Infrastructure.Persistence;

namespace Smartnet.Tests.Http;

/// <summary>
/// A legacy document must reach its editor, not be turned away by the controller's existence guard.
/// </summary>
/// <remarks>
/// The guard used to look the document up in the <c>data_origin == "new"</c> filtered set, which a legacy
/// row is invisible to — so a save returned 404 before the editor, which adopts the legacy row first, could
/// ever run. Reported from production as repeated <c>PUT /api/invoices/{legacy id}</c> → 404. This is
/// exercised over HTTP on purpose: the defect lived in the controller, so the editor's own tests
/// (<see cref="Smartnet.Tests.Documents.LegacyInvoiceAdoptionTests"/>) stayed green throughout.
/// </remarks>
[Collection(nameof(ApiCollection))]
public sealed class LegacyDocumentEditTests
{
    private readonly ApiFixture _api;

    public LegacyDocumentEditTests(ApiFixture api) => _api = api;

    [Fact]
    public async Task Editing_a_legacy_invoice_over_http_reaches_the_editor_instead_of_404ing()
    {
        var (invoiceId, lineId, itemId, itemCode, rowVersion) = await SeedLegacyInvoiceAsync();

        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/invoices/{invoiceId}")
        {
            Content = JsonContent.Create(new
            {
                ExpectedRowVersion = rowVersion,
                PurchaseOrderNo = "PO-L",
                ContactPerson = (string?)null,
                Lines = new[]
                {
                    new
                    {
                        Id = (long?)lineId,
                        ItemId = (long?)itemId,
                        ItemCode = itemCode,
                        Description = "Widget",
                        Quantity = 3m,
                        UnitPrice = 100m,
                        DiscountPercent = 0m,
                        Cost = (decimal?)180m,
                    },
                },
                DocumentDiscountPercent = 0m,
                DocumentCost = (decimal?)null,
                Date = (DateOnly?)null,
            }),
        };
        request.Headers.Add("X-Change-Reason", "Correcting the quantity on a migrated invoice.");

        var response = await _api.SignedIn.SendAsync(request);

        // The bug was a 404 at the gate; the fix lets the request through to the editor, which adopts the
        // legacy row and applies the edit. Assert both: not the old 404, and the edit actually succeeded.
        response.StatusCode.Should().NotBe(
            HttpStatusCode.NotFound,
            "a legacy invoice must be adopted and edited, not rejected by the existence guard");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Seeds a legacy invoice as the old app wrote it — money in varchar columns, customer and item as
    /// codes, <c>data_origin 'legacy'</c> — in the fixture's company, plus its imported opening balance.
    /// Mirrors <see cref="Smartnet.Tests.Documents.LegacyInvoiceAdoptionTests"/>'s seed.
    /// </summary>
    private async Task<(long InvoiceId, long LineId, long ItemId, string ItemCode, int RowVersion)> SeedLegacyInvoiceAsync()
    {
        // A plain context (no audit interceptor), the same way the fixture seeds its own company and user.
        await using var db = new SmartnetDbContext(
            new DbContextOptionsBuilder<SmartnetDbContext>()
                .UseMySql(_api.ConnectionString, SmartnetServerVersion.Value,
                    mysql => mysql.MigrationsAssembly(typeof(SmartnetDbContext).Assembly.FullName))
                .Options);

        var companyId = _api.CompanyId;
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var itemCode = $"IL-{suffix}";

        var customer = new Customer { Code = $"CL-{suffix}", Name = "Legacy Co" };
        var item = new Item { Code = itemCode, Name = "Widget", Cost = 60m, SellingPrice = 100m };
        db.Customers.Add(customer);
        db.Items.Add(item);
        await db.SaveChangesAsync();

        var number = $"SNI-L{suffix}";

        // total 236 (net 200 + 18% VAT), unpaid; one 2 × 100 item line.
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO invoice_h
              (it, invoiceno, invtype, indate, customer, pono, totamount, balance, preparedby, cdatetime,
               cost, company, novattotal, vtype, vper, discountper, beforedisctot, contactperson,
               company_id, data_origin)
            VALUES
              ('ITEM', {number}, 'CREDIT', '2024-05-01', {customer.Code}, 'PO-L', '236', '236', 'Old User',
               '2024-05-01 10:00:00', '120', {companyId.ToString(CultureInfo.InvariantCulture)}, '200', '1',
               '18', '0', '200', 'Mr Legacy', {companyId}, 'legacy')
            """);

        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO invoice_l (inno, itemcode, `desc`, qty, rate, tot)
            VALUES ({number}, {item.Code}, 'Widget', '2', '100', '200')
            """);

        var invoiceId = await db.Database
            .SqlQuery<long>($"SELECT id AS Value FROM invoice_h WHERE invoiceno = {number}")
            .SingleAsync();
        var lineId = await db.Database
            .SqlQuery<long>($"SELECT id AS Value FROM invoice_l WHERE inno = {number} ORDER BY id")
            .FirstAsync();
        var rowVersion = await db.Database
            .SqlQuery<int>($"SELECT row_version AS Value FROM invoice_h WHERE id = {invoiceId}")
            .SingleAsync();

        // The opening balance the cutover imported for this legacy invoice (LEGACY-DATA-POLICY §2).
        db.ReceivablesLedger.Add(new LedgerEntry
        {
            CustomerId = customer.Id,
            Type = LedgerEntryType.OpeningBalance,
            Amount = 236m,
            InvoiceId = invoiceId,
            OccurredAt = new DateTime(2024, 5, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        await db.SaveChangesAsync();

        return (invoiceId, lineId, item.Id, itemCode, rowVersion);
    }
}
