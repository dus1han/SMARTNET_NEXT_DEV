namespace Smartnet.Domain.Exporting;

/// <summary>How a column's values should appear in the spreadsheet.</summary>
public enum ExcelFormat
{
    Text,

    /// <summary>A whole number. Right-aligned, no decimals.</summary>
    WholeNumber,

    /// <summary>
    /// Money. Written as a real numeric cell with two decimals — never as a string.
    /// </summary>
    /// <remarks>
    /// This is the entire reason exports are generated here rather than in the browser. A money
    /// column that arrives in Excel as text cannot be summed, cannot be totalled at the bottom of
    /// the sheet, and quietly right-aligns differently from the column beside it. Accounts staff
    /// then retype it. The value we hold is already <c>decimal</c>; it goes into the cell as a
    /// number, and Excel formats it.
    /// </remarks>
    Money,

    Percent,
    Date,
    DateTime,
    Boolean,
}

/// <param name="Header">The column heading.</param>
/// <param name="Value">Pulls the value out of a row. Return the raw value, not a formatted string.</param>
public sealed record ExcelColumn<T>(string Header, Func<T, object?> Value, ExcelFormat Format = ExcelFormat.Text);

public interface IExcelExporter
{
    /// <summary>
    /// Renders rows to a real .xlsx workbook.
    /// </summary>
    /// <remarks>
    /// Server-side, and deliberately: the server already holds these values as <c>decimal</c> and
    /// <c>DateTime</c>. Shipping them to the browser as JSON and rebuilding a spreadsheet there
    /// means a second implementation of "what is a number" — one that has re-parsed a string, in
    /// whatever locale the user's machine happens to run.
    /// </remarks>
    byte[] Export<T>(string sheetName, IReadOnlyList<ExcelColumn<T>> columns, IEnumerable<T> rows);
}
