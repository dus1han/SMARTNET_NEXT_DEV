using System;
using System.Collections.Generic;

namespace Smartnet.Infrastructure.Entities;

public partial class Payment
{
    public int Id { get; set; }

    public string? Invoiceno { get; set; }

    public string? Amount { get; set; }

    public string? Paymentrecdate { get; set; }

    public string? Enteredby { get; set; }

    public string? Entereddt { get; set; }

    public string? Paym { get; set; }

    public string? Payref { get; set; }

    /// <summary>
    /// Additive discriminator (Phase 7): <c>new</c> on a row the new customer-receipt path dual-wrote,
    /// <c>NULL</c> on a pre-cutover legacy payment — so the payments list can show legacy history without
    /// double-counting the new dual-writes.
    /// </summary>
    public string? DataOrigin { get; set; }
}
