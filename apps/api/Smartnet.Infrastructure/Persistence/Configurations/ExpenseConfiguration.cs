using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Smartnet.Domain.Documents;
using Smartnet.Domain.MasterData;

namespace Smartnet.Infrastructure.Persistence.Configurations;

/// <summary>
/// The <c>expense_tr</c> legacy varchar columns the new save writes alongside its typed ones — EF shadow
/// properties set by the save pipeline, one list so the mapping and the writing cannot drift.
/// <c>expense_desc</c>, <c>paymentm</c> and <c>payment_ref</c> are <b>not</b> here — they map to real
/// <see cref="Expense"/> properties, being shared columns with no type change.
/// </summary>
internal static class ExpenseLegacyShadow
{
    public const string ExpCat = "exp_cat";
    public const string ExpenseDate = "expense_date";
    public const string ExpenseAmount = "expense_amount";
    public const string AddedBy = "addedby";
    public const string AddedDt = "addeddt";
    public const string Company = "company";

    public static readonly (string Name, string Column, int Length)[] All =
    [
        (ExpCat, "exp_cat", 100), (ExpenseDate, "expense_date", 100), (ExpenseAmount, "expense_amount", 100),
        (AddedBy, "addedby", 100), (AddedDt, "addeddt", 100), (Company, "company", 100),
    ];
}

/// <summary>
/// Expenses, mapped onto the adopted legacy <c>expense_tr</c> table (Phase 7, slice 3).
/// </summary>
/// <remarks>
/// Additive adoption. The legacy <c>id</c> was <c>0</c> on every row (never a usable key), so the migration
/// drops it and adds a real surrogate — like <c>invoice_h</c>, not <c>supplier_invoice</c>. The typed columns
/// (amount, date, category link) are new and sit beside the legacy <c>varchar</c> ones, which the save keeps
/// in step for the surviving <c>ExpenseReport</c>. <c>company_id</c> already exists (multi-company migration).
/// </remarks>
public class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> builder)
    {
        builder.ToTable("expense_tr");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        // Shared columns — the same varchar the legacy app reads and writes, no type change.
        builder.Property(e => e.Description).HasColumnName("expense_desc").HasMaxLength(100);
        builder.Property(e => e.Method).HasColumnName("paymentm").HasMaxLength(100);
        builder.Property(e => e.Reference).HasColumnName("payment_ref").HasMaxLength(100);

        // New, typed columns — the new app's source of truth.
        builder.Property(e => e.CompanyId).HasColumnName("company_id");
        builder.Property(e => e.CategoryId).HasColumnName("category_id");
        builder.Property(e => e.Amount).HasColumnName("amount").HasColumnType("decimal(18,4)");
        builder.Property(e => e.Date).HasColumnName("spent_on");
        builder.Property(e => e.DataOrigin).HasColumnName("data_origin").HasMaxLength(16);

        // Legacy shadow columns, written alongside on save so the surviving ExpenseReport reads a whole row.
        foreach (var (name, column, length) in ExpenseLegacyShadow.All)
        {
            builder.Property<string>(name).HasColumnName(column).HasMaxLength(length);
        }

        builder.ConfigureAuditColumns();

        // A plain scalar link to exp_cat_m.id — not a DB foreign key, so the legacy rows (whose new
        // category_id defaults to 0) are unconstrained; the service validates the category on create.
        builder.HasIndex(e => e.CategoryId);

        // Only this app's own expenses; the adopted legacy rows share the table but are read through the
        // legacy model, their new typed columns being meaningless defaults.
        builder.HasQueryFilter(e => e.DataOrigin == "new" && e.DeletedAt == null);
    }
}

/// <summary>
/// Expense categories, on the adopted legacy <c>exp_cat_m</c> table (Phase 7, slice 3) — a shared mini-master.
/// </summary>
/// <remarks>
/// The AUTO_INCREMENT <c>id</c> under a non-unique key is promoted to a primary key (Finding 6). Shared across
/// companies. Soft-deletable, so a removed category stays readable to any expense that still references it.
/// </remarks>
public class ExpenseCategoryConfiguration : IEntityTypeConfiguration<ExpenseCategory>
{
    public void Configure(EntityTypeBuilder<ExpenseCategory> builder)
    {
        builder.ToTable("exp_cat_m");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");
        builder.Property(c => c.Name).HasColumnName("expcatname").HasMaxLength(100);

        builder.ConfigureAuditColumns();

        builder.HasQueryFilter(c => c.DeletedAt == null);
    }
}
