namespace Smartnet.Infrastructure.Persistence;

/// <summary>
/// The legacy tables, in the shape they are in <i>before</i> any of our migrations touch them.
/// </summary>
/// <remarks>
/// Our migrations do not create the legacy schema — they ALTER it, additively, because the tables
/// already exist in production and the legacy app is still reading and writing them. A fresh test
/// container has nothing to alter, so the starting shape has to be recreated here first.
///
/// <para><b>These definitions are copied from the live database, warts included</b> — the whole
/// value of testing against them is that they are wrong in the ways the real ones are wrong. In
/// particular <c>user_m.id</c> is AUTO_INCREMENT under a <i>non-unique</i> KEY rather than a
/// primary key, which is what the AdoptUserTable migration has to fix and what a tidied-up
/// version of this DDL would hide.</para>
///
/// <para><b>TODO (before staging):</b> replace this hand-copied subset with a real
/// <c>mysqldump --no-data</c> baseline of all 49 tables, checked in under <c>infra/sql/</c>. Each
/// table adopted from here on needs its legacy DDL added, and copying them out one at a time will
/// eventually miss one.</para>
/// </remarks>
public static class LegacySchema
{
    /// <summary>Exactly as it stands in smartnet_invsys today. Do not "improve" it.</summary>
    public const string UserM = """
        CREATE TABLE `user_m` (
          `id` int(11) NOT NULL AUTO_INCREMENT,
          `username` varchar(100) DEFAULT NULL,
          `name` varchar(100) DEFAULT NULL,
          `password` varchar(100) DEFAULT NULL,
          `utype` varchar(100) DEFAULT NULL,
          `cuscode` varchar(100) DEFAULT NULL,
          `ustat` varchar(100) NOT NULL,
          `addedby` varchar(100) NOT NULL,
          KEY `id` (`id`)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb3
        """;

    /// <summary>
    /// 35 permission flags as varchar "1"/"0", keyed by a varchar user_id, with no primary key —
    /// the RolesAndPermissions migration adds one.
    /// </summary>
    /// <remarks>
    /// The columns are generated from the permission catalogue rather than typed out, for the same
    /// reason the production mapping is: 35 hand-copied column names is 35 chances to make a typo
    /// that the test suite would then happily confirm.
    /// </remarks>
    public static string UserPermissions
    {
        get
        {
            var flags = string.Join(
                ",\n  ",
                Domain.Identity.Permissions.LegacyPermissions
                    .Select(p => $"`{p}` varchar(100) NOT NULL"));

            return $"""
                CREATE TABLE `user_permissions` (
                  `user_id` varchar(100) NOT NULL,
                  {flags}
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
                """;
        }
    }

    /// <summary>
    /// <c>companies_m</c> — three columns, and `id` is AUTO_INCREMENT under a non-unique KEY
    /// rather than a primary key, exactly like user_m.
    /// </summary>
    public const string CompaniesM = """
        CREATE TABLE `companies_m` (
          `id` int(100) NOT NULL AUTO_INCREMENT,
          `name` varchar(100) NOT NULL,
          `vatcode` varchar(100) DEFAULT NULL,
          KEY `id` (`id`)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
        """;

