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

// --- Analytics (Phase 8) ------------------------------------------------------------------------

/// <summary>One month on a trend line — the figure and the month it belongs to.</summary>
public sealed record MonthPoint(DateOnly Month, decimal Revenue, decimal Profit);

/// <summary>One month of cash movement: what came in, what went out.</summary>
public sealed record CashFlowPoint(DateOnly Month, decimal In, decimal Out);

/// <summary>
/// Receivables by how long they have been owed.
/// </summary>
/// <remarks>
/// The buckets match the outstanding report's, so the dashboard and the report cannot tell different
/// stories about the same money.
/// </remarks>
public sealed record AgeingBucket(string Label, decimal Amount, int Invoices);

/// <summary>One customer's contribution to revenue over the window.</summary>
public sealed record CustomerShare(string Name, decimal Revenue, decimal Share);

/// <summary>
/// One item line: what it sold for, and how much of it moved.
/// </summary>
/// <remarks>
/// <b>Revenue and units, not margin.</b> <c>invoice_l</c> carries no cost column — the legacy system
/// costed a document, never a line — so per-item profit cannot be computed from this data. It could be
/// approximated by apportioning the invoice cost across its lines, but that would put a figure captioned
/// "margin" on the screen that is an assumption about how cost distributes, which is precisely the kind
/// of number people go on to make stocking decisions with. Document margin is real and is shown; item
/// margin is not available and is not implied.
/// </remarks>
public sealed record ItemSales(string Description, decimal Revenue, decimal Quantity, decimal Share);

/// <summary>
/// A figure with the previous period beside it, so a number arrives with its direction.
/// </summary>
/// <remarks>
/// A bare "Revenue 1.2M" tells nobody whether that is good. The comparison is what makes it a reading
/// rather than a total, which is why every headline figure on the dashboard carries one.
/// </remarks>
public sealed record Trend(decimal Value, decimal Previous)
{
    /// <summary>The change as a percentage, or null when there is no base to compare against.</summary>
    public decimal? ChangePercent => Previous == 0m ? null : decimal.Round((Value - Previous) / Math.Abs(Previous) * 100m, 1);
}

/// <summary>
/// The analytical half of the dashboard — the questions a trading business asks about itself.
/// </summary>
/// <remarks>
/// Separate from <see cref="DashboardResponse"/>, which is the at-a-glance month. This is the part that
/// takes real aggregation, so it is its own request: the tiles paint immediately and the analysis fills
/// in behind them, rather than the whole screen waiting on a twelve-month scan.
/// </remarks>
public sealed record DashboardAnalytics(
    Trend Revenue,
    Trend GrossProfit,
    Trend Collected,

    /// <summary>Margin on the current period, as a percentage of revenue.</summary>
    decimal MarginPercent,

    /// <summary>Everything owed past its due date — the actionable half of "outstanding".</summary>
    decimal Overdue,

    IReadOnlyList<MonthPoint> MonthlyTrend,
    IReadOnlyList<AgeingBucket> Ageing,
    IReadOnlyList<CashFlowPoint> CashFlow,
    IReadOnlyList<CustomerShare> TopCustomers,

    /// <summary>What share of revenue the top five customers account for — concentration risk.</summary>
    decimal TopCustomerShare,

    IReadOnlyList<ItemSales> TopItems);
