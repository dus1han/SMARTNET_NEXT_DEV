using System;
using System.Collections.Generic;

namespace Smartnet.Infrastructure.Entities;

public partial class ItemStock
{
    public int Id { get; set; }

    public string? ItemCode { get; set; }

    public string? Unitcost { get; set; }

    public string? Indate { get; set; }

    public string? Warranty { get; set; }

    public string? Quantity { get; set; }

    public string? Balance { get; set; }

    public string? Enteredby { get; set; }

    public string? Enteredat { get; set; }
}
