using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Smartnet.Domain.MasterData;

namespace Smartnet.Infrastructure.Persistence.Configurations;

/// <summary>
/// The master tables, mapped onto the legacy schema they still share with the live legacy app.
/// </summary>
/// <remarks>
/// Two things are true of every configuration in this file, and both matter:
/// <list type="bullet">
/// <item>The legacy column names are kept exactly — <c>cusadd</c>, <c>contactp</c>, <c>vatnum</c>.
/// The old app is still reading them.</item>
/// <item><c>row_version</c> is a concurrency token on every one of them. Two people editing the same
/// customer now conflict loudly instead of one silently overwriting the other, which the legacy app
/// has no protection against at all.</item>
/// </list>
/// <para>
/// <b>No company query filter.</b> Deliberately, and it is not an oversight: the trading entities are
/// not tenants and the customer a Smart Net user is looking at is invoiced by Smart Technologies next
/// week. See <see cref="Customer.AssignedCompanyId"/>.
/// </para>
/// </remarks>
public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("cus_m");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");

        builder.Property(c => c.Code).HasColumnName("cuscode").HasMaxLength(100);
        builder.Property(c => c.Name).HasColumnName("cusname").HasMaxLength(100);
        builder.Property(c => c.Type).HasColumnName("custype").HasMaxLength(100);
        builder.Property(c => c.ContactPerson).HasColumnName("contactp").HasMaxLength(100);
        builder.Property(c => c.Address).HasColumnName("cusadd").HasMaxLength(100);
        builder.Property(c => c.Phone).HasColumnName("contactno").HasMaxLength(100);
        builder.Property(c => c.Email).HasColumnName("email");
        builder.Property(c => c.VatNumber).HasColumnName("vatnum").HasMaxLength(100);

        // The legacy c_form / pro columns: varchars holding numbers, with nothing enforcing that.
        // Mapped as the numbers they are; the migration retypes the columns underneath.
        builder.Property(c => c.AssignedCompanyId).HasColumnName("c_form");
        builder.Property(c => c.ProfitPercentId).HasColumnName("pro");

        // Money. DECIMAL(18,4) everywhere, never double, never varchar — ISSUES B1 and Finding 5.
        builder.Property(c => c.CreditLimit)
            .HasColumnName("climit")
            .HasColumnType("decimal(18,4)");

        builder.ConfigureAuditColumns();
        builder.HasQueryFilter(c => c.DeletedAt == null);

        builder.HasIndex(c => c.Code).IsUnique();

        builder.HasMany(c => c.Contacts)
            .WithOne(ct => ct.Customer)
            .HasForeignKey(ct => ct.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// A customer's structured contacts — a new table (<c>customer_contacts</c>), replacing the legacy
/// <c>;</c>-separated strings (Phase 6, slice 4).
/// </summary>
public class CustomerContactConfiguration : IEntityTypeConfiguration<CustomerContact>
{
    public void Configure(EntityTypeBuilder<CustomerContact> builder)
    {
        builder.ToTable("customer_contacts");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");
        builder.Property(c => c.CustomerId).HasColumnName("customer_id");
        builder.Property(c => c.Name).HasColumnName("name").HasMaxLength(200);
        builder.Property(c => c.Role).HasColumnName("role").HasMaxLength(100);
        builder.Property(c => c.Phone).HasColumnName("phone").HasMaxLength(100);
        builder.Property(c => c.Email).HasColumnName("email").HasMaxLength(200);
        builder.Property(c => c.Usage).HasColumnName("contact_usage").HasMaxLength(32);

        builder.ConfigureAuditColumns();

        // Soft-deleted contacts (a reconcile drops a removed one) stay in the table but out of every read.
        builder.HasQueryFilter(c => c.DeletedAt == null);

        builder.HasIndex(c => c.CustomerId);
    }
}

public class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("sup_m");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");

        builder.Property(s => s.Code).HasColumnName("supcode").HasMaxLength(100);
        builder.Property(s => s.Name).HasColumnName("supname").HasMaxLength(100);
        builder.Property(s => s.ContactPerson).HasColumnName("contactp").HasMaxLength(100);
        builder.Property(s => s.Address).HasColumnName("supadd").HasMaxLength(100);
        builder.Property(s => s.Phone).HasColumnName("contactno").HasMaxLength(100);
        builder.Property(s => s.Email).HasColumnName("email");
        builder.Property(s => s.VatNumber).HasColumnName("vatnum").HasMaxLength(100);

        builder.ConfigureAuditColumns();
        builder.HasQueryFilter(s => s.DeletedAt == null);

        builder.HasIndex(s => s.Code).IsUnique();
    }
}

