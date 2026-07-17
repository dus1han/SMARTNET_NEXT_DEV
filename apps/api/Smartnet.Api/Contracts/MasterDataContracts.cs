using FluentValidation;

namespace Smartnet.Api.Contracts;

// --- Customers -----------------------------------------------------------------------------

/// <param name="Code">The business's identifier — "C-42". Server-allocated; never sent on create.</param>
/// <param name="AssignedCompanyId">
/// The trading entity this customer is associated with — an indication, not a boundary. See
/// <c>Customer.AssignedCompanyId</c>: both entities invoice each other's customers, so this is a
/// default when raising a document and nothing filters on it.
/// </param>
/// <summary>One structured contact (Phase 6, slice 4) — a real row behind the legacy <c>;</c>-separated strings.</summary>
public sealed record CustomerContactDto(long Id, string? Name, string? Phone, string? Email, string Usage);

public sealed record CustomerSummary(
    long Id,
    string Code,
    string Name,
    string? Type,
    // Kept for the list search and back-compat; it is the ;-joined contact names, dual-written from Contacts.
    string? ContactPerson,
    string? Address,
    string? Phone,
    string? Email,
    string? VatNumber,
    long? AssignedCompanyId,
    long? ProfitPercentId,
    decimal CreditLimit,
    // The structured contacts — the source of truth the document contact-pickers now read.
    IReadOnlyList<CustomerContactDto> Contacts);

/// <remarks>
/// No code field: on create the server allocates one from the shared sequence; on edit the code is
/// the identity and does not change. This is the same reason the create-user form has no username
/// generator on the client — the value the business is identified by is the server's to hand out.
/// </remarks>
public sealed record SaveCustomerRequest(
    string Name,
    string? Type,
    string? ContactPerson,
    string? Address,
    string? Phone,
    string? Email,
    string? VatNumber,
    long? AssignedCompanyId,
    long? ProfitPercentId,
    decimal CreditLimit,
    // The structured contacts. When present, the customer's contact rows are reconciled to this list and
    // the legacy contactp/email columns are dual-written (;-joined) from it. Null leaves contacts untouched.
    IReadOnlyList<CustomerContactDto>? Contacts = null);

public sealed record CreateCustomerResponse(long Id, string Code);

/// <summary>
/// What the one-off contacts backfill did (Phase 6, slice 4). <see cref="EmailOnlyRows"/> are the surplus
/// emails that could not be paired with a name — created as email-only contacts, to surface in Data Exceptions.
/// </summary>
public sealed record ContactsBackfillResult(int CustomersBackfilled, int ContactsCreated, int EmailOnlyRows);

/// <summary>A margin band the customer can be put on — "5", "10". From <c>profit_percent</c>.</summary>
public sealed record ProfitPercentDto(long Id, string Name);

// --- Suppliers -----------------------------------------------------------------------------

public sealed record SupplierSummary(
    long Id,
    string Code,
    string Name,
    string? ContactPerson,
    string? Address,
    string? Phone,
    string? Email,
    string? VatNumber);

public sealed record SaveSupplierRequest(
    string Name,
    string? ContactPerson,
    string? Address,
    string? Phone,
    string? Email,
    string? VatNumber);

public sealed record CreateSupplierResponse(long Id, string Code);

// --- Items ---------------------------------------------------------------------------------

/// <param name="StockBalance">Derived — the sum of the item's stock movements. Never stored.</param>
/// <param name="BelowReorder">True when a reorder level is set and the balance has fallen to it.</param>
public sealed record ItemSummary(
    long Id,
    string Code,
    string Name,
    decimal? SellingPrice,
    decimal? Cost,
    decimal? ReorderLevel,
    string? Unit,
    decimal StockBalance,
    bool BelowReorder);

public sealed record SaveItemRequest(
    string Name,
    decimal? SellingPrice,
    decimal? Cost,
    decimal? ReorderLevel,
    string? Unit);

public sealed record CreateItemResponse(long Id, string Code);

// --- Stock ---------------------------------------------------------------------------------

/// <param name="BalanceAfter">The running balance the ledger reached at this movement — a display
/// convenience computed on read, never stored.</param>
public sealed record StockMovementDto(
    long Id,
    string Type,
    decimal Quantity,
    decimal BalanceAfter,
    string? Reason,
    DateTime OccurredAt,
    long? CreatedBy,
    DateTime CreatedAt);

