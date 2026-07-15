namespace Smartnet.Domain.MasterData;

/// <summary>
/// Allocates the next customer code — "C-232" — from the sequence the legacy app also uses.
/// </summary>
/// <remarks>
/// <b>Why not just an AUTO_INCREMENT.</b> The business identifies a customer by its code, and the
/// legacy app allocates that code from <c>cus_seq</c>: it inserts a row and takes the new
/// auto-increment id. <b>The legacy app is still live and still allocating from the same table</b>,
/// so the new app must draw from it too — otherwise the two apps hand out "C-232" to two different
/// customers on the same afternoon, and the unique index this phase just added rejects the second
/// one. Sharing the sequence is what keeps them from colliding during coexistence.
/// <para>
/// The <c>id</c> surrogate key added by the migration is a different thing entirely: it identifies
/// the row to EF and to the audit log. The code identifies the customer to a human.
/// </para>
/// </remarks>
public interface ICustomerCodeAllocator
{
    /// <summary>
    /// Reserves and returns the next code. Must run inside the same transaction as the INSERT that
    /// uses it, so a rolled-back customer creation does not burn a code.
    /// </summary>
    Task<string> NextAsync(CancellationToken cancellationToken = default);
}
