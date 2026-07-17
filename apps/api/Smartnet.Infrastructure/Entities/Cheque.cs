using System;
using System.Collections.Generic;

namespace Smartnet.Infrastructure.Entities;

public partial class Cheque
{
    public int Id { get; set; }

    public string? Chequedate { get; set; }

    public string Payto { get; set; } = null!;

    public string Amount { get; set; } = null!;

    public string Company { get; set; } = null!;

    public string Duedate { get; set; } = null!;

    public string Createdby { get; set; } = null!;

    public string Createddt { get; set; } = null!;

    public string Printeddt { get; set; } = null!;

    public string Bank { get; set; } = null!;

    public string Chkno { get; set; } = null!;

    public string Entry { get; set; } = null!;

    public string Supcode { get; set; } = null!;

    /// <summary>The Phase 7 adoption discriminator: <c>new</c> for a cheque this app raised, <c>legacy</c> for an adopted one.</summary>
    public string? DataOrigin { get; set; }
}
