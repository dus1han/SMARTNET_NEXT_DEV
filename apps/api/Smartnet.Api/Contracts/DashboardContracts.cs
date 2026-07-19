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
public sealed record CustomerShare(string Code, string Name, decimal Revenue, decimal Share);

/// <summary>
/// One customer and what they are late paying — the answer to the question the ageing chart raises.
/// </summary>
/// <remarks>
/// <paramref name="OldestDays"/> is the age of their oldest unpaid invoice, not an average. Somebody
/// owing a little for four hundred days is a different conversation from somebody owing a lot for
/// forty, and a mean over their invoices would hide exactly that.
/// </remarks>
public sealed record CustomerDebt(string Code, string Name, decimal Owed, int Invoices, int OldestDays);

/// <summary>
/// A customer owing more than their agreed credit limit.
/// </summary>
/// <remarks>
/// The limit lives in <c>cus_m.climit</c> and nothing in either system checks it, which is why this is
/// worth a panel rather than a validation rule alone: the control already exists on paper and is simply
/// not being applied.
/// </remarks>
public sealed record CreditBreach(string Code, string Name, decimal Limit, decimal Owed, decimal Excess);

/// <summary>
/// A customer who used to buy and has gone quiet.
/// </summary>
/// <remarks>
/// <paramref name="StillOwed"/> is the part that matters most. A lapsed customer who owes nothing has
/// simply stopped buying; one who still owes is a relationship that ended with money outstanding, which
/// is a different problem and a more urgent one.
/// </remarks>
public sealed record LapsedCustomer(string Code, string Name, DateOnly LastPurchase, int SilentDays, decimal Lifetime, decimal StillOwed);

/// <summary>One supplier and what has been bought from them — concentration, on the buying side.</summary>
public sealed record SupplierShare(string Name, decimal Spend, decimal Share);

/// <summary>How the month split between cash and credit, and what that is worth.</summary>
public sealed record SalesMix(decimal Cash, decimal Credit, int CashCount, int CreditCount);

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

    /// <summary>Cash against credit for the month — how much of the trade is settled at the counter.</summary>
    SalesMix Mix,

    /// <summary>
    /// Average days between an invoice being raised and being paid, over the last twelve months.
    /// </summary>
    /// <remarks>
    /// The number that turns the ageing chart into a habit rather than a snapshot: ageing says what is
    /// late today, this says how long customers take in general. Null when nothing in the window has
    /// been paid, because an average over no settlements is not zero days — it is unknown.
    /// </remarks>
    int? DaysToCollect,

    /// <summary>Invoices raised this month, and their average value.</summary>
    int InvoiceCount,

    decimal AverageInvoice,

    /// <summary>Who is behind the overdue figure, worst first.</summary>
    IReadOnlyList<CustomerDebt> OverdueByCustomer,

    /// <summary>The suppliers the most is bought from, all time.</summary>
    IReadOnlyList<SupplierShare> TopSuppliers,

    /// <summary>Customers whose first-ever invoice fell in this month, against last month.</summary>
    Trend NewCustomers,

    /// <summary>Customers past their agreed credit limit, worst overrun first.</summary>
    IReadOnlyList<CreditBreach> OverCreditLimit,

    /// <summary>Customers who have not bought in ninety days, by what they used to be worth.</summary>
    IReadOnlyList<LapsedCustomer> LapsedCustomers,

    /// <summary>How many customers have gone quiet in total, and what they were worth.</summary>
    int LapsedCount,

    decimal LapsedValue);

// --- Customer insight (the drill-down behind every dashboard panel) ------------------------------

/// <summary>One invoice on the customer's account, with how old and how settled it is.</summary>
public sealed record CustomerInvoiceRow(
    string Number,
    DateOnly? Date,
    string Type,
    decimal Total,
    decimal Balance,
    int AgeDays);

/// <summary>One receipt against the account.</summary>
public sealed record CustomerPaymentRow(string InvoiceNo, DateOnly? Date, decimal Amount, string Method);

/// <summary>
/// Everything about one customer, gathered for the question the dashboard makes a reader ask.
/// </summary>
/// <remarks>
/// <b>This is the page the panels point at.</b> The dashboard can say Sunx Technologies owes 1,174,480
/// and last bought thirteen months ago; it cannot say which invoices, what was ever paid, or whether
/// there was a dispute — and those are what somebody needs before picking up the phone. Every reading
/// here is the same defensive parse of the same legacy columns the dashboard uses, so the two can never
/// disagree about the same customer.
/// </remarks>
public sealed record CustomerInsight(
    string Code,
    string Name,
    string? Address,
    string? Phone,
    string? VatNumber,

    /// <summary>Their agreed limit, or null when none is recorded — 198 of 223 have none.</summary>
    decimal? CreditLimit,

    decimal Lifetime,
    decimal Outstanding,
    decimal Overdue,
    int InvoiceCount,
    DateOnly? FirstPurchase,
    DateOnly? LastPurchase,
    int? SilentDays,

    /// <summary>Their own average days from invoice to settlement — how this one pays, not the average customer.</summary>
    int? DaysToCollect,

    IReadOnlyList<MonthPoint> MonthlyTrend,
    IReadOnlyList<CustomerInvoiceRow> Invoices,
    IReadOnlyList<CustomerPaymentRow> Payments);

// --- The operations dashboard (the day-to-day one) -----------------------------------------------

/// <summary>One document raised recently — the running record of what the counter has been doing.</summary>
public sealed record RecentDocument(string Number, DateOnly? Date, string Customer, decimal Total, string PreparedBy);

/// <summary>
/// The operations dashboard — what somebody serving customers needs, and nothing about what the
/// business earns.
/// </summary>
/// <remarks>
/// <b>Defined by what it withholds.</b> No profit, no margin, no cost, no supplier spend, no customer
/// lifetime value or concentration. What remains is not a cut-down management view but a different
/// question: not "how are we doing" but "what should I do, and can I sell to this person".
///
/// <para>Company-wide rather than per-user, deliberately. A clerk needs to know a customer is ninety
/// days late and over their limit <i>before</i> selling to them on credit, and whether it was their
/// own colleague who raised the last invoice has nothing to do with it.</para>
/// </remarks>
public sealed record OperationsDashboard(
    int InvoicesToday,
    decimal SalesThisMonth,
    int InvoicesThisMonth,

    /// <summary>Everything still owed — what the counter is collecting against.</summary>
    decimal ToCollect,

    decimal Overdue,
    IReadOnlyList<AgeingBucket> Ageing,
    IReadOnlyList<CustomerDebt> OverdueByCustomer,
    IReadOnlyList<CreditBreach> OverCreditLimit,
    IReadOnlyList<RecentDocument> RecentInvoices);
