using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Smartnet.Domain.Settings;

namespace Smartnet.Infrastructure.Persistence.Configurations;

public class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> builder)
    {
        builder.ToTable("companies_m");

        // Like user_m: `id` exists and is indexed, but is not a primary key. The migration
        // promotes it. (Finding 6.)
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");

        // The three legacy columns, untouched — the old app still reads them.
        builder.Property(c => c.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(c => c.VatCode).HasColumnName("vatcode").HasMaxLength(100);

        builder.Property(c => c.IsVatRegistered).HasColumnName("is_vat_registered");
        builder.Property(c => c.VatNumber).HasColumnName("vat_number").HasMaxLength(64);

        builder.Property(c => c.AddressLine1).HasColumnName("address_line1").HasMaxLength(200);
        builder.Property(c => c.AddressLine2).HasColumnName("address_line2").HasMaxLength(200);
        builder.Property(c => c.City).HasColumnName("city").HasMaxLength(100);
        builder.Property(c => c.Country).HasColumnName("country").HasMaxLength(100);
        builder.Property(c => c.Phone).HasColumnName("phone").HasMaxLength(50);
        builder.Property(c => c.Email).HasColumnName("email").HasMaxLength(200);
        builder.Property(c => c.Website).HasColumnName("website").HasMaxLength(200);

        builder.Property(c => c.BankName).HasColumnName("bank_name").HasMaxLength(100);
        builder.Property(c => c.BankBranch).HasColumnName("bank_branch").HasMaxLength(100);
        builder.Property(c => c.BankAccountName).HasColumnName("bank_account_name").HasMaxLength(100);
        builder.Property(c => c.BankAccountNumber).HasColumnName("bank_account_number").HasMaxLength(64);

        builder.Property(c => c.LogoKey).HasColumnName("logo_key").HasMaxLength(255);
        builder.Property(c => c.BrandColour).HasColumnName("brand_colour").HasMaxLength(16);

        Audit.Map(builder);
    }
}

public class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> builder)
    {
        builder.ToTable("app_settings");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");
        builder.Property(s => s.CompanyId).HasColumnName("company_id");
        builder.Property(s => s.Key).HasColumnName("setting_key").HasMaxLength(64).IsRequired();
        builder.Property(s => s.Value).HasColumnName("setting_value").HasMaxLength(500).IsRequired();

        builder.HasIndex(s => new { s.CompanyId, s.Key }).IsUnique();

        Audit.Map(builder);
    }
}

public class DocumentSeriesConfiguration : IEntityTypeConfiguration<DocumentSeries>
{
    public void Configure(EntityTypeBuilder<DocumentSeries> builder)
    {
        builder.ToTable("document_series");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");
        builder.Property(s => s.CompanyId).HasColumnName("company_id");
        builder.Property(s => s.DocType).HasColumnName("doc_type").HasMaxLength(32).IsRequired();
        builder.Property(s => s.Prefix).HasColumnName("prefix").HasMaxLength(32);
        builder.Property(s => s.NextNumber).HasColumnName("next_number");
        builder.Property(s => s.Padding).HasColumnName("padding");

        // One series per document type per company — the constraint that makes
        // "SELECT … FOR UPDATE" on a single row a correct allocator in Phase 5.
        builder.HasIndex(s => new { s.CompanyId, s.DocType }).IsUnique();

        Audit.Map(builder);
    }
}

public class TaxRateConfiguration : IEntityTypeConfiguration<TaxRate>
{
    public void Configure(EntityTypeBuilder<TaxRate> builder)
    {
        builder.ToTable("tax_rates");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id");
        builder.Property(t => t.CompanyId).HasColumnName("company_id");
        builder.Property(t => t.Name).HasColumnName("name").HasMaxLength(64).IsRequired();

        // DECIMAL(9,4), never double. 18% is stored as 18.0000.
        builder.Property(t => t.Percentage)
            .HasColumnName("percentage")
            .HasColumnType("decimal(9,4)");

        builder.Property(t => t.EffectiveFrom).HasColumnName("effective_from");
        builder.Property(t => t.EffectiveTo).HasColumnName("effective_to");
        builder.Property(t => t.IsDefault).HasColumnName("is_default");

        builder.HasIndex(t => new { t.CompanyId, t.EffectiveFrom });

        Audit.Map(builder);
    }
}

