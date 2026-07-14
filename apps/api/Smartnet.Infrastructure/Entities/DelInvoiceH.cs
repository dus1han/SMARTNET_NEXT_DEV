using System;
using System.Collections.Generic;

namespace Smartnet.Infrastructure.Entities;

public partial class DelInvoiceH
{
    public string Deldate { get; set; } = null!;

    public string Delreason { get; set; } = null!;

    public string Deluser { get; set; } = null!;

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
}
