namespace Smartnet.Domain.MasterData;

/// <summary>
/// Allocates the next supplier code — "S-87" — from the sequence the legacy app also uses.
/// </summary>
/// <remarks>
/// The supplier counterpart of <see cref="ICustomerCodeAllocator"/>, and for the identical reason:
/// the legacy app allocates a supplier code from <c>sup_seq</c> (<c>SupplierController.savesupplier</c>
/// inserts a row and takes the new auto-increment), and <b>it is still live and still allocating</b>.
/// The new app must draw from the same table, or the two hand "S-87" to two different suppliers on the
/// same afternoon and the unique index this phase added rejects the second save.
/// </remarks>
public interface ISupplierCodeAllocator
{
    /// <summary>
    /// Reserves and returns the next code. Must run inside the same transaction as the INSERT that
    /// uses it, so a rolled-back supplier creation does not burn a code.
    /// </summary>
    Task<string> NextAsync(CancellationToken cancellationToken = default);
}
