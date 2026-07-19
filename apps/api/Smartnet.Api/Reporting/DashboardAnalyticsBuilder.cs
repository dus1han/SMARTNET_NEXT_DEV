using Smartnet.Api.Contracts;
using Smartnet.Infrastructure.Entities;
using Smartnet.Infrastructure.Reporting;

namespace Smartnet.Api.Reporting;

/// <summary>
/// The analytical dashboard — the pure half, so every figure on the screen is unit-testable.
/// </summary>
/// <remarks>
/// <b>Every reading here answers a question somebody actually asks about the business</b>, which is the
/// filter for what belongs. A chart that is merely available is clutter; the six below are the ones a
/// trading business runs on:
/// <list type="bullet">
///   <item><b>Trend</b> — twelve months of revenue and profit, because a month on its own has no
///   direction and this business is visibly seasonal.</item>
///   <item><b>Ageing</b> — where the receivable actually is. One "outstanding" total says how much is
///   owed but not how worried to be, and those are different questions.</item>
///   <item><b>Cash in and out</b> — profit is not cash. A business can be earning and still run out.</item>
///   <item><b>Customer concentration</b> — the largest single risk in a small trading company, and one
///   nobody sees until the customer leaves.</item>
///   <item><b>Item revenue</b> — what actually moves, and in what quantity.</item>
/// </list>
///
/// <para><b>Money is parsed defensively throughout</b> — <see cref="LegacyValue"/>, so a varchar that
/// will not parse counts as zero rather than throwing. Same posture as every other report over this
/// data, and the reason the dashboard already carries a "flagged" count.</para>
/// </remarks>
public static class DashboardAnalyticsBuilder
{
    /// <summary>How many months of history the trend shows.</summary>
    private const int TrendMonths = 12;

    /// <summary>How many months of cash movement to show — a shorter window, read more closely.</summary>
    private const int CashFlowMonths = 6;

    private const int TopN = 5;

    public static DashboardAnalytics Build(
        IReadOnlyList<InvoiceH> invoices,
        IReadOnlyList<InvoiceL> lines,
        IReadOnlyList<Payment> payments,
        IReadOnlyList<ExpenseTr> expenses,
        IReadOnlyList<(DateOnly? Date, decimal Amount)> supplierPayments,
        IReadOnlyList<(string SupplierCode, decimal Amount)> supplierSpend,
        IReadOnlyDictionary<string, string> customerNames,
        IReadOnlyDictionary<string, string> supplierNames,
        IReadOnlyDictionary<string, decimal> creditLimits,
        DateOnly today)
    {
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var prevStart = monthStart.AddMonths(-1);

        var dated = invoices
            .Select(h => new Sale(
                LegacyValue.Date(h.Indate),
                LegacyValue.Money(h.Totamount),
                LegacyValue.Money(h.Cost),
                LegacyValue.Money(h.Balance),
                h.Customer ?? string.Empty,
                h.Invoiceno ?? string.Empty,
                string.Equals(h.Invtype, "Credit", StringComparison.OrdinalIgnoreCase)))
            .Where(s => s.Date is not null)
            .ToList();

        var thisMonth = dated.Where(s => InMonth(s.Date!.Value, monthStart)).ToList();
        var lastMonth = dated.Where(s => InMonth(s.Date!.Value, prevStart)).ToList();

        var revenue = new Trend(thisMonth.Sum(s => s.Total), lastMonth.Sum(s => s.Total));
        var profit = new Trend(
            thisMonth.Sum(s => s.Total - s.Cost),
            lastMonth.Sum(s => s.Total - s.Cost));

        var receipts = payments
            .Select(p => (Date: LegacyValue.Date(p.Paymentrecdate), Amount: LegacyValue.Money(p.Amount)))
            .Where(p => p.Date is not null)
            .ToList();

        var collected = new Trend(
            receipts.Where(p => InMonth(p.Date!.Value, monthStart)).Sum(p => p.Amount),
            receipts.Where(p => InMonth(p.Date!.Value, prevStart)).Sum(p => p.Amount));

        return new DashboardAnalytics(
            Revenue: revenue,
            GrossProfit: profit,
            Collected: collected,
            MarginPercent: Percent(profit.Value, revenue.Value),
            Overdue: Ageing(dated, today).Where(b => b.Label != "Current").Sum(b => b.Amount),
            MonthlyTrend: Trend12(dated, monthStart),
            Ageing: Ageing(dated, today),
            CashFlow: CashFlow(receipts, expenses, supplierPayments, monthStart),
            TopCustomers: TopCustomers(thisMonth, customerNames, out var topShare),
            TopCustomerShare: topShare,
            Mix: new SalesMix(
                thisMonth.Where(s => !s.IsCredit).Sum(s => s.Total),
                thisMonth.Where(s => s.IsCredit).Sum(s => s.Total),
                thisMonth.Count(s => !s.IsCredit),
                thisMonth.Count(s => s.IsCredit)),
            DaysToCollect: DaysToCollect(dated, payments, monthStart),
            InvoiceCount: thisMonth.Count,
            AverageInvoice: thisMonth.Count == 0 ? 0m : decimal.Round(thisMonth.Sum(s => s.Total) / thisMonth.Count, 2),
            OverdueByCustomer: OverdueByCustomer(dated, customerNames, today),
            TopSuppliers: TopSuppliers(supplierSpend, supplierNames),
            NewCustomers: NewCustomers(dated, monthStart),
            OverCreditLimit: OverCreditLimit(dated, customerNames, creditLimits),
            LapsedCustomers: Lapsed(dated, customerNames, today, out var lapsedCount, out var lapsedValue),
            LapsedCount: lapsedCount,
            LapsedValue: lapsedValue);
    }

