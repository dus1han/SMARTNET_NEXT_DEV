using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smartnet.Api.Auth;
using Smartnet.Api.Contracts;
using Smartnet.Api.Reporting;
using Smartnet.Domain.Auditing;
using Smartnet.Domain.Exporting;
using Smartnet.Domain.Identity;
using Smartnet.Domain.Ledger;
using Smartnet.Domain.Reporting;
using Smartnet.Infrastructure.Entities;
using Smartnet.Infrastructure.Persistence;
using Smartnet.Infrastructure.Reporting;

namespace Smartnet.Api.Controllers;

/// <summary>
/// The reports. Read-only over the legacy tables, one spine underneath them all.
/// </summary>
/// <remarks>
/// This is the reporting spine proved many times over. Each report reads a different legacy table and
/// projects a different column set; they share everything else — the read-only
/// <see cref="SmartnetLegacyDbContext"/>, the defensive money/date parsing, the
/// <see cref="ReportPeriod"/> filter, and the streamed <see cref="IExcelExporter"/>.
///
/// <para><b>Filters ride the request, never the session.</b> The date window and the company come in on
/// the query string. The company is "all" (every company the caller may see, aggregated) or one
/// specific company — but only ever one the token permits (see <see cref="CompanyScope"/>), so it can
/// only narrow to something already allowed. That kills the legacy stale-filter round-trip and the
/// <c>cvatcomp = to</c> class of corrupt-filter bug outright.</para>
///
/// <para>Every endpoint carries its existing legacy permission and denies by default. Nothing here
/// writes business data — the only write is the audit row an export leaves behind.</para>
/// </remarks>
[ApiController]
[Route("api/reports")]
public sealed class ReportsController : ControllerBase
{
    private readonly SmartnetLegacyDbContext _legacy;
    private readonly SmartnetDbContext _db;
    private readonly IExcelExporter _excel;
    private readonly ICompanyContext _company;
    private readonly IAuditWriter _audit;
    private readonly TimeProvider _time;

    public ReportsController(
        SmartnetLegacyDbContext legacy,
        SmartnetDbContext db,
        IExcelExporter excel,
        ICompanyContext company,
        IAuditWriter audit,
        TimeProvider time)
    {
        _legacy = legacy;
        _db = db;
        _excel = excel;
        _company = company;
        _audit = audit;
        _time = time;
    }

    /// <summary>The companies the caller may filter any report by — their accessible set.</summary>
    [HttpGet("companies")]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<CompanyOption>>> Companies(CancellationToken cancellationToken)
    {
        var accessible = _company.Accessible.ToList();

        var companies = await _db.Companies
            .Where(c => accessible.Contains(c.Id))
            .OrderBy(c => c.Name)
            .Select(c => new CompanyOption(c.Id, c.Name))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Ok(companies);
    }

    // --- Sales (sales_rpt) -------------------------------------------------------------------

    [HttpGet("sales")]
    [RequirePermission(Permissions.SalesReport)]
    public async Task<ActionResult<SalesReportResponse>> Sales(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? company,
        CancellationToken cancellationToken) =>
        Ok(await BuildSales(new ReportPeriod(from, to), company, cancellationToken).ConfigureAwait(false));

    [HttpGet("sales/export")]
    [RequirePermission(Permissions.SalesReport)]
    public async Task<IActionResult> SalesExport(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? company,
        CancellationToken cancellationToken)
    {
        var report = await BuildSales(new ReportPeriod(from, to), company, cancellationToken).ConfigureAwait(false);

        var workbook = _excel.Export<SalesReportRow>(
            "Sales",
            [
                new("Category", r => r.Category),
                new("Invoice No", r => r.InvoiceNo),
                new("Type", r => r.Type),
                new("Date", r => r.Date is { } d ? d.ToDateTime(TimeOnly.MinValue) : (DateTime?)null, ExcelFormat.Date),
                new("Customer Code", r => r.CustomerCode),
                new("Customer", r => r.CustomerName),
                new("PO No", r => r.PurchaseOrderNo),
                new("Total", r => r.Total, ExcelFormat.Money),
                new("Balance", r => r.Balance, ExcelFormat.Money),
                new("Cost", r => r.Cost, ExcelFormat.Money),
                new("Profit", r => r.Profit, ExcelFormat.Money),
                new("Prepared By", r => r.PreparedBy),
                new("Generated", r => r.GeneratedAt),
            ],
            report.Rows);

        return await Download(workbook, "sales", report.Rows.Count, cancellationToken).ConfigureAwait(false);
    }

    // --- Expenses (expenses_rpt) -------------------------------------------------------------

    [HttpGet("expenses")]
    [RequirePermission(Permissions.ExpensesReport)]
    public async Task<ActionResult<ExpenseReportResponse>> Expenses(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? company,
        [FromQuery] long? category,
        CancellationToken cancellationToken) =>
        Ok(await BuildExpenses(new ReportPeriod(from, to), company, category, cancellationToken).ConfigureAwait(false));

