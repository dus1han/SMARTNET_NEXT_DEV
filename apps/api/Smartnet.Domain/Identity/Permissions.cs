namespace Smartnet.Domain.Identity;

/// <summary>
/// Every permission in the system, and how each one maps back to the legacy
/// <c>user_permissions</c> table.
/// </summary>
/// <remarks>
/// This is the single source of truth. A permission that is not listed here cannot be granted,
/// cannot be enforced, and cannot be written back to the legacy app — and a policy is registered
/// for each one at startup, so a typo in an endpoint's <c>[RequirePermission]</c> fails the
/// build's policy test rather than silently granting access to everyone.
///
/// <para><b>There are 35 legacy flags, not 36.</b> The planning documents say 36 because
/// <c>user_permissions</c> has 36 columns — but one of them is <c>user_id</c>. Counted from the
/// live schema.</para>
///
/// <para>The legacy column names are preserved exactly, typos and inconsistencies included
/// (<c>chequerpt</c> beside <c>jobcards_rpt</c>), because the legacy app is still reading those
/// columns and will be until Phase 9.</para>
/// </remarks>
public static class Permissions
{
    // --- Permissions that exist only in the new app ------------------------------------------
    // These have no legacy column, so they are never written back. They gate the surfaces the
    // legacy app never had: roles, settings, and the audit log itself.

    /// <summary>
    /// The developer. Bypasses company scoping and reaches the dev-only surfaces.
    /// Not a business role — it is granted deliberately, to a person, and it is audited.
    /// </summary>
    /// <remarks>
    /// Shown in the permission editors as <b>"Developer"</b>, and it is the single grant behind the
    /// Administration screens that are not a company's business: <c>/companies</c> (adding a trading
    /// entity) and <c>/vat-rate</c> (setting the rate for all of them). Those screens deliberately have no
    /// permissions of their own — a surface reachable only by the superuser should not also be grantable
    /// piecemeal, or "only the developer can do this" becomes untrue the moment someone ticks a box.
    /// <para>
    /// Handing it out is itself restricted to a Dev_Admin — see <c>RolesController.CanGrant</c> and
    /// <c>UsersController</c> — so it cannot be self-awarded by an administrator who merely reached the
    /// permission editor.
    /// </para>
    /// </remarks>
    public const string SystemDevAdmin = "system.dev_admin";

    public const string RolesManage = "roles.manage";
    public const string SettingsManage = "settings.manage";
    public const string AuditView = "audit.view";

    /// <summary>The general ledger / trial balance — a surface the legacy app never had.</summary>
    public const string GeneralLedger = "general_ledger";

    /// <summary>
    /// The operations dashboard — the day-to-day one, without the money insights.
    /// </summary>
    /// <remarks>
    /// The counterpart to <see cref="Dashboard"/>, which is the management view: profit, margin, cost,
    /// supplier spend and customer concentration. This one shows what somebody serving customers needs
    /// and nothing about what the business earns.
    ///
    /// <para><b>Exactly one of the two is required</b> — see <see cref="DashboardPermissions"/>. Holding
    /// neither leaves a user with no landing page at all; holding both is not a superset but a
    /// contradiction, because the whole point of this one is what it withholds.</para>
    /// </remarks>
    public const string DashboardOperations = "dashboard.operations";

    /// <summary>
    /// The two dashboards. A user must hold exactly one.
    /// </summary>
    /// <remarks>
    /// Kept as a set rather than checked by name in three places, so the rule and the list cannot drift
    /// apart if a third dashboard is ever added.
    /// </remarks>
    public static readonly IReadOnlyList<string> DashboardPermissions = [Dashboard, DashboardOperations];

    /// <summary>The new permissions, in the order they should appear in the admin UI.</summary>
    public static readonly IReadOnlyList<string> NewPermissions =
        [SystemDevAdmin, RolesManage, SettingsManage, AuditView, GeneralLedger, DashboardOperations];

    // --- The 35 legacy flags -----------------------------------------------------------------

    public const string Dashboard = "dashboard";
    public const string CustomerM = "customer_m";
    public const string SupplierM = "supplier_m";
    public const string ItemM = "item_m";
    public const string ItemStock = "itemstock";
    public const string ItemQuotation = "item_qu";
    public const string ServiceQuotation = "service_qu";
    public const string SearchQuotation = "search_qu";
    public const string ItemInvoice = "item_in";
    public const string ServiceInvoice = "service_in";
    public const string SearchInvoice = "search_in";
    public const string DeletedInvoices = "deleted_in";
    public const string JobCards = "jobcards";
    public const string JobCardsReport = "jobcards_rpt";
    public const string NewCreditNote = "new_cn";
    public const string SearchCreditNote = "search_cn";
    public const string Payments = "payments";
    public const string CustomerOutstanding = "customer_outstanding";
    public const string PurchaseOrder = "purchaseorder";
    public const string SearchPurchaseOrder = "search_po";
    public const string SupplierInvoice = "supplier_in";
    public const string Expenses = "expenses";
    public const string ExpensesReport = "expenses_rpt";
    public const string SalesReport = "sales_rpt";
    public const string CustomerSalesReport = "customersales_rpt";
    public const string SupplierPurchaseReport = "supplierpurchase_rpt";
    public const string SupplierPaymentsReport = "supplierpayments_rpt";
    public const string CustomerVatReport = "cusvat_rpt";
    public const string SupplierVatReport = "suppliervat_rpt";
    public const string Users = "users";
    public const string DocStorage = "docstorage";
    public const string Notes = "notes";
    public const string Email = "email";
    public const string Cheques = "cheques";
    public const string ChequesReport = "chequerpt";

    /// <summary>
    /// The legacy flags. The key is the permission; the value is the <c>user_permissions</c>
    /// column it is written back to, which happens to be identical — deliberately, so that the
    /// write-through cannot drift.
    /// </summary>
    public static readonly IReadOnlyList<string> LegacyPermissions =
    [
        Dashboard, CustomerM, SupplierM, ItemM, ItemStock,
        ItemQuotation, ServiceQuotation, SearchQuotation,
        ItemInvoice, ServiceInvoice, SearchInvoice, DeletedInvoices,
        JobCards, JobCardsReport,
        NewCreditNote, SearchCreditNote,
        Payments, CustomerOutstanding,
        PurchaseOrder, SearchPurchaseOrder, SupplierInvoice,
        Expenses, ExpensesReport,
        SalesReport, CustomerSalesReport, SupplierPurchaseReport, SupplierPaymentsReport,
        CustomerVatReport, SupplierVatReport,
        Users, DocStorage, Notes, Email, Cheques, ChequesReport,
    ];

    /// <summary>Every permission that can be granted.</summary>
    public static readonly IReadOnlyList<string> All =
        [.. NewPermissions, .. LegacyPermissions];

    private static readonly HashSet<string> Known = new(All, StringComparer.Ordinal);

    public static bool IsKnown(string permission) => Known.Contains(permission);

    /// <summary>
    /// True when the permission has a column in the legacy table, and so must be written back to
    /// it while the old app is still running.
    /// </summary>
    public static bool IsLegacy(string permission) => LegacyPermissions.Contains(permission);
}
