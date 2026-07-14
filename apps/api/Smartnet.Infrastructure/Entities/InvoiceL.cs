using System;
using System.Collections.Generic;

namespace Smartnet.Infrastructure.Entities;

public partial class InvoiceL
{
    public string? Inno { get; set; }

    public long? Itemno { get; set; }

    public string? Desc { get; set; }

    public string? Qty { get; set; }

    public string? Rate { get; set; }

    public string? Tot { get; set; }

    public string? Itemcode { get; set; }
}
