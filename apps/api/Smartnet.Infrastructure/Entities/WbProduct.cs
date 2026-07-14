using System;
using System.Collections.Generic;

namespace Smartnet.Infrastructure.Entities;

public partial class WbProduct
{
    public int Id { get; set; }

    public string? Pname { get; set; }

    public string? Cat { get; set; }

    public string? Price { get; set; }

    public string? Descrip { get; set; }

    public string? Imgpath { get; set; }
}
