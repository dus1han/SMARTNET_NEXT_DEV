using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Smartnet.Infrastructure.Persistence.Migrations;
using Smartnet.Tests.Auditing;

namespace Smartnet.Tests.Persistence;

/// <summary>
/// The Phase 9 backfill that made every pre-existing expense settled.
/// </summary>
/// <remarks>
/// Expenses used to be paid at the moment they were recorded — there was no other way — so when settlement
/// became a separate event each historical row needed one, or every expense on file would have read as
/// unpaid. The migration runs once against live data, which is exactly why it is worth proving here: the
/// adopted legacy rows keep their money and date in <c>varchar</c>s, and a single unreadable value must
/// become a skipped row rather than a failed migration.
/// <para>The statement under test is the migration's own constant, not a copy — a copy would only ever
/// prove that the copy works.</para>
/// </remarks>
[Collection(nameof(AuditCollection))]
public sealed class ExpenseSettlementBackfillTests
{
    private readonly AuditFixture _fixture;

    public ExpenseSettlementBackfillTests(AuditFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Every_live_expense_is_backfilled_with_the_settlement_it_was_recorded_with()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext { UserId = 1 });

        // The table as it stood before settlement existed: one of this app's expenses (typed columns), an
        // adopted legacy row (money and date in varchars, thousands separator and all), a legacy row whose
        // amount is not a number at all, and a voided row.
        await Seed(db, "BF new", origin: "new", amount: "0", typedAmount: 1500m, date: "", typedDate: "2026-05-04", method: "Bank", reference: "TRF-42", deleted: false);
        await Seed(db, "BF legacy", origin: "legacy", amount: "1,250.50", typedAmount: 0m, date: "2024-03-05", typedDate: "1970-01-01", method: "Cash", reference: "R-1", deleted: false);
        await Seed(db, "BF unreadable", origin: "legacy", amount: "n/a", typedAmount: 0m, date: "2024-03-06", typedDate: "1970-01-01", method: "Cash", reference: "", deleted: false);
        await Seed(db, "BF voided", origin: "legacy", amount: "99", typedAmount: 0m, date: "2024-03-07", typedDate: "1970-01-01", method: "Cash", reference: "", deleted: true);

        try
        {
            await db.Database.ExecuteSqlRawAsync(Phase9ExpenseSettlement.BackfillSql);

            var settled = await Settlements(db, "BF new");
            settled.Should().ContainSingle();
            settled[0].Amount.Should().Be(1500m);
            settled[0].Date.Should().Be(new DateOnly(2026, 5, 4));
            settled[0].Method.Should().Be("Bank");
            settled[0].Reference.Should().Be("TRF-42");
            settled[0].DataOrigin.Should().Be("migrated");

            // The legacy varchars are read the way LegacyValue reads them — "1,250.50" is 1250.50, and the
            // date comes off expense_date rather than the placeholder spent_on the adoption left behind.
            var legacy = await Settlements(db, "BF legacy");
            legacy.Should().ContainSingle();
            legacy[0].Amount.Should().Be(1250.50m);
            legacy[0].Date.Should().Be(new DateOnly(2024, 3, 5));

            // An amount that is not a number is not money: no settlement is invented for it, and the
            // migration does not fail over it. The expense simply reads as outstanding for someone to fix.
            (await Settlements(db, "BF unreadable")).Should().BeEmpty();

            // A voided expense is not owed and not paid.
            (await Settlements(db, "BF voided")).Should().BeEmpty();
        }
        finally
        {
            // The backfill is table-wide, so it also settles whatever other tests left behind. Undo it.
            await db.Database.ExecuteSqlRawAsync("DELETE FROM expense_payments WHERE data_origin = 'migrated'");
            await db.Database.ExecuteSqlRawAsync("DELETE FROM expense_tr WHERE expense_desc LIKE 'BF %'");
        }
    }

    private static Task<int> Seed(
        Smartnet.Infrastructure.Persistence.SmartnetDbContext db,
        string description,
        string origin,
        string amount,
        decimal typedAmount,
        string date,
        string typedDate,
        string method,
        string reference,
        bool deleted) =>
        db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO `expense_tr`
                (`exp_cat`, `expense_date`, `expense_desc`, `expense_amount`, `paymentm`, `payment_ref`,
                 `addedby`, `addeddt`, `company`, `data_origin`, `category_id`, `amount`, `spent_on`,
                 `net_amount`, `tax_rate_percentage`, `created_at`, `row_version`, `deleted_at`)
            VALUES
                ('1', {date}, {description}, {amount}, {method}, {reference},
                 'test', '2026-08-20 00:00:00', '1', {origin}, 1, {typedAmount}, {typedDate},
                 0, 0, UTC_TIMESTAMP(6), 1, {(deleted ? "2026-08-20 00:00:00" : null)})
            """);

    private static async Task<IReadOnlyList<Smartnet.Domain.Documents.ExpensePayment>> Settlements(
        Smartnet.Infrastructure.Persistence.SmartnetDbContext db, string description) =>
        await db.ExpensePayments
            .Where(p => db.Expenses.IgnoreQueryFilters().Any(e => e.Id == p.ExpenseId && e.Description == description))
            .ToListAsync();
}