/// <summary>One legacy receipt batch — <c>item_stock</c>, shown for reference beside the ledger.</summary>
public sealed record StockBatchDto(
    long Id,
    decimal? Quantity,
    decimal? Balance,
    decimal? UnitCost,
    DateOnly? InDate,
    string? EnteredBy);

/// <param name="Balance">The item's live balance: the sum of every movement. The authority.</param>
public sealed record ItemStockResponse(
    long ItemId,
    string Code,
    string Name,
    decimal? ReorderLevel,
    decimal Balance,
    IReadOnlyList<StockMovementDto> Movements,
    IReadOnlyList<StockBatchDto> Batches);

/// <param name="Quantity">Signed: positive adds stock, negative removes it. Zero is not a movement.</param>
/// <param name="OccurredAt">When the count/write-off actually happened. Defaults to now if omitted.</param>
public sealed record CreateStockAdjustmentRequest(
    decimal Quantity,
    string Reason,
    DateTime? OccurredAt);

// --- Validators ----------------------------------------------------------------------------

public sealed class SaveCustomerRequestValidator : AbstractValidator<SaveCustomerRequest>
{
    public SaveCustomerRequestValidator()
    {
        // 100 is the legacy varchar(100) width. The DB would truncate a longer value silently; the
        // server rejects it loudly, which is the difference between a name that is wrong and a name
        // that is quietly half-there.
        RuleFor(c => c.Name).NotEmpty().MaximumLength(100);
        RuleFor(c => c.ContactPerson).MaximumLength(100);
        RuleFor(c => c.Address).MaximumLength(100);
        RuleFor(c => c.Phone).MaximumLength(100);
        RuleFor(c => c.VatNumber).MaximumLength(100);

        RuleFor(c => c.Type)
            .Must(t => t is null or "Company" or "Individual")
            .WithMessage("Type must be Company or Individual.");

        // A credit limit is money. It cannot be negative — a negative limit is not a smaller limit,
        // it is a nonsense the credit check in Phase 5 would read as "always over".
        RuleFor(c => c.CreditLimit)
            .GreaterThanOrEqualTo(0)
            .WithMessage("A credit limit cannot be negative.");
    }
}

public sealed class SaveSupplierRequestValidator : AbstractValidator<SaveSupplierRequest>
{
    public SaveSupplierRequestValidator()
    {
        RuleFor(s => s.Name).NotEmpty().MaximumLength(100);
        RuleFor(s => s.ContactPerson).MaximumLength(100);
        RuleFor(s => s.Address).MaximumLength(100);
        RuleFor(s => s.Phone).MaximumLength(100);
        RuleFor(s => s.VatNumber).MaximumLength(100);
    }
}

public sealed class SaveItemRequestValidator : AbstractValidator<SaveItemRequest>
{
    public SaveItemRequestValidator()
    {
        RuleFor(i => i.Name).NotEmpty().MaximumLength(100);
        RuleFor(i => i.Unit).MaximumLength(32);

        // Money and quantities cannot be negative — a negative price or cost is not a smaller one.
        RuleFor(i => i.SellingPrice).GreaterThanOrEqualTo(0).When(i => i.SellingPrice.HasValue)
            .WithMessage("A price cannot be negative.");
        RuleFor(i => i.Cost).GreaterThanOrEqualTo(0).When(i => i.Cost.HasValue)
            .WithMessage("A cost cannot be negative.");
        RuleFor(i => i.ReorderLevel).GreaterThanOrEqualTo(0).When(i => i.ReorderLevel.HasValue)
            .WithMessage("A reorder level cannot be negative.");
    }
}

public sealed class CreateStockAdjustmentRequestValidator : AbstractValidator<CreateStockAdjustmentRequest>
{
    public CreateStockAdjustmentRequestValidator()
    {
        // Zero moves nothing, so it is not an adjustment — rejecting it keeps the ledger free of
        // rows that say a quantity changed by nothing.
        RuleFor(a => a.Quantity).NotEqual(0).WithMessage("An adjustment must change the quantity.");

        // The reason is first-class ledger data here, not just an audit note — it is the answer to
        // "why is the count different?", read back off the stock history forever.
        RuleFor(a => a.Reason).NotEmpty().MaximumLength(500)
            .WithMessage("A stock adjustment needs a reason.");
    }
}
