namespace Smartnet.Api.Contracts;

/// <summary>A company the caller may filter a report by — the shared All/company selector's options.</summary>
public sealed record CompanyOption(long Id, string Name);

// The reports read the legacy tables read-only (invoice_h, expense_tr, …); they neither adopt nor
// migrate them — that is Phase 5–7. Every money figure is parsed once, defensively, from the legacy
// varchar columns to decimal (see LegacyValue); a row that carried an unreadable value is flagged
// with HasDataIssue rather than dropped or thrown on.

// --- Sales report (sales_rpt) --------------------------------------------------------------

/// <summary>
/// One invoice in the sales report. The header figures only — the legacy report totals
/// <c>invoice_h</c> and never opens <c>invoice_l</c>.
/// </summary>
/// <param name="Category">Legacy <c>it</c> — "ITEM" or "SERVICE".</param>
/// <param name="Type">Legacy <c>invtype</c> — "Cash" or "Credit". The stored literal that the whole
/// report groups on; it is not derived from the balance.</param>
/// <param name="Date">Parsed from the legacy ISO <c>indate</c>; null when unreadable.</param>
/// <param name="Profit"><c>Total − Cost</c>. Cost is cost-at-current, not cost-at-sale — the same
/// limitation the legacy report has; Phase 5 fixes it by snapshotting cost on the line.</param>
/// <param name="GeneratedAt">Legacy <c>cdatetime</c>, shown as stored.</param>
/// <param name="HasDataIssue">True when a money column was non-numeric or the date was unreadable.</param>
public sealed record SalesReportRow(
    string Category,
    string InvoiceNo,
    string Type,
    DateOnly? Date,
    string CustomerCode,
    string CustomerName,
    string? PurchaseOrderNo,
    decimal Total,
    decimal Balance,
    decimal Cost,
    decimal Profit,
    string? PreparedBy,
    string? GeneratedAt,
    bool HasDataIssue);

/// <summary>
/// The three figures the legacy report screen shows — cash, credit, and the unfiltered total, each
/// with profit. "Total" is every invoice in the window, not Cash + Credit, so it also captures any
/// invoice whose <c>invtype</c> is neither (faithful to the legacy unfiltered sum).
/// </summary>
public sealed record SalesReportSummary(
    decimal CashSales,
    decimal CashProfit,
    decimal CreditSales,
    decimal CreditProfit,
    decimal TotalSales,
    decimal TotalProfit,
    int InvoiceCount,
    int FlaggedCount);

public sealed record SalesReportResponse(
    SalesReportSummary Summary,
    IReadOnlyList<SalesReportRow> Rows);

// --- Expenses report (expenses_rpt) --------------------------------------------------------

/// <param name="Category">The <c>exp_cat_m</c> name the expense's <c>exp_cat</c> id resolves to.</param>
/// <param name="AddedBy">Legacy <c>addedby</c> — a free-text name, shown as stored (approximate: a
/// rename breaks the attribution, which Phase 5 fixes by carrying a user id).</param>
/// <param name="HasDataIssue">True when the amount was non-numeric or the date was unreadable.</param>
public sealed record ExpenseReportRow(
    long Id,
    DateOnly? Date,
    string Category,
    string? Description,
    decimal Amount,
    string? PaymentMethod,
    string? Reference,
    string? AddedBy,
    bool HasDataIssue);

public sealed record ExpenseReportResponse(
    decimal Total,
    int Count,
    int FlaggedCount,
    IReadOnlyList<ExpenseReportRow> Rows);

/// <summary>A selectable expense category — <c>exp_cat_m</c>, for the report's category filter.</summary>
public sealed record ExpenseCategoryDto(long Id, string Name);

// --- Customer sales report (customersales_rpt) ---------------------------------------------

/// <summary>
/// One customer's sales for the period — <c>invoice_h</c> grouped by customer, the Sales report's
/// mirror cut. Ranked by profit, as the legacy report is.
/// </summary>
public sealed record CustomerSalesRow(
    string CustomerCode,
    string CustomerName,
    int InvoiceCount,
    decimal Total,
    decimal Cost,
    decimal Profit,
    decimal Balance,
    bool HasDataIssue);

public sealed record CustomerSalesResponse(
    decimal TotalSales,
    decimal TotalProfit,
    int CustomerCount,
    int FlaggedCount,
    IReadOnlyList<CustomerSalesRow> Rows);

// --- Cheques report (chequerpt) ------------------------------------------------------------

/// <param name="CreatedAt">Legacy <c>createddt</c>, shown as stored. In its own column — the legacy
/// export wrote createdby, createddt and printeddt all into one cell, so only the last survived.</param>
/// <param name="AmountInWords">Derived from the amount, not read from the legacy <c>inwords</c> column
/// (which the report never populated). Cheque printing itself is Phase 8; this is the report's copy.</param>
public sealed record ChequeRow(
    long Id,
    DateOnly? ChequeDate,
    DateOnly? DueDate,
    string? PayTo,
    decimal Amount,
    string AmountInWords,
    string? Bank,
    string? ChequeNo,
    string? CreatedBy,
    string? CreatedAt,
    string? PrintedAt,
    bool HasDataIssue);

