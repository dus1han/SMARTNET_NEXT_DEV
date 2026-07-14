using System;
using System.Collections.Generic;

namespace Smartnet.Infrastructure.Entities;

public partial class Docstore
{
    public int Id { get; set; }

    public string? Title { get; set; }

    public byte[]? Pdfdoc { get; set; }

    public string? Addeddate { get; set; }

    public string? Addedby { get; set; }

    public string? Docext { get; set; }
}
