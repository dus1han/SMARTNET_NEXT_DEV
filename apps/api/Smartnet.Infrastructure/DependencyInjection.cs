using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Smartnet.Domain.Auditing;
using Smartnet.Domain.Documents;
using Smartnet.Domain.Reporting;
using Smartnet.Infrastructure.Auditing;
using Smartnet.Infrastructure.Documents;
using Smartnet.Infrastructure.Persistence;

namespace Smartnet.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers both contexts and the audit machinery.
    /// </summary>
    /// <remarks>
    /// The audit interceptor is attached here, at the composition root, rather than left to each
    /// DbContext registration to remember. There is exactly one way to get a
    /// <see cref="SmartnetDbContext"/> in this application, and it audits.
    /// </remarks>
    public static IServiceCollection AddSmartnetPersistence(
        this IServiceCollection services,
        string connectionString)
    {
        var serverVersion = SmartnetServerVersion.Value;

        services.AddScoped<AuditSaveChangesInterceptor>();
        services.AddScoped<IAuditWriter, AuditWriter>();

        // The read side. Every history surface goes through it, so the company scoping on the audit
        // tables lives in one place rather than being re-derived on each screen that reads them.
        services.AddScoped<IAuditHistory, AuditHistoryReader>();

        // The write side of document_versions — Phase 5's supply of the writer Phase 1 left for it.
        services.AddScoped<IDocumentVersionWriter, DocumentVersionWriter>();

        // The invoice save pipeline (Phase 5, slice 1): tax engine + number + ledger + stock + snapshot,
        // one transaction.
        services.AddScoped<IInvoiceCreator, InvoiceCreator>();

        // Quotations (Phase 5, slice 3): the same pipeline without ledger or stock, and the converter
        // that turns a quote into an invoice through the invoice pipeline, once only.
        services.AddScoped<IQuotationCreator, QuotationCreator>();
        services.AddScoped<IQuotationConverter, QuotationConverter>();

        // Quotation edit/void + legacy adoption (Phase 5, slice 5, legacy parity): the mirror of the invoice
        // edit/void without a ledger or stock; a spent (converted) quote is not editable.
        services.AddScoped<ILegacyQuotationAdopter, LegacyQuotationAdopter>();
        services.AddScoped<IQuotationEditor, QuotationEditor>();
        services.AddScoped<IQuotationDeleter, QuotationDeleter>();

        // Credit notes (Phase 5, slice 4): the mirror of the invoice pipeline — a Credit ledger entry
        // (opposite sign) and, where goods are returned, a stock receipt, at the parent invoice's rate.
        services.AddScoped<ICreditNoteCreator, CreditNoteCreator>();

        // Legacy invoice adoption (Phase 5, slice 5b, legacy parity): materialise a legacy invoice into the
        // new model on first edit/void — reused by the go-live bulk migration.
        services.AddScoped<ILegacyInvoiceAdopter, LegacyInvoiceAdopter>();

        // The versioned, reason-gated, concurrency-guarded invoice edit (Phase 5, slice 5): re-tax at the
        // snapshot rate, reconcile lines in place, write a new version, adjust the ledger by a delta.
        services.AddScoped<IInvoiceEditor, InvoiceEditor>();

        // The soft, recoverable, attributable invoice delete (Phase 5, slice 5): reverse the ledger and
        // stock through new entries, then soft-delete — never erase.
        services.AddScoped<IInvoiceDeleter, InvoiceDeleter>();

        // Purchase orders (Phase 6, slice 1): the quotation pipeline addressed to a supplier — tax engine
        // + number + snapshot, one transaction, no ledger and no stock (an order, not a payable or a
        // receipt; the payable is the supplier invoice, the receipt the deferred GRN).
        services.AddScoped<IPurchaseOrderCreator, PurchaseOrderCreator>();

        // Supplier invoices (Phase 6, slice 2): the accounts-payable record — a Purchase payable entry on
        // create, Payment entries for (partial) payments, a soft, reason-gated void that reverses the
        // payable. One service behind both interfaces, so the create and payment paths share a scope.
        services.AddScoped<SupplierInvoiceService>();
        services.AddScoped<ISupplierInvoiceCreator>(sp => sp.GetRequiredService<SupplierInvoiceService>());
        services.AddScoped<ISupplierInvoicePayments>(sp => sp.GetRequiredService<SupplierInvoiceService>());

        // Job cards (Phase 6, slice 3): the lightest document — no tax, ledger or stock — with structured
        // serial lines and a guarded PENDING -> CLOSED close. One service behind both interfaces.
        services.AddScoped<JobCardService>();
        services.AddScoped<IJobCardCreator>(sp => sp.GetRequiredService<JobCardService>());
        services.AddScoped<IJobCardWorkflow>(sp => sp.GetRequiredService<JobCardService>());

        // Customer receipts (Phase 7, slice 1): money received, allocated across open invoices — Payment
        // entries on the receivables ledger, dual-writing the legacy payments rows + invoice_h.balance,
        // idempotent, with a soft void that reverses. One service behind create and void.
        services.AddScoped<CustomerReceiptService>();
        services.AddScoped<ICustomerReceiptCreator>(sp => sp.GetRequiredService<CustomerReceiptService>());
        services.AddScoped<ICustomerReceiptVoider>(sp => sp.GetRequiredService<CustomerReceiptService>());

        // Data-exception corrections (LEGACY-DATA-POLICY §4): audited, transactional fixes behind the Data
        // Exceptions screen — remove a duplicate payment, record a missing one, or restore a receivable.
        services.AddScoped<IDataExceptionResolver, DataExceptionResolver>();

        // Supplier payments (Phase 7): the payables mirror — money paid, allocated across supplier invoices
        // (new and adopted-legacy alike), Payment entries on the payables ledger dual-writing supplier_inv_pay
        // + paymentstat, idempotent, with a soft void that reverses. One service behind create and void.
        services.AddScoped<SupplierPaymentService>();
        services.AddScoped<ISupplierPaymentCreator>(sp => sp.GetRequiredService<SupplierPaymentService>());
        services.AddScoped<ISupplierPaymentVoider>(sp => sp.GetRequiredService<SupplierPaymentService>());

        // Cheques (Phase 7, slice 2): the cheque register — a standalone adopted record (no ledger, no
        // balance) that dual-writes the legacy cheques row for the surviving ChequeReport. One service.
        services.AddScoped<ChequeService>();
        services.AddScoped<IChequeCreator>(sp => sp.GetRequiredService<ChequeService>());
        services.AddScoped<IChequeVoider>(sp => sp.GetRequiredService<ChequeService>());

        // Expenses (Phase 7, slice 3): a flat adopted log (no ledger, no balance) that dual-writes the legacy
        // expense_tr row for the surviving ExpenseReport. One service; categories are managed on the controller.
        services.AddScoped<ExpenseService>();
        services.AddScoped<IExpenseCreator>(sp => sp.GetRequiredService<ExpenseService>());
        services.AddScoped<IExpenseVoider>(sp => sp.GetRequiredService<ExpenseService>());

        services.AddDbContext<SmartnetDbContext>((provider, options) => options
            .UseMySql(connectionString, serverVersion)
            .AddInterceptors(provider.GetRequiredService<AuditSaveChangesInterceptor>()));

        // The Phase 0 scaffold. Read-only reference to the legacy schema — 46 of its 49 tables
        // are keyless, so EF cannot write to them even if we wanted it to. Tables leave it for
        // SmartnetDbContext as each phase adopts them; it is deleted in Phase 9.
        services.AddDbContext<SmartnetLegacyDbContext>(options => options
            .UseMySql(connectionString, serverVersion)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));

        return services;
    }
}
