using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Smartnet.Domain.Documents;

namespace Smartnet.Infrastructure.Persistence.Configurations;

/// <summary>
/// The <c>jobs_m</c> legacy varchar/text columns the new save writes alongside its typed ones — EF shadow
/// properties set by the save pipeline. <b>Every one is NOT NULL</b> (the legacy table has no nullable
/// column), so the save must set them all, including the ones only meaningful at close (<c>cost</c>,
/// <c>sell</c>, <c>completedby</c>, <c>dompleteddt</c>), which are written empty until then. The shared
/// columns (<c>jobno</c>, <c>contactperson</c>, <c>faultD</c>, <c>remarks</c>, <c>jobdoneby</c>,
/// <c>jstat</c>, <c>completionremarks</c>) map to real entity properties and are <b>not</b> here.
/// </summary>
internal static class JobCardLegacyShadow
{
    public const string Company = "company";
    public const string Customer = "customer";
    public const string JDate = "jdate";
    public const string EnteredBy = "enteredby";
    public const string EnteredDt = "entereddt";
    public const string Cost = "cost";
    public const string Sell = "sell";
    public const string CompletedBy = "completedby";
    public const string CompletedDt = "dompleteddt"; // the legacy misspelling, kept verbatim
    public const string Items = "items";

    public static readonly (string Name, string Column, string? Type, int Length)[] All =
    [
        (Company, "company", null, 100), (Customer, "customer", null, 100), (JDate, "jdate", null, 100),
        (EnteredBy, "enteredby", null, 100), (EnteredDt, "entereddt", null, 100),
        (Cost, "cost", null, 100), (Sell, "sell", null, 100),
        (CompletedBy, "completedby", null, 100), (CompletedDt, "dompleteddt", null, 100),
        (Items, "items", "text", 0),
    ];
}

/// <summary>
/// Job cards, mapped onto the adopted legacy <c>jobs_m</c> table (Phase 6, slice 3).
/// </summary>
/// <remarks>
/// Additive adoption of a fully NOT NULL, keyless table: the migration adds a surrogate <c>id</c> and the
/// typed columns beside the legacy varchars, which the save keeps in step for the legacy Crystal job sheet.
/// The shared columns map directly; everything with a type (dates, money, the user ids) gets a new column.
/// </remarks>
public class JobCardConfiguration : IEntityTypeConfiguration<JobCard>
{
    public void Configure(EntityTypeBuilder<JobCard> builder)
    {
        builder.ToTable("jobs_m");

        builder.HasKey(j => j.Id);
        builder.Property(j => j.Id).HasColumnName("id");

        // Shared with the legacy app.
        builder.Property(j => j.Number).HasColumnName("jobno").HasMaxLength(100);
        builder.Property(j => j.ContactPerson).HasColumnName("contactperson").HasMaxLength(100);
        builder.Property(j => j.FaultDescription).HasColumnName("faultD").HasColumnType("text");
        builder.Property(j => j.Remarks).HasColumnName("remarks").HasColumnType("text");
        builder.Property(j => j.Technician).HasColumnName("jobdoneby").HasMaxLength(100);
        builder.Property(j => j.Status).HasColumnName("jstat").HasMaxLength(100);
        builder.Property(j => j.CompletionRemarks).HasColumnName("completionremarks").HasColumnType("text");

        // New, typed columns.
        builder.Property(j => j.CompanyId).HasColumnName("company_id");
        builder.Property(j => j.CustomerId).HasColumnName("customer_id");
        builder.Property(j => j.Date).HasColumnName("job_date");
        builder.Property(j => j.EnteredBy).HasColumnName("entered_by");
        builder.Property(j => j.EnteredAt).HasColumnName("entered_at");
        builder.Property(j => j.Cost).HasColumnName("cost_amount").HasColumnType("decimal(18,4)");
        builder.Property(j => j.Sell).HasColumnName("sell_amount").HasColumnType("decimal(18,4)");
        builder.Property(j => j.CompletedBy).HasColumnName("completed_by");
        builder.Property(j => j.CompletedAt).HasColumnName("completed_at");
        builder.Property(j => j.DataOrigin).HasColumnName("data_origin").HasMaxLength(16);
        builder.Ignore(j => j.IsClosed);

        foreach (var (name, column, type, length) in JobCardLegacyShadow.All)
        {
            var property = builder.Property<string>(name).HasColumnName(column);
            if (type is not null) property.HasColumnType(type); else property.HasMaxLength(length);
        }

        builder.ConfigureAuditColumns();

        builder.HasQueryFilter(j => j.DataOrigin == "new" && j.DeletedAt == null);

        builder.HasMany(j => j.Lines)
            .WithOne(l => l.JobCard)
            .HasForeignKey(l => l.JobCardId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Job-card lines — a genuinely new table (<c>jobcard_l</c>); the legacy job card had none.</summary>
public class JobCardLineConfiguration : IEntityTypeConfiguration<JobCardLine>
{
    public void Configure(EntityTypeBuilder<JobCardLine> builder)
    {
        builder.ToTable("jobcard_l");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasColumnName("id");
        builder.Property(l => l.JobCardId).HasColumnName("job_card_id");
        builder.Property(l => l.ItemId).HasColumnName("item_id");
        builder.Property(l => l.Description).HasColumnName("description").HasColumnType("text");
        builder.Property(l => l.Serial).HasColumnName("serial").HasMaxLength(200);
        builder.Property(l => l.Sort).HasColumnName("sort");

        builder.ConfigureAuditColumns();

        builder.HasIndex(l => l.JobCardId);
    }
}
