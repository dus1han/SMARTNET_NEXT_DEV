using System;
using System.Collections.Generic;

namespace Smartnet.Infrastructure.Entities;

public partial class PoH
{
    /// <summary>The surrogate id added by the Phase 6 PO adoption — a stable handle for legacy reads.</summary>
    public long Id { get; set; }

    /// <summary>The legacy/new discriminator added by the Phase 6 PO adoption; legacy rows are <c>legacy</c>.</summary>
    public string? DataOrigin { get; set; }

    public string? PoNo { get; set; }

    public string? Podate { get; set; }

    public string? Supplier { get; set; }

    public string? Totamount { get; set; }

    public string? Preparedby { get; set; }

    public string? Cdatetime { get; set; }

    public string? Company { get; set; }

    public string? Nonvattotal { get; set; }

    public string? Vatty { get; set; }

    public string? Vatpercent { get; set; }
}
