using System;
using System.Collections.Generic;

namespace Smartnet.Infrastructure.Entities;

public partial class DelCnH
{
    public string Deldate { get; set; } = null!;

    public string Delreason { get; set; } = null!;

    public string Deluser { get; set; } = null!;

    public string? Cnno { get; set; }

    public string Invoiceno { get; set; } = null!;

    public string? Cndate { get; set; }

    public string? Totamount { get; set; }

    public string? Preparedby { get; set; }

    public string? Cdatetime { get; set; }

    public string? Novattotal { get; set; }

    public string? Vtype { get; set; }

    public string? Vper { get; set; }

    public string Stockposting { get; set; } = null!;
}