    /// <summary>
    /// The document tables, reduced to the columns the multi-company migration actually reads or
    /// writes: the legacy <c>company</c> varchar it backfills from, and the <c>invoiceno</c> that
    /// payments and credit notes derive their company through.
    /// </summary>
    /// <remarks>
    /// These are <b>deliberately partial</b>. Reproducing all 18 columns of invoice_h here would
    /// be 18 more chances to mistype one, and the migration does not look at the other 16. The
    /// columns that ARE here are faithful — <c>company</c> is a varchar holding "1"/"2", because
    /// that is what it is in production, and a tidier <c>int</c> would hide the CAST the migration
    /// has to do.
    ///
    /// <para>See the TODO above: the real fix is a <c>mysqldump --no-data</c> baseline of all 49
    /// tables. This is the second migration to need hand-copied legacy DDL, which is the signal
    /// that it should stop being hand-copied.</para>
    /// </remarks>
    public static IReadOnlyList<string> DocumentTables =>
    [
        // invoice_h and invoice_l are now ADOPTED (Phase 5), not merely company-id'd, so they need
        // their full legacy shape — every column the new entity maps or the seed migration reads,
        // including the three that are NOT NULL (discountper, beforedisctot, contactperson), because a
        // new-invoice insert has to satisfy them just as a legacy one did. Faithful to production
        // (SHOW COLUMNS, 2026-07-15): all money and dates are varchar (Finding 5), there is no key.
        InvoiceH,
        InvoiceL,

        // quotation_h and quotation_l are now ADOPTED (Phase 5, slice 3), like invoices, so they need
        // their full legacy shape — every column the new entity maps or shadows, including the three that
        // are NOT NULL (discountper, beforedisctot, contactperson), because a new-quotation insert has to
        // satisfy them just as a legacy one did.
        QuotationH,
        QuotationL,

        // cn_h and cn_l are now ADOPTED (Phase 5, slice 4), like invoices and quotations, so they need their
        // full legacy shape — every column the new entity maps or shadows, including the two that are NOT
        // NULL (invoiceno, stockposting), because a new-credit-note insert has to satisfy them just as a
        // legacy one did.
        CnH,
        CnL,

        // po_h and po_l are now ADOPTED (Phase 6, slice 1), like invoices and quotations, so they need
        // their full legacy shape — every column the new entity maps or shadows. Unlike invoice_h/
        // quotation_h, po_h has NO NOT NULL columns (all varchar DEFAULT NULL, per the live scaffold), so
        // the dual-write keeps the legacy reader whole rather than gating the insert. The header is
        // thinner than an invoice's: no it/contactperson/discountper/beforedisctot, and its VAT columns
        // are vatty/vatpercent/nonvattotal.
        PoH,
        PoL,

        // jobs_m is now ADOPTED (Phase 6, slice 3). It has no key (a surrogate id is added) and — unusually
        // — every column is NOT NULL, so a new job card writes them all. jobcard_l (its structured serial
        // lines) is a genuinely new table, created by the migration, not recreated here.
        JobsMTable,

        // supplier_invoice is now ADOPTED (Phase 6, slice 2). Like item_stock it already has an
        // AUTO_INCREMENT `id` — but under a non-unique KEY, not a primary key (Finding 6) — which the
        // adoption promotes; supplier_inv_pay (its payments) is retained for the dual-write.
        SupplierInvoiceTable,
        SupplierInvPayTable,

        // Company-aware but unnumbered by the legacy app.
        Document("cheques"),
        Document("expense_tr"),
        Document("del_invoice_h"),

        // NOT company-aware: these hang off an invoice and inherit its company through invoiceno.
        // payments carries its full legacy shape — the customer-receipt dual-write (Phase 7, slice 1) writes
        // every column the old savePay did, so the stub invoiceno-only shape no longer suffices.
        Payments,
        Child("del_cn_h", numberColumn: null),
    ];

    /// <summary>
    /// <c>invoice_h</c> in its full pre-adoption shape — all 18 columns, faithful to production. Money
    /// and dates are <c>varchar</c> (Finding 5); <c>discountper</c>, <c>beforedisctot</c> and
    /// <c>contactperson</c> are NOT NULL, which is why a new invoice must write them (the dual-write),
    /// not just its own <c>decimal</c> columns.
    /// </summary>
    public const string InvoiceH = """
        CREATE TABLE `invoice_h` (
          `it` varchar(100) DEFAULT NULL,
          `invoiceno` varchar(100) DEFAULT NULL,
          `invtype` varchar(100) DEFAULT NULL,
          `indate` varchar(100) DEFAULT NULL,
          `customer` varchar(100) DEFAULT NULL,
          `pono` varchar(100) DEFAULT NULL,
          `totamount` varchar(100) DEFAULT NULL,
          `balance` varchar(100) DEFAULT NULL,
          `preparedby` varchar(100) DEFAULT NULL,
          `cdatetime` varchar(100) DEFAULT NULL,
          `cost` varchar(100) DEFAULT NULL,
          `company` varchar(100) DEFAULT NULL,
          `novattotal` varchar(100) DEFAULT NULL,
          `vtype` varchar(100) DEFAULT NULL,
          `vper` varchar(100) DEFAULT NULL,
          `discountper` varchar(50) NOT NULL,
          `beforedisctot` varchar(100) NOT NULL,
          `contactperson` varchar(100) NOT NULL
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
        """;

