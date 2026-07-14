using System;
using System.Collections.Generic;

namespace Smartnet.Infrastructure.Entities;

public partial class CompaniesM
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public string? Vatcode { get; set; }
}