    /// <summary>Revenue and profit by month, oldest first, with empty months kept so gaps show as gaps.</summary>
    private static List<MonthPoint> Trend12(IReadOnlyList<Sale> sales, DateOnly monthStart)
    {
        var points = new List<MonthPoint>(TrendMonths);

        for (var i = TrendMonths - 1; i >= 0; i--)
        {
            var month = monthStart.AddMonths(-i);
            var inMonth = sales.Where(s => InMonth(s.Date!.Value, month)).ToList();

            points.Add(new MonthPoint(month, inMonth.Sum(s => s.Total), inMonth.Sum(s => s.Total - s.Cost)));
        }

        return points;
    }

    /// <summary>
    /// What is owed, by how long it has been owed.
    /// </summary>
    /// <remarks>
    /// Ages from the invoice date, as the outstanding report does — this data has no due date, and
    /// inventing one from payment terms nobody recorded would make the buckets look precise while being
    /// a guess. Only invoices with something still on them are counted.
    /// </remarks>
    private static List<AgeingBucket> Ageing(IReadOnlyList<Sale> sales, DateOnly today)
    {
        var open = sales.Where(s => s.Balance > 0m).ToList();

        (string Label, Func<int, bool> Match)[] buckets =
        [
            ("Current", days => days <= 30),
            ("31–60 days", days => days is > 30 and <= 60),
            ("61–90 days", days => days is > 60 and <= 90),
            ("Over 90 days", days => days > 90),
        ];

        return buckets
            .Select(b =>
            {
                var matching = open.Where(s => b.Match(Math.Max(0, today.DayNumber - s.Date!.Value.DayNumber))).ToList();
                return new AgeingBucket(b.Label, matching.Sum(s => s.Balance), matching.Count);
            })
            .ToList();
    }

    /// <summary>
    /// Money in against money out, by month.
    /// </summary>
    /// <remarks>
    /// In is customer receipts; out is expenses plus what was paid to suppliers. Deliberately cash
    /// movement rather than accruals — the question this answers is whether the bank balance is being
    /// refilled faster than it is drained, which invoices raised and bills received do not tell you.
    /// </remarks>
    private static List<CashFlowPoint> CashFlow(
        IReadOnlyList<(DateOnly? Date, decimal Amount)> receipts,
        IReadOnlyList<ExpenseTr> expenses,
        IReadOnlyList<(DateOnly? Date, decimal Amount)> supplierPayments,
        DateOnly monthStart)
    {
        var spend = expenses
            .Select(e => (Date: LegacyValue.Date(e.ExpenseDate), Amount: LegacyValue.Money(e.ExpenseAmount)))
            .Concat(supplierPayments)
            .Where(x => x.Date is not null)
            .ToList();

        var points = new List<CashFlowPoint>(CashFlowMonths);

        for (var i = CashFlowMonths - 1; i >= 0; i--)
        {
            var month = monthStart.AddMonths(-i);

            points.Add(new CashFlowPoint(
                month,
                receipts.Where(r => InMonth(r.Date!.Value, month)).Sum(r => r.Amount),
                spend.Where(s => InMonth(s.Date!.Value, month)).Sum(s => s.Amount)));
        }

        return points;
    }

