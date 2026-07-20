using FluentAssertions;
using Smartnet.Domain.Documents;
using Smartnet.Domain.Settings;

namespace Smartnet.Tests.Documents;

/// <summary>
/// The tax engine — the piece the whole of Phase 5's money correctness rests on.
/// </summary>
/// <remarks>
/// One rate per document, the selected company's, applied to every line (the
/// <c>one-vat-rate-per-document</c> decision). Pure arithmetic, so no database and no fixture: these run
/// in milliseconds and are the fast half of the non-negotiable test (DEVELOPMENT.md §9). The slow half —
/// the same case end to end through the endpoint and the ledger — arrives with the save pipeline.
/// </remarks>
public sealed class TaxEngineTests
{
    private static readonly TaxEngine Engine = new();

    private static readonly DateOnly Today = new(2026, 7, 15);

    private static TaxRate Rate(
        long id,
        string name,
        decimal percentage,
        bool isDefault = true,
        DateOnly? from = null,
        DateOnly? to = null) => new()
        {
            Id = id,
            CompanyId = 1,
            Name = name,
            Percentage = percentage,
            IsDefault = isDefault,
            EffectiveFrom = from ?? new DateOnly(2024, 1, 1),
            EffectiveTo = to,
        };

    private static readonly TaxRate Vat18 = Rate(1, "VAT 18%", 18m);

    private static TaxCalculationRequest Request(
        IReadOnlyList<TaxLineInput> lines,
        bool vatRegistered = true,
        TaxRounding rounding = TaxRounding.PerLine,
        DateOnly? date = null,
        IReadOnlyList<TaxRate>? rates = null,
        decimal documentDiscountPercent = 0m) =>
        new(date ?? Today, vatRegistered, rounding, lines, rates ?? [Vat18], documentDiscountPercent);

    // --- The non-negotiable case: one rate across the lines, with a discount -------------------

    [Fact]
    public void The_company_rate_applies_to_every_line_with_a_discount()
    {
        var result = Engine.Calculate(Request([
            // 2 × 100, 10% off: net 180, VAT 32.40.
            new TaxLineInput(2m, 100m, 10m),
            // 1 × 50, no discount: net 50, VAT 9.
            new TaxLineInput(1m, 50m, 0m),
        ]));

        // The rate is snapshotted onto the document, by name and percentage — not a bare number.
        result.TaxRateId.Should().Be(Vat18.Id);
        result.TaxRateName.Should().Be("VAT 18%");
        result.TaxRatePercentage.Should().Be(18m);

        result.Lines[0].Net.Should().Be(180m);
        result.Lines[0].Tax.Should().Be(32.40m);
        result.Lines[0].Total.Should().Be(212.40m);
        result.Lines[1].Net.Should().Be(50m);
        result.Lines[1].Tax.Should().Be(9m);

        result.Totals.Subtotal.Should().Be(250m);
        result.Totals.Discount.Should().Be(20m);
        result.Totals.Net.Should().Be(230m);
        result.Totals.Tax.Should().Be(41.40m);
        result.Totals.Total.Should().Be(271.40m);
    }

    [Fact]
    public void The_discount_is_taken_before_vat()
    {
        var result = Engine.Calculate(Request([new TaxLineInput(1m, 1000m, 10m)]));

        // 1000 − 10% = 900 taxable, VAT 162, total 1062 — not VAT on the pre-discount 1000.
        result.Lines[0].Net.Should().Be(900m);
        result.Lines[0].Tax.Should().Be(162m);
        result.Totals.Total.Should().Be(1062m);
    }

    [Fact]
    public void A_document_discount_is_taken_after_line_discounts_and_before_vat()
    {
        var result = Engine.Calculate(Request(
        [
            // 2 × 100, 10% off: line net 180.
            new TaxLineInput(2m, 100m, 10m),
            // 1 × 50, no line discount: line net 50.
            new TaxLineInput(1m, 50m, 0m),
        ],
        // A further 5% off the whole document.
        documentDiscountPercent: 5m));

        // Lines net to 230; 5% of that is 11.50 off, leaving 218.50 taxable. VAT 18% = 39.33, total 257.83.
        result.Totals.Subtotal.Should().Be(250m);
        result.Totals.Discount.Should().Be(31.50m); // 20 line + 11.50 document
        result.Totals.Net.Should().Be(218.50m);
        result.Totals.Tax.Should().Be(39.33m);
        result.Totals.Total.Should().Be(257.83m);

        // The line figures are unchanged — the document discount lives on the foot, not on a line.
        result.Lines[0].Net.Should().Be(180m);
        result.Lines[1].Net.Should().Be(50m);
    }

