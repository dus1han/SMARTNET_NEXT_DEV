using System.Globalization;
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

    private static readonly IReadOnlyDictionary<string, string> SupplierNames =
        new Dictionary<string, string>(StringComparer.Ordinal) { ["S-1"] = "Lanka Cables" };

    private static SupplierInvoice SupplierInvoice(long id, string invNo, string amount, string status) =>
        new() { Id = id, Invno = invNo, Amount = amount, Paymentstat = status, Supcode = "S-1", Company = "2" };

    private static SupplierInvPay Settlement(long supplierInvoiceId) =>
        new() { Supinvid = supplierInvoiceId.ToString(CultureInfo.InvariantCulture), PayMethod = "Cash" };

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
    public void Flags_an_invoice_paid_twice_weeks_apart_that_the_duplicate_rule_cannot_see()
    {
        // STI-38: 71,000 taken on 2025-06-12 and again on 2025-06-29. Not a duplicate by (invoice, amount,
        // date), and the stored balance said zero, so nothing surfaced it until the payments were measured
        // against the invoice.
        var invoices = new[] { Invoice("STI-38", total: "71000", balance: "0") };
        var payments = new[]
        {
            Pay("STI-38", "71000", "2025-06-12"),
            Pay("STI-38", "71000", "2025-06-29"),
        };

        var report = DataExceptionsReport.Build(invoices, payments, [], Names);

        report.DuplicatePayments.Should().Be(0, "the dates differ, so the duplicate key does not match");
        report.Overpaid.Should().Be(1);
        report.Rows.Single(r => r.Type == "Overpaid").Amount.Should().Be(71000m);
    }

    [Fact]
    public void Instalments_that_sum_to_the_invoice_are_not_an_overpayment()
    {
        var invoices = new[] { Invoice("SNI-2", total: "1000", balance: "0") };
        var payments = new[] { Pay("SNI-2", "400", "2026-01-01"), Pay("SNI-2", "600", "2026-02-01") };

        var report = DataExceptionsReport.Build(invoices, payments, [], Names);

        report.Overpaid.Should().Be(0);
    }

    [Fact]
    public void A_sub_rupee_overpayment_is_rounding_not_a_defect()
    {
        var invoices = new[] { Invoice("SNI-3", total: "1000", balance: "0") };
        var payments = new[] { Pay("SNI-3", "1000.40") };

        var report = DataExceptionsReport.Build(invoices, payments, [], Names);

        report.Overpaid.Should().Be(0);
    }

    [Fact]
    public void Flags_a_payment_naming_an_invoice_that_does_not_exist()
    {
        var orphaned = new[] { Pay("SNI-1045", "66375", "2025-03-04") };

        var report = DataExceptionsReport.Build([], [], [], Names, new LegacyDataScan { OrphanedPayments = orphaned });

        report.OrphanedPayments.Should().Be(1);
        var row = report.Rows.Single(r => r.Type == "Payment without an invoice");
        row.Reference.Should().Be("SNI-1045");
        row.Amount.Should().Be(66375m);
    }

    [Fact]
    public void Flags_a_payment_naming_no_invoice_at_all_under_its_own_id()
    {
        var orphaned = new[] { new Payment { Id = 349, Invoiceno = "", Amount = "3006" } };

        var report = DataExceptionsReport.Build([], [], [], Names, new LegacyDataScan { OrphanedPayments = orphaned });

        report.OrphanedPayments.Should().Be(1);
        report.Rows.Single(r => r.Type == "Payment without an invoice").Reference.Should().Be("Payment #349");
    }

    [Fact]
    public void Flags_a_supplier_invoice_marked_paid_with_nothing_settling_it()
    {
        var invoices = new[] { SupplierInvoice(1, "SUP-9", amount: "45000", status: "Paid") };

        var report = DataExceptionsReport.Build([], [], [], Names, new LegacyDataScan { SupplierInvoices = invoices, SupplierNames = SupplierNames });

        report.SupplierSettlements.Should().Be(1);
        var row = report.Rows.Single(r => r.Type == "Supplier paid, not settled");
        row.CustomerName.Should().Be("Lanka Cables");
        row.Amount.Should().Be(45000m);
    }

    [Fact]
    public void Flags_a_supplier_invoice_settled_twice()
    {
        var invoices = new[] { SupplierInvoice(621, "SUP-621", amount: "12000", status: "Paid") };
        var settlements = new[] { Settlement(621), Settlement(621) };

        var report = DataExceptionsReport.Build([], [], [], Names, new LegacyDataScan { SupplierInvoices = invoices, SupplierSettlements = settlements, SupplierNames = SupplierNames });

        report.SupplierSettlements.Should().Be(1);
        // Each settlement stands for the whole invoice, so the second is a second payment of the same money.
        report.Rows.Single(r => r.Type == "Supplier settled twice").Amount.Should().Be(12000m);
    }

    [Fact]
    public void A_pending_supplier_invoice_with_no_settlement_is_not_flagged()
    {
        var invoices = new[] { SupplierInvoice(2, "SUP-2", amount: "8000", status: "Pending") };

        var report = DataExceptionsReport.Build([], [], [], Names, new LegacyDataScan { SupplierInvoices = invoices, SupplierNames = SupplierNames });

        report.SupplierSettlements.Should().Be(0);
    }

    [Fact]
    public void A_blank_supinvid_does_not_settle_the_invoice_numbered_zero()
    {
        // supinvid is a varchar; a blank one must not parse to 0 and quietly settle whatever holds that id.
        var invoices = new[] { SupplierInvoice(0, "SUP-0", amount: "500", status: "Paid") };
        var settlements = new[] { new SupplierInvPay { Id = 1, Supinvid = "", PayMethod = "Cash" } };

        var report = DataExceptionsReport.Build([], [], [], Names, new LegacyDataScan { SupplierInvoices = invoices, SupplierSettlements = settlements, SupplierNames = SupplierNames });

        report.SupplierSettlements.Should().Be(1, "the blank settlement does not count for invoice 0");
    }

    [Fact]
    public void Groups_orphaned_lines_by_the_document_they_name()
    {
        var lines = new[] { Line("STI-1159", "500"), Line("STI-1159", "250"), Line("STI-833", "100") };

        var report = DataExceptionsReport.Build([], [], [], Names, new LegacyDataScan { OrphanedInvoiceLines = lines });

        report.OrphanedLines.Should().Be(2, "three lines, but only two documents");
        var row = report.Rows.Single(r => r.Reference == "STI-1159");
        row.Type.Should().Be("Lines without a document");
        row.Amount.Should().Be(750m);
        row.Detail.Should().Contain("2 lines");
    }

    [Fact]
    public void Orphaned_quotation_lines_are_described_as_quotations_not_invoices()
    {
        var lines = new[] { new QuotationL { Qno = "STQ-88", Total = "1200" } };

        var report = DataExceptionsReport.Build([], [], [], Names, new LegacyDataScan { OrphanedQuotationLines = lines });

        report.OrphanedLines.Should().Be(1);
        report.Rows.Single().Detail.Should().Contain("quotation STQ-88");
    }

    [Fact]
    public void Flags_a_document_number_used_twice()
    {
        // STQ-0 — two quotations, two customers, one number. Carries no amount: nothing is miscounted, but
        // the unique index cannot be built.
        var duplicates = new[] { new DuplicateDocumentNumber("quotation", "STQ-0", 2) };

        var report = DataExceptionsReport.Build([], [], [], Names, new LegacyDataScan { DuplicateNumbers = duplicates });

        report.DuplicateNumbers.Should().Be(1);
        var row = report.Rows.Single();
        row.Reference.Should().Be("STQ-0");
        row.Amount.Should().Be(0m);
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
