namespace Smartnet.Api.Contracts;

// One dashboard, two shapes, chosen by permission — the old AdminDashboard and UserDashboard collapsed.
// Read-only over the legacy invoice_h, scoped to the company the caller is signed into, every figure
// parsed once and defensively (see LegacyValue). The customer dashboard is gone — the business
// confirmed it is unused (Finding 7).

/// <param name="View">"company" (holds the <c>dashboard</c> permission) or "my" (does not).</param>
/// <param name="Approximate">True for the "my" view: it joins on the legacy <c>preparedby</c> name
/// string, which a rename breaks. Phase 5 gives documents a <c>prepared_by</c> user id and this
/// becomes exact.</param>
/// <param name="Outstanding">The legacy <c>balance</c> column summed, as the business reports it today.
/// That column is wrong by Rs 1.55M (Finding 1); Phase 4 reports it, the remediation phase fixes it.
/// This tile shows it plainly — the outstanding *report* (slice 5) is where it is cross-flagged.</param>
/// <param name="Profit"><c>Σ(total − cost)</c> at header level, matching the sales report. Cost is
/// cost-at-current, not cost-at-sale — the same limitation the old dashboard had.</param>
/// <param name="SelectedCompanyId">The company the figures are scoped to, or null for "all" — every
/// company the caller may see, aggregated. Echoed back so the client can confirm what it is showing.</param>
/// <param name="Companies">The companies the caller may filter by — their accessible set, resolved from
/// the token. The client builds the in-card selector from this; a company they cannot see is never in
/// it, so the filter can only ever narrow to something already permitted.</param>
public sealed record DashboardResponse(
    string View,
    bool Approximate,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal CashSales,
    decimal CreditSales,
    decimal TotalSales,
    decimal Outstanding,
    decimal Profit,
    int FlaggedCount,
    IReadOnlyList<DailySalesPoint> Chart,
    long? SelectedCompanyId,
    IReadOnlyList<DashboardCompanyOption> Companies);

/// <summary>One day of the month: cash and credit sales, for the daily chart.</summary>
public sealed record DailySalesPoint(DateOnly Date, decimal Cash, decimal Credit);

/// <summary>A company the dashboard can be filtered to — one option in the in-card selector.</summary>
public sealed record DashboardCompanyOption(long Id, string Name);
