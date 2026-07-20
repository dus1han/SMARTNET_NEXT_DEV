using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Smartnet.Infrastructure.Persistence;

namespace Smartnet.Tests.Persistence;

/// <summary>
/// Which tables the new context shares with the legacy schema — and, more to the point, which it
/// must not.
/// </summary>
/// <remarks>
/// <para><b>This exists because of a real mistake.</b> Phase 7 slice 5 added a per-entity notes table
/// and called it <c>notes</c>. The legacy schema already has a <c>notes</c> table (49 rows) that
/// LEGACY-DATA-POLICY says to leave in place, and the migration failed on the dev database with
/// <i>"Table 'notes' already exists"</i>. Nothing was lost, but only because that table happened to
/// exist on the copy being migrated — the safety came from MySQL, not from anything in this
/// repository.</para>
///
/// <para><b>Sharing a name is not always wrong.</b> Most of the intersection below is deliberate: the
/// strangler migration <i>adopts</i> legacy tables, adding a key and audit columns to
/// <c>invoice_h</c>, <c>cheques</c>, <c>expense_tr</c> and the rest, and the new context maps the same
/// physical table on purpose. So this cannot assert "no overlap" — it pins the overlap instead.</para>
///
/// <para><b>How it fails.</b> A new table that accidentally takes a legacy name grows the intersection
/// and this test fails, naming it. The fix is either to rename the new table (what
/// <c>entity_notes</c> did) or, if the table really is being adopted, to add it to the list below —
/// which makes the adoption a decision somebody wrote down rather than a collision nobody noticed.</para>
/// </remarks>
public sealed class LegacyTableNameTests
{
    /// <summary>
    /// Legacy tables the new context maps on purpose, because the migration adopted them.
    /// </summary>
    /// <remarks>
    /// Add to this list only when a table is genuinely being adopted — never to make this test pass.
    /// </remarks>
    /// <remarks>
    /// Note what is <b>absent</b>: <c>payments</c>, <c>docstore</c> and <c>supplier_inv_pay</c> are
    /// dual-written by raw SQL rather than mapped as entities, so they never enter this intersection.
    /// The strangler rule still applies to them — it is simply not enforced here.
    /// </remarks>
    private static readonly string[] DeliberatelyAdopted =
    [
        "cheques",
        "cn_h",
        "cn_l",
        "companies_m",
        "cus_m",
        "exp_cat_m",
        "expense_tr",
        "invoice_h",
        "invoice_l",
        "item_m",
        "item_stock",
        "jobs_m",
        "po_h",
        "po_l",
        "profit_percent",
        "quotation_h",
        "quotation_l",
        "sup_m",
        "supplier_invoice",
        "user_m",
        "user_permissions",
    ];

    [Fact]
    public void No_new_table_quietly_takes_a_legacy_table_name()
    {
        var shared = TableNames<SmartnetDbContext>()
            .Intersect(TableNames<SmartnetLegacyDbContext>(), StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        shared.Should().BeEquivalentTo(
            DeliberatelyAdopted,
            because:
                "a new table sharing a legacy table's name collides with data the migration policy "
                + "keeps. Rename the new table, or add it above if it is genuinely being adopted.");
    }

    /// <summary>The specific collision that caused this test to exist.</summary>
    [Fact]
    public void The_notes_table_does_not_collide_with_the_legacy_one()
    {
        var newTables = TableNames<SmartnetDbContext>();

        newTables.Should().Contain("user_notes");
        newTables.Should().NotContain("notes");
    }

    /// <summary>
    /// The mapped table names of a context, built without touching a database.
    /// </summary>
    /// <remarks>
    /// Model building is offline — the connection string is never opened — so this stays a fast unit
    /// test rather than joining the Testcontainers collection.
    /// </remarks>
    private static HashSet<string> TableNames<TContext>()
        where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>()
            .UseMySql("Server=localhost;Database=none;User=none;Password=none;", SmartnetServerVersion.Value)
            .Options;

        using var context = (TContext)Activator.CreateInstance(typeof(TContext), options)!;

        return context.Model
            .GetEntityTypes()
            .Select(type => type.GetTableName())
            .Where(name => name is not null)
            .Select(name => name!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
