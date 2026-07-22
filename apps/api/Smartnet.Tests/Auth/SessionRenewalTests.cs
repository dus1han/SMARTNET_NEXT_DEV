using FluentAssertions;
using Smartnet.Api.Auth;

namespace Smartnet.Tests.Auth;

/// <summary>
/// When a session in use is handed a fresh token.
/// </summary>
/// <remarks>
/// The behaviour this protects is a user not being signed out mid-invoice. The behaviour it protects
/// against is a session that never ends: renewal on activity means a stolen cookie stays valid for as
/// long as somebody keeps using it, and the absolute cap is the only thing standing between that and
/// indefinite. Both directions are tested, including at the boundaries, because that is where a mistake
/// looks correct.
/// </remarks>
public sealed class SessionRenewalTests
{
    private static readonly DateTime Now = new(2026, 7, 22, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);
    private static readonly TimeSpan TwelveHours = TimeSpan.FromHours(12);

    private static bool IsDue(DateTime expires, DateTime started) =>
        SessionRenewal.IsDue(Now, expires, started, Hour, TwelveHours);

    [Fact]
    public void A_fresh_token_is_left_alone() =>
        // Signed in a minute ago: fifty-nine minutes left, nothing to do. Renewing here would mean a
        // database read and a Set-Cookie on every single request.
        IsDue(Now.AddMinutes(59), Now.AddMinutes(-1)).Should().BeFalse();

    [Fact]
    public void A_token_past_half_its_life_is_renewed() =>
        IsDue(Now.AddMinutes(20), Now.AddMinutes(-40)).Should().BeTrue();

    [Fact]
    public void Exactly_half_spent_is_renewed() =>
        // The boundary is inclusive. An exclusive one would be wrong only for an instant, which is
        // precisely the kind of detail that survives review and fails in production.
        IsDue(Now.AddMinutes(30), Now.AddMinutes(-30)).Should().BeTrue();

    [Fact]
    public void A_second_before_half_spent_is_not() =>
        IsDue(Now.AddMinutes(30).AddSeconds(1), Now.AddMinutes(-30)).Should().BeFalse();

    [Fact]
    public void An_hour_of_continuous_work_keeps_renewing() =>
        // The point of the whole change: activity, not elapsed time since sign-in, decides.
        IsDue(Now.AddMinutes(5), Now.AddHours(-3)).Should().BeTrue();

    [Fact]
    public void A_session_at_the_absolute_cap_is_not_renewed_again() =>
        // Twelve hours old and otherwise due. It keeps the token it has and lapses with it.
        IsDue(Now.AddMinutes(5), Now.AddHours(-12)).Should().BeFalse();

    [Fact]
    public void A_session_just_inside_the_cap_still_is() =>
        IsDue(Now.AddMinutes(5), Now.AddHours(-12).AddSeconds(1)).Should().BeTrue();

    [Fact]
    public void An_expired_token_is_not_resurrected() =>
        // The browser is entitled to have discarded this cookie already. Reviving one that survived is
        // a sign-in dressed as a renewal, and would make the idle limit meaningless.
        IsDue(Now.AddSeconds(-1), Now.AddHours(-2)).Should().BeFalse();

    [Fact]
    public void A_token_expiring_this_instant_is_not_resurrected() =>
        IsDue(Now, Now.AddHours(-2)).Should().BeFalse();

    [Fact]
    public void A_session_that_claims_to_start_in_the_future_is_still_capped_sanely() =>
        // A clock that disagrees with itself must not produce an unbounded session. Negative age is
        // below the cap, so this falls through to the ordinary half-life rule rather than to "renew".
        IsDue(Now.AddMinutes(59), Now.AddHours(1)).Should().BeFalse();
}