    /// <summary>
    /// <c>payments</c> — a customer invoice's receipts, keyed to the invoice by <c>invoiceno</c>. An
    /// AUTO_INCREMENT <c>id</c> (the legacy <c>deletepay</c> deletes <c>WHERE id=…</c>), all data columns
    /// <c>varchar DEFAULT NULL</c> — faithful to production, where several legacy INSERTs omit <c>paym</c>/
    /// <c>payref</c>. Retained (not adopted): the new app's receipts live in the receivables ledger and a row
    /// is dual-written here for the surviving legacy outstanding report.
    /// </summary>
    public const string Payments = """
        CREATE TABLE `payments` (
          `id` int(11) NOT NULL AUTO_INCREMENT,
          `invoiceno` varchar(100) DEFAULT NULL,
          `amount` varchar(100) DEFAULT NULL,
          `paymentrecdate` varchar(100) DEFAULT NULL,
          `enteredby` varchar(100) DEFAULT NULL,
          `entereddt` varchar(100) DEFAULT NULL,
          `paym` varchar(100) DEFAULT NULL,
          `payref` varchar(100) DEFAULT NULL,
          PRIMARY KEY (`id`)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
        """;

    /// <summary><c>invoice_l</c> — the invoice's lines, all nullable, keyless. <c>desc</c> is text.</summary>
    public const string InvoiceL = """
        CREATE TABLE `invoice_l` (
          `inno` varchar(100) DEFAULT NULL,
          `itemno` bigint(21) DEFAULT NULL,
          `desc` text DEFAULT NULL,
          `qty` varchar(100) DEFAULT NULL,
          `rate` varchar(100) DEFAULT NULL,
          `tot` varchar(100) DEFAULT NULL,
          `itemcode` varchar(100) DEFAULT NULL
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
        """;

    /// <summary>
    /// <c>quotation_h</c> in its full pre-adoption shape, faithful to the legacy app's own
    /// <c>INSERT INTO quotation_h(…)</c> (ItemQuotationController). Like <c>invoice_h</c>, money and dates
    /// are <c>varchar</c>; <c>discountper</c>, <c>beforedisctot</c> and <c>contactperson</c> are NOT NULL,
    /// which is why a new quotation must write them (the dual-write). The header carries <c>q_valid</c>
    /// (how long the price holds) and <c>quotecost</c> — the quotation's own column names.
    /// </summary>
    public const string QuotationH = """
        CREATE TABLE `quotation_h` (
          `q_no` varchar(100) DEFAULT NULL,
          `qdate` varchar(100) DEFAULT NULL,
          `customer` varchar(100) DEFAULT NULL,
          `totamount` varchar(100) DEFAULT NULL,
          `preparedby` varchar(100) DEFAULT NULL,
          `cdatetime` varchar(100) DEFAULT NULL,
          `company` varchar(100) DEFAULT NULL,
          `it` varchar(100) DEFAULT NULL,
          `quotecost` varchar(100) DEFAULT NULL,
          `novattotal` varchar(100) DEFAULT NULL,
          `vtype` varchar(100) DEFAULT NULL,
          `vper` varchar(100) DEFAULT NULL,
          `q_valid` varchar(100) DEFAULT NULL,
          `discountper` varchar(50) NOT NULL,
          `beforedisctot` varchar(100) NOT NULL,
          `contactperson` varchar(100) NOT NULL
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
        """;

    /// <summary>
    /// <c>quotation_l</c> — the quotation's lines, all nullable, keyless. The line total column is
    /// <c>total</c> (invoices call theirs <c>tot</c>); <c>desc</c> is text.
    /// </summary>
    public const string QuotationL = """
        CREATE TABLE `quotation_l` (
          `qno` varchar(100) DEFAULT NULL,
          `itemno` bigint(21) DEFAULT NULL,
          `desc` text DEFAULT NULL,
          `qty` varchar(100) DEFAULT NULL,
          `rate` varchar(100) DEFAULT NULL,
          `total` varchar(100) DEFAULT NULL,
          `itemcode` varchar(100) DEFAULT NULL
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
        """;

