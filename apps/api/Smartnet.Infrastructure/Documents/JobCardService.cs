using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Auditing;
using Smartnet.Domain.Documents;
using Smartnet.Domain.MasterData;
using Smartnet.Domain.Settings;
using Smartnet.Infrastructure.Persistence;
using Smartnet.Infrastructure.Persistence.Configurations;

namespace Smartnet.Infrastructure.Documents;

/// <summary>
/// Creates job cards and runs the close workflow (Phase 6, slice 3). The lightest document service —
/// no tax, no ledger, no stock — over a fully NOT NULL legacy table with structured serial lines beside it.
/// </summary>
public sealed class JobCardService : IJobCardCreator, IJobCardWorkflow
{
    private readonly SmartnetDbContext _db;
    private readonly IDocumentNumberAllocator _numbers;
    private readonly IDocumentVersionWriter _versions;
    private readonly IChangeContext _change;
    private readonly TimeProvider _time;

    public JobCardService(
        SmartnetDbContext db,
        IDocumentNumberAllocator numbers,
        IDocumentVersionWriter versions,
        IChangeContext change,
        TimeProvider time)
    {
        _db = db;
        _numbers = numbers;
        _versions = versions;
        _change = change;
        _time = time;
    }

    public async Task<JobCardCreated> CreateAsync(NewJobCard request, CancellationToken cancellationToken = default)
    {
        var company = await _db.Companies
            .FirstOrDefaultAsync(c => c.Id == request.CompanyId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Company {request.CompanyId} does not exist.");

        var customer = await _db.Customers
            .FirstOrDefaultAsync(c => c.Id == request.CustomerId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Customer {request.CustomerId} does not exist.");

        await using var transaction = await _db.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var number = await _numbers
            .AllocateAsync(request.CompanyId, DocumentTypes.JobCard, request.Date, cancellationToken)
            .ConfigureAwait(false);

        var enteredByName = await UserNameAsync(_change.UserId, cancellationToken).ConfigureAwait(false);
        var now = _time.GetUtcNow().UtcDateTime;

        var jobCard = new JobCard
        {
            Number = number,
            CompanyId = request.CompanyId,
            CustomerId = request.CustomerId,
            Date = request.Date,
            // These map to NOT NULL legacy columns, so an absent value is an empty string, never null.
            ContactPerson = request.ContactPerson ?? string.Empty,
            FaultDescription = request.FaultDescription ?? string.Empty,
            Remarks = request.Remarks ?? string.Empty,
            Technician = request.Technician ?? string.Empty,
            EnteredBy = _change.UserId,
            EnteredAt = now,
            Status = JobCardStatus.Pending,
            CompletionRemarks = string.Empty, // set at close; the legacy column is NOT NULL
            DataOrigin = "new",

            Lines = [.. request.Lines.Select((l, i) => new JobCardLine
            {
                ItemId = l.ItemId,
                Description = l.Description,
                Serial = l.Serial,
                Sort = i,
            })],
        };

        _db.JobCards.Add(jobCard);
        SetCreateShadow(jobCard, customer, enteredByName, now);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await _versions
            .WriteAsync(DocumentTypes.JobCard, jobCard.Id, request.CompanyId, Snapshot(jobCard, customer, company), reason: null, cancellationToken)
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new JobCardCreated(jobCard.Id, number);
    }

    public async Task CloseAsync(long jobCardId, CloseJobCard request, int expectedRowVersion, CancellationToken cancellationToken = default)
    {
        var jobCard = await _db.JobCards
            .Include(j => j.Lines)
            .FirstOrDefaultAsync(j => j.Id == jobCardId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Job card {jobCardId} does not exist.");

        // Guarded: only a PENDING card can be closed (the legacy re-close hazard), and not a stale one.
        if (jobCard.Status != JobCardStatus.Pending)
        {
            throw new JobCardNotPendingException(jobCard.Number);
        }
        if (jobCard.RowVersion != expectedRowVersion)
        {
            throw new DbUpdateConcurrencyException(
                "This job card was changed by someone else while you were viewing it.");
        }

        var customer = await _db.Customers
            .FirstOrDefaultAsync(c => c.Id == jobCard.CustomerId, cancellationToken)
            .ConfigureAwait(false);
        var company = jobCard.CompanyId is { } cid
            ? await _db.Companies.FirstOrDefaultAsync(c => c.Id == cid, cancellationToken).ConfigureAwait(false)
            : null;

        await using var transaction = await _db.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var completedByName = await UserNameAsync(_change.UserId, cancellationToken).ConfigureAwait(false);
        var now = _time.GetUtcNow().UtcDateTime;

        jobCard.Cost = request.Cost;
        jobCard.Sell = request.Sell;
        jobCard.CompletionRemarks = request.CompletionRemarks ?? string.Empty;
        jobCard.CompletedBy = _change.UserId;
        jobCard.CompletedAt = now;
        jobCard.Status = JobCardStatus.Closed;

        // Keep the legacy close columns in step (all NOT NULL) for the legacy job sheet.
        var entry = _db.Entry(jobCard);
        entry.Property(JobCardLegacyShadow.Cost).CurrentValue = Money(request.Cost);
        entry.Property(JobCardLegacyShadow.Sell).CurrentValue = Money(request.Sell);
        entry.Property(JobCardLegacyShadow.CompletedBy).CurrentValue = completedByName ?? string.Empty;
        entry.Property(JobCardLegacyShadow.CompletedDt).CurrentValue = now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await _versions
            .WriteAsync(DocumentTypes.JobCard, jobCard.Id, jobCard.CompanyId, Snapshot(jobCard, customer, company), reason: null, cancellationToken)
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes the NOT NULL legacy columns at create — including the close columns, empty until close.</summary>
    private void SetCreateShadow(JobCard jobCard, Customer customer, string? enteredByName, DateTime now)
    {
        var entry = _db.Entry(jobCard);
        void Set(string name, string? value) => entry.Property(name).CurrentValue = value ?? string.Empty;

        Set(JobCardLegacyShadow.Company, jobCard.CompanyId?.ToString(CultureInfo.InvariantCulture));
        Set(JobCardLegacyShadow.Customer, customer.Code);
        Set(JobCardLegacyShadow.JDate, jobCard.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Set(JobCardLegacyShadow.EnteredBy, enteredByName);
        Set(JobCardLegacyShadow.EnteredDt, now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        Set(JobCardLegacyShadow.Cost, string.Empty);
        Set(JobCardLegacyShadow.Sell, string.Empty);
        Set(JobCardLegacyShadow.CompletedBy, string.Empty);
        Set(JobCardLegacyShadow.CompletedDt, string.Empty);
        Set(JobCardLegacyShadow.Items, BuildItemsBlob(jobCard.Lines));
    }

    /// <summary>The legacy <c>items</c> blob: one line per unit, in the legacy Crystal sheet's own format.</summary>
    private static string BuildItemsBlob(IEnumerable<JobCardLine> lines)
    {
        var builder = new StringBuilder();
        foreach (var line in lines.OrderBy(l => l.Sort))
        {
            builder.Append("Item : ").Append(line.Description ?? string.Empty)
                .Append(" | Qty : 1 | Serial No : ").Append(line.Serial ?? string.Empty).Append('\n');
        }
        return builder.ToString();
    }

    private async Task<string?> UserNameAsync(long? userId, CancellationToken cancellationToken)
    {
        if (userId is not { } id) return null;
        return await _db.Users
            .Where(u => u.Id == id)
            .Select(u => u.Name ?? u.Username)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static string Money(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static object Snapshot(JobCard jobCard, Customer? customer, Company? company) => new
    {
        jobCard = new
        {
            jobCard.Number,
            jobCard.Date,
            jobCard.Status,
            jobCard.FaultDescription,
            jobCard.Remarks,
            jobCard.Technician,
            jobCard.ContactPerson,
            jobCard.Cost,
            jobCard.Sell,
            jobCard.CompletionRemarks,
        },
        customer = customer is null ? null : new { customer.Id, customer.Code, customer.Name },
        company = company is null ? null : new { company.Id, company.Name },
        lines = jobCard.Lines.OrderBy(l => l.Sort).Select(l => new { l.ItemId, l.Description, l.Serial }),
    };
}
