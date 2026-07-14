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
}
