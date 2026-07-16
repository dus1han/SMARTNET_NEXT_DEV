using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Smartnet.Domain.Auditing;
using Smartnet.Domain.Documents;
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