    /// <summary>
    /// <c>cn_h</c> in its full pre-adoption shape, faithful to the legacy app's own <c>INSERT INTO cn_h(…)</c>
    /// (CNoteController.saveCN). A credit note has a smaller header than an invoice — no customer, company,
    /// discount or contact columns (it inherits those from its parent invoice) — but its own
    /// <c>invoiceno</c> (the parent invoice number) and <c>stockposting</c> flag are NOT NULL, which is why a
    /// new credit note must write them (the dual-write). Money and dates are <c>varchar</c> (Finding 5).
    /// </summary>
    public const string CnH = """
        CREATE TABLE `cn_h` (
          `cnno` varchar(100) DEFAULT NULL,
          `invoiceno` varchar(100) NOT NULL,
          `cndate` varchar(100) DEFAULT NULL,
          `totamount` varchar(100) DEFAULT NULL,
          `preparedby` varchar(100) DEFAULT NULL,
          `cdatetime` varchar(100) DEFAULT NULL,
          `novattotal` varchar(100) DEFAULT NULL,
          `vtype` varchar(100) DEFAULT NULL,
          `vper` varchar(100) DEFAULT NULL,
          `stockposting` varchar(100) NOT NULL
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
        """;

    /// <summary>
    /// <c>cn_l</c> — the credit note's lines, all nullable, keyless. The line total column is <c>tot</c> (as
    /// on <c>invoice_l</c>); <c>desc</c> is text.
    /// </summary>
    public const string CnL = """
        CREATE TABLE `cn_l` (
          `cnno` varchar(100) DEFAULT NULL,
          `itemno` bigint(21) DEFAULT NULL,
          `desc` text DEFAULT NULL,
          `qty` varchar(100) DEFAULT NULL,
          `rate` varchar(100) DEFAULT NULL,
          `tot` varchar(100) DEFAULT NULL,
          `itemcode` varchar(100) DEFAULT NULL
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
        """;

    /// <summary>
    /// <c>po_h</c> in its full pre-adoption shape, faithful to the live scaffold (every column is
    /// <c>varchar(100) DEFAULT NULL</c> — the legacy PO header has no NOT NULL columns). Its VAT columns
    /// are <c>vatty</c>/<c>vatpercent</c>/<c>nonvattotal</c>, and there is no <c>it</c>, <c>contactperson</c>,
    /// <c>discountper</c> or <c>beforedisctot</c> — the header is thinner than an invoice's.
    /// </summary>
    public const string PoH = """
        CREATE TABLE `po_h` (
          `po_no` varchar(100) DEFAULT NULL,
          `podate` varchar(100) DEFAULT NULL,
          `supplier` varchar(100) DEFAULT NULL,
          `totamount` varchar(100) DEFAULT NULL,
          `preparedby` varchar(100) DEFAULT NULL,
          `cdatetime` varchar(100) DEFAULT NULL,
          `company` varchar(100) DEFAULT NULL,
          `nonvattotal` varchar(100) DEFAULT NULL,
          `vatty` varchar(100) DEFAULT NULL,
          `vatpercent` varchar(100) DEFAULT NULL
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
        """;

    /// <summary>
    /// <c>po_l</c> — the PO's lines, all nullable, keyless. The line total column is <c>total</c> (as on
    /// <c>quotation_l</c>); <c>desc</c> is text; <c>itemno</c> is a legacy varchar (a cart sequence number,
    /// not a real item), and there is no legacy <c>itemcode</c> column.
    /// </summary>
    public const string PoL = """
        CREATE TABLE `po_l` (
          `pono` varchar(100) DEFAULT NULL,
          `itemno` varchar(100) DEFAULT NULL,
          `desc` text DEFAULT NULL,
          `qty` varchar(100) DEFAULT NULL,
          `rate` varchar(100) DEFAULT NULL,
          `total` varchar(100) DEFAULT NULL
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
        """;

