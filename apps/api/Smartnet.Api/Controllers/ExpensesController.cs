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
/// Expenses &amp; their categories (Phase 7, slice 3) — money owed for what was incurred, and what settles it.
/// </summary>
/// <remarks>
/// Adopted additively: this app's expenses (typed columns) and the legacy ones (varchar) share
/// <c>expense_tr</c>; categories are the shared, adopted <c>exp_cat_m</c>. The save dual-writes the legacy
/// row so the existing <c>ExpenseReport</c> keeps reading.
/// <para>An expense is recorded when it is incurred and settled afterwards — in one payment or several — so
/// what it still owes is derived (amount − Σ payments) rather than kept in a flag, which is what the legacy
/// app could not do. Voiding one is refused while any payment stands against it.</para>
/// </remarks>
[ApiController]
[Route("api/expenses")]
public sealed class ExpensesController : ControllerBase
{
    private readonly IExpenseCreator _creator;
    private readonly IExpenseVoider _voider;
    private readonly IExpensePayments _payments;
    private readonly ICompanyContext _company;
    private readonly SmartnetDbContext _db;
    private readonly SmartnetLegacyDbContext _legacy;

    public ExpensesController(
        IExpenseCreator creator,
        IExpenseVoider voider,
        IExpensePayments payments,
        ICompanyContext company,
        SmartnetDbContext db,
        SmartnetLegacyDbContext legacy)
    {
        _creator = creator;
        _voider = voider;
        _payments = payments;
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
            .Select(e => new { e.Id, e.Date, e.InvoiceNo, e.CategoryId, e.Description, e.NetAmount, e.VatNumber, e.Amount, e.Method, e.Reference, e.CompanyId, e.RowVersion })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // What each expense has been settled by — the outstanding is derived from this, never stored.
        var paid = await _db.ExpensePayments
            .GroupBy(p => p.ExpenseId)
            .Select(g => new { ExpenseId = g.Key, Amount = g.Sum(p => p.Amount) })
            .ToDictionaryAsync(g => g.ExpenseId, g => g.Amount, cancellationToken)
            .ConfigureAwait(false);

        var rows = newExpenses
            .Select(e => Row(
                e.Id, e.Date, e.InvoiceNo, e.CategoryId, categoryNames.GetValueOrDefault(e.CategoryId), e.Description,
                e.NetAmount, e.Amount - e.NetAmount, e.VatNumber, e.Amount, paid.GetValueOrDefault(e.Id),
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
            var legacyTotal = LegacyValue.Money(e.ExpenseAmount);
            return Row(
                e.Id, LegacyValue.Date(e.ExpenseDate) ?? DateOnly.MinValue, null, catId, categoryNames.GetValueOrDefault(catId),
                e.ExpenseDesc, legacyTotal, 0m, null, legacyTotal, paid.GetValueOrDefault(e.Id),
                e.Paymentm, e.PaymentRef, companyName, e.RowVersion, "legacy");
        }));