public class MailSettingsConfiguration : IEntityTypeConfiguration<MailSettings>
{
    public void Configure(EntityTypeBuilder<MailSettings> builder)
    {
        builder.ToTable("mail_settings");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id");
        builder.Property(m => m.CompanyId).HasColumnName("company_id");
        builder.Property(m => m.Host).HasColumnName("host").HasMaxLength(200).IsRequired();
        builder.Property(m => m.Port).HasColumnName("port");
        builder.Property(m => m.UseSsl).HasColumnName("use_ssl");
        builder.Property(m => m.Username).HasColumnName("username").HasMaxLength(200);

        // Encrypted at rest. Never returned by any endpoint, and redacted in the audit log.
        builder.Property(m => m.PasswordEncrypted)
            .HasColumnName("password_encrypted")
            .HasMaxLength(1024);

        builder.Property(m => m.FromAddress).HasColumnName("from_address").HasMaxLength(200);
        builder.Property(m => m.FromName).HasColumnName("from_name").HasMaxLength(100);
        builder.Property(m => m.ReplyTo).HasColumnName("reply_to").HasMaxLength(200);
        builder.Property(m => m.Bcc).HasColumnName("bcc").HasMaxLength(200);
        builder.Property(m => m.SendEnabled).HasColumnName("send_enabled");
        builder.Property(m => m.DailyLimit).HasColumnName("daily_limit");

        builder.HasIndex(m => m.CompanyId).IsUnique();

        Audit.Map(builder);
    }
}

public class EmailTemplateConfiguration : IEntityTypeConfiguration<EmailTemplate>
{
    public void Configure(EntityTypeBuilder<EmailTemplate> builder)
    {
        builder.ToTable("email_templates");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id");
        builder.Property(t => t.CompanyId).HasColumnName("company_id");
        builder.Property(t => t.TemplateKey).HasColumnName("template_key").HasMaxLength(64).IsRequired();
        builder.Property(t => t.Subject).HasColumnName("subject").HasMaxLength(255).IsRequired();
        builder.Property(t => t.Body).HasColumnName("body").HasColumnType("text").IsRequired();

        builder.HasIndex(t => new { t.CompanyId, t.TemplateKey }).IsUnique();

        Audit.Map(builder);
    }
}

public class EmailLogEntryConfiguration : IEntityTypeConfiguration<EmailLogEntry>
{
    public void Configure(EntityTypeBuilder<EmailLogEntry> builder)
    {
        builder.ToTable("email_log");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.CompanyId).HasColumnName("company_id");
        builder.Property(e => e.Recipient).HasColumnName("recipient").HasMaxLength(320).IsRequired();
        builder.Property(e => e.TemplateKey).HasColumnName("template_key").HasMaxLength(64);
        builder.Property(e => e.DocumentRef).HasColumnName("document_ref").HasMaxLength(64);
        builder.Property(e => e.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(e => e.Error).HasColumnName("error").HasMaxLength(1000);
        builder.Property(e => e.SentAt).HasColumnName("sent_at");
        builder.Property(e => e.SentBy).HasColumnName("sent_by");

        builder.HasIndex(e => new { e.CompanyId, e.SentAt });
        builder.HasIndex(e => e.DocumentRef);
    }
}

/// <summary>
/// The audit columns are identical on every entity that has them, so they are mapped in one place
/// rather than copied into a dozen configurations — where one of them would eventually be missing
/// a column and nobody would notice until the audit trail had a hole in it.
/// </summary>
internal static class Audit
{
    public static void Map<T>(EntityTypeBuilder<T> builder)
        where T : class, Domain.Auditing.IAuditable
    {
        builder.Property(e => e.CreatedBy).HasColumnName("created_by");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        builder.Property(e => e.DeletedBy).HasColumnName("deleted_by");
        builder.Property(e => e.DeletedAt).HasColumnName("deleted_at");
        builder.Property(e => e.RowVersion).HasColumnName("row_version").IsConcurrencyToken();
    }
}
