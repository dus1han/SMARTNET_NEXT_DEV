using System;
using System.Collections.Generic;

namespace Smartnet.Infrastructure.Entities;

public partial class UserM
{
    public int Id { get; set; }

    public string? Username { get; set; }

    public string? Name { get; set; }

    public string? Password { get; set; }

    public string? Utype { get; set; }

    public string? Cuscode { get; set; }

    public string Ustat { get; set; } = null!;

    public string Addedby { get; set; } = null!;
}
