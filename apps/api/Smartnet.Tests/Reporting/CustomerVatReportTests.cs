using FluentAssertions;
using Smartnet.Api.Reporting;
using Smartnet.Domain.Reporting;
using Smartnet.Infrastructure.Entities;

namespace Smartnet.Tests.Reporting;

public sealed class CustomerVatReportTests
{
    private static readonly IReadOnlyDictionary<string, (string Name, string? Vat)> Customers =
        new Dictionary<string, (string, string?)>(StringComparer.Ordinal) { ["C-1"] = ("Kandy", "VAT-100") };

    private static InvoiceH Inv(string vtype, string total, string novat, string date = "2026-07-15") =>
        new()
        {
            Vtype = vtype,
            Totamount = total,
            Novattotal = novat,
            Customer = "C-1",
            Indate = date,
            It = "ITEM",
            Invoiceno = "SNI-1",
        };

    [Fact]
    public void Only_tax_invoices_are_included_and_vat_is_total_minus_novattotal()
    {
        var invoices = new[]
        {
            Inv("1", total: "115", novat: "100"), // tax invoice → VAT 15
            Inv("0", total: "230", novat: "200"), // NOT a tax invoice → excluded
            Inv("1", total: "57.5", novat: "50"), // → VAT 7.5
        };

        var report = CustomerVatReport.Build(invoices, Customers, ReportPeriod.All);

        report.Rows.Should().HaveCount(2);
        report.TotalValue.Should().Be(172.5m);
        report.TotalVat.Should().Be(22.5m);
        report.Rows[0].VatNumber.Should().Be("VAT-100");
        report.Rows[0].CustomerName.Should().Be("Kandy");
        report.Rows[0].DocumentType.Should().Be("ITEM Invoice");
    }

    [Fact]
    public void It_filters_by_period_and_flags_a_bad_amount()
    {
        var invoices = new[]
        {
            Inv("1", total: "bad", novat: "0", date: "2026-07-10"),
            Inv("1", total: "115", novat: "100", date: "2026-08-10"), // outside July
        };

        var july = new ReportPeriod(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));
        var report = CustomerVatReport.Build(invoices, Customers, july);

        report.Rows.Should().ContainSingle();
        report.Rows[0].HasDataIssue.Should().BeTrue();
        report.FlaggedCount.Should().Be(1);
    }
}
