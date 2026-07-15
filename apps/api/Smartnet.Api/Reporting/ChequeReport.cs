using Smartnet.Api.Contracts;
using Smartnet.Domain.Reporting;
using Smartnet.Infrastructure.Entities;
using Smartnet.Infrastructure.Reporting;

namespace Smartnet.Api.Reporting;

/// <summary>
/// The cheques report — <c>cheques</c> by period, on the spine. It also quietly fixes two legacy export
/// faults by construction: created-by / created-at / printed-at each get their own field (the legacy
/// export wrote all three into one cell, so only the last survived), and the amount-in-words is derived
/// from the parsed amount rather than read from the <c>inwords</c> column the report never filled.
/// </summary>
public static class ChequeReport
{
    public static ChequeReportResponse Build(IReadOnlyList<Cheque> cheques, ReportPeriod period)
    {
        var rows = cheques
            .Where(c => period.ContainsIso(c.Chequedate))
            .Select(Row)
            .OrderByDescending(r => r.ChequeDate ?? DateOnly.MinValue) // legacy shows newest cheques first
            .ThenByDescending(r => r.Id)
            .ToList();

        return new ChequeReportResponse(
            Total: rows.Sum(r => r.Amount),
            Count: rows.Count,
            FlaggedCount: rows.Count(r => r.HasDataIssue),
            Rows: rows);
    }

    private static ChequeRow Row(Cheque c)
    {
        var amount = LegacyValue.Money(c.Amount, out var amountOk);
        var chequeDate = LegacyValue.Date(c.Chequedate);
        var dateOk = string.IsNullOrWhiteSpace(c.Chequedate) || chequeDate is not null;

        return new ChequeRow(
            Id: c.Id,
            ChequeDate: chequeDate,
            DueDate: LegacyValue.Date(c.Duedate),
            PayTo: c.Payto,
            Amount: amount,
            AmountInWords: AmountInWords.Of(amount),
            Bank: c.Bank,
            ChequeNo: c.Chkno,
            CreatedBy: c.Createdby,
            CreatedAt: c.Createddt,
            PrintedAt: c.Printeddt,
            HasDataIssue: !amountOk || !dateOk);
    }
}
