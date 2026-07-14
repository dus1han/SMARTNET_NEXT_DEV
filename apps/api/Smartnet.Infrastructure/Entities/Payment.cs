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

    public string Paym { get; set; } = null!;

    public string Payref { get; set; } = null!;
}
