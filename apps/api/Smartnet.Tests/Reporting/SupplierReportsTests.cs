using FluentAssertions;
using Smartnet.Api.Reporting;
using Smartnet.Domain.Reporting;
using Smartnet.Infrastructure.Entities;

namespace Smartnet.Tests.Reporting;

public sealed class SupplierReportsTests
{
    private static readonly IReadOnlyDictionary<string, (string Name, string? Vat)> Parties =
        new Dictionary<string, (string, string?)>(StringComparer.Ordinal) { ["S-1"] = ("ABC Traders", "SV-9") };

    private static readonly IReadOnlyDictionary<string, string> Names =
        new Dictionary<string, string>(StringComparer.Ordinal) { ["S-1"] = "ABC Traders", ["S-2"] = "XYZ" };

    private static SupplierInvoice Sup(int id, string amount, string status, string vtype = "1", string date = "2026-07-15", string supcode = "S-1") =>
        new()
        {
            Id = id,
            Amount = amount,
            Paymentstat = status,
            Vtype = vtype,
            Invdate = date,
            Supcode = supcode,
            Invno = "PI-" + id,
            Novattotal = "0",
            Company = "1",
        };

    // --- Supplier VAT --------------------------------------------------------------------------

    [Fact]
    public void Supplier_vat_includes_only_tax_invoices_and_derives_amount_minus_novattotal()
    {
        var invoices = new[]
        {
            new SupplierInvoice { Id = 1, Amount = "115", Novattotal = "100", Vtype = "1", Invdate = "2026-07-15", Supcode = "S-1", Invno = "PI-1", Company = "1" },
            new SupplierInvoice { Id = 2, Amount = "230", Novattotal = "200", Vtype = "0", Invdate = "2026-07-15", Supcode = "S-1", Invno = "PI-2", Company = "1" },
        };

        var report = SupplierVatReport.Build(invoices, Parties, ReportPeriod.All);

        report.Rows.Should().ContainSingle();
        report.TotalVat.Should().Be(15m);
        report.Rows[0].SupplierName.Should().Be("ABC Traders");
        report.Rows[0].VatNumber.Should().Be("SV-9");
    }

    // --- Supplier purchase summary -------------------------------------------------------------

    [Fact]
    public void Supplier_purchase_groups_and_sums_pending_only_for_pending_invoices()
    {
        var invoices = new[]
        {
            Sup(1, "500", "Paid"),
            Sup(2, "300", "Pending"),
            Sup(3, "900", "Pending", supcode: "S-2"),
        };

        var report = SupplierPurchaseReport.Build(invoices, Names, ReportPeriod.All);

        report.Rows.Should().HaveCount(2);
        // Ordered by pending balance desc → S-2 (900) first.
        report.Rows[0].SupplierName.Should().Be("XYZ");
        report.Rows[0].PendingBalance.Should().Be(900m);

        var abc = report.Rows.Single(r => r.SupplierCode == "S-1");
        abc.TotalPurchase.Should().Be(800m); // 500 + 300
        abc.PendingBalance.Should().Be(300m); // only the Pending one
    }

    // --- Supplier payments ---------------------------------------------------------------------

    private static SupplierInvPay Pay(string supinvid, string paidDate) =>
        new() { Id = 1, Supinvid = supinvid, Paiddate = paidDate, PayMethod = "Bank", Referenceno = "R1" };

    [Fact]
    public void Supplier_payments_totals_paid_invoices_in_the_paid_date_window()
    {
        var joined = new[]
        {
            (Sup(1, "500", "Paid"), Pay("1", "2026-07-15")),
            (Sup(2, "200", "Pending"), Pay("2", "2026-07-16")), // not paid → excluded
            (Sup(3, "300", "Paid"), Pay("3", "2026-08-20")),     // paid outside window → excluded
        };

        var july = new ReportPeriod(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));
        var report = SupplierPaymentReport.Build(joined, Names, july);

        report.Count.Should().Be(1);
        report.Total.Should().Be(500m);
        report.Rows[0].PaidDate.Should().Be(new DateOnly(2026, 7, 15));
        report.Rows[0].SupplierName.Should().Be("ABC Traders");
    }
}
