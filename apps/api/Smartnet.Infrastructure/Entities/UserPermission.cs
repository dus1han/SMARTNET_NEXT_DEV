using System;
using System.Collections.Generic;

namespace Smartnet.Infrastructure.Entities;

public partial class UserPermission
{
    public string UserId { get; set; } = null!;

    public string Dashboard { get; set; } = null!;

    public string CustomerM { get; set; } = null!;

    public string SupplierM { get; set; } = null!;

    public string ItemM { get; set; } = null!;

    public string Itemstock { get; set; } = null!;

    public string ItemQu { get; set; } = null!;

    public string ServiceQu { get; set; } = null!;

    public string SearchQu { get; set; } = null!;

    public string ItemIn { get; set; } = null!;

    public string ServiceIn { get; set; } = null!;

    public string SearchIn { get; set; } = null!;

    public string DeletedIn { get; set; } = null!;

    public string Jobcards { get; set; } = null!;

    public string JobcardsRpt { get; set; } = null!;

    public string NewCn { get; set; } = null!;

    public string SearchCn { get; set; } = null!;

    public string Payments { get; set; } = null!;

    public string CustomerOutstanding { get; set; } = null!;

    public string Purchaseorder { get; set; } = null!;

    public string SearchPo { get; set; } = null!;

    public string SupplierIn { get; set; } = null!;

    public string Expenses { get; set; } = null!;

    public string ExpensesRpt { get; set; } = null!;

    public string SalesRpt { get; set; } = null!;

    public string CustomersalesRpt { get; set; } = null!;

    public string SupplierpurchaseRpt { get; set; } = null!;

    public string SupplierpaymentsRpt { get; set; } = null!;

    public string CusvatRpt { get; set; } = null!;

    public string SuppliervatRpt { get; set; } = null!;

    public string Users { get; set; } = null!;

    public string Docstorage { get; set; } = null!;

    public string Notes { get; set; } = null!;

    public string Email { get; set; } = null!;

    public string Cheques { get; set; } = null!;

    public string Chequerpt { get; set; } = null!;
}
