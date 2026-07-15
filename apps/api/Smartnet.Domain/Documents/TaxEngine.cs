using Smartnet.Domain.Settings;

namespace Smartnet.Domain.Documents;

/// <summary>
/// The one tax engine. Every document type — invoice, quotation, credit note — runs its lines through
/// this, and nowhere else computes tax.
/// </summary>
/// <remarks>
/// It is pure <c>decimal</c> arithmetic over inputs the caller supplies (PHASE-5-PLAN §slice 1). It
/// does not read the database, does not know what a document is, and does not depend on EF or HTTP — so
/// the whole of what the legacy system got wrong about money can be proven right in a unit test, which
/// is the point of putting it in <c>Smartnet.Domain</c>.
///
/// <para>One rate per document — the selected company's, applied to every line (the
/// <c>one-vat-rate-per-document</c> decision). The engine does not mix rates; what it fixes is:</para>
/// <list type="bullet">
/// <item><b>The rate is the document's, not today's (B5/B6).</b> Resolution is as-of
/// <see cref="TaxCalculationRequest.DocumentDate"/>, and the resolved percentage is snapshotted onto the
/// document so a reprint reproduces it. The legacy <c>vat_validity</c> lookup used <c>CURDATE()</c>.</item>
/// <item><b>Non-registered means zero.</b> A company not registered for VAT is taxed at 0%, enforced
/// here rather than hoped for.</item>
/// <item><b>Rounding is a decision, not a side effect.</b> Explicit, per <see cref="TaxRounding"/> —
/// never the residue of binary floating point.</item>
/// </list>
/// </remarks>
public interface ITaxEngine
{
    TaxCalculationResult Calculate(TaxCalculationRequest request);
}

/// <inheritdoc cref="ITaxEngine"/>
public sealed class TaxEngine : ITaxEngine
{
    /// <summary>Money is carried at four places but rounded to the minor unit (two) for figures.</summary>
    private const int MoneyScale = 2;

    public TaxCalculationResult Calculate(TaxCalculationRequest request)
    {
        // Resolved once, for the whole document — the company's rate, as of the document's date.
        var rate = ResolveDocumentRate(request);

        var lines = new List<TaxLineResult>(request.Lines.Count);

        foreach (var (input, index) in request.Lines.Select((line, i) => (line, i)))
        {
            var gross = input.Quantity * input.UnitPrice;
            var discount = gross * (input.DiscountPercent / 100m);
            var net = Round(gross) - Round(discount);

            // The line always shows a rounded tax figure. The rounding *mode* only changes how the
            // document foot is totalled (see Total) — per line it sums these; per document it rounds the
            // tax on the summed net once.
            var tax = Round(net * (rate.Percentage / 100m));

            lines.Add(new TaxLineResult(
                Index: index,
                Quantity: input.Quantity,
                UnitPrice: input.UnitPrice,
                DiscountPercent: input.DiscountPercent,
                Gross: Round(gross),
                Discount: Round(discount),
                Net: net,
                Tax: tax,
                Total: net + tax));
        }

        var totals = Total(lines, rate.Percentage, request.Rounding);

        return new TaxCalculationResult(rate.Id, rate.Name, rate.Percentage, lines, totals);
    }

    /// <summary>The single rate the whole document is taxed at: which percentage, name, and row.</summary>
    private static ResolvedRate ResolveDocumentRate(TaxCalculationRequest request)
    {
        // A company not registered for VAT charges none — full stop.
        if (!request.IsVatRegistered)
        {
            return new ResolvedRate(null, "No VAT", 0m);
        }

        var rate = request.AvailableRates
            .Where(r => r.DeletedAt is null
                && r.IsDefault
                && r.EffectiveFrom <= request.DocumentDate
                && (r.EffectiveTo is null || request.DocumentDate <= r.EffectiveTo))
            .OrderByDescending(r => r.EffectiveFrom)
            .FirstOrDefault()
            ?? throw new TaxRateNotResolvableException(
                $"No default tax rate is in force on {request.DocumentDate:yyyy-MM-dd}.");

        return new ResolvedRate(rate.Id, rate.Name, rate.Percentage);
    }

    private static TaxTotals Total(IReadOnlyList<TaxLineResult> lines, decimal ratePercentage, TaxRounding rounding)
    {
        var subtotal = lines.Sum(l => l.Gross);
        var discount = lines.Sum(l => l.Discount);
        var net = lines.Sum(l => l.Net);

        // Per line, the document tax is the sum of the already-rounded line taxes, so the printed lines
        // re-add to the foot. Per document, it is the once-rounded tax on the summed net — a hair
        // different, and the figure the foot then prints.
        var tax = rounding == TaxRounding.PerLine
            ? lines.Sum(l => l.Tax)
            : Round(net * (ratePercentage / 100m));

        return new TaxTotals(subtotal, discount, net, tax, net + tax);
    }

    private static decimal Round(decimal value) =>
        // Commercial rounding — half away from zero — is what an invoice foot does, and what staff
        // expect. Banker's rounding would surprise them on a .005.
        decimal.Round(value, MoneyScale, MidpointRounding.AwayFromZero);

    private readonly record struct ResolvedRate(long? Id, string Name, decimal Percentage);
}
