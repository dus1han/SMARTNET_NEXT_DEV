namespace Smartnet.Domain.MasterData;

/// <summary>
/// Allocates the next item code — "I-501" — from the sequence the legacy app also uses.
/// </summary>
/// <remarks>
/// The third of the master-data allocators, same reason as <see cref="ICustomerCodeAllocator"/> and
/// <see cref="ISupplierCodeAllocator"/>: the legacy app allocates an item code from <c>item_seq</c>
/// (<c>ItemController.saveitem</c> inserts a row and takes the new auto-increment), and it is still
/// live. The new app draws from the same table so the two cannot hand "I-501" to two different items.
/// <para>
/// This one is called from inside the invoice screen as well as the items list — an item that is not
/// in the master gets added to the master without leaving the document (Phase 3 plan). The cheapest
/// way to add to the catalogue is the only way it stays worth having.
/// </para>
/// </remarks>
public interface IItemCodeAllocator
{
    Task<string> NextAsync(CancellationToken cancellationToken = default);
}