public sealed record ChequeReportResponse(
    decimal Total,
    int Count,
    int FlaggedCount,
    IReadOnlyList<ChequeRow> Rows);

// --- Job cards report (jobcards_rpt) -------------------------------------------------------

/// <param name="Status">Legacy <c>jstat</c> — "PENDING" or "CLOSED".</param>
/// <param name="Profit"><c>Sell − Cost</c>, but only for a job that is not pending — a pending job has
/// no cost or sell yet, so its profit is null (shown blank), never a misleading zero.</param>
public sealed record JobCardRow(
    string JobNo,
    DateOnly? Date,
    string CustomerName,
    string Status,
    decimal Cost,
    decimal Sell,
    decimal? Profit,
    string? JobDoneBy,
    string? CompletedBy,
    bool HasDataIssue);

public sealed record JobCardReportResponse(
    decimal TotalCost,
    decimal TotalSell,
    decimal TotalProfit,
    int Count,
    int FlaggedCount,
    IReadOnlyList<JobCardRow> Rows);

// --- Customer VAT report (cusvat_rpt) ------------------------------------------------------

/// <param name="Vat">Output VAT, derived <c>totamount − novattotal</c> as the legacy report does.</param>
public sealed record CustomerVatRow(
    DateOnly? Date,
    string InvoiceNo,
    string CustomerName,
    string? VatNumber,
    string DocumentType,
    decimal Value,
    decimal Vat,
    bool HasDataIssue);

public sealed record CustomerVatResponse(
    decimal TotalValue,
    decimal TotalVat,
    int Count,
    int FlaggedCount,
    IReadOnlyList<CustomerVatRow> Rows);

// --- Supplier VAT report (suppliervat_rpt) -------------------------------------------------

/// <param name="Vat">Input VAT, derived <c>amount − novattotal</c> — the mirror of the customer report.</param>
public sealed record SupplierVatRow(
    DateOnly? Date,
    string InvoiceNo,
    string SupplierName,
    string? VatNumber,
    decimal Value,
    decimal Vat,
    bool HasDataIssue);

public sealed record SupplierVatResponse(
    decimal TotalValue,
    decimal TotalVat,
    int Count,
    int FlaggedCount,
    IReadOnlyList<SupplierVatRow> Rows);

// --- Trial balance (general ledger) --------------------------------------------------------

/// <summary>One account's totals in the trial balance — the summed debits and credits of its GL lines.</summary>
/// <param name="Balance">Debit − Credit: positive shows in the debit column, negative in the credit column.</param>
public sealed record TrialBalanceRow(
    string Code,
    string Name,
    string Type,
    decimal Debit,
    decimal Credit,
    decimal Balance);

/// <param name="Balances">True when total debits equal total credits — a well-formed ledger always does.</param>
public sealed record TrialBalanceResponse(
    decimal TotalDebit,
    decimal TotalCredit,
    bool Balances,
    IReadOnlyList<TrialBalanceRow> Rows);

// --- Profit & loss (general ledger) --------------------------------------------------------

/// <summary>One income or expense account's contribution to the P&amp;L for the period.</summary>
/// <param name="Section">Revenue, Cost of Sales, or Expenses — how the account groups on the statement.</param>
/// <param name="Amount">The account's P&amp;L amount, always positive: revenue earned, or cost incurred.</param>
public sealed record ProfitLossLine(
    string Section,
    string Code,
    string Name,
    decimal Amount);

/// <summary>
/// The bridge from the dashboard's headline sales figure to this statement's Revenue, so the two
/// screens can be seen to be the same money rather than a mystery variance. The dashboard totals
/// <b>gross invoiced sales</b> (net + VAT, before returns); Revenue is what a P&amp;L recognises —
/// VAT is a liability collected for the tax authority, and credit notes reduce the sale. The identity
/// holds to the cent: <c>GrossInvoicedSales − OutputVat − SalesReturns = Revenue</c>.
/// </summary>
/// <param name="GrossInvoicedSales">Σ of the period's invoices, VAT included — the figure the
/// dashboard "Total Sales" shows for the same period and company.</param>
/// <param name="OutputVat">The VAT charged on those invoices; collected for the tax authority, not
/// earned, so it is not revenue.</param>
/// <param name="SalesReturns">Credit notes (net) raised in the period — returns that lower recognised
/// revenue but never appeared in the gross invoiced figure.</param>
public sealed record ProfitLossReconciliation(
    decimal GrossInvoicedSales,
    decimal OutputVat,
    decimal SalesReturns);

/// <param name="GrossProfit">Revenue − Cost of Sales.</param>
/// <param name="NetProfit">Gross Profit − Expenses: the bottom line for the period.</param>
/// <param name="SalesReconciliation">The bridge from gross invoiced sales (what the dashboard shows) to
/// Revenue — see <see cref="ProfitLossReconciliation"/>.</param>
public sealed record ProfitLossResponse(
    decimal Revenue,
    decimal CostOfSales,
    decimal GrossProfit,
    decimal Expenses,
    decimal NetProfit,
    ProfitLossReconciliation SalesReconciliation,
    IReadOnlyList<ProfitLossLine> Lines);

