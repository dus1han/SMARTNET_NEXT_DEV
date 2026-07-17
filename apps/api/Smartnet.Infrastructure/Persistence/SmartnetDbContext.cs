using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Auditing;
using Smartnet.Domain.Documents;
using Smartnet.Domain.Identity;
using Smartnet.Domain.Ledger;
using Smartnet.Domain.MasterData;
using Smartnet.Domain.Settings;

namespace Smartnet.Infrastructure.Persistence;

/// <summary>
/// The application's own context: it owns the migrations, and everything it tracks is audited.
/// </summary>
/// <remarks>
/// It sits alongside the Phase 0 scaffolded <see cref="SmartnetLegacyDbContext"/>, which mirrors
/// the legacy schema as it stands (46 of its 49 tables are keyless, so EF cannot write to them
/// at all). Tables move across to this context as each phase adopts them, gaining a primary key
/// and audit columns on the way. The legacy context is deleted in Phase 9 along with the app.
/// </remarks>
public class SmartnetDbContext : DbContext
{
    public SmartnetDbContext(DbContextOptions<SmartnetDbContext> options) : base(options)
    {
    }

    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();

    /// <summary>
    /// The legacy <c>user_m</c> table, adopted into this context in Slice 2. It is the first
    /// legacy table to cross over: it gains a primary key, audit columns and a password hash,
    /// all additively, while the legacy app keeps reading and writing it.
    /// </summary>
    public DbSet<User> Users => Set<User>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<UserRole> UserRoles => Set<UserRole>();

    public DbSet<UserPermissionOverride> UserPermissionOverrides => Set<UserPermissionOverride>();

    // --- Settings (Slice 4) -------------------------------------------------------------------

    /// <summary>The legacy <c>companies_m</c>, adopted and extended with the document header.</summary>
    public DbSet<Company> Companies => Set<Company>();

    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    public DbSet<DocumentSeries> DocumentSeries => Set<DocumentSeries>();

    public DbSet<TaxRate> TaxRates => Set<TaxRate>();

    public DbSet<MailSettings> MailSettings => Set<MailSettings>();

    public DbSet<EmailTemplate> EmailTemplates => Set<EmailTemplate>();

    public DbSet<EmailLogEntry> EmailLog => Set<EmailLogEntry>();

    // --- Master data (Phase 3, slice 1) -------------------------------------------------------
    //
    // Three more legacy tables cross over: cus_m, sup_m and item_m, none of which had a primary key
    // (Finding 6 — three keys in a 49-table database). They gain one, the audit columns, and — in
    // the case of the money and date columns — a type. Additively: the legacy app is still writing
    // all three.

    public DbSet<Customer> Customers => Set<Customer>();

    /// <summary>A customer's structured contacts (Phase 6, slice 4) — the real rows behind the legacy strings.</summary>
    public DbSet<CustomerContact> CustomerContacts => Set<CustomerContact>();

    public DbSet<Supplier> Suppliers => Set<Supplier>();

    public DbSet<Item> Items => Set<Item>();

    /// <summary>Stock receipts. Six rows, and nothing has ever consumed one — see <see cref="StockBatch"/>.</summary>
    public DbSet<StockBatch> StockBatches => Set<StockBatch>();

    /// <summary>The immutable stock ledger. An item's balance is the sum of these — see <see cref="StockMovement"/>.</summary>
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    /// <summary>Reference data: the margin bands a customer can be put on.</summary>
    public DbSet<ProfitPercent> ProfitPercents => Set<ProfitPercent>();

    // --- Documents (Phase 5) -----------------------------------------------------------------

    /// <summary>Invoices, on the adopted legacy <c>invoice_h</c>. Balance is derived from the ledger.</summary>
    public DbSet<Invoice> Invoices => Set<Invoice>();

    /// <summary>Invoice lines, on the adopted legacy <c>invoice_l</c>.</summary>
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();

