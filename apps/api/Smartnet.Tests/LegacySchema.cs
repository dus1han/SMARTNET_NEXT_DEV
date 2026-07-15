namespace Smartnet.Tests;

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
        // Carries both: the company it belongs to, and the number the children join on.
        Numbered("invoice_h", "invoiceno", company: true),

        // Already company-aware in the legacy schema, and each numbers its own documents.
        Numbered("quotation_h", "q_no", company: true),
        Numbered("po_h", "po_no", company: true),
        Numbered("jobs_m", "jobno", company: true),

        // Company-aware but unnumbered by the legacy app.
        Document("cheques"),
        Document("expense_tr"),
        Document("supplier_invoice"),
        Document("del_invoice_h"),

        // NOT company-aware: these hang off an invoice and inherit its company through invoiceno.
        // Credit notes are numbered as well as parented.
        Child("payments", numberColumn: null),
        Child("cn_h", numberColumn: "cnno"),
        Child("del_cn_h", numberColumn: null),
    ];

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
