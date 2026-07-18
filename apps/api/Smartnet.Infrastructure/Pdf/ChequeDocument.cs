using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Smartnet.Infrastructure.Pdf;

/// <summary>What a cheque overlay prints. Four values and nothing else — the paper supplies the rest.</summary>
public sealed record ChequeModel(
    string PayTo,
    decimal Amount,

    /// <summary>The six date digits, in the order the boxes take them: D D M M Y Y.</summary>
    string DateDigits);

/// <summary>
/// The cheque — an overlay on pre-printed bank stationery, not a document of ours.
/// </summary>
/// <remarks>
/// <b>This deliberately does not derive from <see cref="HouseDocument"/>.</b> Every other document in the
/// system is ours to lay out: masthead, section bars, footer, page numbers. A cheque is somebody else's
/// paper, already printed, already laid out, and already legally significant. The only job here is to put
/// four values in the four places the bank left blank. A section header or a footer on this page would
/// print across the bank's own artwork.
///
/// <para><b>Positions are absolute and measured, not composed.</b> Every coordinate below is taken from
/// the cheque PDF the current system prints — a sample confirmed to register correctly on the real
/// stationery — mapped onto the measured 7in x 3.5in page. So a cheque from the new app lands where a
/// cheque from the old one lands, and the stationery does not need re-registering. They are millimetres
/// from the top-left, which is why they are stated as constants rather than buried in layout code: when
/// the printer or the stationery changes, this block is the only thing that moves.</para>
///
/// <para><b>The page size is the cheque</b>, not A4, and the driver must not scale it. Printing this at
/// "fit to page" on A4 enlarges every offset by about 18%, which puts the amount clean outside its box —
/// the one setting most likely to spoil a sheet of real cheques.</para>
/// </remarks>
public sealed class ChequeDocument : IDocument
{
    // --- the measured geometry ---------------------------------------------------------------
    //
    // Positions come from the cheque PDF the current system prints — a sample confirmed to register
    // correctly on the real stationery — proportionally mapped onto the measured page below. If a value
    // here is wrong the cheque is wrong, so each is named for the thing it positions rather than folded
    // into a layout expression.

    /// <summary>The stationery: 7in x 3.5in, measured off a real cheque.</summary>
    private const float PageWidthMm = 177.8f;  // 7in
    private const float PageHeightMm = 88.9f;  // 3.5in

    /// <summary>Top of the date digits.</summary>
    private const float DateTopMm = 11.1f;

    /// <summary>Left edge of each date digit. Six boxes: D D M M then a wider gap, then Y Y.</summary>
    private static readonly float[] DateDigitLeftMm = [121.9f, 128.5f, 135.1f, 141.5f, 161.0f, 167.6f];

    private const float PayToLeftMm = 13.0f;
    private const float PayToTopMm = 24.5f;

    private const float WordsLeftMm = 16.1f;
    private const float WordsFirstLineTopMm = 36.0f;

    /// <summary>Distance from the first words line to the second — the ruled lines on the cheque.</summary>
    private const float WordsLineHeightMm = 6.6f;

    /// <summary>The widest the words may run before wrapping to the second ruled line.</summary>
    private const float WordsWidthMm = 94.6f;

    private const float FiguresLeftMm = 124.8f;
    private const float FiguresTopMm = 40.0f;

    /// <summary>
    /// Scaled with the page, so the text keeps the same size relative to the printed boxes.
    /// </summary>
    private const float FontSize = 8.75f;

    private readonly ChequeModel _m;

    public ChequeDocument(ChequeModel model) => _m = model;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageWidthMm, PageHeightMm, Unit.Millimetre);
            page.Margin(0);
            page.DefaultTextStyle(t => t
                .FontSize(FontSize)
                .FontFamily(Fonts.Arial)
                .FontColor(Colors.Black)
                .LineHeight(1.25f));

            // One layer per value, each placed from the page's top-left corner. Layers rather than a
            // column because nothing here flows: moving one value must not move another.
            page.Content().Layers(layers =>
            {
                // The primary layer decides the page's extent; it is empty because the paper is the page.
                layers.PrimaryLayer().Element(_ => { });

                foreach (var (digit, index) in _m.DateDigits.Select((d, i) => (d, i)))
                {
                    if (index >= DateDigitLeftMm.Length) break;

                    layers.Layer()
                        .OffsetX(DateDigitLeftMm[index], Unit.Millimetre)
                        .OffsetY(DateTopMm, Unit.Millimetre)
                        .Text(digit.ToString());
                }

                layers.Layer()
                    .OffsetX(PayToLeftMm, Unit.Millimetre)
                    .OffsetY(PayToTopMm, Unit.Millimetre)
                    .Text(_m.PayTo);

                layers.Layer()
                    .OffsetX(WordsLeftMm, Unit.Millimetre)
                    .OffsetY(WordsFirstLineTopMm, Unit.Millimetre)
                    .Width(WordsWidthMm, Unit.Millimetre)
                    .Text(AmountInWords.Cheque(_m.Amount))
                    .LineHeight(WordsLineHeightMm / MillimetresPerLine);

                layers.Layer()
                    .OffsetX(FiguresLeftMm, Unit.Millimetre)
                    .OffsetY(FiguresTopMm, Unit.Millimetre)
                    .Text(AmountInWords.Figures(_m.Amount));
            });
        });
    }

    /// <summary>
    /// One line of text at <see cref="FontSize"/>, in millimetres — what a line height of 1.0 spans.
    /// </summary>
    /// <remarks>
    /// The words wrap onto the cheque's second ruled line, so the gap between them has to match the
    /// ruling rather than whatever the font's default leading happens to be. QuestPDF takes line height
    /// as a multiple of the font size, so the measured millimetre gap is converted here instead of a
    /// magic multiplier being tuned by eye.
    /// </remarks>
    private const float MillimetresPerLine = FontSize * 25.4f / 72f;
}
