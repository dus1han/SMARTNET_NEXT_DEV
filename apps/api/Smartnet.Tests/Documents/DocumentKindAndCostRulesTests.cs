using FluentAssertions;
using Smartnet.Api.Contracts;
using Smartnet.Domain.Documents;

namespace Smartnet.Tests.Documents;

/// <summary>
/// The two rules that decide what a document's cost is, and whether it is allowed to have one kind of line
/// or two. Pure functions and a validator — no database, so these run anywhere.
/// </summary>
public sealed class DocumentKindAndCostRulesTests
{
    // --- The cost basis --------------------------------------------------------------------------

    [Fact]
    public void The_cost_basis_multiplies_each_unit_cost_by_its_quantity()
    {
        // 10 at 500 is 5,000 — not 500, which is what it recorded before 2026-07-20.
        DocumentCostBasis.Of([(500m, 10m)]).Should().Be(5_000m);
    }

    [Fact]
    public void The_cost_basis_sums_across_lines()
    {
        DocumentCostBasis.Of([(500m, 10m), (25m, 4m), (1m, 1m)]).Should().Be(5_101m);
    }

    [Fact]
    public void A_line_with_no_cost_contributes_nothing_rather_than_failing()
    {
        // Every item in the master is currently priceless (no cost recorded), so this is the live case,
        // not a curiosity: the basis comes out zero rather than throwing.
        DocumentCostBasis.Of([(null, 10m), (60m, 2m)]).Should().Be(120m);
        DocumentCostBasis.Of([(null, 10m)]).Should().Be(0m);
    }

    [Fact]
    public void A_fractional_quantity_scales_the_cost_with_it()
    {
        DocumentCostBasis.Of([(100m, 2.5m)]).Should().Be(250m);
    }

    [Fact]
    public void No_lines_is_no_cost()
    {
        DocumentCostBasis.Of([]).Should().Be(0m);
    }

    // --- Mixed documents are allowed ---------------------------------------------------------------

    [Fact]
    public void A_document_may_hold_item_and_service_lines_at_once()
    {
        // Parts plus labour on one repair — Phase 5 design decision B, and the shape every acceptance test
        // in this suite uses. Pinned as a test because a "one kind per document" rule is an easy and
        // plausible thing to add, and it would refuse an invoice for a repair that used a part.
        new CreateQuotationRequestValidator()
            .Validate(QuotationWith([Line(itemId: 7), Line(itemId: null)]))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void An_all_item_and_an_all_service_document_are_both_fine()
    {
        var validator = new CreateQuotationRequestValidator();

        validator.Validate(QuotationWith([Line(itemId: 7), Line(itemId: 8)])).IsValid.Should().BeTrue();
        validator.Validate(QuotationWith([Line(itemId: null), Line(itemId: null)])).IsValid.Should().BeTrue();
    }

    // --- The convert request ---------------------------------------------------------------------

    [Fact]
    public void A_conversion_may_omit_the_cost_here_because_the_converter_decides_if_it_is_needed()
    {
        // Whether a cost is required depends on the stored quotation's kind, which this request cannot see.
        // The rule lives in QuotationConverter; all this may check is the shape.
        new ConvertQuotationRequestValidator()
            .Validate(new ConvertQuotationRequest("Credit", new DateOnly(2026, 8, 1), null, null))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void A_negative_cost_is_refused_whatever_the_document_kind()
    {
        new ConvertQuotationRequestValidator()
            .Validate(new ConvertQuotationRequest("Credit", new DateOnly(2026, 8, 1), null, null, DocumentCost: -1m))
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void A_zero_cost_is_a_legitimate_answer()
    {
        new ConvertQuotationRequestValidator()
            .Validate(new ConvertQuotationRequest("Credit", new DateOnly(2026, 8, 1), null, null, DocumentCost: 0m))
            .IsValid.Should().BeTrue();
    }

    // --- Helpers ---------------------------------------------------------------------------------

    private static CreateInvoiceLineRequest Line(long? itemId) =>
        new(itemId, itemId is null ? null : $"I-{itemId}", "Something", 1m, 100m, 0m, null);

    private static CreateQuotationRequest QuotationWith(IReadOnlyList<CreateInvoiceLineRequest> lines) =>
        new(1, 1, new DateOnly(2026, 7, 20), null, "30 Days", lines);
}