    /// <summary>
    /// The largest customers by revenue, and what share the top few represent.
    /// </summary>
    /// <remarks>
    /// The share is the point, not the ranking. A trading company that does not know one customer is
    /// half its revenue discovers it the month that customer stops buying.
    /// </remarks>
    private static List<CustomerShare> TopCustomers(
        IReadOnlyList<Sale> sales,
        IReadOnlyDictionary<string, string> customerNames,
        out decimal topShare)
    {
        var total = sales.Sum(s => s.Total);

        var byCustomer = sales
            .Where(s => s.CustomerCode.Length > 0)
            .GroupBy(s => s.CustomerCode, StringComparer.Ordinal)
            .Select(g => new { Code = g.Key, Revenue = g.Sum(s => s.Total) })
            .OrderByDescending(x => x.Revenue)
            .ToList();

        topShare = Percent(byCustomer.Take(TopN).Sum(x => x.Revenue), total);

        return byCustomer
            .Take(TopN)
            .Select(x => new CustomerShare(
                customerNames.GetValueOrDefault(x.Code, x.Code),
                x.Revenue,
                Percent(x.Revenue, total)))
            .ToList();
    }

    /// <summary>
    /// Average days from invoice to payment, over the last twelve months.
    /// </summary>
    /// <remarks>
    /// Matched by invoice number, so only invoices that were actually settled count — an unpaid one has
    /// no collection time yet, and folding it in as zero would flatter the figure. Where an invoice was
    /// paid in instalments the last payment is used: the invoice is not collected until it is closed.
    ///
    /// <para>Null rather than zero when nothing in the window has been paid. Zero days would read as
    /// "customers pay immediately", which is the opposite of what no data means.</para>
    /// </remarks>
    private static int? DaysToCollect(
        IReadOnlyList<Sale> sales,
        IReadOnlyList<Payment> payments,
        DateOnly monthStart)
    {
        var window = monthStart.AddMonths(-11);

        var lastPaymentFor = payments
            .Where(p => !string.IsNullOrEmpty(p.Invoiceno))
            .Select(p => (Invoice: p.Invoiceno!, Date: LegacyValue.Date(p.Paymentrecdate)))
            .Where(p => p.Date is not null)
            .GroupBy(p => p.Invoice, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Max(p => p.Date!.Value), StringComparer.Ordinal);

        var spans = sales
            .Where(s => s.Date >= window && s.Number.Length > 0)
            .Select(s => lastPaymentFor.TryGetValue(s.Number, out var paid)
                ? paid.DayNumber - s.Date!.Value.DayNumber
                : (int?)null)
            .Where(days => days is >= 0)
            .Select(days => days!.Value)
            .ToList();

        return spans.Count == 0 ? null : (int)Math.Round(spans.Average());
    }

    /// <summary>
    /// Who the overdue money is with, worst first.
    /// </summary>
    /// <remarks>
    /// The ageing chart says how much is late; it cannot say who, and "who" is the only part anybody can
    /// act on. Restricted to balances past the current bucket so it lists people to chase rather than
    /// everybody who happens to owe something this week.
    /// </remarks>
    private static List<CustomerDebt> OverdueByCustomer(
        IReadOnlyList<Sale> sales,
        IReadOnlyDictionary<string, string> customerNames,
        DateOnly today)
    {
        return sales
            .Where(s => s.Balance > 0m && today.DayNumber - s.Date!.Value.DayNumber > 30)
            .GroupBy(s => s.CustomerCode, StringComparer.Ordinal)
            .Select(g => new CustomerDebt(
                customerNames.GetValueOrDefault(g.Key, g.Key.Length == 0 ? "Unknown" : g.Key),
                g.Sum(s => s.Balance),
                g.Count(),
                g.Max(s => today.DayNumber - s.Date!.Value.DayNumber)))
            .OrderByDescending(c => c.Owed)
            .Take(TopN)
            .ToList();
    }

    /// <summary>
    /// The suppliers the most has been bought from — the mirror of customer concentration.
    /// </summary>
    /// <remarks>
    /// All time rather than the month, unlike the customer ranking. A buying relationship is a standing
    /// dependency built over years, and one month of purchasing says very little about who the business
    /// would struggle to replace.
    /// </remarks>
    private static List<SupplierShare> TopSuppliers(
        IReadOnlyList<(string SupplierCode, decimal Amount)> spend,
        IReadOnlyDictionary<string, string> supplierNames)
    {
        var total = spend.Sum(s => s.Amount);

        return spend
            .Where(s => s.SupplierCode.Length > 0)
            .GroupBy(s => s.SupplierCode, StringComparer.Ordinal)
            .Select(g => new SupplierShare(
                supplierNames.GetValueOrDefault(g.Key, g.Key),
                g.Sum(s => s.Amount),
                Percent(g.Sum(s => s.Amount), total)))
            .OrderByDescending(s => s.Spend)
            .Take(TopN)
            .ToList();
    }