    [HttpGet("expenses/export")]
    [RequirePermission(Permissions.ExpensesReport)]
    public async Task<IActionResult> ExpensesExport(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? company,
        [FromQuery] long? category,
        CancellationToken cancellationToken)
    {
        var report = await BuildExpenses(new ReportPeriod(from, to), company, category, cancellationToken).ConfigureAwait(false);

        var workbook = _excel.Export<ExpenseReportRow>(
            "Expenses",
            [
                new("Date", r => r.Date is { } d ? d.ToDateTime(TimeOnly.MinValue) : (DateTime?)null, ExcelFormat.Date),
                new("Category", r => r.Category),
                new("Description", r => r.Description),
                new("Amount", r => r.Amount, ExcelFormat.Money),
                new("Payment Method", r => r.PaymentMethod),
                new("Reference", r => r.Reference),
                new("Added By", r => r.AddedBy),
            ],
            report.Rows);

        return await Download(workbook, "expenses", report.Rows.Count, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The expense categories, for the report's category filter.</summary>
    [HttpGet("expenses/categories")]
    [RequirePermission(Permissions.ExpensesReport)]
    public async Task<ActionResult<IReadOnlyList<ExpenseCategoryDto>>> ExpenseCategories(
        CancellationToken cancellationToken)
    {
        var categories = await _legacy.ExpCatMs
            .OrderBy(c => c.Expcatname)
            .Select(c => new ExpenseCategoryDto(c.Id, c.Expcatname ?? string.Empty))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Ok(categories);
    }

    // --- Customer sales (customersales_rpt) --------------------------------------------------

    [HttpGet("customer-sales")]
    [RequirePermission(Permissions.CustomerSalesReport)]
    public async Task<ActionResult<CustomerSalesResponse>> CustomerSales(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? company,
        CancellationToken cancellationToken) =>
        Ok(await BuildCustomerSales(new ReportPeriod(from, to), company, cancellationToken).ConfigureAwait(false));

    [HttpGet("customer-sales/export")]
    [RequirePermission(Permissions.CustomerSalesReport)]
    public async Task<IActionResult> CustomerSalesExport(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? company,
        CancellationToken cancellationToken)
    {
        var report = await BuildCustomerSales(new ReportPeriod(from, to), company, cancellationToken).ConfigureAwait(false);

        var workbook = _excel.Export<CustomerSalesRow>(
            "Customer sales",
            [
                new("Customer Code", r => r.CustomerCode),
                new("Customer", r => r.CustomerName),
                new("Invoices", r => r.InvoiceCount, ExcelFormat.WholeNumber),
                new("Total", r => r.Total, ExcelFormat.Money),
                new("Cost", r => r.Cost, ExcelFormat.Money),
                new("Profit", r => r.Profit, ExcelFormat.Money),
                new("Balance", r => r.Balance, ExcelFormat.Money),
            ],
            report.Rows);

        return await Download(workbook, "customer-sales", report.Rows.Count, cancellationToken).ConfigureAwait(false);
    }

    // --- Cheques (chequerpt) -----------------------------------------------------------------

    [HttpGet("cheques")]
    [RequirePermission(Permissions.ChequesReport)]
    public async Task<ActionResult<ChequeReportResponse>> Cheques(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? company,
        CancellationToken cancellationToken) =>
        Ok(await BuildCheques(new ReportPeriod(from, to), company, cancellationToken).ConfigureAwait(false));

    [HttpGet("cheques/export")]
    [RequirePermission(Permissions.ChequesReport)]
    public async Task<IActionResult> ChequesExport(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? company,
        CancellationToken cancellationToken)
    {
        var report = await BuildCheques(new ReportPeriod(from, to), company, cancellationToken).ConfigureAwait(false);

        var workbook = _excel.Export<ChequeRow>(
            "Cheques",
            [
                new("Date", r => r.ChequeDate is { } d ? d.ToDateTime(TimeOnly.MinValue) : (DateTime?)null, ExcelFormat.Date),
                new("Due", r => r.DueDate is { } d ? d.ToDateTime(TimeOnly.MinValue) : (DateTime?)null, ExcelFormat.Date),
                new("Pay To", r => r.PayTo),
                new("Amount", r => r.Amount, ExcelFormat.Money),
                new("Amount in Words", r => r.AmountInWords),
                new("Bank", r => r.Bank),
                new("Cheque No", r => r.ChequeNo),
                // Each in its own column — the legacy export overwrote all three into one.
                new("Created By", r => r.CreatedBy),
                new("Created At", r => r.CreatedAt),
                new("Printed At", r => r.PrintedAt),
            ],
            report.Rows);

        return await Download(workbook, "cheques", report.Rows.Count, cancellationToken).ConfigureAwait(false);
    }

    // --- Job cards (jobcards_rpt) ------------------------------------------------------------

    [HttpGet("job-cards")]
    [RequirePermission(Permissions.JobCardsReport)]
    public async Task<ActionResult<JobCardReportResponse>> JobCards(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? company,
        CancellationToken cancellationToken) =>
        Ok(await BuildJobCards(new ReportPeriod(from, to), company, cancellationToken).ConfigureAwait(false));

    [HttpGet("job-cards/export")]
    [RequirePermission(Permissions.JobCardsReport)]
    public async Task<IActionResult> JobCardsExport(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? company,
        CancellationToken cancellationToken)
    {
        var report = await BuildJobCards(new ReportPeriod(from, to), company, cancellationToken).ConfigureAwait(false);

        var workbook = _excel.Export<JobCardRow>(
            "Job cards",
            [
                new("Date", r => r.Date is { } d ? d.ToDateTime(TimeOnly.MinValue) : (DateTime?)null, ExcelFormat.Date),
                new("Job No", r => r.JobNo),
                new("Customer", r => r.CustomerName),
                new("Status", r => r.Status),
                new("Cost", r => r.Cost, ExcelFormat.Money),
                new("Sell", r => r.Sell, ExcelFormat.Money),
                // Null for a pending job (blank cell), never a misleading zero.
                new("Profit", r => r.Profit, ExcelFormat.Money),
                new("Job Done By", r => r.JobDoneBy),
                new("Completed By", r => r.CompletedBy),
            ],
            report.Rows);

        return await Download(workbook, "job-cards", report.Rows.Count, cancellationToken).ConfigureAwait(false);
    }

    // --- Customer VAT (cusvat_rpt) -----------------------------------------------------------

    [HttpGet("customer-vat")]
    [RequirePermission(Permissions.CustomerVatReport)]
    public async Task<ActionResult<CustomerVatResponse>> CustomerVat(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? company,
        CancellationToken cancellationToken) =>
        Ok(await BuildCustomerVat(new ReportPeriod(from, to), company, cancellationToken).ConfigureAwait(false));

    [HttpGet("customer-vat/export")]
    [RequirePermission(Permissions.CustomerVatReport)]
    public async Task<IActionResult> CustomerVatExport(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? company,
        CancellationToken cancellationToken)
    {
        var report = await BuildCustomerVat(new ReportPeriod(from, to), company, cancellationToken).ConfigureAwait(false);

        var workbook = _excel.Export<CustomerVatRow>(
            "Customer VAT",
            [
                new("Date", r => r.Date is { } d ? d.ToDateTime(TimeOnly.MinValue) : (DateTime?)null, ExcelFormat.Date),
                new("Invoice No", r => r.InvoiceNo),
                new("Customer", r => r.CustomerName),
                new("VAT No", r => r.VatNumber),
                new("Type", r => r.DocumentType),
                new("Value", r => r.Value, ExcelFormat.Money),
                new("VAT", r => r.Vat, ExcelFormat.Money),
            ],
            report.Rows);

        return await Download(workbook, "customer-vat", report.Rows.Count, cancellationToken).ConfigureAwait(false);
    }

    // --- Supplier VAT (suppliervat_rpt) ------------------------------------------------------

    [HttpGet("supplier-vat")]
    [RequirePermission(Permissions.SupplierVatReport)]
    public async Task<ActionResult<SupplierVatResponse>> SupplierVat(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? company,
        CancellationToken cancellationToken) =>
        Ok(await BuildSupplierVat(new ReportPeriod(from, to), company, cancellationToken).ConfigureAwait(false));

    [HttpGet("supplier-vat/export")]
    [RequirePermission(Permissions.SupplierVatReport)]
    public async Task<IActionResult> SupplierVatExport(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? company,
        CancellationToken cancellationToken)
    {
        var report = await BuildSupplierVat(new ReportPeriod(from, to), company, cancellationToken).ConfigureAwait(false);

        var workbook = _excel.Export<SupplierVatRow>(
            "Supplier VAT",
            [
                new("Date", r => r.Date is { } d ? d.ToDateTime(TimeOnly.MinValue) : (DateTime?)null, ExcelFormat.Date),
                new("Invoice No", r => r.InvoiceNo),
                new("Supplier", r => r.SupplierName),
                new("VAT No", r => r.VatNumber),
                new("Value", r => r.Value, ExcelFormat.Money),
                new("VAT", r => r.Vat, ExcelFormat.Money),
            ],
            report.Rows);

        return await Download(workbook, "supplier-vat", report.Rows.Count, cancellationToken).ConfigureAwait(false);
    }

    // --- Trial balance (general ledger) ------------------------------------------------------

    [HttpGet("trial-balance")]
    [RequirePermission(Permissions.GeneralLedger)]
    public async Task<ActionResult<TrialBalanceResponse>> TrialBalance(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? company,
        CancellationToken cancellationToken) =>
        Ok(await BuildTrialBalance(new ReportPeriod(from, to), company, cancellationToken).ConfigureAwait(false));

    [HttpGet("trial-balance/export")]
    [RequirePermission(Permissions.GeneralLedger)]
    public async Task<IActionResult> TrialBalanceExport(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? company,
        CancellationToken cancellationToken)
    {
        var report = await BuildTrialBalance(new ReportPeriod(from, to), company, cancellationToken).ConfigureAwait(false);

        var workbook = _excel.Export<TrialBalanceRow>(
            "Trial balance",
            [
                new("Code", r => r.Code),
                new("Account", r => r.Name),
                new("Type", r => r.Type),
                new("Debit", r => r.Debit, ExcelFormat.Money),
                new("Credit", r => r.Credit, ExcelFormat.Money),
                new("Balance", r => r.Balance, ExcelFormat.Money),
            ],
            report.Rows);

        return await Download(workbook, "trial-balance", report.Rows.Count, cancellationToken).ConfigureAwait(false);
    }

    // --- Profit & loss (general ledger) ------------------------------------------------------

    [HttpGet("profit-loss")]
    [RequirePermission(Permissions.GeneralLedger)]
    public async Task<ActionResult<ProfitLossResponse>> ProfitLoss(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? company,
        CancellationToken cancellationToken) =>
        Ok(await BuildProfitLoss(new ReportPeriod(from, to), company, cancellationToken).ConfigureAwait(false));

    [HttpGet("profit-loss/export")]
    [RequirePermission(Permissions.GeneralLedger)]
    public async Task<IActionResult> ProfitLossExport(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? company,
        CancellationToken cancellationToken)
    {
        var report = await BuildProfitLoss(new ReportPeriod(from, to), company, cancellationToken).ConfigureAwait(false);

        var rows = ProfitLossExportRows(report);

        var workbook = _excel.Export<ProfitLossLine>(
            "Profit and loss",
            [
                new("Section", r => r.Section),
                new("Code", r => r.Code),
                new("Account", r => r.Name),
                new("Amount", r => r.Amount, ExcelFormat.Money),
            ],
            rows);

        return await Download(workbook, "profit-loss", rows.Count, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The P&amp;L export laid out exactly as the on-screen statement: each section's account lines followed by
    /// its subtotal, then Gross profit, then Net profit, and finally the dashboard reconciliation — so the
    /// downloaded sheet reads as the same statement, not a raw list of accounts. Reconciliation deductions are
    /// signed negative so that block sums to Revenue in Excel.
    /// </summary>
    private static List<ProfitLossLine> ProfitLossExportRows(ProfitLossResponse report)
    {
        var recon = report.SalesReconciliation;
        var rows = new List<ProfitLossLine>();

        void Section(string section, decimal subtotal)
        {
            rows.AddRange(report.Lines.Where(l => l.Section == section));
            rows.Add(new ProfitLossLine(section, "", $"Total {section.ToLowerInvariant()}", subtotal));
        }

        Section("Revenue", report.Revenue);
        Section("Cost of Sales", report.CostOfSales);
        rows.Add(new ProfitLossLine("", "", "Gross profit", report.GrossProfit));
        Section("Expenses", report.Expenses);
        rows.Add(new ProfitLossLine("", "", "Net profit", report.NetProfit));

        rows.Add(new ProfitLossLine("Reconciliation", "", "Gross invoiced sales (incl. VAT) — matches the dashboard", recon.GrossInvoicedSales));
        rows.Add(new ProfitLossLine("Reconciliation", "", "Less VAT collected", -recon.OutputVat));
        rows.Add(new ProfitLossLine("Reconciliation", "", "Less sales returns (credit notes)", -recon.SalesReturns));
        rows.Add(new ProfitLossLine("Reconciliation", "", "Revenue", report.Revenue));

        return rows;
    }

    // --- Supplier purchase summary (supplierpurchase_rpt) ------------------------------------

    [HttpGet("supplier-purchase")]
    [RequirePermission(Permissions.SupplierPurchaseReport)]
    public async Task<ActionResult<SupplierPurchaseResponse>> SupplierPurchase(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? company,
        CancellationToken cancellationToken) =>
        Ok(await BuildSupplierPurchase(new ReportPeriod(from, to), company, cancellationToken).ConfigureAwait(false));

    [HttpGet("supplier-purchase/export")]
    [RequirePermission(Permissions.SupplierPurchaseReport)]
    public async Task<IActionResult> SupplierPurchaseExport(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? company,
        CancellationToken cancellationToken)
    {
        var report = await BuildSupplierPurchase(new ReportPeriod(from, to), company, cancellationToken).ConfigureAwait(false);

        var workbook = _excel.Export<SupplierPurchaseRow>(
            "Supplier purchase",
            [
                new("Supplier Code", r => r.SupplierCode),
                new("Supplier", r => r.SupplierName),
                new("Total Purchase", r => r.TotalPurchase, ExcelFormat.Money),
                new("Pending", r => r.PendingBalance, ExcelFormat.Money),
            ],
            report.Rows);

        return await Download(workbook, "supplier-purchase", report.Rows.Count, cancellationToken).ConfigureAwait(false);
    }

    // --- Supplier payments (supplierpayments_rpt) --------------------------------------------

    [HttpGet("supplier-payments")]
    [RequirePermission(Permissions.SupplierPaymentsReport)]
    public async Task<ActionResult<SupplierPaymentResponse>> SupplierPayments(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? company,
        [FromQuery] string? supplier,
        CancellationToken cancellationToken) =>
        Ok(await BuildSupplierPayments(new ReportPeriod(from, to), company, supplier, cancellationToken).ConfigureAwait(false));

    [HttpGet("supplier-payments/export")]
    [RequirePermission(Permissions.SupplierPaymentsReport)]
    public async Task<IActionResult> SupplierPaymentsExport(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? company,
        [FromQuery] string? supplier,
        CancellationToken cancellationToken)
    {
        var report = await BuildSupplierPayments(new ReportPeriod(from, to), company, supplier, cancellationToken).ConfigureAwait(false);

        var workbook = _excel.Export<SupplierPaymentRow>(
            "Supplier payments",
            [
                new("Paid Date", r => r.PaidDate is { } d ? d.ToDateTime(TimeOnly.MinValue) : (DateTime?)null, ExcelFormat.Date),
                new("Invoice No", r => r.InvoiceNo),
                new("Invoice Date", r => r.InvoiceDate is { } d ? d.ToDateTime(TimeOnly.MinValue) : (DateTime?)null, ExcelFormat.Date),
                new("Supplier", r => r.SupplierName),
                new("Amount", r => r.Amount, ExcelFormat.Money),
                new("Method", r => r.PayMethod),
                new("Reference", r => r.Reference),
            ],
            report.Rows);

        return await Download(workbook, "supplier-payments", report.Rows.Count, cancellationToken).ConfigureAwait(false);
    }

    // --- Customer outstanding (customer_outstanding) -----------------------------------------

    [HttpGet("outstanding")]
    [RequirePermission(Permissions.CustomerOutstanding)]
    public async Task<ActionResult<OutstandingResponse>> Outstanding(
        [FromQuery] string? company,
        [FromQuery] DateOnly? asAt,
        CancellationToken cancellationToken) =>
        Ok(await BuildOutstanding(company, asAt, cancellationToken).ConfigureAwait(false));

    [HttpGet("outstanding/export")]
    [RequirePermission(Permissions.CustomerOutstanding)]
    public async Task<IActionResult> OutstandingExport(
        [FromQuery] string? company,
        [FromQuery] DateOnly? asAt,
        CancellationToken cancellationToken)
    {
        var report = await BuildOutstanding(company, asAt, cancellationToken).ConfigureAwait(false);

        // The as-of date rides along in the sheet name, so a historical export says on its face what date
        // it is the outstanding for.
        var workbook = _excel.Export<OutstandingRow>(
            $"Outstanding as at {report.AsAt:yyyy-MM-dd}",
            [
                new("Customer Code", r => r.CustomerCode),
                new("Customer", r => r.CustomerName),
                new("Outstanding", r => r.Outstanding, ExcelFormat.Money),
                new("Current", r => r.Current, ExcelFormat.Money),
                new("31-60", r => r.Days30, ExcelFormat.Money),
                new("61-90", r => r.Days60, ExcelFormat.Money),
                new("90+", r => r.Days90, ExcelFormat.Money),
                new("Oldest (days)", r => r.OldestDays, ExcelFormat.WholeNumber),
                new("Invoices", r => r.InvoiceCount, ExcelFormat.WholeNumber),
                // Finding 1 — a negative-balance invoice is distorting this figure.
                new("Data defect", r => r.HasDefect, ExcelFormat.Boolean),
            ],
            report.Rows);

        return await Download(workbook, $"outstanding-{report.AsAt:yyyy-MM-dd}", report.Rows.Count, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The outstanding invoice list for a chosen set of customers — the "export selected" sheet.</summary>
    [HttpGet("outstanding/detail/export")]
    [RequirePermission(Permissions.CustomerOutstanding)]
    public async Task<IActionResult> OutstandingDetailExport(
        [FromQuery] string? company,
        [FromQuery] string? customers,
        [FromQuery] DateOnly? asAt,
        CancellationToken cancellationToken)
    {
        var asOf = asAt ?? DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime);
        var scope = CompanyScope(company);
        var codes = (customers ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        IReadOnlyList<OutstandingDetailRow> rows = [];

        if (scope.Count > 0 && codes.Count > 0)
        {
            var invoices = await _legacy.InvoiceHs
                .Where(h => scope.Contains(h.Company!) && codes.Contains(h.Customer!))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var names = await CustomerNames(cancellationToken).ConfigureAwait(false);
            var paidAfter = await PaidAfterAsync(invoices, asOf, cancellationToken).ConfigureAwait(false);
            rows = OutstandingReport.Detail(invoices, names, asOf, paidAfter);
        }

        var workbook = _excel.Export<OutstandingDetailRow>(
            "Outstanding detail",
            [
                new("Customer Code", r => r.CustomerCode),
                new("Customer", r => r.CustomerName),
                new("Category", r => r.Category),
                new("Invoice No", r => r.InvoiceNo),
                new("Date", r => r.Date is { } d ? d.ToDateTime(TimeOnly.MinValue) : (DateTime?)null, ExcelFormat.Date),
                new("PO No", r => r.PurchaseOrderNo),
                new("Total", r => r.Total, ExcelFormat.Money),
                new("Balance", r => r.Balance, ExcelFormat.Money),
                new("Days", r => r.Days, ExcelFormat.WholeNumber),
            ],
            rows);

        return await Download(workbook, "outstanding-detail", rows.Count, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The suppliers, for the supplier-payments filter.</summary>
    [HttpGet("suppliers")]
    [RequirePermission(Permissions.SupplierPaymentsReport)]
    public async Task<ActionResult<IReadOnlyList<SupplierOption>>> Suppliers(CancellationToken cancellationToken)
    {
        var suppliers = await _legacy.SupMs
            .Where(s => s.Supcode != null)
            .OrderBy(s => s.Supname)
            .Select(s => new SupplierOption(s.Supcode!, s.Supname ?? string.Empty))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Ok(suppliers);
    }

    // --- The spine ---------------------------------------------------------------------------

    /// <summary>
    /// The company id(s) a report is scoped to — one when a specific accessible company is chosen,
    /// otherwise every company the caller may see ("all" or absent). Never a company outside the token's
    /// accessible set, so the filter can only narrow to something already permitted. A List, not an
    /// array, so the downstream <c>Contains</c> translates to a SQL IN (see the dashboard fix).
    /// </summary>
    private List<string> CompanyScope(string? company)
    {
        var accessible = _company.Accessible.ToList();

        if (long.TryParse(company, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            && accessible.Contains(id))
        {
            return [id.ToString(CultureInfo.InvariantCulture)];
        }

        return accessible.Select(i => i.ToString(CultureInfo.InvariantCulture)).ToList();
    }

    /// <summary>
    /// The trial balance — every GL account's summed debits and credits over the period, from the new app's
    /// own gl_* tables (no legacy parsing). Grouped by account <b>code</b> across the scoped companies, so
    /// "all companies" consolidates the shared chart rather than repeating each code per company. A
    /// well-formed ledger always balances (Σ debit = Σ credit), which the response states outright.
    /// </summary>
    private async Task<TrialBalanceResponse> BuildTrialBalance(ReportPeriod period, string? company, CancellationToken cancellationToken)
    {
        var scope = CompanyScope(company)
            .Select(s => long.Parse(s, CultureInfo.InvariantCulture))
            .ToList();
        if (scope.Count == 0)
        {
            return new TrialBalanceResponse(0m, 0m, true, []);
        }

        var grouped = await (
            from l in _db.GlLines
            join a in _db.GlAccounts on l.AccountId equals a.Id
            join e in _db.GlEntries on l.GlEntryId equals e.Id
            where scope.Contains(a.CompanyId)
                && (period.From == null || e.Date >= period.From)
                && (period.To == null || e.Date <= period.To)
            group new { l.Debit, l.Credit } by new { a.Code, a.Name, a.Type } into g
            select new
            {
                g.Key.Code,
                g.Key.Name,
                g.Key.Type,
                Debit = g.Sum(x => x.Debit),
                Credit = g.Sum(x => x.Credit),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = grouped
            .OrderBy(r => r.Code, StringComparer.Ordinal)
            .Select(r => new TrialBalanceRow(r.Code, r.Name, r.Type.ToString(), r.Debit, r.Credit, r.Debit - r.Credit))
            .ToList();

        var totalDebit = rows.Sum(r => r.Debit);
        var totalCredit = rows.Sum(r => r.Credit);

        return new TrialBalanceResponse(totalDebit, totalCredit, Math.Abs(totalDebit - totalCredit) < 0.005m, rows);
    }

    /// <summary>
    /// The profit &amp; loss statement — the income and expense accounts of the GL over the period, from the
    /// app's own gl_* tables. Revenue is the income accounts (a credit balance shown positive); Cost of Sales
    /// is Purchases; Expenses are the per-category expense accounts. Gross profit = revenue − cost of sales,
    /// net profit = gross − expenses. VAT and balance-sheet accounts are excluded, as a P&amp;L should.
    /// </summary>
    private async Task<ProfitLossResponse> BuildProfitLoss(ReportPeriod period, string? company, CancellationToken cancellationToken)
    {
        var scope = CompanyScope(company)
            .Select(s => long.Parse(s, CultureInfo.InvariantCulture))
            .ToList();
        if (scope.Count == 0)
        {
            return new ProfitLossResponse(0m, 0m, 0m, 0m, 0m, new ProfitLossReconciliation(0m, 0m, 0m), []);
        }

        var grouped = await (
            from l in _db.GlLines
            join a in _db.GlAccounts on l.AccountId equals a.Id
            join e in _db.GlEntries on l.GlEntryId equals e.Id
            where scope.Contains(a.CompanyId)
                && (a.Type == AccountType.Income || a.Type == AccountType.Expense)
                && (period.From == null || e.Date >= period.From)
                && (period.To == null || e.Date <= period.To)
            group new { l.Debit, l.Credit } by new { a.Code, a.Name, a.Type } into g
            select new
            {
                g.Key.Code,
                g.Key.Name,
                g.Key.Type,
                Debit = g.Sum(x => x.Debit),
                Credit = g.Sum(x => x.Credit),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var lines = new List<(int Order, ProfitLossLine Line)>();
        decimal revenue = 0m, costOfSales = 0m, expenses = 0m;

        foreach (var r in grouped)
        {
            if (r.Type == AccountType.Income)
            {
                var amount = r.Credit - r.Debit; // income is a credit balance, shown positive
                if (amount == 0m) continue;
                revenue += amount;
                lines.Add((0, new ProfitLossLine("Revenue", r.Code, r.Name, amount)));
            }
            else // Expense
            {
                var amount = r.Debit - r.Credit; // expense is a debit balance, shown positive
                if (amount == 0m) continue;
                if (r.Code == GlAccountCodes.Purchases)
                {
                    costOfSales += amount;
                    lines.Add((1, new ProfitLossLine("Cost of Sales", r.Code, r.Name, amount)));
                }
                else
                {
                    expenses += amount;
                    lines.Add((2, new ProfitLossLine("Expenses", r.Code, r.Name, amount)));
                }
            }
        }

        var ordered = lines
            .OrderBy(x => x.Order)
            .ThenBy(x => x.Line.Code, StringComparer.Ordinal)
            .Select(x => x.Line)
            .ToList();

        var grossProfit = revenue - costOfSales;
        var netProfit = grossProfit - expenses;

        var reconciliation = await BuildSalesReconciliation(scope, period, cancellationToken).ConfigureAwait(false);

        return new ProfitLossResponse(revenue, costOfSales, grossProfit, expenses, netProfit, reconciliation, ordered);
    }

    /// <summary>
    /// The bridge from the dashboard's gross invoiced sales down to the P&amp;L's Revenue, computed from the
    /// same GL the statement reads so it always ties. Invoice postings credit Sales (net) and Output VAT
    /// (vat), so the gross invoiced figure is those two together — which equals the dashboard's Σ totamount
    /// for the same period and scope. Credit-note postings debit Sales, so the returns figure is that debit.
    /// The identity <c>GrossInvoicedSales − OutputVat − SalesReturns = Revenue</c> holds because Sales is the
    /// only income account (Revenue = invoice Sales − credit-note Sales).
    /// </summary>
    private async Task<ProfitLossReconciliation> BuildSalesReconciliation(
        List<long> scope,
        ReportPeriod period,
        CancellationToken cancellationToken)
    {
        var parts = await (
            from l in _db.GlLines
            join a in _db.GlAccounts on l.AccountId equals a.Id
            join e in _db.GlEntries on l.GlEntryId equals e.Id
            where scope.Contains(a.CompanyId)
                && (e.SourceType == GlSources.Invoice || e.SourceType == GlSources.CreditNote)
                && (a.Code == GlAccountCodes.Sales || a.Code == GlAccountCodes.OutputVat)
                && (period.From == null || e.Date >= period.From)
                && (period.To == null || e.Date <= period.To)
            group new { l.Debit, l.Credit } by new { e.SourceType, a.Code } into g
            select new
            {
                g.Key.SourceType,
                g.Key.Code,
                Debit = g.Sum(x => x.Debit),
                Credit = g.Sum(x => x.Credit),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        decimal Net(string source, string code) =>
            parts.Where(p => p.SourceType == source && p.Code == code).Sum(p => p.Credit - p.Debit);

        var invoiceNet = Net(GlSources.Invoice, GlAccountCodes.Sales);       // Sales credited on invoices
        var invoiceVat = Net(GlSources.Invoice, GlAccountCodes.OutputVat);   // Output VAT credited on invoices
        var creditNoteNet = -Net(GlSources.CreditNote, GlAccountCodes.Sales); // Sales debited on credit notes (returns)

        return new ProfitLossReconciliation(
            GrossInvoicedSales: invoiceNet + invoiceVat,
            OutputVat: invoiceVat,
            SalesReturns: creditNoteNet);
    }

    private async Task<SalesReportResponse> BuildSales(ReportPeriod period, string? company, CancellationToken cancellationToken)
    {
        var scope = CompanyScope(company);
        if (scope.Count == 0)
        {
            return new SalesReportResponse(EmptySalesSummary, []);
        }

        // Read-only and parameterised (EF, no string concatenation). Scoped to the chosen company (or
        // all of them) in SQL; the date window is applied in memory by the same ISO string comparison
        // the legacy SQL used, so a blank/malformed date is handled rather than throwing.
        var invoices = await _legacy.InvoiceHs
            .Where(h => scope.Contains(h.Company!))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var customerNames = await CustomerNames(cancellationToken).ConfigureAwait(false);

        return SalesReport.Build(invoices, customerNames, period);
    }

    private async Task<ExpenseReportResponse> BuildExpenses(
        ReportPeriod period,
        string? company,
        long? category,
        CancellationToken cancellationToken)
    {
        var scope = CompanyScope(company);
        if (scope.Count == 0)
        {
            return new ExpenseReportResponse(0m, 0, 0, []);
        }

        var query = _legacy.ExpenseTrs.Where(e => scope.Contains(e.Company) && e.DeletedAt == null);

        if (category is { } categoryId)
        {
            var categoryText = categoryId.ToString(CultureInfo.InvariantCulture);
            query = query.Where(e => e.ExpCat == categoryText);
        }

        var expenses = await query.ToListAsync(cancellationToken).ConfigureAwait(false);

        var categoryNames = await _legacy.ExpCatMs
            .ToDictionaryAsync(c => c.Id, c => c.Expcatname ?? string.Empty, cancellationToken)
            .ConfigureAwait(false);

        return ExpenseReport.Build(expenses, categoryNames, period);
    }

    private async Task<CustomerSalesResponse> BuildCustomerSales(ReportPeriod period, string? company, CancellationToken cancellationToken)
    {
        var scope = CompanyScope(company);
        if (scope.Count == 0)
        {
            return new CustomerSalesResponse(0m, 0m, 0, 0, []);
        }

        var invoices = await _legacy.InvoiceHs
            .Where(h => scope.Contains(h.Company!))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var customerNames = await CustomerNames(cancellationToken).ConfigureAwait(false);

        return CustomerSalesReport.Build(invoices, customerNames, period);
    }

    private async Task<ChequeReportResponse> BuildCheques(ReportPeriod period, string? company, CancellationToken cancellationToken)
    {
        var scope = CompanyScope(company);
        if (scope.Count == 0)
        {
            return new ChequeReportResponse(0m, 0, 0, []);
        }

        var cheques = await _legacy.Cheques
            .Where(c => scope.Contains(c.Company))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return ChequeReport.Build(cheques, period);
    }

    private async Task<JobCardReportResponse> BuildJobCards(ReportPeriod period, string? company, CancellationToken cancellationToken)
    {
        var scope = CompanyScope(company);
        if (scope.Count == 0)
        {
            return new JobCardReportResponse(0m, 0m, 0m, 0, 0, []);
        }

        var jobs = await _legacy.JobsMs
            .Where(j => scope.Contains(j.Company))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var customerNames = await CustomerNames(cancellationToken).ConfigureAwait(false);

        return JobCardReport.Build(jobs, customerNames, period);
    }

    private async Task<CustomerVatResponse> BuildCustomerVat(ReportPeriod period, string? company, CancellationToken cancellationToken)
    {
        var scope = CompanyScope(company);
        if (scope.Count == 0)
        {
            return new CustomerVatResponse(0m, 0m, 0, 0, []);
        }

        var invoices = await _legacy.InvoiceHs
            .Where(h => scope.Contains(h.Company!))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var customers = await CustomerParties(cancellationToken).ConfigureAwait(false);

        return CustomerVatReport.Build(invoices, customers, period);
    }

    private async Task<SupplierVatResponse> BuildSupplierVat(ReportPeriod period, string? company, CancellationToken cancellationToken)
    {
        var scope = CompanyScope(company);
        if (scope.Count == 0)
        {
            return new SupplierVatResponse(0m, 0m, 0, 0, []);
        }

        var invoices = await _legacy.SupplierInvoices
            .Where(i => scope.Contains(i.Company!))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var suppliers = await SupplierParties(cancellationToken).ConfigureAwait(false);
        var report = SupplierVatReport.Build(invoices, suppliers, period);

        // An expense with VAT is input VAT too, so it belongs in this report alongside the supplier invoices.
        var scopeIds = scope.Select(s => long.Parse(s, CultureInfo.InvariantCulture)).ToHashSet();
        var vatExpenses = await _db.Expenses
            .Where(e => e.CompanyId != null && scopeIds.Contains(e.CompanyId.Value) && e.Amount > e.NetAmount)
            .Select(e => new { e.Date, e.InvoiceNo, e.Description, e.NetAmount, e.Amount })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var expenseRows = vatExpenses
            .Where(e => (period.From is null || e.Date >= period.From) && (period.To is null || e.Date <= period.To))
            .Select(e => new SupplierVatRow(
                e.Date, e.InvoiceNo ?? string.Empty, $"Expense — {e.Description}", null, e.NetAmount, e.Amount - e.NetAmount, false))
            .ToList();

        if (expenseRows.Count == 0)
        {
            return report;
        }

        var rows = report.Rows.Concat(expenseRows)
            .OrderBy(r => r.Date ?? DateOnly.MaxValue)
            .ThenBy(r => r.InvoiceNo, StringComparer.Ordinal)
            .ToList();

        return new SupplierVatResponse(
            rows.Sum(r => r.Value), rows.Sum(r => r.Vat), rows.Count, rows.Count(r => r.HasDataIssue), rows);
    }

    private async Task<SupplierPurchaseResponse> BuildSupplierPurchase(ReportPeriod period, string? company, CancellationToken cancellationToken)
    {
        var scope = CompanyScope(company);
        if (scope.Count == 0)
        {
            return new SupplierPurchaseResponse(0m, 0m, 0, 0, []);
        }

        var invoices = await _legacy.SupplierInvoices
            .Where(i => scope.Contains(i.Company!))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var names = SupplierNamesFrom(await SupplierParties(cancellationToken).ConfigureAwait(false));

        return SupplierPurchaseReport.Build(invoices, names, period);
    }

    private async Task<SupplierPaymentResponse> BuildSupplierPayments(
        ReportPeriod period,
        string? company,
        string? supplierCode,
        CancellationToken cancellationToken)
    {
        var scope = CompanyScope(company);
        if (scope.Count == 0)
        {
            return new SupplierPaymentResponse(0m, 0, 0, []);
        }

        var query = _legacy.SupplierInvoices.Where(i => scope.Contains(i.Company!));

        // One supplier, or every supplier ("all" or absent).
        if (!string.IsNullOrWhiteSpace(supplierCode)
            && !string.Equals(supplierCode, "all", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(i => i.Supcode == supplierCode);
        }

        var invoices = await query.ToListAsync(cancellationToken).ConfigureAwait(false);

        if (invoices.Count == 0)
        {
            return new SupplierPaymentResponse(0m, 0, 0, []);
        }

        // A List, not a HashSet, for the IN filter — see the note on CompanyScope / the dashboard fix.
        var invoiceIds = invoices.Select(i => i.Id.ToString(CultureInfo.InvariantCulture)).ToList();

        var payments = await _legacy.SupplierInvPays
            .Where(p => invoiceIds.Contains(p.Supinvid!))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var invoiceById = invoices
            .GroupBy(i => i.Id.ToString(CultureInfo.InvariantCulture), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var joined = payments
            .Where(p => p.Supinvid is not null && invoiceById.ContainsKey(p.Supinvid))
            .Select(p => (invoiceById[p.Supinvid!], p))
            .ToList();

        var names = SupplierNamesFrom(await SupplierParties(cancellationToken).ConfigureAwait(false));

        return SupplierPaymentReport.Build(joined, names, period);
    }

    private async Task<OutstandingResponse> BuildOutstanding(string? company, DateOnly? asAt, CancellationToken cancellationToken)
    {
        var asOf = asAt ?? DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime);

        var scope = CompanyScope(company);
        if (scope.Count == 0)
        {
            return new OutstandingResponse(0m, 0m, 0m, 0m, 0m, 0, 0, 0, asOf, []);
        }

        // Outstanding is point-in-time: every unpaid invoice for the company, aged from its date to the
        // as-of date. Today (the default) is the live figure; a past date rolls each invoice back to what it
        // owed then, by adding back the payments recorded after it and dropping invoices issued after it.
        var invoices = await _legacy.InvoiceHs
            .Where(h => scope.Contains(h.Company!))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var customerNames = await CustomerNames(cancellationToken).ConfigureAwait(false);
        var paidAfter = await PaidAfterAsync(invoices, asOf, cancellationToken).ConfigureAwait(false);

        return OutstandingReport.Build(invoices, customerNames, asOf, paidAfter);
    }

    /// <summary>
    /// Per invoice number, the sum of payments recorded <b>after</b> the as-of date — what the outstanding
    /// report adds back to roll a balance to an earlier date. Empty for an as-of of today (nothing is paid
    /// after today), so the live report is untouched. Payment dates and amounts are legacy <c>varchar</c>,
    /// parsed defensively (a bad value contributes nothing rather than throwing).
    /// </summary>
    private async Task<IReadOnlyDictionary<string, decimal>> PaidAfterAsync(
        IReadOnlyList<Smartnet.Infrastructure.Entities.InvoiceH> invoices,
        DateOnly asOf,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime);
        if (asOf >= today)
        {
            return new Dictionary<string, decimal>(StringComparer.Ordinal); // nothing paid after today
        }

        var invoiceNos = invoices
            .Select(h => h.Invoiceno)
            .Where(n => !string.IsNullOrEmpty(n))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var payments = await _legacy.Payments
            .Where(p => p.Invoiceno != null && invoiceNos.Contains(p.Invoiceno))
            .Select(p => new { p.Invoiceno, p.Amount, p.Paymentrecdate })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return payments
            .Where(p => LegacyValue.Date(p.Paymentrecdate) is { } d && d > asOf)
            .GroupBy(p => p.Invoiceno!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(p => LegacyValue.Money(p.Amount)), StringComparer.Ordinal);
    }

    /// <summary>Customer code → (name, VAT number), for the customer-VAT report.</summary>
    private async Task<IReadOnlyDictionary<string, (string Name, string? Vat)>> CustomerParties(CancellationToken cancellationToken)
    {
        var customers = await _legacy.CusMs
            .Select(c => new { c.Cuscode, c.Cusname, c.Vatnum })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return customers
            .Where(c => !string.IsNullOrEmpty(c.Cuscode))
            .GroupBy(c => c.Cuscode!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (g.First().Cusname ?? string.Empty, g.First().Vatnum), StringComparer.Ordinal);
    }

    /// <summary>Supplier code → (name, VAT number), for the supplier reports.</summary>
    private async Task<IReadOnlyDictionary<string, (string Name, string? Vat)>> SupplierParties(CancellationToken cancellationToken)
    {
        var suppliers = await _legacy.SupMs
            .Select(s => new { s.Supcode, s.Supname, s.Vatnum })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return suppliers
            .Where(s => !string.IsNullOrEmpty(s.Supcode))
            .GroupBy(s => s.Supcode!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (g.First().Supname ?? string.Empty, g.First().Vatnum), StringComparer.Ordinal);
    }

    private static Dictionary<string, string> SupplierNamesFrom(
        IReadOnlyDictionary<string, (string Name, string? Vat)> parties) =>
        parties.ToDictionary(p => p.Key, p => p.Value.Name, StringComparer.Ordinal);

    /// <summary>Customer code → name, for the sales detail. One lookup, tolerant of duplicate codes.</summary>
    private async Task<IReadOnlyDictionary<string, string>> CustomerNames(CancellationToken cancellationToken)
    {
        var customers = await _legacy.CusMs
            .Select(c => new { c.Cuscode, c.Cusname })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return customers
            .Where(c => !string.IsNullOrEmpty(c.Cuscode))
            .GroupBy(c => c.Cuscode!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Cusname ?? string.Empty, StringComparer.Ordinal);
    }

    private async Task<IActionResult> Download(
        byte[] workbook,
        string report,
        int rows,
        CancellationToken cancellationToken)
    {
        // An export is a permissioned, audited action like any other — a report leaving the building
        // is a thing the audit log should be able to answer for, even though it reads no adopted data.
        await _audit.RecordAsync(
            AuditAction.Export,
            "Report",
            report,
            details: new { report, rows },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return File(
            workbook,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"{report}-{_time.GetUtcNow():yyyy-MM-dd}.xlsx");
    }

    private static readonly SalesReportSummary EmptySalesSummary = new(0m, 0m, 0m, 0m, 0m, 0m, 0, 0);
}
