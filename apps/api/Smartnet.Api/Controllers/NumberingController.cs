using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smartnet.Api.Auditing;
using Smartnet.Api.Auth;
using Smartnet.Api.Contracts;
using Smartnet.Domain.Identity;
using Smartnet.Domain.Settings;
using Smartnet.Infrastructure.Persistence;

namespace Smartnet.Api.Controllers;

/// <summary>
/// Document numbering: the prefix an administrator controls, and the counter that must continue
/// from where the legacy app left off.
/// </summary>
[ApiController]
[Route("api/settings/numbering")]
[RequirePermission(Permissions.SettingsManage)]
public sealed class NumberingController : ControllerBase
{
    private readonly SmartnetDbContext _db;
    private readonly ICompanyContext _companies;
    private readonly INumberSeriesInitialiser _initialiser;
    private readonly TimeProvider _time;

    public NumberingController(
        SmartnetDbContext db,
        ICompanyContext companies,
        INumberSeriesInitialiser initialiser,
        TimeProvider time)
    {
        _db = db;
        _companies = companies;
        _initialiser = initialiser;
        _time = time;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DocumentSeriesDto>>> List(
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime);

        var series = await _db.DocumentSeries
            .Where(s => s.CompanyId == _companies.Active)
            .OrderBy(s => s.DocType)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Ok(series
            .Select(s => new DocumentSeriesDto(
                s.Id,
                s.DocType,
                s.Prefix,
                s.NextNumber,
                s.Padding,

                // What the next document will actually be called. This is the number that matters
                // to whoever is reading this screen, and it is far more use than the template.
                Example: s.Format(s.NextNumber, today),
                RowVersion: s.RowVersion))
            .ToList());
    }

    /// <summary>
    /// Changes the prefix or the padding — never the next number.
    /// </summary>
    /// <remarks>
    /// The counter is deliberately not editable here. Typing a number into a settings form is how
    /// somebody reissues invoice 1200 by accident; the counter moves only by allocation, or by the
    /// initialiser below, which cannot move it backwards.
    /// </remarks>
    [HttpPut("{id:long}")]
    [RequireChangeReason]
    public async Task<IActionResult> Update(
        long id,
        SaveDocumentSeriesRequest request,
        CancellationToken cancellationToken)
    {
        var series = await _db.DocumentSeries
            .FirstOrDefaultAsync(
                s => s.Id == id && s.CompanyId == _companies.Active,
                cancellationToken)
            .ConfigureAwait(false);

        if (series is null)
        {
            return NotFound();
        }

        // The one setting where a silently lost edit reissues numbers already printed on documents:
        // two administrators changing a prefix at once, and the second overwriting the first, is how a
        // series ends up producing a number that is already on a customer's invoice.
        if (this.StaleEdit(series, request.ExpectedRowVersion, "numbering series") is { } stale)
        {
            return stale;
        }

        series.Prefix = request.Prefix;
        series.Padding = request.Padding;

        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title:
                    "Someone else changed this numbering series while you were editing it. Reload to "
                    + "see their version, then make your changes again.");
        }

        return NoContent();
    }

    /// <summary>Renders a prefix template without saving it, so the admin can see what they typed.</summary>
    [HttpPost("preview")]
    public ActionResult<NumberPreview> Preview(PreviewNumberRequest request)
    {
        var today = DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime);

        return Ok(new NumberPreview(
            DocumentNumberFormat.Render(request.Prefix, request.NextNumber, request.Padding, today),

            // A year ahead, so a template that silently never changes is visible as one that
            // silently never changes.
            DocumentNumberFormat.Render(
                request.Prefix, request.NextNumber + 1, request.Padding, today.AddMonths(1))));
    }

    /// <summary>
    /// Reads the legacy numbering and points each series at the next unused number.
    /// </summary>
    /// <remarks>
    /// <b>This is the go-live step.</b> Run it as a dry run first (<c>apply=false</c>, the
    /// default) and read what it says. Then run it for real at cutover — immediately after the
    /// legacy app stops issuing that document type, and not before: any invoice the old app raises
    /// after this has run takes a number the new app also believes is free.
    ///
    /// <para>Safe to run repeatedly. It never moves a counter backwards, so re-running it after the
    /// new app has issued documents leaves it exactly where it is.</para>
    /// </remarks>
    [HttpPost("initialise")]
    [RequireChangeReason]
    public async Task<ActionResult<IReadOnlyList<SeriesInitialisation>>> Initialise(
        [FromQuery] bool apply,
        CancellationToken cancellationToken) =>
        Ok(await _initialiser.InitialiseAsync(apply, cancellationToken).ConfigureAwait(false));
}
