using System;
using System.Collections.Generic;

namespace Smartnet.Infrastructure.Entities;

public partial class ExpenseTr
{
    public long Id { get; set; }

    public string ExpCat { get; set; } = null!;

    public string ExpenseDate { get; set; } = null!;

    public string ExpenseDesc { get; set; } = null!;

    public string ExpenseAmount { get; set; } = null!;

    public string Paymentm { get; set; } = null!;

    public string PaymentRef { get; set; } = null!;

    public string Addedby { get; set; } = null!;

    public string Addeddt { get; set; } = null!;

    public string Company { get; set; } = null!;

    /// <summary>The Phase 7 adoption discriminator: <c>new</c> for an expense this app raised, <c>legacy</c> for an adopted one.</summary>
    public string? DataOrigin { get; set; }

    /// <summary>The concurrency token (Phase 7 adoption), so a legacy expense can be voided with its version guarded.</summary>
    public int RowVersion { get; set; }

    /// <summary>Set when the expense has been voided (soft delete) — filtered out of the list and the report.</summary>
    public DateTime? DeletedAt { get; set; }
}
