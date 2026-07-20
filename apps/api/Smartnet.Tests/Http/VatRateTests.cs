using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Smartnet.Tests.Http;

/// <summary>
/// Setting the business VAT rate, over HTTP — the fan-out across VAT companies, and the one-default-per-
/// start-date invariant that lets a rate change be scheduled without felling the rate in force.
/// </summary>
/// <remarks>
/// This is the pair of behaviours that, wrong, take down invoicing: the fan-out has to reach every
/// VAT-registered company, and adding a future-dated rate must NOT clear the current open-ended one — the
/// bug the earlier overlap-based clearing carried. The seeded company is VAT-registered and starts with no
/// rates, so what these assert about it is exactly what the endpoint put there.
/// </remarks>
[Collection(nameof(ApiCollection))]
public sealed class VatRateTests
{
    private readonly ApiFixture _api;

    public VatRateTests(ApiFixture api) => _api = api;

    private sealed record RateRow(
        long Id, string Name, decimal Percentage, DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool IsDefault);

    private async Task<HttpResponseMessage> SetVatRate(string name, decimal percentage, string effectiveFrom)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/settings/vat-rate")
        {
            Content = JsonContent.Create(new { name, percentage, effectiveFrom }),
        };
        request.Headers.Add("X-Change-Reason", "Scheduling a VAT rate change for the whole business.");
        return await _api.SignedIn.SendAsync(request);
    }

    private async Task<IReadOnlyList<RateRow>> RatesForSeededCompany()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/settings/tax-rates");
        request.Headers.Add("X-Company-Id", _api.CompanyId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var response = await _api.SignedIn.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<RateRow>>())!;
    }

    [Fact]
    public async Task Setting_a_vat_rate_applies_it_to_the_vat_registered_company()
    {
        var response = await SetVatRate("VAT 18%", 18m, "2024-01-01");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var applied = (await response.Content.ReadFromJsonAsync<VatRateApplied>())!;
        applied.CompaniesAffected.Should().BeGreaterThanOrEqualTo(1);

        var rates = await RatesForSeededCompany();
        rates.Should().Contain(r => r.Name == "VAT 18%" && r.Percentage == 18m && r.IsDefault);
    }

    [Fact]
    public async Task A_future_rate_does_not_clear_the_current_one_but_a_same_day_rate_supersedes_it()
    {
        // Establish a current rate, then schedule a later one. The earlier overlap-based clearing would
        // have cleared the open-ended current rate here, leaving every pre-2027 document with no rate in
        // force. Both must stay default — different start dates never conflict.
        (await SetVatRate("VAT 18%", 18m, "2024-01-01")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SetVatRate("VAT 20%", 20m, "2027-01-01")).StatusCode.Should().Be(HttpStatusCode.OK);

        var afterSchedule = await RatesForSeededCompany();
        afterSchedule.Should().Contain(r => r.Percentage == 18m && r.EffectiveFrom == new DateOnly(2024, 1, 1) && r.IsDefault);
        afterSchedule.Should().Contain(r => r.Percentage == 20m && r.EffectiveFrom == new DateOnly(2027, 1, 1) && r.IsDefault);

        // Now correct the scheduled rate — same start date. THAT is the only case that supersedes: two
        // defaults sharing a day, where "the latest" would be a coin toss.
        (await SetVatRate("VAT 22%", 22m, "2027-01-01")).StatusCode.Should().Be(HttpStatusCode.OK);

        var afterCorrection = await RatesForSeededCompany();
        afterCorrection.Should().Contain(r => r.Percentage == 18m && r.EffectiveFrom == new DateOnly(2024, 1, 1) && r.IsDefault);
        afterCorrection.Should().Contain(r => r.Percentage == 22m && r.EffectiveFrom == new DateOnly(2027, 1, 1) && r.IsDefault);
        // The 20% it replaced is no longer a default — it was superseded on its start date.
        afterCorrection.Should().Contain(r => r.Percentage == 20m && r.EffectiveFrom == new DateOnly(2027, 1, 1) && !r.IsDefault);
    }

    private sealed record VatRateApplied(int CompaniesAffected);
}
