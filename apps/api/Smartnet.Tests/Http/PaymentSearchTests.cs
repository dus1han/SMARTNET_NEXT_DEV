using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Documents;
using Smartnet.Domain.Ledger;
using Smartnet.Domain.MasterData;
using Smartnet.Infrastructure.Persistence;

namespace Smartnet.Tests.Http;

/// <summary>
/// Finding a payment by the invoice it settled.
/// </summary>
/// <remarks>
/// <para>The list used to show a count of invoices and search only customer, reference and method. Asked
/// "how was SI-35 paid?" it could not answer: a pre-cutover payment carries no reference at all, so the
/// row was on the screen and unfindable by the one thing that identifies it.</para>
///
/// <para>Both halves of the list are separate tables and separate queries — a legacy payment names its
/// invoice on its own row, a receipt this app recorded reaches its invoices through allocations — so both
/// are asserted, and the multi-invoice case with them.</para>
/// </remarks>
[Collection(nameof(ApiCollection))]
public sealed class PaymentSearchTests
{
    private readonly ApiFixture _api;

    public PaymentSearchTests(ApiFixture api) => _api = api;

    private sealed record Row(
        long Id, DateOnly Date, string? CustomerName, decimal Amount, string? Method, string? Reference,
        int Invoices, string Origin, List<string> InvoiceNumbers);

    private sealed record Page(List<Row> Rows, int Total, int PageNumber, int PageSize);

    private async Task<Page> Search(string term)
    {
        var response = await _api.SignedIn.GetAsync($"/api/customer-receipts?search={Uri.EscapeDataString(term)}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Page>())!;
    }

    [Fact]
    public async Task A_legacy_payment_is_found_by_the_invoice_it_settled()
    {
        // Exactly the SI-35 shape: a pre-cutover payment with no reference of its own.
        var (number, _) = await SeedLegacyInvoiceAsync(200_000m);
        await SeedLegacyPaymentAsync(number, 50_000m);

        var found = await Search(number);

        found.Rows.Should().ContainSingle(r => r.InvoiceNumbers.Contains(number));

        var row = found.Rows.Single(r => r.InvoiceNumbers.Contains(number));
        row.Origin.Should().Be("legacy");
        row.Amount.Should().Be(50_000m);
        row.Reference.Should().BeNullOrEmpty("this is the case that had nothing else to search by");
    }

    [Fact]
    public async Task A_receipt_settling_several_invoices_is_found_by_any_of_them()
    {
        var (firstNumber, firstId) = await SeedLegacyInvoiceAsync(10_000m);
        var (secondNumber, secondId) = await SeedLegacyInvoiceAsync(20_000m);
        var customerId = await SeedCustomerChargesAsync(firstId, secondId, 10_000m, 20_000m);

        var response = await _api.SignedIn.PostAsJsonAsync("/api/customer-receipts", new
        {
            companyId = _api.CompanyId,
            customerId,
            date = DateOnly.FromDateTime(DateTime.UtcNow),
            method = "BANK",
            reference = (string?)null,
            idempotencyKey = Guid.NewGuid().ToString(),
            allocations = new[]
            {
                new { invoiceId = firstId, amount = 4_000m },
                new { invoiceId = secondId, amount = 5_000m },
            },
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Searched by the SECOND invoice — the one that is not first in the list — because matching only
        // the first would pass a test and still fail the person looking for the other one.
        var found = await Search(secondNumber);
        var row = found.Rows.Should().ContainSingle(r => r.InvoiceNumbers.Contains(secondNumber)).Subject;

        row.Origin.Should().Be("new");
        row.Invoices.Should().Be(2);
        row.InvoiceNumbers.Should().BeEquivalentTo([firstNumber, secondNumber]);
        row.Amount.Should().Be(9_000m);

        // And by the first, for the same receipt.
        (await Search(firstNumber)).Rows
            .Should().ContainSingle(r => r.Id == row.Id);
    }

    [Fact]
    public async Task Searching_an_invoice_number_that_settled_nothing_finds_nothing()
    {
        var (number, _) = await SeedLegacyInvoiceAsync(5_000m); // raised, never paid

        (await Search(number)).Rows.Should().BeEmpty();
    }

    // --- Seeding ----------------------------------------------------------------------------------

    private SmartnetDbContext NewContext() => new(
        new DbContextOptionsBuilder<SmartnetDbContext>()
            .UseMySql(_api.ConnectionString, SmartnetServerVersion.Value,
                mysql => mysql.MigrationsAssembly(typeof(SmartnetDbContext).Assembly.FullName))
            .Options);

    /// <summary>A legacy invoice on the fixture's company, with the NOT NULL varchars the table demands.</summary>
    private async Task<(string Number, long Id)> SeedLegacyInvoiceAsync(decimal total)
    {
        await using var db = NewContext();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var number = $"PS-{suffix}";
        var company = _api.CompanyId.ToString(CultureInfo.InvariantCulture);
        var money = total.ToString(CultureInfo.InvariantCulture);

        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO invoice_h
              (it, invoiceno, invtype, indate, customer, pono, totamount, balance, preparedby, cdatetime,
               cost, company, novattotal, vtype, vper, discountper, beforedisctot, contactperson,
               company_id, data_origin)
            VALUES
              ('ITEM', {number}, 'CREDIT', '2024-05-01', 'PSC-1', 'PO-1', {money}, {money}, 'Old User',
               '2024-05-01 10:00:00', '0', {company}, {money}, '1', '0', '0', {money}, 'Mr Test',
               {_api.CompanyId}, 'legacy')
            """);

        var id = await db.Database
            .SqlQuery<long>($"SELECT id AS Value FROM invoice_h WHERE invoiceno = {number}")
            .SingleAsync();

        return (number, id);
    }

    /// <summary>A pre-cutover payment against that invoice — no reference, as the old app left them.</summary>
    private async Task SeedLegacyPaymentAsync(string invoiceNumber, decimal amount)
    {
        await using var db = NewContext();

        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO payments (invoiceno, amount, paymentrecdate, enteredby, entereddt, paym, payref, company_id)
            VALUES ({invoiceNumber}, {amount.ToString(CultureInfo.InvariantCulture)}, '2024-06-01',
                    'Old User', '2024-06-01 09:00:00', '', '', {_api.CompanyId})
            """);
    }

    /// <summary>
    /// A customer owing both invoices — the receivables ledger is what the receipt allocates against, so
    /// a Charge per invoice is what makes them settleable.
    /// </summary>
    private async Task<long> SeedCustomerChargesAsync(long firstId, long secondId, decimal first, decimal second)
    {
        await using var db = NewContext();

        var customer = new Customer { Code = $"PSC-{Guid.NewGuid().ToString("N")[..8]}", Name = "Payment Search Co" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        // The invoices were seeded against a placeholder code; point them at this customer so the
        // receipt's customer check passes.
        await db.Database.ExecuteSqlAsync(
            $"UPDATE invoice_h SET customer = {customer.Code} WHERE id IN ({firstId}, {secondId})");

        db.ReceivablesLedger.AddRange(
            Charge(customer.Id, firstId, first),
            Charge(customer.Id, secondId, second));

        await db.SaveChangesAsync();

        return customer.Id;
    }

    private static LedgerEntry Charge(long customerId, long invoiceId, decimal amount) => new()
    {
        CustomerId = customerId,
        InvoiceId = invoiceId,
        Type = LedgerEntryType.Charge,
        Amount = amount,
        OccurredAt = new DateTime(2024, 5, 1, 0, 0, 0, DateTimeKind.Utc),
    };
}
