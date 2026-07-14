using System;
using System.Collections.Generic;

namespace Smartnet.Infrastructure.Entities;

public partial class QuotationH
{
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
}
