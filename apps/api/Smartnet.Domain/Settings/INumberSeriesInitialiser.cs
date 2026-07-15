namespace Smartnet.Domain.Settings;

/// <param name="DocType">INVOICE, QUOTATION, …</param>
/// <param name="ObservedPrefix">The literal prefix on the most recent document, e.g. "26JUL_SNIN_".</param>
/// <param name="Prefix">
/// The template that will be stored. Where the observed prefix visibly contains the document's own
/// date, it is proposed back as a token — "26JUL_SNIN_" becomes "{YY}{MON}_SNIN_" — so that August
/// does not go out stamped JUL. A prefix with no date in it is stored unchanged.
/// </param>
/// <param name="Example">What the next document number will actually look like. Check this.</param>
/// <param name="LastIssued">The highest number found in the legacy data. Null if none.</param>
/// <param name="NextNumber">What the next document will be numbered.</param>
/// <param name="Source">Where NextNumber came from, so the operator can sanity-check it.</param>
public sealed record SeriesInitialisation(
    long CompanyId,
    string DocType,
    string ObservedPrefix,
    string Prefix,
    string Example,
    long? LastIssued,
    long NextNumber,
    string Source);

public interface INumberSeriesInitialiser
{
    /// <summary>
    /// Reads the legacy numbering and points <c>document_series</c> at the next unused number, so
    /// that the new app continues the sequence rather than restarting it.
    /// </summary>
    /// <remarks>
    /// <b>Run this at cutover, immediately after the legacy app stops issuing that document type.</b>
    /// Not before: any invoice the old app raises after this runs takes a number the new app also
    /// believes is free. The UNIQUE index on the number column turns that into a loud failure
    /// rather than a duplicate in the ledger, but a loud failure is still an outage.
    ///
    /// <para>Safe to run repeatedly: it never moves a counter <i>backwards</i>. Re-running after
    /// the new app has issued documents leaves it exactly where it is.</para>
    /// </remarks>
    /// <param name="apply">
    /// False performs a dry run — it reports what it would do and writes nothing. The default,
    /// because a numbering change that nobody previewed is not a change anybody should make.
    /// </param>
    Task<IReadOnlyList<SeriesInitialisation>> InitialiseAsync(
        bool apply,
        CancellationToken cancellationToken = default);
}
