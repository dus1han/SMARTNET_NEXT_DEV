namespace Smartnet.Domain.Documents;

/// <summary>
/// Materialises a legacy purchase order into the new model, on first edit.
/// </summary>
/// <remarks>
/// The mirror of <see cref="ILegacyQuotationAdopter"/>, addressed to a supplier. A legacy order lives in
/// <c>po_h</c>/<c>po_l</c> with only its original <c>varchar</c> columns filled: the typed decimals are
/// zero and the lines carry no <c>purchase_order_id</c>, being linked by <c>pono</c> alone. Editing such a
/// row without adopting it first would find no existing lines, treat every posted line as new, and leave
/// the document with no record of what it looked like when it arrived.
///
/// <para>Adoption fixes that: the typed columns are valued from the legacy figures through the one tax
/// engine, the lines are linked by key, and a <b>version 1 "as imported" snapshot</b> is written — so the
/// edit that follows becomes version 2 and the History tab can show what actually changed.</para>
/// </remarks>
public interface ILegacyPurchaseOrderAdopter
{
    /// <summary>
    /// Materialises the order inside the caller's transaction. A no-op for an order already adopted, so
    /// the editor can call it unconditionally.
    /// </summary>
    Task MaterialiseInCurrentTransactionAsync(PurchaseOrder order, CancellationToken cancellationToken = default);
}
