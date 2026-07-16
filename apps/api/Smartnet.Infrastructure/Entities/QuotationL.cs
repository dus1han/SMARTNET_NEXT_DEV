using System;
using System.Collections.Generic;

namespace Smartnet.Infrastructure.Entities;

public partial class QuotationL
{
    /// <summary>The surrogate key the Phase 5 quotation adoption added to <c>quotation_l</c>.</summary>
    public long Id { get; set; }

    public string? Qno { get; set; }

    public string? Itemno { get; set; }

    public string? Desc { get; set; }

    public string? Qty { get; set; }

    public string? Rate { get; set; }

    public string? Total { get; set; }

    public string Itemcode { get; set; } = null!;
}
