using System;
using System.Collections.Generic;

namespace Smartnet.Infrastructure.Entities;

public partial class SupplierInvoice
{
    /// <summary>Promoted to a <c>bigint</c> primary key by the Phase 6 supplier-invoice adoption.</summary>
    public long Id { get; set; }

    /// <summary>The legacy/new discriminator added by the Phase 6 adoption; legacy rows are <c>legacy</c>.</summary>
    public string? DataOrigin { get; set; }

    public string? Invno { get; set; }

    public string? Supcode { get; set; }

    public string? Amount { get; set; }

    public string? Paymentstat { get; set; }

    public string? Invdate { get; set; }

    public string? Company { get; set; }

    public string? Novattotal { get; set; }

    public string? Vtype { get; set; }

    public string? Vper { get; set; }
}
