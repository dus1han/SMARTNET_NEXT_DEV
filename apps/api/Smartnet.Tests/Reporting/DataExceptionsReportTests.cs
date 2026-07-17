using FluentAssertions;
using Smartnet.Api.Reporting;
using Smartnet.Infrastructure.Entities;

namespace Smartnet.Tests.Reporting;

public sealed class DataExceptionsReportTests
{
    private static readonly IReadOnlyDictionary<string, string> Names =
        new Dictionary<string, string>(StringComparer.Ordinal) { ["C-1"] = "Kandy Engineering" };

    private static InvoiceH Invoice(
        string invoiceNo,
        string type = "Credit",
        string total = "1000",
        string balance = "0",
        string beforeDisc = "1000",
        string customer = "C-1") =>
        new()
        {
            Invoiceno = invoiceNo,
            Invtype = type,
            Totamount = total,
            Balance = balance,
            Beforedisctot = beforeDisc,
            Customer = customer,
        };

    private static Payment Pay(string invoiceNo, string amount, string date = "2026-01-01") =>
        new() { Invoiceno = invoiceNo, Amount = amount, Paymentrecdate = date };

    private static InvoiceL Line(string invoiceNo, string tot) =>
        new() { Inno = invoiceNo, Tot = tot };

    [Fact]
    public void Flags_a_duplicate_payment_group_with_the_overstated_value()
    {
        var invoices = new[] { Invoice("SNI-1", balance: "0") };
        var payments = new[]
        {
            Pay("SNI-1", "500", "2026-01-01"),
            Pay("SNI-1", "500", "2026-01-01"), // same invoice/amount/date = duplicate
        };

        var report = DataExceptionsReport.Build(invoices, payments, [], Names);

        report.DuplicatePayments.Should().Be(1);
        var row = report.Rows.Single(r => r.Type == "Duplicate payment");
        row.Reference.Should().Be("SNI-1");
        row.CustomerName.Should().Be("Kandy Engineering");
        row.Amount.Should().Be(500m); // one copy past the first
    }

    [Fact]
    public void Two_payments_of_the_same_amount_on_different_dates_are_not_a_duplicate()
    {
        var invoices = new[] { Invoice("SNI-1", total: "1000", balance: "0") };
        var payments = new[]
        {
            Pay("SNI-1", "500", "2026-01-01"),
            Pay("SNI-1", "500", "2026-02-01"), // different date — a real instalment
        };

        var report = DataExceptionsReport.Build(invoices, payments, [], Names);

        report.DuplicatePayments.Should().Be(0);
    }

    [Fact]
    public void Flags_a_credit_invoice_settled_with_no_payment_behind_it()
    {
        var invoices = new[] { Invoice("STI-1068", type: "Credit", total: "21500", balance: "0") };

        var report = DataExceptionsReport.Build(invoices, [], [], Names);

        report.PaidNoPayment.Should().Be(1);
        report.Rows.Single(r => r.Type == "Paid, no payment").Amount.Should().Be(21500m);
    }

    [Fact]
    public void A_cash_invoice_with_no_payment_row_is_not_flagged()
    {
        // Cash settles at issue and legitimately carries no payment row.
        var invoices = new[] { Invoice("STI-2", type: "Cash", total: "5000", balance: "0") };

        var report = DataExceptionsReport.Build(invoices, [], [], Names);

        report.PaidNoPayment.Should().Be(0);
    }

    [Fact]
    public void A_credit_invoice_with_a_matching_payment_is_not_flagged()
    {
        var invoices = new[] { Invoice("STI-3", type: "Credit", total: "5000", balance: "0") };
        var payments = new[] { Pay("STI-3", "5000") };

        var report = DataExceptionsReport.Build(invoices, payments, [], Names);

        report.PaidNoPayment.Should().Be(0);
    }

    [Fact]
    public void Flags_an_invoice_whose_lines_do_not_sum_to_the_header()
    {
        var invoices = new[] { Invoice("STI-1150", beforeDisc: "12041") };
        var lines = new[] { Line("STI-1150", "1916") };

        var report = DataExceptionsReport.Build(invoices, [], lines, Names);

        report.LinesNotHeader.Should().Be(1);
        report.Rows.Single(r => r.Type == "Lines ≠ header").Amount.Should().Be(10125m);
    }

    [Fact]
    public void A_sub_rupee_header_line_difference_is_rounding_not_a_defect()
    {
        var invoices = new[] { Invoice("STI-9", beforeDisc: "1000.40") };
        var lines = new[] { Line("STI-9", "1000") };

        var report = DataExceptionsReport.Build(invoices, [], lines, Names);

        report.LinesNotHeader.Should().Be(0);
    }

    [Fact]
    public void Clean_data_produces_no_exceptions()
    {
        var invoices = new[] { Invoice("SNI-1", type: "Credit", total: "1000", balance: "1000", beforeDisc: "1000") };
        var lines = new[] { Line("SNI-1", "1000") };

        var report = DataExceptionsReport.Build(invoices, [], lines, Names);

        report.Total.Should().Be(0);
        report.Rows.Should().BeEmpty();
    }
}
