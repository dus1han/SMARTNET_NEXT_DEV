using ClosedXML.Excel;
using Smartnet.Domain.Exporting;

namespace Smartnet.Infrastructure.Exporting;

/// <inheritdoc cref="IExcelExporter"/>
public sealed class ExcelExporter : IExcelExporter
{
    public byte[] Export<T>(
        string sheetName,
        IReadOnlyList<ExcelColumn<T>> columns,
        IEnumerable<T> rows)
    {
        using var workbook = new XLWorkbook();

        // Excel refuses these characters in a sheet name and 31 is its hard limit — a silent
        // exception on export is a support call nobody can diagnose from the message.
        var safeName = new string(sheetName.Where(c => !"[]*/\\?:".Contains(c, StringComparison.Ordinal)).ToArray());
        var sheet = workbook.AddWorksheet(safeName.Length > 31 ? safeName[..31] : safeName);

        for (var c = 0; c < columns.Count; c++)
        {
            var header = sheet.Cell(1, c + 1);

            header.Value = columns[c].Header;
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.FromArgb(0xF1, 0xF2, 0xF6);
            header.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        }

        var r = 2;

        foreach (var row in rows)
        {
            for (var c = 0; c < columns.Count; c++)
            {
                Write(sheet.Cell(r, c + 1), columns[c].Value(row), columns[c].Format);
            }

            r++;
        }

        // The header row stays put while the user scrolls 2,000 invoices, and every column can be
        // filtered. Both are things staff do to an exported list within about four seconds of
        // opening it.
        sheet.SheetView.FreezeRows(1);

        if (r > 2)
        {
            sheet.Range(1, 1, r - 1, columns.Count).SetAutoFilter();
        }

        sheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        return stream.ToArray();
    }

    /// <summary>The characters a spreadsheet may treat as the start of a formula.</summary>
    private static bool IsFormulaLike(string text) =>
        text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r';

    private static void Write(IXLCell cell, object? value, ExcelFormat format)
    {
        if (value is null)
        {
            return;
        }

        switch (format)
        {
            case ExcelFormat.Money:
                // A NUMBER, not a string. This is the whole point: a money column written as text
                // cannot be summed, and the first thing anyone does with an exported list of
                // invoices is total it.
                cell.Value = Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture);
                cell.Style.NumberFormat.Format = "#,##0.00";
                break;

            case ExcelFormat.Percent:
                cell.Value = Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture) / 100m;
                cell.Style.NumberFormat.Format = "0.00%";
                break;

            case ExcelFormat.WholeNumber:
                cell.Value = Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
                cell.Style.NumberFormat.Format = "#,##0";
                break;

            case ExcelFormat.Date:
                cell.Value = Convert.ToDateTime(value, System.Globalization.CultureInfo.InvariantCulture);
                cell.Style.DateFormat.Format = "yyyy-mm-dd";
                break;

            case ExcelFormat.DateTime:
                cell.Value = Convert.ToDateTime(value, System.Globalization.CultureInfo.InvariantCulture);
                cell.Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
                break;

            case ExcelFormat.Boolean:
                cell.Value = Convert.ToBoolean(value, System.Globalization.CultureInfo.InvariantCulture);
                break;

            case ExcelFormat.Text:
            default:
                var text = value.ToString() ?? string.Empty;

                // Formula injection. A leading =, +, - or @ can make a spreadsheet read the cell as
                // a FORMULA rather than as text, so a customer named "=cmd|' /c calc'!A1" executes
                // on the machine of whoever opens the export — who is an accountant, not a security
                // researcher. Neutralised by forcing it to text and, where it is genuinely
                // dangerous, prefixing it.
                //
                // Belt and braces on purpose: the exact behaviour varies between Excel, LibreOffice
                // and Google Sheets, and this is not a thing to be clever about.
                cell.Style.NumberFormat.Format = "@";
                cell.SetValue(IsFormulaLike(text) ? "'" + text : text);
                break;
        }
    }
}
