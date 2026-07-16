using System;
using System.Collections.Generic;

namespace Smartnet.Infrastructure.Entities;

public partial class CnH
{
    /// <summary>The surrogate key the Phase 5 credit-note adoption added to <c>cn_h</c> (it had none).</summary>
    public long Id { get; set; }

    public string? Cnno { get; set; }

    public string Invoiceno { get; set; } = null!;

    public string? Cndate { get; set; }

    public string? Totamount { get; set; }

    public string? Preparedby { get; set; }

    public string? Cdatetime { get; set; }

    public string? Novattotal { get; set; }

    public string? Vtype { get; set; }

    public string? Vper { get; set; }

    public string Stockposting { get; set; } = null!;

    /// <summary>
    /// The <c>company_id</c> the multi-company migration added to <c>cn_h</c>, backfilled from the parent
    /// invoice. A credit note has no legacy <c>company</c> varchar (it inherits the company from its
    /// invoice), so this bigint is how a legacy reader scopes credit notes by company.
    /// </summary>
    public long? CompanyId { get; set; }

    /// <summary>
    /// The <c>data_origin</c> the Phase 5 credit-note adoption added — <c>legacy</c> by default, <c>new</c>
    /// for rows the new app writes. A legacy reader filters on it so it never sees a new note twice.
    /// </summary>
    public string? DataOrigin { get; set; }
}
