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

    // c_form, pro and climit are numeric in the database. They were varchar when this model was
    // scaffolded and have since been altered, so a `string` property here throws InvalidCastException
    // the moment the whole entity is materialised (projections that skip these columns did not notice).
    public long? CForm { get; set; }

    public long? Pro { get; set; }

    public string? Vatnum { get; set; }

    public decimal? Climit { get; set; }
}
