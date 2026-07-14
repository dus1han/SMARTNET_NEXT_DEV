using System;
using System.Collections.Generic;

namespace Smartnet.Infrastructure.Entities;

public partial class ExpenseTr
{
    public int Id { get; set; }

    public string ExpCat { get; set; } = null!;

    public string ExpenseDate { get; set; } = null!;

    public string ExpenseDesc { get; set; } = null!;

    public string ExpenseAmount { get; set; } = null!;

    public string Paymentm { get; set; } = null!;

    public string PaymentRef { get; set; } = null!;

    public string Addedby { get; set; } = null!;

    public string Addeddt { get; set; } = null!;

    public string Company { get; set; } = null!;
}