    /// <summary>
    /// <c>supplier_invoice</c> in its full pre-adoption shape, faithful to the live scaffold. Like
    /// <c>item_stock</c>, it has an <c>int</c> <c>id</c> that is AUTO_INCREMENT under a plain non-unique
    /// <c>KEY</c> rather than a primary key (Finding 6) — which the Phase 6 adoption promotes to a
    /// <c>bigint</c> primary key. Everything else is <c>varchar(100) DEFAULT NULL</c>.
    /// </summary>
    public const string SupplierInvoiceTable = """
        CREATE TABLE `supplier_invoice` (
          `id` int(100) NOT NULL AUTO_INCREMENT,
          `invno` varchar(100) DEFAULT NULL,
          `supcode` varchar(100) DEFAULT NULL,
          `amount` varchar(100) DEFAULT NULL,
          `paymentstat` varchar(100) DEFAULT NULL,
          `invdate` varchar(100) DEFAULT NULL,
          `company` varchar(100) DEFAULT NULL,
          `novattotal` varchar(100) DEFAULT NULL,
          `vtype` varchar(100) DEFAULT NULL,
          `vper` varchar(100) DEFAULT NULL,
          KEY `id` (`id`)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
        """;

    /// <summary>
    /// <c>supplier_inv_pay</c> — a supplier invoice's payments, retained for the dual-write. The same
    /// AUTO_INCREMENT-under-a-KEY <c>id</c> pattern; <c>pay_method</c> is NOT NULL (per the live scaffold).
    /// Not adopted as a new entity — the new app's payments live in the payables ledger and a row is
    /// dual-written here for the surviving legacy readers.
    /// </summary>
    public const string SupplierInvPayTable = """
        CREATE TABLE `supplier_inv_pay` (
          `id` int(100) NOT NULL AUTO_INCREMENT,
          `supinvid` varchar(100) DEFAULT NULL,
          `paiddate` varchar(100) DEFAULT NULL,
          `referenceno` varchar(100) DEFAULT NULL,
          `pay_method` varchar(100) NOT NULL,
          KEY `id` (`id`)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
        """;

    /// <summary>
    /// <c>jobs_m</c> in its full pre-adoption shape, faithful to the live scaffold: keyless, and — unusually
    /// — <b>every column NOT NULL</b> (a new job card must write them all). Charset/collation match the live
    /// table. The Phase 6 adoption adds a surrogate <c>id</c> and the typed columns beside these.
    /// </summary>
    public const string JobsMTable = """
        CREATE TABLE `jobs_m` (
          `jdate` varchar(100) NOT NULL,
          `jobno` varchar(100) NOT NULL,
          `company` varchar(100) NOT NULL,
          `customer` varchar(100) NOT NULL,
          `contactperson` varchar(100) NOT NULL,
          `faultD` text NOT NULL,
          `remarks` text NOT NULL,
          `enteredby` varchar(100) NOT NULL,
          `entereddt` varchar(100) NOT NULL,
          `jobdoneby` varchar(100) NOT NULL,
          `cost` varchar(100) NOT NULL,
          `sell` varchar(100) NOT NULL,
          `completionremarks` text NOT NULL,
          `completedby` varchar(100) NOT NULL,
          `dompleteddt` varchar(100) NOT NULL,
          `jstat` varchar(100) NOT NULL,
          `items` text NOT NULL
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """;

    /// <summary>A document table that carries both a company and its own document number.</summary>
    private static string Numbered(string table, string numberColumn, bool company) => $"""
        CREATE TABLE `{table}` (
          `{numberColumn}` varchar(100) DEFAULT NULL{(company ? ",\n  `company` varchar(100) DEFAULT NULL" : string.Empty)}
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
        """;

    private static string Document(string table) => $"""
        CREATE TABLE `{table}` (
          `company` varchar(100) DEFAULT NULL
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
        """;

    /// <summary>Hangs off an invoice via invoiceno; may or may not have a number of its own.</summary>
    private static string Child(string table, string? numberColumn) => $"""
        CREATE TABLE `{table}` (
          `invoiceno` varchar(100) DEFAULT NULL{(numberColumn is null ? string.Empty : $",\n  `{numberColumn}` varchar(100) DEFAULT NULL")}
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
        """;

