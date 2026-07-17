using System;
using System.Collections.Generic;

namespace Smartnet.Infrastructure.Entities;

public partial class SupplierInvPay
{
    public int Id { get; set; }

    public string? Supinvid { get; set; }

    public string? Paiddate { get; set; }

    public string? Referenceno { get; set; }

    public string PayMethod { get; set; } = null!;

    /// <summary>
    /// Additive discriminator (Phase 7): <c>new</c> on a row the new supplier-payment path dual-wrote,
    /// <c>NULL</c> on a pre-cutover legacy payment — so the supplier-payments list can show legacy history
    /// without double-counting the new dual-writes.
    /// </summary>
    public string? DataOrigin { get; set; }
}