    /// <summary>Quotations, on the adopted legacy <c>quotation_h</c>. No ledger, no stock — a priced offer.</summary>
    public DbSet<Quotation> Quotations => Set<Quotation>();

    /// <summary>Quotation lines, on the adopted legacy <c>quotation_l</c>.</summary>
    public DbSet<QuotationLine> QuotationLines => Set<QuotationLine>();

    /// <summary>Credit notes, on the adopted legacy <c>cn_h</c>. Posts the opposite ledger sign to an invoice.</summary>
    public DbSet<CreditNote> CreditNotes => Set<CreditNote>();

    /// <summary>Credit-note lines, on the adopted legacy <c>cn_l</c>.</summary>
    public DbSet<CreditNoteLine> CreditNoteLines => Set<CreditNoteLine>();

    /// <summary>The receivables ledger. A customer's balance is the sum of these — see <see cref="LedgerEntry"/>.</summary>
    public DbSet<LedgerEntry> ReceivablesLedger => Set<LedgerEntry>();

    // --- Purchasing (Phase 6) ----------------------------------------------------------------

    /// <summary>Purchase orders, on the adopted legacy <c>po_h</c>. An order — no ledger, no stock.</summary>
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();

    /// <summary>Purchase-order lines, on the adopted legacy <c>po_l</c>.</summary>
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();

    /// <summary>Supplier invoices, on the adopted legacy <c>supplier_invoice</c>. Header-only; the payable is the ledger.</summary>
    public DbSet<SupplierInvoice> SupplierInvoices => Set<SupplierInvoice>();

    /// <summary>The payables ledger. A supplier's balance is the sum of these — see <see cref="PayablesLedgerEntry"/>.</summary>
    public DbSet<PayablesLedgerEntry> PayablesLedger => Set<PayablesLedgerEntry>();

    /// <summary>Customer receipts (Phase 7) — money received, allocated across invoices; the settlement is the ledger.</summary>
    public DbSet<CustomerReceipt> CustomerReceipts => Set<CustomerReceipt>();

    /// <summary>Receipt allocations — one per invoice a receipt settles.</summary>
    public DbSet<ReceiptAllocation> ReceiptAllocations => Set<ReceiptAllocation>();

    /// <summary>Job cards, on the adopted legacy <c>jobs_m</c>. A service/repair document — no tax, ledger or stock.</summary>
    public DbSet<JobCard> JobCards => Set<JobCard>();

    /// <summary>Job-card lines (structured serial units), on the new <c>jobcard_l</c> table.</summary>
    public DbSet<JobCardLine> JobCardLines => Set<JobCardLine>();

    /// <summary>
    /// Wraps the save in a transaction so that the business change and the audit rows the
    /// interceptor writes for it commit together — or not at all.
    /// </summary>
    /// <remarks>
    /// <b>This override is load-bearing.</b> The interceptor writes its rows in a second save
    /// (an inserted row has no key until the INSERT has run, and the audit row must name it).
    /// Without an enclosing transaction those are two independent commits, and a failure between
    /// them leaves a business change with no audit trail — the exact divergence AUDIT.md exists
    /// to prevent.
    /// <para>
    /// When the caller has already opened a transaction — the normal case for a business
    /// operation, which is required to be one transaction — we join theirs rather than nesting.
    /// </para>
    /// </remarks>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (Database.CurrentTransaction is not null)
        {
            return await base.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var transaction = await Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var written = await base.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return written;
    }

    /// <summary>
    /// The synchronous path is not supported: the audit interceptor's second write is async, and
    /// a half-audited save is worse than a failed one.
    /// </summary>
    public override int SaveChanges() => throw new NotSupportedException(
        "Use SaveChangesAsync. The audit interceptor writes its rows asynchronously, and the " +
        "synchronous path cannot guarantee they land in the same transaction.");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SmartnetDbContext).Assembly);

        // A property-bag entity, so it cannot be registered by IEntityTypeConfiguration.
        LegacyUserPermissions.Configure(modelBuilder);

        base.OnModelCreating(modelBuilder);
    }
}
