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
using Smartnet.Domain.Reporting;
using Smartnet.Infrastructure.Entities;
using Smartnet.Infrastructure.Persistence;

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

        var query = _legacy.ExpenseTrs.Where(e => scope.Contains(e.Company));

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

        return SupplierVatReport.Build(invoices, suppliers, period);
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
