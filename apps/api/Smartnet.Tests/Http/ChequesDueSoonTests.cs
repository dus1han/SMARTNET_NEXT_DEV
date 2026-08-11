using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Documents;
using Smartnet.Infrastructure.Persistence;

namespace Smartnet.Tests.Http;

/// <summary>
/// The cheque due-soon warning, over HTTP — what the dashboard strip asks for.
/// </summary>
/// <remarks>
/// <para><b>Both origins, which is the whole risk.</b> Every cheque in production is a legacy row whose
/// due date lives in the <c>duedate</c> varchar rather than the typed column. An implementation that
/// read only <c>_db.Cheques</c> would pass a naive test and warn about nothing at all in the live
/// system, so the legacy case is asserted here explicitly.</para>
///
/// <para><b>Dates are relative to today, not literals.</b> The endpoint asks the real clock, so a test
/// pinned to fixed dates would pass this week and fail next. The window arithmetic itself is pinned
/// against a real calendar in <see cref="Smartnet.Tests.Documents.BusinessDaysTests"/>; what these
/// assert is that the endpoint uses it, over both tables, and honours the edges.</para>
/// </remarks>
[Collection(nameof(ApiCollection))]
public sealed class ChequesDueSoonTests
{
    private readonly ApiFixture _api;

    public ChequesDueSoonTests(ApiFixture api) => _api = api;

    private sealed record DueSoonRow(long Id, DateOnly DueDate, string PayTo, decimal Amount, string? CompanyName);

    private sealed record DueSoon(DateOnly From, DateOnly To, int Count, List<DueSoonRow> Cheques);

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static DateOnly WindowEnd => BusinessDays.AddTo(Today, 2);

