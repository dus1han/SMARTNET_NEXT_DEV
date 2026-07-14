using System;
using System.Collections.Generic;

namespace Smartnet.Infrastructure.Entities;

public partial class WbProject
{
    public int Id { get; set; }

    public string? Projecttitle { get; set; }

    public string? Client { get; set; }

    public string? Location { get; set; }

    public string? Descrip { get; set; }

    public string? Imagepath { get; set; }
}