    [Fact]
    public void With_no_document_discount_the_foot_is_unchanged()
    {
        var lines = new[] { new TaxLineInput(2m, 100m, 10m), new TaxLineInput(1m, 50m, 0m) };

        var withZero = Engine.Calculate(Request(lines, documentDiscountPercent: 0m));
        var without = Engine.Calculate(Request(lines));

        withZero.Totals.Should().BeEquivalentTo(without.Totals);
        withZero.Totals.Discount.Should().Be(20m); // line discounts only
    }

    // --- Rules ---------------------------------------------------------------------------------

    [Fact]
    public void A_company_not_registered_for_vat_charges_none()
    {
        var result = Engine.Calculate(Request(
            [new TaxLineInput(1m, 1000m, 0m)],
            vatRegistered: false));

        result.TaxRatePercentage.Should().Be(0m);
        result.TaxRateName.Should().Be("No VAT");
        result.TaxRateId.Should().BeNull();
        result.Lines[0].Tax.Should().Be(0m);
        result.Totals.Total.Should().Be(1000m);
    }

    [Fact]
    public void The_rate_is_resolved_as_of_the_document_date_not_today()
    {
        // 15% until end of 2023, 18% from 2024 — the company's default rate, two effective rows.
        var rates = new[]
        {
            Rate(3, "VAT 15%", 15m, from: new DateOnly(2023, 1, 1), to: new DateOnly(2023, 12, 31)),
            Rate(4, "VAT 18%", 18m, from: new DateOnly(2024, 1, 1)),
        };

        var old = Engine.Calculate(Request(
            [new TaxLineInput(1m, 100m, 0m)], date: new DateOnly(2023, 6, 1), rates: rates));
        var recent = Engine.Calculate(Request(
            [new TaxLineInput(1m, 100m, 0m)], date: new DateOnly(2026, 6, 1), rates: rates));

        // A 2023 document is 15%, a 2026 one is 18% — the legacy CURDATE() lookup would make both 18%.
        old.Lines[0].Tax.Should().Be(15m);
        old.TaxRateName.Should().Be("VAT 15%");
        recent.Lines[0].Tax.Should().Be(18m);
    }

    [Fact]
    public void A_company_with_no_rate_in_force_is_rejected_rather_than_taxed_at_zero()
    {
        // The default rate only starts in 2030 — nothing is in force today. The legacy app taxes at 0
        // and issues anyway; we refuse.
        var future = Rate(9, "VAT 20%", 20m, from: new DateOnly(2030, 1, 1));

        var act = () => Engine.Calculate(Request([new TaxLineInput(1m, 100m, 0m)], rates: [future]));

        act.Should().Throw<TaxRateNotResolvableException>();
    }

    // --- Scheduling a rate change --------------------------------------------------------------

    [Fact]
    public void A_rate_change_can_be_scheduled_without_disturbing_the_rate_in_force()
    {
        // The case the business actually has: VAT goes to 20% on 1 January, and somebody enters that in
        // advance. Both rates are default, because they are never in force on the same day.
        var current = Rate(1, "VAT 18%", 18m, from: new DateOnly(2024, 1, 1), to: new DateOnly(2026, 12, 31));
        var scheduled = Rate(2, "VAT 20%", 20m, from: new DateOnly(2027, 1, 1));

        var today = Engine.Calculate(Request([new TaxLineInput(1m, 100m, 0m)], rates: [current, scheduled]));
        today.Totals.Tax.Should().Be(18m);

        var newYear = Engine.Calculate(Request(
            [new TaxLineInput(1m, 100m, 0m)],
            rates: [current, scheduled],
            date: new DateOnly(2027, 1, 1)));
        newYear.Totals.Tax.Should().Be(20m);
    }

