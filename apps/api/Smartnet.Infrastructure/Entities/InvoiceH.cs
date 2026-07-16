using System;
using System.Collections.Generic;

namespace Smartnet.Infrastructure.Entities;

public partial class InvoiceH
{
    /// <summary>
    /// The surrogate key the Phase 5 adoption added to <c>invoice_h</c> (it had none). Present on every
    /// row now, including legacy ones — a stable, unique handle for a legacy invoice in a list.
    /// </summary>
    public long Id { get; set; }

    /// <summary>The concurrency token, so the edit screen can load a legacy invoice's version and echo it back.</summary>
    public int RowVersion { get; set; }

    public string? It { get; set; }

    public string? Invoiceno { get; set; }

    public string? Invtype { get; set; }

    public string? Indate { get; set; }

    public string? Customer { get; set; }

    public string? Pono { get; set; }

    public string? Totamount { get; set; }

    public string? Balance { get; set; }

    public string? Preparedby { get; set; }

    public string? Cdatetime { get; set; }

    public string? Cost { get; set; }

    public string? Company { get; set; }

    public string? Novattotal { get; set; }

    public string? Vtype { get; set; }

    public string? Vper { get; set; }

    public string Discountper { get; set; } = null!;

    public string Beforedisctot { get; set; } = null!;

    public string Contactperson { get; set; } = null!;

    /// <summary>
    /// The <c>data_origin</c> discriminator the Phase 5 invoice adoption added. Legacy rows are
    /// <c>legacy</c> (the default); rows the new app writes are <c>new</c>. This read-model is for the
    /// legacy rows, so a reader that must not double-count the new invoices filters on it.
    /// </summary>
    public string? DataOrigin { get; set; }
}
