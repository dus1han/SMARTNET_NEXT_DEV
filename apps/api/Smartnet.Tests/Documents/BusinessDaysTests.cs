using FluentAssertions;
using Smartnet.Domain.Documents;

namespace Smartnet.Tests.Documents;

/// <summary>
/// Counting forward in working days — the arithmetic behind the cheque due-soon warning.
/// </summary>
/// <remarks>
/// These dates are real ones: 2026-08-07 is a Friday, so the week either side of it is written out
/// rather than computed, and a test that disagrees with a calendar is a test that is wrong.
/// </remarks>
public sealed class BusinessDaysTests
{
    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    [Theory]
    // Mid-week: nothing to skip.
    [InlineData("2026-08-05", "2026-08-07")] // Wednesday  → Friday
    [InlineData("2026-08-03", "2026-08-05")] // Monday     → Wednesday
    // The case the whole helper exists for: two calendar days from Friday is Sunday, and a cheque
    // banked on Monday would never be warned about.
    [InlineData("2026-08-07", "2026-08-11")] // Friday     → Tuesday
    [InlineData("2026-08-06", "2026-08-10")] // Thursday   → Monday
    // Asked over the weekend, the answer is the same as the Friday before — the window still covers
    // the two working days that follow, rather than starting to count on Monday.
    [InlineData("2026-08-08", "2026-08-11")] // Saturday   → Tuesday
    [InlineData("2026-08-09", "2026-08-11")] // Sunday     → Tuesday
    public void Two_business_days_skips_the_weekend(string from, string expected)
    {
        var invariant = System.Globalization.CultureInfo.InvariantCulture;

        BusinessDays.AddTo(DateOnly.Parse(from, invariant), 2)
            .Should().Be(DateOnly.Parse(expected, invariant));
    }

    [Fact]
    public void One_business_day_from_friday_is_monday()
    {
        BusinessDays.AddTo(D(2026, 8, 7), 1).Should().Be(D(2026, 8, 10));
    }

    [Fact]
    public void A_week_of_business_days_is_seven_calendar_days()
    {
        // Five working days from a Friday lands on the next Friday.
        BusinessDays.AddTo(D(2026, 8, 7), 5).Should().Be(D(2026, 8, 14));
    }

    [Fact]
    public void Zero_days_is_the_day_itself_even_on_a_weekend()
    {
        BusinessDays.AddTo(D(2026, 8, 8), 0).Should().Be(D(2026, 8, 8));
        BusinessDays.AddTo(D(2026, 8, 7), 0).Should().Be(D(2026, 8, 7));
    }

    [Fact]
    public void The_weekend_is_not_a_business_day_and_the_rest_of_the_week_is()
    {
        BusinessDays.IsBusinessDay(D(2026, 8, 8)).Should().BeFalse(); // Saturday
        BusinessDays.IsBusinessDay(D(2026, 8, 9)).Should().BeFalse(); // Sunday

        foreach (var day in new[] { 3, 4, 5, 6, 7 }) // Monday to Friday
        {
            BusinessDays.IsBusinessDay(D(2026, 8, day)).Should().BeTrue();
        }
    }
}
