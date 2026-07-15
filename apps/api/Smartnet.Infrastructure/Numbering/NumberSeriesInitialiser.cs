using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Smartnet.Domain.Settings;
using Smartnet.Infrastructure.Persistence;

namespace Smartnet.Infrastructure.Numbering;

/// <inheritdoc cref="INumberSeriesInitialiser"/>
public sealed class NumberSeriesInitialiser : INumberSeriesInitialiser
{
    private readonly SmartnetDbContext _db;
    private readonly TimeProvider _time;

    public NumberSeriesInitialiser(SmartnetDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public async Task<IReadOnlyList<SeriesInitialisation>> InitialiseAsync(
        bool apply,
        CancellationToken cancellationToken = default)
    {
        var companies = await _db.Companies
            .Where(c => c.DeletedAt == null)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var existing = await _db.DocumentSeries
            .ToDictionaryAsync(s => (s.CompanyId, s.DocType), cancellationToken)
            .ConfigureAwait(false);

        var today = DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime);

        var results = new List<SeriesInitialisation>();

        foreach (var series in LegacyNumbering.All)
        {
            foreach (var companyId in companies)
            {
                var reading = await ReadLegacyAsync(series, companyId, cancellationToken)
                    .ConfigureAwait(false);

                existing.TryGetValue((companyId, series.DocType), out var current);

                // Never backwards. Re-running this after the new app has already issued documents
                // must not hand out numbers it has just used — which is exactly what would happen
                // if we recomputed from the legacy tables and ignored where we had got to.
                var next = Math.Max(reading.Next, current?.NextNumber ?? 0);

                var source = current is not null && current.NextNumber >= reading.Next
                    ? "already ahead of the legacy data; left where it was"
                    : reading.Source;

                // An administrator who has already set a prefix keeps it. This routine reads the
                // legacy numbering; it does not overrule a decision somebody has since made.
                var prefix = current is not null && !string.IsNullOrEmpty(current.Prefix)
                    ? current.Prefix
                    : DocumentNumberFormat.Templatise(reading.Prefix, today);

                var padding = current?.Padding ?? 0;

                results.Add(new SeriesInitialisation(
                    companyId,
                    series.DocType,
                    reading.Prefix,
                    prefix,
                    DocumentNumberFormat.Render(prefix, next, padding, today),
                    reading.LastIssued,
                    next,
                    source));

                if (!apply)
                {
                    continue;
                }

                if (current is null)
                {
                    _db.DocumentSeries.Add(new DocumentSeries
                    {
                        CompanyId = companyId,
                        DocType = series.DocType,
                        Prefix = prefix,
                        NextNumber = next,

                        // No padding: the legacy numbers run STI-999, STI-1214, and the oldest is
                        // SI-10. Padding to 5 would produce STI-01215 — a number matching none of
                        // the 2,500 documents already printed and filed.
                        Padding = 0,
                    });
                }
                else
                {
                    current.NextNumber = next;
                }
            }
        }

        if (apply)
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return results;
    }

    private sealed record Reading(string Prefix, long? LastIssued, long Next, string Source);

    /// <summary>
    /// Takes the highest of two independent readings, because either alone can be wrong.
    /// </summary>
    /// <remarks>
    /// The ticket table's AUTO_INCREMENT is the legacy allocator's own idea of the next number,
    /// and it is authoritative — a number can be allocated and the document then abandoned, so the
    /// counter runs ahead of the documents actually saved.
    ///
    /// <para>But it is not sufficient. MySQL keeps AUTO_INCREMENT in memory for InnoDB; before
    /// 8.0 it was recomputed from MAX(id)+1 on restart, and a truncated or restored table can also
    /// reset it. If it has fallen behind the numbers actually printed on documents, trusting it
    /// alone reissues a number that is already on a customer's invoice.</para>
    ///
    /// <para>So: take the larger. It is the only reading that is wrong in neither direction.</para>
    /// </remarks>
    private async Task<Reading> ReadLegacyAsync(
        LegacyNumbering.Series series,
        long companyId,
        CancellationToken cancellationToken)
    {
        var connection = (MySqlConnection)_db.Database.GetDbConnection();

        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        // --- Reading 1: the highest number actually printed on a document ---------------------
        //
        // The trailing digits, because the prefix changed over time (SI- became STI-) while the
        // counter kept running. Grouping by prefix would restart numbering at values already used.
        await using var documents = connection.CreateCommand();

        documents.CommandText = $"""
            SELECT REGEXP_REPLACE(`{series.NumberColumn}`, '[0-9]+$', '') AS prefix,
                   CAST(REGEXP_SUBSTR(`{series.NumberColumn}`, '[0-9]+$') AS UNSIGNED) AS number
            FROM `{series.DocumentTable}`
            WHERE company_id = @companyId
              AND `{series.NumberColumn}` REGEXP '[0-9]+$'
            ORDER BY number DESC
            LIMIT 1
            """;

        documents.Parameters.Add(new MySqlParameter("@companyId", companyId));

        string? prefix = null;
        long? lastIssued = null;

        await using (var reader = await documents.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                prefix = reader.IsDBNull(0) ? null : reader.GetString(0);
                lastIssued = reader.IsDBNull(1) ? null : reader.GetInt64(1);
            }
        }

        // --- Reading 2: the legacy ticket table's next auto-increment -------------------------
        long? ticket = null;

        if (series.SequenceTables.TryGetValue(companyId, out var sequenceTable))
        {
            await using var command = connection.CreateCommand();

            // information_schema, not SHOW TABLE STATUS: it is queryable and parameterisable.
            command.CommandText = """
                SELECT auto_increment
                FROM information_schema.tables
                WHERE table_schema = DATABASE() AND table_name = @table
                """;

            command.Parameters.Add(new MySqlParameter("@table", sequenceTable));

            var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

            ticket = value is null or DBNull ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }

        var fromDocuments = (lastIssued ?? 0) + 1;
        var fromTicket = ticket ?? 0;

        var next = Math.Max(fromDocuments, fromTicket);

        // A brand-new company has neither: it starts at 1, which is correct — it has issued nothing.
        if (next < 1)
        {
            next = 1;
        }

        var source = fromTicket > fromDocuments
            ? $"{sequenceTable}.AUTO_INCREMENT ({fromTicket}) — ahead of the last document ({lastIssued?.ToString(CultureInfo.InvariantCulture) ?? "none"})"
            : lastIssued is null
                ? "no documents and no counter; starting at 1"
                : $"last document issued ({lastIssued}) + 1";

        return new Reading(prefix ?? string.Empty, lastIssued, next, source);
    }
}
