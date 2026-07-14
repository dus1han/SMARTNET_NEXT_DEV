using System;
using System.Collections.Generic;

namespace Smartnet.Infrastructure.Entities;

public partial class QuotationL
{
    public string? Qno { get; set; }

    public string? Itemno { get; set; }

    public string? Desc { get; set; }

    public string? Qty { get; set; }

    public string? Rate { get; set; }

    public string? Total { get; set; }

    public string Itemcode { get; set; } = null!;
}
