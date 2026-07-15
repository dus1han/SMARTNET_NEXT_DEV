using Smartnet.Api.Contracts;
using Smartnet.Domain.Reporting;
using Smartnet.Infrastructure.Entities;
using Smartnet.Infrastructure.Reporting;

namespace Smartnet.Api.Reporting;

/// <summary>
/// The job-cards report — <c>jobs_m</c> by period, on the spine. Profit is <c>sell − cost</c> but only
/// once a job is out of <c>PENDING</c>: a pending job has no cost or sell yet, so its profit is null
/// (shown blank), never a misleading zero. The legacy export <c>double.Parse</c>d those blank pending
/// figures and threw; the defensive parser simply reads them as zero and the pending row omits profit.
/// </summary>
public static class JobCardReport
{
    public static JobCardReportResponse Build(
        IReadOnlyList<JobsM> jobs,
        IReadOnlyDictionary<string, string> customerNames,
        ReportPeriod period)
    {
        var rows = jobs
            .Where(j => period.ContainsIso(j.Jdate))
            .Select(j => Row(j, customerNames))
            .OrderByDescending(r => r.Date ?? DateOnly.MinValue)
            .ThenBy(r => r.JobNo, StringComparer.Ordinal)
            .ToList();

        return new JobCardReportResponse(
            TotalCost: rows.Sum(r => r.Cost),
            TotalSell: rows.Sum(r => r.Sell),
            TotalProfit: rows.Where(r => r.Profit.HasValue).Sum(r => r.Profit!.Value),
            Count: rows.Count,
            FlaggedCount: rows.Count(r => r.HasDataIssue),
            Rows: rows);
    }

    private static JobCardRow Row(JobsM j, IReadOnlyDictionary<string, string> names)
    {
        var pending = string.Equals(j.Jstat, "PENDING", StringComparison.OrdinalIgnoreCase);

        var cost = LegacyValue.Money(j.Cost, out var costOk);
        var sell = LegacyValue.Money(j.Sell, out var sellOk);

        var date = LegacyValue.Date(j.Jdate);
        var dateOk = string.IsNullOrWhiteSpace(j.Jdate) || date is not null;

        return new JobCardRow(
            JobNo: j.Jobno,
            Date: date,
            CustomerName: j.Customer is { Length: > 0 } code ? names.GetValueOrDefault(code, string.Empty) : string.Empty,
            Status: j.Jstat,
            Cost: cost,
            Sell: sell,
            // Profit only where the job is not pending — a pending job's cost/sell are not set yet.
            Profit: pending ? null : sell - cost,
            JobDoneBy: j.Jobdoneby,
            CompletedBy: j.Completedby,
            HasDataIssue: !costOk || !sellOk || !dateOk);
    }
}
