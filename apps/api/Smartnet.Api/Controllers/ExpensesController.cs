using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smartnet.Api.Auditing;
using Smartnet.Api.Auth;
using Smartnet.Api.Contracts;
using Smartnet.Domain.Documents;
using Smartnet.Domain.Identity;
using Smartnet.Domain.MasterData;
using Smartnet.Infrastructure.Persistence;
using Smartnet.Infrastructure.Reporting;

namespace Smartnet.Api.Controllers;

/// <summary>
/// Expenses &amp; their categories (Phase 7, slice 3) — a flat log of money spent.
/// </summary>
/// <remarks>
/// No ledger, no balance, exactly as the legacy app treated expenses. Adopted additively: this app's expenses
/// (typed columns) and the legacy ones (varchar) share <c>expense_tr</c>; categories are the shared, adopted
/// <c>exp_cat_m</c>. The save dual-writes the legacy row so the existing <c>ExpenseReport</c> keeps reading.
/// </remarks>
[ApiController]
[Route("api/expenses")]
public sealed class ExpensesController : ControllerBase
{
    private readonly IExpenseCreator _creator;
    private readonly IExpenseVoider _voider;
    private readonly ICompanyContext _company;
    private readonly SmartnetDbContext _db;
    private readonly SmartnetLegacyDbContext _legacy;

    public ExpensesController(
        IExpenseCreator creator,
        IExpenseVoider voider,
        ICompanyContext company,
        SmartnetDbContext db,
        SmartnetLegacyDbContext legacy)
    {
        _creator = creator;
        _voider = voider;
        _company = company;
        _db = db;
        _legacy = legacy;
    }

    /// <summary>Every expense the caller may see, newest first — this app's own and the legacy ones.</summary>
    [HttpGet]
    [RequirePermission(Permissions.Expenses)]
    public async Task<ActionResult<IReadOnlyList<ExpenseSummary>>> List(CancellationToken cancellationToken)
    {
        var accessible = _company.Accessible.ToList();
        var accessibleText = accessible.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToHashSet();
        var companyNames = await _db.Companies
            .Where(c => accessible.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken)
            .ConfigureAwait(false);
        var categoryNames = await _db.ExpenseCategories
            .IgnoreQueryFilters()
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken)
            .ConfigureAwait(false);

        var newExpenses = await _db.Expenses
            .Where(e => e.CompanyId != null && accessible.Contains(e.CompanyId.Value))
            .Select(e => new { e.Id, e.Date, e.CategoryId, e.Description, e.Amount, e.Method, e.Reference, e.CompanyId, e.RowVersion })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = newExpenses
            .Select(e => new ExpenseSummary(
                e.Id, e.Date, e.CategoryId, categoryNames.GetValueOrDefault(e.CategoryId), e.Description, e.Amount,
                e.Method, e.Reference, e.CompanyId is { } cid ? companyNames.GetValueOrDefault(cid) : null, e.RowVersion, "new"))
            .ToList();

        var legacyExpenses = (await _legacy.ExpenseTrs
            .Where(e => e.DataOrigin != "new" && e.DeletedAt == null)
            .Select(e => new { e.Id, e.ExpCat, e.ExpenseDate, e.ExpenseDesc, e.ExpenseAmount, e.Paymentm, e.PaymentRef, e.Company, e.RowVersion })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .Where(e => e.Company != null && accessibleText.Contains(e.Company));

        rows.AddRange(legacyExpenses.Select(e =>
        {
            var catId = long.TryParse(e.ExpCat, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ci) ? ci : 0;
            var companyName = long.TryParse(e.Company, NumberStyles.Integer, CultureInfo.InvariantCulture, out var co)
                ? companyNames.GetValueOrDefault(co) : null;
            return new ExpenseSummary(
                e.Id, LegacyValue.Date(e.ExpenseDate) ?? DateOnly.MinValue, catId, categoryNames.GetValueOrDefault(catId),
                e.ExpenseDesc, LegacyValue.Money(e.ExpenseAmount), e.Paymentm, e.PaymentRef, companyName, e.RowVersion, "legacy");
        }));

        return Ok(rows
            .OrderByDescending(r => r.Date)
            .ThenByDescending(r => r.Id)
            .ToList());
    }

    /// <summary>Record an expense — a flat log entry; dual-writes the legacy row for the ExpenseReport.</summary>
    [HttpPost]
    [RequirePermission(Permissions.Expenses)]
    public async Task<ActionResult<ExpenseCreatedResponse>> Create(CreateExpenseRequest request, CancellationToken cancellationToken)
    {
        if (!_company.Accessible.Contains(request.CompanyId))
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "You cannot record an expense in that company.");
        }

        var created = await _creator.CreateAsync(
            new NewExpense(request.CompanyId, request.CategoryId, request.Date, request.Description, request.Amount, request.Method, request.Reference),
            cancellationToken).ConfigureAwait(false);

        return Ok(new ExpenseCreatedResponse(created.Id, created.Amount));
    }

    /// <summary>Void an expense — soft, reason-gated (not the legacy hard delete).</summary>
    [HttpDelete("{id:long}")]
    [RequirePermission(Permissions.Expenses)]
    [RequireChangeReason]
    public async Task<IActionResult> Delete(long id, [FromQuery] int expectedRowVersion, CancellationToken cancellationToken)
    {
        var accessible = _company.Accessible.ToList();
        var companyId = await _db.Expenses
            .IgnoreQueryFilters()
            .Where(e => e.Id == id && e.DeletedAt == null)
            .Select(e => e.CompanyId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (companyId is null || !accessible.Contains(companyId.Value))
        {
            return NotFound();
        }

        try
        {
            await _voider.VoidAsync(id, expectedRowVersion, cancellationToken).ConfigureAwait(false);
            return NoContent();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "This expense was changed by someone else. Reload and try again.");
        }
    }

    // --- Categories ---------------------------------------------------------------------------------

    /// <summary>Every expense category (shared across companies), by name.</summary>
    [HttpGet("categories")]
    [RequirePermission(Permissions.Expenses)]
    public async Task<ActionResult<IReadOnlyList<ExpenseCategoryDto>>> Categories(CancellationToken cancellationToken)
    {
        var categories = await _db.ExpenseCategories
            .OrderBy(c => c.Name)
            .Select(c => new ExpenseCategoryDto(c.Id, c.Name ?? string.Empty))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Ok(categories);
    }

    /// <summary>Add a category — writes exp_cat_m, which the legacy app reads too.</summary>
    [HttpPost("categories")]
    [RequirePermission(Permissions.Expenses)]
    public async Task<ActionResult<ExpenseCategoryDto>> AddCategory(SaveExpenseCategoryRequest request, CancellationToken cancellationToken)
    {
        var category = new ExpenseCategory { Name = request.Name.Trim() };
        _db.ExpenseCategories.Add(category);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new ExpenseCategoryDto(category.Id, category.Name ?? string.Empty));
    }

    /// <summary>Rename a category — audited.</summary>
    [HttpPut("categories/{id:long}")]
    [RequirePermission(Permissions.Expenses)]
    public async Task<IActionResult> RenameCategory(long id, SaveExpenseCategoryRequest request, CancellationToken cancellationToken)
    {
        var category = await _db.ExpenseCategories
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (category is null)
        {
            return NotFound();
        }

        category.Name = request.Name.Trim();
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return NoContent();
    }
}
