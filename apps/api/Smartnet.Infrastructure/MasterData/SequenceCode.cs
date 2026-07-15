using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Smartnet.Infrastructure.Persistence;

namespace Smartnet.Infrastructure.MasterData;

/// <summary>
/// The one mechanism behind every legacy code allocator: insert a row into a <c>*_seq</c> table and
/// take the AUTO_INCREMENT it produced, on a single pinned connection.
/// </summary>
/// <remarks>
/// This lived inline in <c>CustomerCodeAllocator</c> until Suppliers needed the identical dance for
/// <c>sup_seq</c>. Copying it would have re-created the legacy app's own worst habit — a subtlety
/// fixed on one screen and left broken on another. There is exactly one connection-pinning here, and
/// both allocators share it, so it can only be right or wrong in one place.
/// </remarks>
internal static class SequenceCode
{
    public static async Task<string> NextAsync(
        SmartnetDbContext db,
        string sequenceTable,
        string prefix,
        string today,
        CancellationToken cancellationToken)
    {
        // sequenceTable is a compile-time constant from our own code ("cus_seq", "sup_seq") — never
        // user input. The guard is belt-and-braces: it can never be a value an attacker chose, but
        // interpolating an identifier into SQL is the kind of thing that must not be reachable by
        // accident from anywhere later.
        if (!IsSafeIdentifier(sequenceTable))
        {
            throw new ArgumentException($"'{sequenceTable}' is not a valid sequence table name.", nameof(sequenceTable));
        }

        // LAST_INSERT_ID() is connection-scoped, so the INSERT and the SELECT must run on the SAME
        // physical connection. EF pools connections and would happily run the two calls on two
        // different ones — returning 0 from a connection that never did the INSERT. Opening the
        // connection explicitly makes EF hold and reuse it across both. (Inside a controller's
        // transaction this is already true; opening it here makes the allocator correct on its own,
        // which is what the tests exercise.)
        var opened = db.Database.GetDbConnection().State != System.Data.ConnectionState.Open;

        if (opened)
        {
            await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            // The identifier is inlined (it cannot be parameterised in SQL and is validated above);
            // the date — the only value — is a real {0} parameter. Concatenated rather than
            // interpolated so it is ExecuteSqlRaw's plain-string overload, not the interpolated one
            // EF1002 (correctly) refuses.
            var insert = "INSERT INTO " + sequenceTable + " (dt) VALUES ({0})";

            await db.Database
                .ExecuteSqlRawAsync(insert, [today], cancellationToken)
                .ConfigureAwait(false);

            var sequence = await db.Database
                .SqlQuery<long>($"SELECT LAST_INSERT_ID() AS Value")
                .SingleAsync(cancellationToken)
                .ConfigureAwait(false);

            return $"{prefix}{sequence.ToString(CultureInfo.InvariantCulture)}";
        }
        finally
        {
            if (opened)
            {
                await db.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }
    }

    private static bool IsSafeIdentifier(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        foreach (var c in value)
        {
            if (!char.IsAsciiLetterLower(c) && c != '_')
            {
                return false;
            }
        }

        return true;
    }
}