// --- Supplier purchase summary (supplierpurchase_rpt) --------------------------------------

/// <param name="PendingBalance">Σ amount of this supplier's invoices still flagged <c>paymentstat =
/// 'Pending'</c>. "Pending" is a whole-invoice flag — the legacy model has no partial payment — and is
/// reported as such, not reinterpreted.</param>
public sealed record SupplierPurchaseRow(
    string SupplierCode,
    string SupplierName,
    decimal TotalPurchase,
    decimal PendingBalance,
    bool HasDataIssue);

public sealed record SupplierPurchaseResponse(
    decimal TotalPurchase,
    decimal TotalPending,
    int SupplierCount,
    int FlaggedCount,
    IReadOnlyList<SupplierPurchaseRow> Rows);

// --- Supplier payments report (supplierpayments_rpt) ---------------------------------------

public sealed record SupplierPaymentRow(
    DateOnly? PaidDate,
    string InvoiceNo,
    DateOnly? InvoiceDate,
    decimal Amount,
    string? PayMethod,
    string? Reference,
    string SupplierName,
    bool HasDataIssue);

public sealed record SupplierPaymentResponse(
    decimal Total,
    int Count,
    int FlaggedCount,
    IReadOnlyList<SupplierPaymentRow> Rows);

/// <summary>A selectable supplier — <c>sup_m</c> — for the supplier-payments filter.</summary>
public sealed record SupplierOption(string Code, string Name);

// --- Customer outstanding report (customer_outstanding) ------------------------------------

/// <param name="Outstanding">Σ of this customer's invoice balances that are &gt; 0 — the figure the
/// business already puts on its statements, read from the legacy <c>balance</c> column as-is.</param>
/// <param name="Current">The 0–30 day slice of <see cref="Outstanding"/>; the others are 31–60, 61–90
/// and 90+, aged from <c>indate</c>.</param>
/// <param name="HasDefect">True when this customer has an invoice with a <b>negative</b> balance
/// (Finding 1). The legacy <c>balance &gt; 0</c> filter ignores those, so the outstanding shown is
/// overstated — it is displayed as the business sends it, and flagged, never silently "corrected"
/// (Phase 4 is read-only; the data-remediation phase fixes it).</param>
public sealed record OutstandingRow(
    string CustomerCode,
    string CustomerName,
    decimal Outstanding,
    decimal Current,
    decimal Days30,
    decimal Days60,
    decimal Days90,
    int OldestDays,
    int InvoiceCount,
    bool HasDataIssue,
    bool HasDefect);

public sealed record OutstandingResponse(
    decimal TotalOutstanding,
    decimal TotalCurrent,
    decimal Total30,
    decimal Total60,
    decimal Total90,
    int CustomerCount,
    int FlaggedCount,
    int DefectCount,
    // The date the outstanding is reconstructed as of — today by default, or a past date to see what was
    // owed then (later payments added back, invoices issued after it excluded). Aging is relative to it.
    DateOnly AsAt,
    IReadOnlyList<OutstandingRow> Rows);

// --- Data exceptions (LEGACY-DATA-POLICY §4) -----------------------------------------------

/// <summary>
/// One known legacy-data defect — a row on the Data Exceptions screen. The screen lists what is wrong in
/// the imported data, live, so it is visible and does not quietly grow (LEGACY-DATA-POLICY §4). It is
/// read-only for now; the permission-gated, audited correction is a later slice.
/// </summary>
/// <param name="Type">The kind of defect — <c>Duplicate payment</c>, <c>Paid, no payment</c>, or
/// <c>Lines ≠ header</c>.</param>
/// <param name="Reference">The invoice number the defect sits on.</param>
/// <param name="CustomerName">The customer, for context (blank when the code does not resolve).</param>
/// <param name="Detail">A human-readable statement of the discrepancy.</param>
/// <param name="Amount">The money at stake — the duplicated value, the unbacked balance, or the
/// header/lines gap.</param>
public sealed record DataExceptionRow(
    string Type,
    string Reference,
    string CustomerName,
    string Detail,
    decimal Amount);

/// <param name="DuplicatePayments">Invoices still carrying a duplicate payment group.</param>
/// <param name="PaidNoPayment">Invoices marked paid with no payment record behind them.</param>
/// <param name="LinesNotHeader">Invoices whose line items do not sum to the header.</param>
public sealed record DataExceptionsResponse(
    int DuplicatePayments,
    int PaidNoPayment,
    int LinesNotHeader,
    int Total,
    IReadOnlyList<DataExceptionRow> Rows);

/// <summary>One outstanding invoice — the per-invoice drill-down behind the "export selected" list,
/// the legacy outstanding-invoice sheet for the chosen customers.</summary>
public sealed record OutstandingDetailRow(
    string CustomerCode,
    string CustomerName,
    string Category,
    string InvoiceNo,
    DateOnly? Date,
    string? PurchaseOrderNo,
    decimal Total,
    decimal Balance,
    int Days,
    bool HasDataIssue);
