using System;
using System.Collections.Generic;

namespace Smartnet.Infrastructure.Entities;

public partial class QuotationH
{
    /// <summary>The surrogate key the Phase 5 adoption added to <c>quotation_h</c> (it had none).</summary>
    public long Id { get; set; }

    public string? QNo { get; set; }

    public string? Qdate { get; set; }

    public string? Customer { get; set; }

    public string? Totamount { get; set; }

    public string? Preparedby { get; set; }

    public string? Cdatetime { get; set; }

    public string? Company { get; set; }

    public string? It { get; set; }

    public string? Quotecost { get; set; }

    public string? Novattotal { get; set; }

    public string? Vtype { get; set; }

    public string? Vper { get; set; }

    public string QValid { get; set; } = null!;

    public string Discountper { get; set; } = null!;

    public string Beforedisctot { get; set; } = null!;

    public string Contactperson { get; set; } = null!;

    /// <summary>
    /// The <c>data_origin</c> the Phase 5 quotation adoption added — <c>legacy</c> by default, <c>new</c>
    /// for rows the new app writes. A legacy reader filters on it so it never sees a new quotation twice.
    /// </summary>
    public string? DataOrigin { get; set; }

    /// <summary>
    /// The invoice a legacy quotation was converted into <b>through the new app</b>, or null. The new
    /// conversion sets this on the legacy row too, so a legacy quote shows as converted and cannot be
    /// converted twice.
    /// </summary>
    public long? ConvertedToInvoiceId { get; set; }
}
