using System;
using System.Collections.Generic;

namespace Smartnet.Infrastructure.Entities;

public partial class SupplierInvoice
{
    public int Id { get; set; }

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
