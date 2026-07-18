namespace Smartnet.Infrastructure.Pdf;

/// <summary>One ordered line — the five detail columns the legacy report carried.</summary>
public sealed record PurchaseOrderItem(
    string ItemNo,
    string Description,
    decimal Quantity,
    decimal Rate,
    decimal Total);

/// <summary>
/// What a purchase order renders from.
/// </summary>
/// <remarks>
/// The quotation's mirror image, addressed outward instead of inward: the party block names a supplier
/// rather than a client, and there is no acceptance to sign — an order is an instruction, not an offer.
/// The VAT rows follow the same rule as everywhere else, appearing only when the ordering company is
/// registered.
/// </remarks>
public sealed record PurchaseOrderModel(
    byte[]? Logo,
    string CompanyName,
    string CompanyContact,
    string? AccentColour,
    string OrderNo,
    string Date,
    string SupplierName,
    string SupplierAddress,
    string SupplierContact,

    /// <summary>The supplier's telephone, already grouped for reading.</summary>
    string SupplierPhone,

    string PreparedBy,
    IReadOnlyList<PurchaseOrderItem> Items,
    decimal Subtotal,
    decimal DiscountPercent,
    decimal DiscountAmount,
    decimal NetTotal,

    /// <summary>The VAT label, e.g. "VAT (18%)" — null when the company is not VAT-registered.</summary>
    string? TaxLabel,

    /// <summary>Null when the company is not VAT-registered, which omits the VAT rows entirely.</summary>
    decimal? TaxAmount,

    decimal Total,

    /// <summary>Where goods are to be sent — the ordering company's own address.</summary>
    string DeliverTo);
