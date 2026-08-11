namespace Smartnet.Domain.Documents;

/// <summary>
/// Counting forward in working days rather than calendar ones.
/// </summary>
/// <remarks>
/// <para>Written for the cheque due-soon warning, where the difference is the whole point: two calendar
/// days from a Friday is Sunday, so a cheque that becomes bankable on Monday would never be warned
/// about — the one warning that most needed giving, given nobody is at a desk over the weekend to act
/// on it.</para>
///
/// <para><b>Saturday and Sunday only.</b> Public holidays are not modelled anywhere in this system —
/// there is no calendar table and Sri Lanka's poya days move with the lunar month, so there is nothing
/// to read. A holiday inside the window therefore shortens the real notice by a day. That is a known
/// limit of a weekend-only rule, and it errs towards warning slightly early rather than slightly late,
/// which is the right way round for a payment leaving the account.</para>
/// </remarks>
public static class BusinessDays
{
    /// <summary>Whether this date is a working day — everything except Saturday and Sunday.</summary>
    public static bool IsBusinessDay(DateOnly date) =>
        date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

    /// <summary>
    /// The date <paramref name="days"/> business days after <paramref name="from"/>.
    /// </summary>
    /// <remarks>
    /// Counts working days landed on, not days elapsed: two business days from Friday is Tuesday, from
    /// Wednesday is Friday. <paramref name="from"/> itself is never counted, whether or not it is a
    /// working day, so asking from a Saturday gives the same answer as asking from the Friday before it
    /// — a window opened over the weekend still covers the two working days that follow.
    /// </remarks>
    /// <param name="days">How many business days forward. Zero or less returns <paramref name="from"/>.</param>
    public static DateOnly AddTo(DateOnly from, int days)
    {
        var date = from;

        for (var counted = 0; counted < days;)
        {
            date = date.AddDays(1);

            if (IsBusinessDay(date))
            {
                counted++;
            }
        }

        return date;
    }
}
