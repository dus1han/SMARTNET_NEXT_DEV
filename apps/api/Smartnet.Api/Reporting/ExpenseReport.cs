using System.Globalization;
using Smartnet.Api.Contracts;
using Smartnet.Domain.Reporting;
using Smartnet.Infrastructure.Entities;
using Smartnet.Infrastructure.Reporting;

namespace Smartnet.Api.Reporting;

/// <summary>
/// The sales report's clone test. <c>expense_tr</c> instead of <c>invoice_h</c>, a different column
/// set — and nothing else: the same <see cref="LegacyValue"/> money parser, the same
/// <see cref="ReportPeriod"/> filter, the same defensive flagging. If this needed a second money
/// parser or a second date filter, the spine was not finished.
/// </summary>
public static class ExpenseReport
{
    public static ExpenseReportResponse Build(
        IReadOnlyList<ExpenseTr> expenses,
        IReadOnlyDictionary<int, string> categoryNames,
        ReportPeriod period)
    {
        var rows = expenses
            .Where(e => period.ContainsIso(e.ExpenseDate))
            .Select(e => Row(e, categoryNames))
            .OrderByDescending(r => r.Date ?? DateOnly.MinValue) // legacy: ORDER BY expense_date DESC
            .ThenByDescending(r => r.Id)
            .ToList();

        return new ExpenseReportResponse(
            Total: rows.Sum(r => r.Amount),
            Count: rows.Count,
            FlaggedCount: rows.Count(r => r.HasDataIssue),
            Rows: rows);
    }

    private static ExpenseReportRow Row(ExpenseTr e, IReadOnlyDictionary<int, string> categories)
    {
        var amount = LegacyValue.Money(e.ExpenseAmount, out var amountOk);
        var date = LegacyValue.Date(e.ExpenseDate);
        var dateOk = string.IsNullOrWhiteSpace(e.ExpenseDate) || date is not null;

        return new ExpenseReportRow(
            Id: e.Id,
            Date: date,
            Category: ResolveCategory(e.ExpCat, categories),
            Description: e.ExpenseDesc,
            Amount: amount,
            PaymentMethod: e.Paymentm,
            Reference: e.PaymentRef,
            AddedBy: e.Addedby,
            HasDataIssue: !amountOk || !dateOk);
    }

    /// <summary>The <c>exp_cat_m</c> name for an expense's <c>exp_cat</c> id, or the raw value when it
    /// resolves to nothing — never blank, so a category that lost its master row is still visible.</summary>
    private static string ResolveCategory(string? expCat, IReadOnlyDictionary<int, string> categories)
    {
        if (int.TryParse(expCat, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            && categories.TryGetValue(id, out var name)
            && !string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        return string.IsNullOrWhiteSpace(expCat) ? "—" : expCat;
    }
}
