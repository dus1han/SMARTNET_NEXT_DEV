using System;
using System.Collections.Generic;

namespace Smartnet.Infrastructure.Entities;

public partial class JobsM
{
    public string Jdate { get; set; } = null!;

    public string Jobno { get; set; } = null!;

    public string Company { get; set; } = null!;

    public string Customer { get; set; } = null!;

    public string Contactperson { get; set; } = null!;

    public string FaultD { get; set; } = null!;

    public string Remarks { get; set; } = null!;

    public string Enteredby { get; set; } = null!;

    public string Entereddt { get; set; } = null!;

    public string Jobdoneby { get; set; } = null!;

    public string Cost { get; set; } = null!;

    public string Sell { get; set; } = null!;

    public string Completionremarks { get; set; } = null!;

    public string Completedby { get; set; } = null!;

    public string Dompleteddt { get; set; } = null!;

    public string Jstat { get; set; } = null!;

    public string Items { get; set; } = null!;
}
