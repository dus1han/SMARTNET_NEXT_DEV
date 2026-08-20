using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Smartnet.Domain.Documents;

namespace Smartnet.Infrastructure.Persistence.Configurations;

/// <summary>
/// Expense payments — settling an expense over time is a new concept (the legacy app had no such thing: an
/// expense was money already gone), so a new table, not a legacy adoption.
/// </summary>
/// <remarks>
/// The link to <c>expense_tr</c> is a plain scalar, not a foreign key, because it points at an adopted table
/// that holds this app's expenses and the legacy ones side by side — the same reasoning as the
/// supplier-payment allocations. Unlike those, the query filter here keeps <b>every</b> origin visible:
/// a <see cref="ExpensePaymentOrigin.Migrated"/> row is the settlement of an expense recorded before this
/// existed, and hiding it would make a paid expense read as outstanding.
/// </remarks>
public class ExpensePaymentConfiguration : IEntityTypeConfiguration<ExpensePayment>
{
    public void Configure(EntityTypeBuilder<ExpensePayment> builder)
    {
        builder.ToTable("expense_payments");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.ExpenseId).HasColumnName("expense_id");
        builder.Property(p => p.CompanyId).HasColumnName("company_id");
        builder.Property(p => p.Date).HasColumnName("paid_on");
        builder.Property(p => p.Amount).HasColumnName("amount").HasColumnType("decimal(18,4)");
        builder.Property(p => p.Method).HasColumnName("method").HasMaxLength(50);
        builder.Property(p => p.Reference).HasColumnName("reference").HasMaxLength(200);
        builder.Property(p => p.DataOrigin).HasColumnName("data_origin").HasMaxLength(16);

        builder.ConfigureAuditColumns();

        builder.HasQueryFilter(p => p.DeletedAt == null);

        builder.HasIndex(p => p.ExpenseId);
    }
}
