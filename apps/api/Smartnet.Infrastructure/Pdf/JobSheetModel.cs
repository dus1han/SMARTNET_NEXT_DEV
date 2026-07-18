namespace Smartnet.Infrastructure.Pdf;

/// <summary>One item received against a job (the job sheet's detail line: description, qty, serial).</summary>
public sealed record JobItem(string Description, string Qty, string Serial);

/// <summary>
/// The data a job sheet renders from — the company header (logo/name/contact) and the job card's fields,
/// resolved and cleaned by <see cref="JobSheetRenderer"/>. The same model drives every company's layout.
/// </summary>
public sealed record JobSheetModel(
    byte[]? Logo,
    string CompanyName,
    string CompanyTagline,
    string CompanyContact,
    string JobNo,
    string Date,
    string Status,
    string ClientName,
    string ClientAddress,
    string ClientPhone,
    string ContactPerson,
    string PreparedBy,
    string FaultDescription,
    string Remarks,
    IReadOnlyList<JobItem> Items);