    /// <summary>
    /// The master tables, copied from the live schema exactly as they are — warts included.
    /// </summary>
    /// <remarks>
    /// The warts are the test. <c>climit</c> is a <c>varchar(100)</c> holding money and <c>c_form</c>
    /// a <c>varchar</c> holding a foreign key (Finding 5); <c>cus_m</c>, <c>sup_m</c> and
    /// <c>item_m</c> have <b>no primary key at all</b>, and <c>item_stock</c> has an AUTO_INCREMENT
    /// <c>id</c> under a plain non-unique <c>KEY</c> (Finding 6). Writing these tidily here would
    /// mean the migration is never tested against the schema it actually has to run against.
    /// </remarks>
    public static IReadOnlyList<string> MasterTables =>
    [
        """
        CREATE TABLE `cus_m` (
          `cuscode` varchar(100) DEFAULT NULL,
          `cusname` varchar(100) DEFAULT NULL,
          `custype` varchar(100) DEFAULT NULL,
          `contactp` varchar(100) DEFAULT NULL,
          `cusadd` varchar(100) DEFAULT NULL,
          `contactno` varchar(100) DEFAULT NULL,
          `email` text DEFAULT NULL,
          `c_form` varchar(100) DEFAULT NULL,
          `pro` varchar(100) DEFAULT NULL,
          `vatnum` varchar(100) DEFAULT NULL,
          `climit` varchar(100) NOT NULL
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
        """,

        """
        CREATE TABLE `sup_m` (
          `supcode` varchar(100) DEFAULT NULL,
          `supname` varchar(100) DEFAULT NULL,
          `contactp` varchar(100) DEFAULT NULL,
          `supadd` varchar(100) DEFAULT NULL,
          `contactno` varchar(100) DEFAULT NULL,
          `email` text DEFAULT NULL,
          `vatnum` varchar(100) DEFAULT NULL
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
        """,

        """
        CREATE TABLE `item_m` (
          `itemcode` varchar(100) DEFAULT NULL,
          `itemname` varchar(100) DEFAULT NULL
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
        """,

        // The AUTO_INCREMENT id under a KEY rather than a PRIMARY KEY is not a typo. It is what the
        // live table looks like, and it is why the migration has to promote it.
        """
        CREATE TABLE `item_stock` (
          `id` int(100) NOT NULL AUTO_INCREMENT,
          `item_code` varchar(100) DEFAULT NULL,
          `unitcost` varchar(100) DEFAULT NULL,
          `indate` varchar(100) DEFAULT NULL,
          `warranty` varchar(100) DEFAULT NULL,
          `quantity` varchar(100) DEFAULT NULL,
          `balance` varchar(100) DEFAULT NULL,
          `enteredby` varchar(100) DEFAULT NULL,
          `enteredat` varchar(100) DEFAULT NULL,
          KEY `id` (`id`)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
        """,

        // One of the three tables in the whole legacy schema that has a primary key.
        """
        CREATE TABLE `profit_percent` (
          `id` int(100) NOT NULL AUTO_INCREMENT,
          `name` varchar(100) DEFAULT NULL,
          PRIMARY KEY (`id`)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
        """,

        // The customer-code sequence. The legacy app allocates "C-{n}" by inserting a row here and
        // taking the auto-increment; the new app draws from the SAME table so the two cannot hand
        // out the same code during coexistence. The AUTO_INCREMENT under a plain KEY is faithful.
        """
        CREATE TABLE `cus_seq` (
          `id` int(100) NOT NULL AUTO_INCREMENT,
          `dt` varchar(100) DEFAULT NULL,
          KEY `id` (`id`)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
        """,

        // The supplier-code sequence, the exact twin of cus_seq: the legacy app allocates "S-{n}" by
        // inserting a row here (SupplierController.savesupplier), and the new app draws from the same
        // table so the two cannot hand out the same code during coexistence.
        """
        CREATE TABLE `sup_seq` (
          `id` int(100) NOT NULL AUTO_INCREMENT,
          `dt` varchar(100) DEFAULT NULL,
          KEY `id` (`id`)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
        """,

        // The item-code sequence, the third twin: the legacy app allocates "I-{n}" by inserting here
        // (ItemController.saveitem), and the new app draws from the same table.
        """
        CREATE TABLE `item_seq` (
          `id` int(100) NOT NULL AUTO_INCREMENT,
          `dt` varchar(100) DEFAULT NULL,
          KEY `id` (`id`)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
        """,
    ];

    public static IReadOnlyList<string> All =>
        [UserM, UserPermissions, CompaniesM, .. DocumentTables, .. MasterTables];
}
