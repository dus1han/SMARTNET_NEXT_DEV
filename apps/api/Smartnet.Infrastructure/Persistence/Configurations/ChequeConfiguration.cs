using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Smartnet.Domain.Documents;
using Smartnet.Domain.MasterData;

namespace Smartnet.Infrastructure.Persistence.Configurations;

/// <summary>
/// The <c>cheques</c> legacy varchar columns the new save writes alongside its typed ones — EF shadow
/// properties set by the save pipeline, one list so the mapping and the writing cannot drift. <c>payto</c>,
/// <c>bank</c>, <c>chkno</c>, <c>entry</c> and <c>supcode</c> are <b>not</b> here — they map to real
/// <see cref="Cheque"/> properties, being shared columns with no type change.
/// </summary>
internal static class ChequeLegacyShadow
{
    public const string ChequeDate = "chequedate";
    public const string DueDate = "duedate";
    public const string Amount = "amount";
    public const string Company = "company";
    public const string CreatedBy = "createdby";
    public const string CreatedDt = "createddt";
    public const string PrintedDt = "printeddt";

    public static readonly (string Name, string Column, int Length)[] All =
    [
        (ChequeDate, "chequedate", 100), (DueDate, "duedate", 100), (Amount, "amount", 100),
        (Company, "company", 100), (CreatedBy, "createdby", 100), (CreatedDt, "createddt", 100),
        (PrintedDt, "printeddt", 100),
    ];
}

/// <summary>
/// Cheques, mapped onto the adopted legacy <c>cheques</c> table (Phase 7, slice 2).
/// </summary>
/// <remarks>
/// Additive adoption, exactly like <c>supplier_invoice</c>: the table already had an <c>id</c> under a
/// non-unique key, so the migration promotes it to a real primary key rather than adding one. The typed
/// columns (amount, dates, supplier_id) are new and sit beside the legacy <c>varchar</c> ones, which the save
/// keeps in step for the surviving <c>ChequeReport</c>. <c>company_id</c> already exists (multi-company
/// migration). <c>payto</c>/<c>bank</c>/<c>chkno</c>/<c>entry</c>/<c>supcode</c> are shared columns.
/// </remarks>
public class ChequeConfiguration : IEntityTypeConfiguration<Cheque>
{
    public void Configure(EntityTypeBuilder<Cheque> builder)
    {
        builder.ToTable("cheques");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");

        // Shared columns — the same varchar the legacy app reads and writes, no type change.
        builder.Property(c => c.EntryType).HasColumnName("entry").HasMaxLength(100);
        builder.Property(c => c.PayTo).HasColumnName("payto").HasMaxLength(100);
        builder.Property(c => c.SupplierCode).HasColumnName("supcode").HasMaxLength(100);
        builder.Property(c => c.Bank).HasColumnName("bank").HasMaxLength(100);
        builder.Property(c => c.ChequeNumber).HasColumnName("chkno").HasMaxLength(100);

        // New, typed columns — the new app's source of truth.
        builder.Property(c => c.CompanyId).HasColumnName("company_id");
        builder.Property(c => c.SupplierId).HasColumnName("supplier_id");
        builder.Property(c => c.Amount).HasColumnName("cheque_amount").HasColumnType("decimal(18,4)");
        builder.Property(c => c.ChequeDate).HasColumnName("cheque_date");
        builder.Property(c => c.DueDate).HasColumnName("due_date");
        builder.Property(c => c.PrintedAt).HasColumnName("printed_at");
        builder.Property(c => c.SourceType).HasColumnName("source_type").HasMaxLength(24);
        builder.Property(c => c.SourceId).HasColumnName("source_id");
        builder.Property(c => c.DataOrigin).HasColumnName("data_origin").HasMaxLength(16);

        builder.HasIndex(c => new { c.SourceType, c.SourceId });

        // Legacy shadow columns, written alongside on save so the surviving ChequeReport reads a whole row.
        foreach (var (name, column, length) in ChequeLegacyShadow.All)
        {
            builder.Property<string>(name).HasColumnName(column).HasMaxLength(length);
        }

        builder.ConfigureAuditColumns();

        // Optional link to a supplier (a Supplier-entry cheque); null for Manual and for legacy rows.
        builder.HasOne<Supplier>()
            .WithMany()
            .HasForeignKey(c => c.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        // Only this app's own cheques; the adopted legacy rows share the table but are read through the
        // legacy model, their new typed columns being meaningless defaults.
        builder.HasQueryFilter(c => c.DataOrigin == "new" && c.DeletedAt == null);
    }
}
