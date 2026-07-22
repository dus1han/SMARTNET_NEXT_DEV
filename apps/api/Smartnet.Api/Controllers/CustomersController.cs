using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smartnet.Api.Auditing;
using Smartnet.Api.Auth;
using Smartnet.Api.Contracts;
using Smartnet.Domain.Auditing;
using Smartnet.Domain.Exporting;
using Smartnet.Domain.Identity;
using Smartnet.Domain.MasterData;
using Smartnet.Infrastructure.Persistence;

namespace Smartnet.Api.Controllers;

/// <summary>
/// Customers — the first master-data screen, and the clone of the Users reference screen.
/// </summary>
/// <remarks>
/// The Phase 2 exit criterion, cashed in: if this controller needed a shape the Users controller did
/// not, the abstractions did not hold. It does not. Same list, same export, same create/edit, same
/// mandatory-reason delete, same audit-for-free — a different table underneath.
/// </remarks>
[ApiController]
[Route("api/customers")]
[RequirePermission(Permissions.CustomerM)]
public sealed class CustomersController : ControllerBase
{
    private readonly SmartnetDbContext _db;
    private readonly ICustomerCodeAllocator _codes;
    private readonly IExcelExporter _excel;
    private readonly IAuditWriter _audit;
    private readonly TimeProvider _time;

    public CustomersController(
        SmartnetDbContext db,
        ICustomerCodeAllocator codes,
        IExcelExporter excel,
        IAuditWriter audit,
        TimeProvider time)
    {
        _db = db;
        _codes = codes;
        _excel = excel;
        _audit = audit;
        _time = time;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CustomerSummary>>> List(CancellationToken cancellationToken)
    {
        // No company filter. Deliberately: the customer a Smart Net user is looking at is invoiced by
        // Smart Technologies next week (see Customer.AssignedCompanyId), so scoping the list would
        // hide customers the caller genuinely works with.
        var customers = await _db.Customers
            .Select(Summarise)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Ordered by code, numerically — "C-2" before "C-10". Done in memory rather than in SQL
        // because the order is on the *number inside* the code, which no plain ORDER BY expresses.
        // The client sorts too, but the export reads this order, so it lives here as well.
        return Ok(customers.OrderBy(c => CodeOrder(c.Code)).ThenBy(c => c.Code, StringComparer.Ordinal).ToList());
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export(CancellationToken cancellationToken)
    {
        var customers = (await _db.Customers
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .OrderBy(c => CodeOrder(c.Code))
            .ThenBy(c => c.Code, StringComparer.Ordinal)
            .ToList();

        var workbook = _excel.Export<Customer>(
            "Customers",
            [
                new("Code", c => c.Code),
                new("Name", c => c.Name),
                new("Type", c => c.Type),
                new("Contact", c => c.ContactPerson),
                new("Address", c => c.Address),
                new("Phone", c => c.Phone),
                new("Email", c => c.Email),
                new("VAT number", c => c.VatNumber),
                // Money as a real numeric cell, so the column can be summed — the whole reason the
                // export is built on the server (Finding 5, and ExcelFormat.Money).
                new("Credit limit", c => c.CreditLimit, ExcelFormat.Money),
            ],
            customers);

        await _audit.RecordAsync(
            AuditAction.Export,
            nameof(Customer),
            "*",
            details: new { list = "customers", rows = customers.Count },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return File(
            workbook,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"customers-{_time.GetUtcNow():yyyy-MM-dd}.xlsx");
    }

    /// <summary>The margin bands a customer can be put on — for the form's dropdown.</summary>
    [HttpGet("profit-percents")]
    public async Task<ActionResult<IReadOnlyList<ProfitPercentDto>>> ProfitPercents(
        CancellationToken cancellationToken)
    {
        return Ok(await _db.ProfitPercents
            .OrderBy(p => p.Id)
            .Select(p => new ProfitPercentDto(p.Id, p.Name ?? string.Empty))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false));
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<CustomerSummary>> Get(long id, CancellationToken cancellationToken)
    {
        var customer = await _db.Customers
            .Where(c => c.Id == id)
            .Select(Summarise)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return customer is null ? NotFound() : Ok(customer);
    }

    [HttpPost]
    public async Task<ActionResult<CreateCustomerResponse>> Create(
        SaveCustomerRequest request,
        CancellationToken cancellationToken)
    {
        // The code allocation and the customer insert are one transaction: a rolled-back create must
        // not burn a code out of the sequence the legacy app is also drawing from.
        await using var transaction = await _db.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var code = await _codes.NextAsync(cancellationToken).ConfigureAwait(false);

        var customer = new Customer { Code = code };
        Apply(customer, request);
        ReconcileContacts(customer, request.Contacts);

        _db.Customers.Add(customer);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new CreateCustomerResponse(customer.Id, code));
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(
        long id,
        SaveCustomerRequest request,
        CancellationToken cancellationToken)
    {
        var customer = await Find(id, cancellationToken).ConfigureAwait(false);

        if (customer is null)
        {
            return NotFound();
        }

        // Two people on one customer used to end with the second save winning and the first person's
        // change gone, unremarked. The version they loaded has to still be the current one.
        if (this.StaleEdit(customer, request.ExpectedRowVersion, "customer") is { } stale)
        {
            return stale;
        }

        Apply(customer, request);
        ReconcileContacts(customer, request.Contacts);

        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The check above closes the window it can see; the token in the UPDATE closes the one it
            // cannot, when two saves pass the check together. Caught so the loser gets a 409 that says
            // what happened rather than a 500 that says the server broke.
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title:
                    "Someone else changed this customer while you were editing it. Reload to see their "
                    + "version, then make your changes again.");
        }

        return NoContent();
    }

    /// <summary>
    /// Removes a customer — soft, and only when it carries no transaction history.
    /// </summary>
    /// <remarks>
    /// The legacy app refuses to delete a customer that appears on any invoice or quotation
    /// (<c>CustomerController.deletecustomer</c>), and that guard is kept — but for a subtler reason
    /// than the legacy one. The documents reference the customer by its <b>code</b> string, not by a
    /// foreign key, so removing the customer would not orphan a row EF could see. It would orphan the
    /// <i>name</i> on every one of those documents: reprint last year's invoice and the "Bill To" is
    /// blank. So a customer with history stays, and the reason is stated back to the user.
    /// </remarks>
    [HttpDelete("{id:long}")]
    [RequireChangeReason]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        var customer = await Find(id, cancellationToken).ConfigureAwait(false);

        if (customer is null)
        {
            return NotFound();
        }

        var documents = await DocumentCountFor(customer.Code, cancellationToken).ConfigureAwait(false);

        if (documents > 0)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: $"{customer.Name} appears on {documents} document(s) and cannot be removed. "
                       + "Its history would lose the name it was issued under.");
        }

