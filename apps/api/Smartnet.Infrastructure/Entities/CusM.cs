using System;
using System.Collections.Generic;

namespace Smartnet.Infrastructure.Entities;

public partial class CusM
{
    public string? Cuscode { get; set; }

    public string? Cusname { get; set; }

    public string? Custype { get; set; }

    public string? Contactp { get; set; }

    public string? Cusadd { get; set; }

    public string? Contactno { get; set; }

    public string? Email { get; set; }

    public string? CForm { get; set; }

    public string? Pro { get; set; }

    public string? Vatnum { get; set; }

    public string Climit { get; set; } = null!;
}