    [Fact]
    public void An_open_ended_rate_yields_to_a_later_one_once_that_starts()
    {
        // The likelier shape in practice: nobody closed off the old rate, so it is open-ended and the two
        // overlap. The engine takes the latest EffectiveFrom on or before the document date, so the
        // scheduled rate wins from its start date and the old one governs everything before it. This is
        // what lets ClearOtherDefaults leave the old rate alone instead of rewriting its end date.
        var current = Rate(1, "VAT 18%", 18m, from: new DateOnly(2024, 1, 1));
        var scheduled = Rate(2, "VAT 20%", 20m, from: new DateOnly(2027, 1, 1));

        Engine.Calculate(Request([new TaxLineInput(1m, 100m, 0m)], rates: [current, scheduled]))
            .Totals.Tax.Should().Be(18m);

        Engine.Calculate(Request(
                [new TaxLineInput(1m, 100m, 0m)],
                rates: [current, scheduled],
                date: new DateOnly(2027, 6, 1)))
            .Totals.Tax.Should().Be(20m);
    }

    [Fact]
    public void Rates_that_never_coexist_do_not_overlap_and_ones_that_do_are_caught()
    {
        var current = Rate(1, "VAT 18%", 18m, from: new DateOnly(2024, 1, 1), to: new DateOnly(2026, 12, 31));
        var scheduled = Rate(2, "VAT 20%", 20m, from: new DateOnly(2027, 1, 1));
        var openEnded = Rate(3, "VAT 18%", 18m, from: new DateOnly(2024, 1, 1));

        // Adjacent, not overlapping — 31 December and 1 January.
        current.Overlaps(scheduled).Should().BeFalse();
        scheduled.Overlaps(current).Should().BeFalse();

        // An open end runs into everything after it, in both directions.
        openEnded.Overlaps(scheduled).Should().BeTrue();
        scheduled.Overlaps(openEnded).Should().BeTrue();

        // And a rate always overlaps itself, which is why the caller excludes the one being saved.
        current.Overlaps(current).Should().BeTrue();
    }

    [Fact]
    public void A_rate_knows_the_days_it_applies_to()
    {
        var rate = Rate(1, "VAT 18%", 18m, from: new DateOnly(2026, 1, 1), to: new DateOnly(2026, 12, 31));

        rate.IsInForceOn(new DateOnly(2025, 12, 31)).Should().BeFalse();
        rate.IsInForceOn(new DateOnly(2026, 1, 1)).Should().BeTrue();   // inclusive
        rate.IsInForceOn(new DateOnly(2026, 12, 31)).Should().BeTrue(); // inclusive
        rate.IsInForceOn(new DateOnly(2027, 1, 1)).Should().BeFalse();

        Rate(2, "Open", 5m, from: new DateOnly(2026, 1, 1))
            .IsInForceOn(new DateOnly(2999, 1, 1)).Should().BeTrue();
    }

    // --- Rounding ------------------------------------------------------------------------------

    [Fact]
    public void Per_line_rounding_makes_the_lines_re_sum_to_the_total()
    {
        var lines = Enumerable.Range(0, 3).Select(_ => new TaxLineInput(1m, 0.10m, 0m)).ToList();

        var result = Engine.Calculate(Request(lines, rounding: TaxRounding.PerLine));

        // Each line: 0.10 × 18% = 0.018 → rounds to 0.02. Three of them = 0.06.
        result.Lines.Should().OnlyContain(l => l.Tax == 0.02m);
        result.Totals.Tax.Should().Be(0.06m);
        result.Lines.Sum(l => l.Total).Should().Be(result.Totals.Total);
    }

    [Fact]
    public void Per_document_rounding_rounds_once_at_the_foot()
    {
        var lines = Enumerable.Range(0, 3).Select(_ => new TaxLineInput(1m, 0.10m, 0m)).ToList();

        var result = Engine.Calculate(Request(lines, rounding: TaxRounding.PerDocument));

        // The exact tax is 0.30 × 18% = 0.054 → rounds once to 0.05, a cent under the per-line 0.06.
        result.Totals.Tax.Should().Be(0.05m);
        result.Totals.Total.Should().Be(0.35m);
    }
}
