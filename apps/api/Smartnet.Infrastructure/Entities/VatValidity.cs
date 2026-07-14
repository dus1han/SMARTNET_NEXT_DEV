using System;
using System.Collections.Generic;

namespace Smartnet.Infrastructure.Entities;

public partial class VatValidity
{
    public int Id { get; set; }

    public string? Vatval { get; set; }

    public string? Startdate { get; set; }

    public string? Enddate { get; set; }

    public string? Ty { get; set; }
}