    /// <summary>
    /// Customers whose first-ever invoice fell in the month — new business, as opposed to repeat.
    /// </summary>
    /// <remarks>
    /// "First-ever" is judged across every invoice in scope, not just the window shown, so somebody who
    /// last bought two years ago counts as returning rather than new.
    /// </remarks>
    private static Trend NewCustomers(IReadOnlyList<Sale> sales, DateOnly monthStart)
    {
        var firstSale = sales
            .Where(s => s.CustomerCode.Length > 0)
            .GroupBy(s => s.CustomerCode, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Min(s => s.Date!.Value), StringComparer.Ordinal);

        return new Trend(
            firstSale.Count(f => InMonth(f.Value, monthStart)),
            firstSale.Count(f => InMonth(f.Value, monthStart.AddMonths(-1))));
    }

    /// <summary>
    /// Customers owing more than their agreed limit.
    /// </summary>
    /// <remarks>
    /// Only customers who have a limit recorded can breach one, so a customer with no limit is absent
    /// rather than treated as having a limit of zero — 198 of the 223 have none set, and reading those
    /// as "limit nil, therefore every rupee is a breach" would bury the twenty-five real ones.
    /// </remarks>
    private static List<CreditBreach> OverCreditLimit(
        IReadOnlyList<Sale> sales,
        IReadOnlyDictionary<string, string> customerNames,
        IReadOnlyDictionary<string, decimal> creditLimits)
    {
        return sales
            .Where(s => s.Balance > 0m && s.CustomerCode.Length > 0)
            .GroupBy(s => s.CustomerCode, StringComparer.Ordinal)
            .Select(g => new
            {
                Code = g.Key,
                Owed = g.Sum(s => s.Balance),
                Limit = creditLimits.GetValueOrDefault(g.Key),
            })
            .Where(c => c.Limit > 0m && c.Owed > c.Limit)
            .Select(c => new CreditBreach(
                customerNames.GetValueOrDefault(c.Code, c.Code),
                c.Limit,
                c.Owed,
                c.Owed - c.Limit))
            .OrderByDescending(c => c.Excess)
            .Take(TopN)
            .ToList();
    }

    /// <summary>
    /// Customers who used to buy and have not for ninety days.
    /// </summary>
    /// <remarks>
    /// Bounded at two years on the far side. Beyond that a customer has not lapsed, they have simply
    /// gone, and a list that carries every name from the system's whole history is an archive rather
    /// than something to work through.
    ///
    /// <para>The count and value cover every lapsed customer; the list itself is the largest few, so the
    /// panel can say "70 customers worth 19.8M" while showing the handful worth ringing first.</para>
    /// </remarks>
    private static List<LapsedCustomer> Lapsed(
        IReadOnlyList<Sale> sales,
        IReadOnlyDictionary<string, string> customerNames,
        DateOnly today,
        out int count,
        out decimal value)
    {
        const int SilentFor = 90;
        const int GoneFor = 730;

        var lapsed = sales
            .Where(s => s.CustomerCode.Length > 0)
            .GroupBy(s => s.CustomerCode, StringComparer.Ordinal)
            .Select(g => new
            {
                Code = g.Key,
                Last = g.Max(s => s.Date!.Value),
                Lifetime = g.Sum(s => s.Total),
                Owed = g.Sum(s => s.Balance),
            })
            .Select(c => new { c.Code, c.Last, c.Lifetime, c.Owed, Silent = today.DayNumber - c.Last.DayNumber })
            .Where(c => c.Silent is > SilentFor and <= GoneFor)
            .ToList();

        count = lapsed.Count;
        value = lapsed.Sum(c => c.Lifetime);

        return lapsed
            .OrderByDescending(c => c.Lifetime)
            .Take(TopN)
            .Select(c => new LapsedCustomer(
                customerNames.GetValueOrDefault(c.Code, c.Code),
                c.Last,
                c.Silent,
                c.Lifetime,
                c.Owed))
            .ToList();
    }

    private static bool InMonth(DateOnly date, DateOnly monthStart) =>
        date.Year == monthStart.Year && date.Month == monthStart.Month;

    private static decimal Percent(decimal part, decimal whole) =>
        whole == 0m ? 0m : decimal.Round(part / whole * 100m, 1);

    /// <summary>One invoice reduced to the figures the analytics actually use.</summary>
    private sealed record Sale(
        DateOnly? Date,
        decimal Total,
        decimal Cost,
        decimal Balance,
        string CustomerCode,
        string Number,
        bool IsCredit);
}
