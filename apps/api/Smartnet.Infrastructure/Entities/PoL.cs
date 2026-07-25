using System;
using System.Collections.Generic;

namespace Smartnet.Infrastructure.Entities;

public partial class PoL
{
    public string? Pono { get; set; }

    public string? Itemno { get; set; }

    public string? Desc { get; set; }

    public string? Qty { get; set; }

    public string? Rate { get; set; }

    public string? Total { get; set; }

    /// <summary>Set when the new app's editor dropped this line — see <see cref="InvoiceL.DeletedAt"/>.</summary>
    public DateTime? DeletedAt { get; set; }
}