    private async Task<DueSoon> GetDueSoon()
    {
        var response = await _api.SignedIn.GetAsync("/api/cheques/due-soon");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<DueSoon>())!;
    }

    [Fact]
    public async Task The_window_is_today_to_two_business_days_out()
    {
        var due = await GetDueSoon();

        due.From.Should().Be(Today);
        due.To.Should().Be(WindowEnd);

        // Stated rather than assumed: over a weekend the window is longer in calendar days, which is
        // the entire reason it is counted in business days.
        due.To.Should().BeOnOrAfter(Today);
    }

    [Fact]
    public async Task A_cheque_due_inside_the_window_is_warned_about_and_one_beyond_it_is_not()
    {
        var inside = await SeedTypedCheque("Inside the window", Today);
        var edge = await SeedTypedCheque("On the last day", WindowEnd);
        var beyond = await SeedTypedCheque("Beyond the window", WindowEnd.AddDays(1));
        var past = await SeedTypedCheque("Already due", Today.AddDays(-1));

        var due = await GetDueSoon();
        var ids = due.Cheques.Select(c => c.Id).ToList();

        ids.Should().Contain(inside);
        ids.Should().Contain(edge, "the last day of the window is inside it");
        ids.Should().NotContain(beyond);

        // Forward-looking only. Nothing can mark a cheque as banked, so an overdue list would only ever
        // grow and the warning would become permanent furniture.
        ids.Should().NotContain(past, "a cheque already due is not something this can tell you to act on");
    }

    [Fact]
    public async Task A_legacy_cheque_is_warned_about_too()
    {
        // The case that matters: in production every cheque is one of these.
        var legacyId = await SeedLegacyCheque("Legacy payee", Today);

        var due = await GetDueSoon();

        due.Cheques.Should().Contain(c => c.Id == legacyId,
            "every cheque in the live register is a legacy row, so reading only the typed table warns about none of them");

        var row = due.Cheques.Single(c => c.Id == legacyId);
        row.PayTo.Should().Be("Legacy payee");
        row.DueDate.Should().Be(Today);
        row.Amount.Should().Be(4_500m);
    }

    [Fact]
    public async Task A_legacy_cheque_with_an_unreadable_due_date_is_skipped_rather_than_guessed_at()
    {
        // The old app accepted dates no calendar has. Warning about one on some epoch date names a day
        // that never existed; leaving it out is the honest answer.
        var unreadable = await SeedLegacyCheque("Impossible date", dueDate: null, rawDueDate: "0024-06-07");

        var due = await GetDueSoon();

        due.Cheques.Should().NotContain(c => c.Id == unreadable);
    }

    [Fact]
    public async Task The_count_matches_the_rows_and_they_are_soonest_first()
    {
        await SeedTypedCheque("Later", WindowEnd);
        await SeedTypedCheque("Sooner", Today);

        var due = await GetDueSoon();

        due.Count.Should().Be(due.Cheques.Count);
        due.Cheques.Select(c => c.DueDate).Should().BeInAscendingOrder();
    }

    // --- Seeding ----------------------------------------------------------------------------------

    private SmartnetDbContext NewContext() => new(
        new DbContextOptionsBuilder<SmartnetDbContext>()
            .UseMySql(_api.ConnectionString, SmartnetServerVersion.Value,
                mysql => mysql.MigrationsAssembly(typeof(SmartnetDbContext).Assembly.FullName))
            .Options);

    /// <summary>A cheque this app raised — typed columns, <c>data_origin = 'new'</c>.</summary>
    /// <remarks>
    /// The legacy varchars are written too, because every one of them is NOT NULL and the real creator
    /// dual-writes them. Going around the creator means writing them here or the insert is rejected.
    /// </remarks>
    private async Task<long> SeedTypedCheque(string payTo, DateOnly dueDate)
    {
        await using var db = NewContext();

        var cheque = new Cheque
        {
            CompanyId = _api.CompanyId,
            EntryType = "Manual",
            PayTo = payTo,
            SupplierCode = string.Empty,
            Bank = "Sampath",
            ChequeNumber = $"T-{Guid.NewGuid().ToString("N")[..8]}",
            Amount = 1_000m,
            ChequeDate = dueDate,
            DueDate = dueDate,
            DataOrigin = "new",
        };

        db.Cheques.Add(cheque);

        var iso = dueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var entry = db.Entry(cheque);
        entry.Property("chequedate").CurrentValue = iso;
        entry.Property("duedate").CurrentValue = iso;
        entry.Property("amount").CurrentValue = "1000";
        entry.Property("company").CurrentValue = _api.CompanyId.ToString(CultureInfo.InvariantCulture);
        entry.Property("createdby").CurrentValue = "tests";
        entry.Property("createddt").CurrentValue = "2026-01-01 00:00:00";
        entry.Property("printeddt").CurrentValue = string.Empty;

        await db.SaveChangesAsync();

        return cheque.Id;
    }

    /// <summary>
    /// An adopted legacy cheque: every value a varchar, including the due date this endpoint has to
    /// parse before it can compare it.
    /// </summary>
    private async Task<long> SeedLegacyCheque(string payTo, DateOnly? dueDate, string? rawDueDate = null)
    {
        await using var db = NewContext();

        var number = $"LC-{Guid.NewGuid().ToString("N")[..8]}";
        var company = _api.CompanyId.ToString(CultureInfo.InvariantCulture);
        var due = rawDueDate ?? dueDate!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // Every legacy column is NOT NULL bar chequedate — the pre-adoption shape, faithful to production.
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO cheques
              (payto, bank, chkno, amount, chequedate, duedate, company, createdby, createddt, printeddt,
               entry, supcode, data_origin)
            VALUES
              ({payTo}, 'Sampath', {number}, '4500', {due}, {due}, {company}, 'Old User',
               '2024-01-01 09:00:00', '', 'Manual', '', 'legacy')
            """);

        return await db.Database
            .SqlQuery<long>($"SELECT id AS Value FROM cheques WHERE chkno = {number}")
            .SingleAsync();
    }
}