public class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> builder)
    {
        builder.ToTable("item_m");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasColumnName("id");

        builder.Property(i => i.Code).HasColumnName("itemcode").HasMaxLength(100);
        builder.Property(i => i.Name).HasColumnName("itemname").HasMaxLength(100);

        // The columns Slice 4 adds. New names, cleanly chosen — the legacy app knows nothing of them.
        // Money and quantities are DECIMAL(18,4), never double (B1); the tax rate is a percentage.
        builder.Property(i => i.SellingPrice).HasColumnName("selling_price").HasColumnType("decimal(18,4)");
        builder.Property(i => i.Cost).HasColumnName("cost").HasColumnType("decimal(18,4)");
        builder.Property(i => i.ReorderLevel).HasColumnName("reorder_level").HasColumnType("decimal(18,4)");
        builder.Property(i => i.Unit).HasColumnName("unit").HasMaxLength(32);

        builder.ConfigureAuditColumns();
        builder.HasQueryFilter(i => i.DeletedAt == null);

        builder.HasIndex(i => i.Code).IsUnique();
    }
}

/// <summary>
/// The stock ledger — the one table the new app writes when a quantity moves (B3).
/// </summary>
/// <remarks>
/// It has no legacy counterpart: <c>item_stock</c> stays as the batch table it always was, and this
/// sits beside it as the append-only movement log a balance is derived from. Note the absence of a
/// query filter — there is no <c>deleted_at</c> to filter on, because a movement is never deleted.
/// </remarks>
public class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("stock_movements");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id");
        builder.Property(m => m.ItemId).HasColumnName("item_id");

        // The enum stored as its name, not its ordinal — "Adjustment" survives a reordering of the
        // enum, an integer would silently re-map.
        builder.Property(m => m.Type)
            .HasColumnName("type")
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(m => m.Quantity).HasColumnName("quantity").HasColumnType("decimal(18,4)");
        builder.Property(m => m.Reason).HasColumnName("reason").HasMaxLength(500);
        builder.Property(m => m.OccurredAt).HasColumnName("occurred_at");

        builder.ConfigureAuditColumns();

        // A real foreign key to item_m — the join the legacy schema never had. Restrict, not cascade:
        // an item with movements cannot be hard-deleted out from under its own ledger (and the app
        // soft-deletes anyway).
        builder.HasOne<Item>()
            .WithMany()
            .HasForeignKey(m => m.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        // Stock is always read for one item: "what is its balance, and how did it get there?"
        builder.HasIndex(m => m.ItemId);
    }
}

public class StockBatchConfiguration : IEntityTypeConfiguration<StockBatch>
{
    public void Configure(EntityTypeBuilder<StockBatch> builder)
    {
        builder.ToTable("item_stock");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).HasColumnName("id");

        builder.Property(b => b.ItemCode).HasColumnName("item_code").HasMaxLength(100);

        builder.Property(b => b.UnitCost).HasColumnName("unitcost").HasColumnType("decimal(18,4)");
        builder.Property(b => b.InDate).HasColumnName("indate");
        builder.Property(b => b.Warranty).HasColumnName("warranty").HasMaxLength(100);
        builder.Property(b => b.Quantity).HasColumnName("quantity").HasColumnType("decimal(18,4)");
        builder.Property(b => b.Balance).HasColumnName("balance").HasColumnType("decimal(18,4)");

        builder.Property(b => b.EnteredBy).HasColumnName("enteredby").HasMaxLength(100);
        builder.Property(b => b.EnteredAt).HasColumnName("enteredat");

        builder.ConfigureAuditColumns();
        builder.HasQueryFilter(b => b.DeletedAt == null);

        // Stock is always read for one item at a time: "what have I got, and in which batches?"
        builder.HasIndex(b => b.ItemCode);
    }
}

public class ProfitPercentConfiguration : IEntityTypeConfiguration<ProfitPercent>
{
    public void Configure(EntityTypeBuilder<ProfitPercent> builder)
    {
        builder.ToTable("profit_percent");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.Name).HasColumnName("name").HasMaxLength(100);
    }
}
