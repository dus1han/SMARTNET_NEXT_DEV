namespace Smartnet.Domain.Auditing;

/// <summary>
/// The audit columns carried by every table we own.
/// </summary>
/// <remarks>
/// The user <b>id</b>, never a display name. The legacy app stores <c>addedby</c> as the string
/// "Saboor A. : 2026-07-14 10:33:12" — rename the user and the history becomes ambiguous.
/// <para>
/// All timestamps are UTC. Nothing is hard-deleted: <see cref="DeletedAt"/> is what a delete
/// sets, so every delete is recoverable and attributable.
/// </para>
/// </remarks>
public interface IAuditable
{
    long? CreatedBy { get; set; }
    DateTime CreatedAt { get; set; }

    long? UpdatedBy { get; set; }
    DateTime? UpdatedAt { get; set; }

    long? DeletedBy { get; set; }
    DateTime? DeletedAt { get; set; }

    /// <summary>
    /// Optimistic concurrency. Two users editing the same invoice now conflict loudly instead
    /// of one silently overwriting the other — the legacy app has no protection against this.
    /// </summary>
    int RowVersion { get; set; }
}

/// <summary>Marks an entity as soft-deletable, so a global query filter can hide deleted rows.</summary>
public interface ISoftDeletable
{
    DateTime? DeletedAt { get; set; }
}