        // The interceptor turns this into a soft delete and audits it with the reason attached.
        _db.Customers.Remove(customer);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return NoContent();
    }

    /// <summary>
    /// One-off backfill: splits the legacy <c>;</c>-separated <c>contactp</c>/<c>email</c> strings into
    /// structured <c>customer_contacts</c> rows (Phase 6, slice 4).
    /// </summary>
    /// <remarks>
    /// Idempotent — a customer that already has contacts is skipped, so it is safe to run more than once
    /// (and safe to run at cutover after some customers have been edited into the new shape). Because the
    /// two legacy lists are <b>independent</b> (the Nth name does not correspond to the Nth email), it only
    /// pairs a name with an email when the customer has exactly as many of each; otherwise names become
    /// name-only contacts and the surplus emails become email-only rows, flagged for Data Exceptions rather
    /// than guessed at. The legacy columns are re-joined from the rows, so nothing is lost.
    /// </remarks>
    [HttpPost("backfill-contacts")]
    public async Task<ActionResult<ContactsBackfillResult>> BackfillContacts(CancellationToken cancellationToken)
    {
        var customers = await _db.Customers
            .Include(c => c.Contacts)
            .Where(c => c.Contacts.Count == 0 && (c.ContactPerson != null || c.Email != null))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        int backfilled = 0, contactsCreated = 0, emailOnly = 0;

        foreach (var customer in customers)
        {
            var names = SplitList(customer.ContactPerson);
            var emails = SplitList(customer.Email);
            if (names.Count == 0 && emails.Count == 0)
            {
                continue;
            }

            var rows = new List<CustomerContact>();
            var pair = names.Count > 0 && names.Count == emails.Count; // only pair when the counts line up

            for (var i = 0; i < names.Count; i++)
            {
                rows.Add(new CustomerContact { CustomerId = customer.Id, Name = names[i], Email = pair ? emails[i] : null });
            }

            for (var i = pair ? emails.Count : 0; i < emails.Count; i++)
            {
                // An unpaired email has no name — a notification target only, not a document contact.
                rows.Add(new CustomerContact { CustomerId = customer.Id, Email = emails[i], Usage = ContactUsage.NotificationsOnly });
                emailOnly++;
            }

            if (rows.Count == 0)
            {
                continue;
            }

            foreach (var row in rows)
            {
                customer.Contacts.Add(row);
            }

            // Re-join from the rows, so the legacy strings stay consistent with the structured data.
            customer.ContactPerson = string.Join(";", customer.Contacts.Select(c => c.Name).Where(n => !string.IsNullOrWhiteSpace(n)));
            customer.Email = string.Join(";", customer.Contacts.Select(c => c.Email).Where(e => !string.IsNullOrWhiteSpace(e)));

            contactsCreated += rows.Count;
            backfilled++;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new ContactsBackfillResult(backfilled, contactsCreated, emailOnly));
    }

    private static List<string> SplitList(string? value) =>
        [.. (value ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    // --- helpers -----------------------------------------------------------------------------

    private Task<Customer?> Find(long id, CancellationToken cancellationToken) => _db.Customers
        .Include(c => c.Contacts)
        .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    /// <summary>
    /// The number inside a customer code, so "C-2" orders before "C-10".
    /// </summary>
    /// <remarks>
    /// A plain string sort puts "C-10" first, because '1' &lt; '2' — which is the jumble this fixes.
    /// A code with no digits (should not occur) sorts to the end rather than throwing.
    /// </remarks>
    private static long CodeOrder(string? code)
    {
        var digits = new string((code ?? string.Empty).Where(char.IsDigit).ToArray());

        return long.TryParse(digits, System.Globalization.CultureInfo.InvariantCulture, out var n)
            ? n
            : long.MaxValue;
    }

    private static void Apply(Customer customer, SaveCustomerRequest request)
    {
        customer.Name = request.Name;
        customer.Type = request.Type;
        customer.ContactPerson = request.ContactPerson;
        customer.Address = request.Address;
        customer.Phone = request.Phone;
        customer.Email = request.Email;
        customer.VatNumber = request.VatNumber;
        customer.AssignedCompanyId = request.AssignedCompanyId;
        customer.ProfitPercentId = request.ProfitPercentId;
        customer.CreditLimit = request.CreditLimit;
    }

    /// <summary>
    /// How many legacy documents name this customer code.
    /// </summary>
    /// <remarks>
    /// Raw SQL against the legacy tables: <c>invoice_h</c> and <c>quotation_h</c> are not in this
    /// context (they belong to the legacy app until Phase 5 adopts them), and they reference the
    /// customer by its code string, exactly as the legacy delete guard checks.
    /// </remarks>
    private async Task<int> DocumentCountFor(string? code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(code))
        {
            return 0;
        }

        return await _db.Database
            .SqlQuery<int>($"""
                SELECT
                    (SELECT COUNT(*) FROM invoice_h   WHERE customer = {code})
                  + (SELECT COUNT(*) FROM quotation_h WHERE customer = {code}) AS Value
                """)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static readonly System.Linq.Expressions.Expression<Func<Customer, CustomerSummary>> Summarise =
        c => new CustomerSummary(
            c.Id,
            c.Code ?? string.Empty,
            c.Name ?? string.Empty,
            c.Type,
            c.ContactPerson,
            c.Address,
            c.Phone,
            c.Email,
            c.VatNumber,
            c.AssignedCompanyId,
            c.ProfitPercentId,
            c.CreditLimit,
            c.Contacts
                .OrderBy(ct => ct.Id)
                .Select(ct => new CustomerContactDto(ct.Id, ct.Name, ct.Phone, ct.Email, ct.Usage))
                .ToList(),
            // The version the edit screen will echo back, so a concurrent save is refused rather than
            // silently overwriting whoever got there first.
            c.RowVersion);

    /// <summary>
    /// Reconciles the customer's contact rows to the request's list, and dual-writes the legacy
    /// <c>contactp</c> / <c>email</c> columns (<c>;</c>-joined) so the still-live legacy app keeps reading.
    /// A null list leaves contacts (and the legacy strings) untouched.
    /// </summary>
    private static void ReconcileContacts(Customer customer, IReadOnlyList<CustomerContactDto>? contacts)
    {
        if (contacts is null)
        {
            return;
        }

        // Replace-all: clearing the loaded collection deletes the removed rows (cascade), the new ones insert.
        customer.Contacts.Clear();
        foreach (var contact in contacts)
        {
            customer.Contacts.Add(new CustomerContact
            {
                Name = contact.Name,
                Phone = contact.Phone,
                Email = contact.Email,
                Usage = contact.Usage is ContactUsage.NotificationsOnly ? ContactUsage.NotificationsOnly : ContactUsage.DocumentsAndNotifications,
            });
        }

        customer.ContactPerson = string.Join(";", customer.Contacts
            .Select(c => c.Name).Where(n => !string.IsNullOrWhiteSpace(n)));
        customer.Email = string.Join(";", customer.Contacts
            .Select(c => c.Email).Where(e => !string.IsNullOrWhiteSpace(e)));
    }
}