        return Ok(rows
            .OrderByDescending(r => r.Date)
            .ThenByDescending(r => r.Id)
            .ToList());
    }

    /// <summary>
    /// One list row, with what it has been settled by. The outstanding is floored at zero: a legacy amount is
    /// read from a varchar, and a cent of parse drift must not make a settled expense read as still owing.
    /// </summary>
    private static ExpenseSummary Row(
        long id, DateOnly date, string? invoiceNo, long categoryId, string? category, string description,
        decimal netAmount, decimal taxAmount, string? vatNumber, decimal amount, decimal paidAmount,
        string? method, string? reference, string? companyName, int rowVersion, string origin) =>
        new(id, date, invoiceNo, categoryId, category, description, netAmount, taxAmount, vatNumber, amount,
            paidAmount, Math.Max(0m, amount - paidAmount), method, reference, companyName, rowVersion, origin);

    /// <summary>Record an expense — unpaid; dual-writes the legacy row for the ExpenseReport.</summary>
    [HttpPost]
    [RequirePermission(Permissions.Expenses)]
    public async Task<ActionResult<ExpenseCreatedResponse>> Create(CreateExpenseRequest request, CancellationToken cancellationToken)
    {
        if (!_company.Accessible.Contains(request.CompanyId))
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "You cannot record an expense in that company.");
        }

        var created = await _creator.CreateAsync(
            new NewExpense(request.CompanyId, request.CategoryId, request.Date, request.InvoiceNo, request.Description,
                request.NetAmount, request.TaxRatePercentage, request.VatNumber, request.Amount),
            cancellationToken).ConfigureAwait(false);

        return Ok(new ExpenseCreatedResponse(created.Id, created.Amount));
    }

    // --- Settlement ---------------------------------------------------------------------------------

    /// <summary>Every settlement against an expense, oldest first — including the backfilled ones.</summary>
    [HttpGet("{id:long}/payments")]
    [RequirePermission(Permissions.Expenses)]
    public async Task<ActionResult<IReadOnlyList<ExpensePaymentSummary>>> Payments(long id, CancellationToken cancellationToken)
    {
        if (await CompanyOfAsync(id, cancellationToken).ConfigureAwait(false) is not { } companyId
            || !_company.Accessible.Contains(companyId))
        {
            return NotFound();
        }

        var payments = await _db.ExpensePayments
            .Where(p => p.ExpenseId == id)
            .OrderBy(p => p.Date)
            .ThenBy(p => p.Id)
            .Select(p => new ExpensePaymentSummary(p.Id, p.Date, p.Amount, p.Method, p.Reference, p.RowVersion, p.DataOrigin))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Ok(payments);
    }

    /// <summary>Settle an expense — all of what it still owes, or part of it.</summary>
    [HttpPost("{id:long}/payments")]
    [RequirePermission(Permissions.Expenses)]
    public async Task<ActionResult<ExpensePaymentRecordedResponse>> Pay(long id, RecordExpensePaymentRequest request, CancellationToken cancellationToken)
    {
        if (await CompanyOfAsync(id, cancellationToken).ConfigureAwait(false) is not { } companyId
            || !_company.Accessible.Contains(companyId))
        {
            return NotFound();
        }

        try
        {
            var recorded = await _payments.RecordPaymentAsync(
                id,
                new RecordExpensePayment(request.Amount, request.Date, request.Method, request.Reference,
                    request.ChequePayee, request.ChequeBank, request.ChequeNumber, request.ChequeDate, request.ChequeDueDate),
                cancellationToken).ConfigureAwait(false);

            return Ok(new ExpensePaymentRecordedResponse(
                recorded.ExpenseId, recorded.PaymentId, recorded.AmountPaid, recorded.Outstanding));
        }
        catch (ExpensePaymentExceedsOutstandingException e)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: e.Message);
        }
    }

    /// <summary>Void a settlement — soft, reason-gated; the expense goes back to owing that much.</summary>
    [HttpDelete("payments/{paymentId:long}")]
    [RequirePermission(Permissions.Expenses)]
    [RequireChangeReason]
    public async Task<IActionResult> VoidPayment(long paymentId, [FromQuery] int expectedRowVersion, CancellationToken cancellationToken)
    {
        var companyId = await _db.ExpensePayments
            .Where(p => p.Id == paymentId)
            .Select(p => p.CompanyId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (companyId is null || !_company.Accessible.Contains(companyId.Value))
        {
            return NotFound();
        }

        try
        {
            await _payments.VoidPaymentAsync(paymentId, expectedRowVersion, cancellationToken).ConfigureAwait(false);
            return NoContent();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "This payment was changed by someone else. Reload and try again.");
        }
    }

    /// <summary>
    /// The company an expense belongs to, whichever app recorded it — the legacy <c>company</c> varchar is
    /// dual-written for this app's expenses too, so one lookup covers both origins.
    /// </summary>
    private async Task<long?> CompanyOfAsync(long expenseId, CancellationToken cancellationToken)
    {
        var company = await _legacy.ExpenseTrs
            .Where(e => e.Id == expenseId && e.DeletedAt == null)
            .Select(e => e.Company)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return long.TryParse(company, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : null;
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
        catch (ExpenseHasPaymentsException e)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: e.Message);
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
