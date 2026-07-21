# Smart_InvSys — Function Inventory

Every action in the app: **248 actions across 44 controllers**, grouped into the
modules you'd actually build in the new stack.

Legend:
- 🗒️ **Index** — renders the page shell (becomes a Next.js route, not an API endpoint)
- 🔄 **API** — real work; becomes an ASP.NET Core endpoint
- 📄 **PDF/Print** — *deferred to the end* (see `rpt-dump.json` for field contracts)
- 📊 **Excel export** — ClosedXML; ports to .NET Core largely as-is
- ✉️ **Email**
- ⚠️ Note worth reading before building

---

## 1. Auth & Users  *(build first — this is the security fix)*

### Login *(5)*
| Action | Type | Notes |
|---|---|---|
| `LoginIndex` | 🗒️ | |
| `userauth` | 🔄 | ⚠️ Plaintext password compare; SQL-injectable pre-auth |
| `logout` | 🔄 | |
| `c_Password` | 🔄 | ⚠️ Writes password in plaintext |
| `getUserType` | 🔄 | Reads `utype` / `cuscode` from session → becomes a token claim |

### ManageUser *(7)*
| Action | Type | Notes |
|---|---|---|
| `ManageUserIndex` | 🗒️ | Only place the `users` permission is actually checked |
| `getUsers` | 🔄 | |
| `saveUser` | 🔄 | ⚠️ New users get hardcoded password `1234` |
| `getUserPer` | 🔄 | ⚠️ **No permission check** — any logged-in user can call it |
| `updatepermission` | 🔄 | ⚠️ **No permission check** — privilege escalation |
| `resetPassword` | 🔄 | |
| `disableUser` | 🔄 | |

**Permission model:** 36 boolean flags in `user_permissions`, loaded into session at
login and used only to show/hide UI. In the rebuild these become **claims enforced
per endpoint**.

---

## 2. Settings *(new — does not exist today)*

Not in the current app; everything is hardcoded. To build:
`companies` · `document_series` (numbering) · `tax_rates` · `document_templates` ·
`app_settings`. See `MIGRATION.md`.

---

## 3. Master Data

### Customer *(5)*
`CustomerIndex` 🗒️ · `getCustomerData` 🔄 · `savecustomer` 🔄 · `deletecustomer` 🔄 · `ExportCustomerData` 📊

### Supplier *(4)*
`SupplierIndex` 🗒️ · `getSupplierData` 🔄 · `saveSupplier` 🔄 · `ExportSupplierData` 📊
⚠️ No delete action — suppliers can't be removed.

### Item *(4)*
`ItemIndex` 🗒️ · `getItemData` 🔄 · `saveItem` 🔄 · `ExportItemData` 📊
⚠️ No delete action.

### ItemStock *(6)*
`ItemStockIndex` 🗒️ · `getStockSummary` 🔄 · `getStockbreak` 🔄 (stock breakdown by batch)
· `saveItemStock` 🔄 · `delItemStock` 🔄 · `ExportItemStock` 📊

---

## 4. Quotations

⚠️ **Four controllers, one concept.** Item vs Service vs their Edit twins.
These collapse into **one module** with a line-type field.

### ItemQuotation *(9)*
`ItemQuotationIndex` 🗒️ · `saveIquote` 🔄 · `saveQcustomer` 🔄 · `addQitemtoCart` 🔄 ·
`removeQItem` 🔄 · `QitemcartLoad` 🔄 · `getQAllItemsStk` 🔄 · `getQCompanyData` 🔄 ·
`getprofitpercentage` 🔄

**`getprofitpercentage` is the auto-pricing rule**, and the only place it lives. It fetches the
customer's band from `cus_m.pro` → `profit_percent`, and the screen fills the rate with
`ceil(cost × (1 + band%))` — see STATUS §8. Not ported; blocked on item costs.

### Quotation (service) *(7)*
`QuotationIndex` 🗒️ · `savequote` 🔄 · `addtoCart` 🔄 · `removeQItem` 🔄 ·
`getCompanyData` 🔄 · `getCusData` 🔄 · `getQuoteContactP` 🔄

### EditItemQuotation *(8)* / EditServiceQuotation *(7)*
`updateIquote` 🔄 · `EaddQitemtoCart` 🔄 · `EremoveQItem` 🔄 · `EQitemcartLoad` 🔄 ·
`getEQuotationBr` 🔄 · `getEIQuotationData` 🔄 (+ service equivalents)

### SearchQuotation *(13)*
`SearchQuotationIndex` 🗒️ · `getQuotationData` 🔄 · `getQuotationBr` 🔄 ·
`updatequote` 🔄 · `getQuoteCompany` 🔄 ·
**`convertItemInvoice`** 🔄 · **`convertSerInvoice`** 🔄 ← *quote→invoice conversion, key business flow* ·
`setselectedqno` / `setselectedEditqno` / `setselectedqnoreturntype` 🔄 ⚠️ *session-based selection — must become URL params* ·
`generateQPDF` 📄 · `emailQPDF` ✉️ · `getQuoteemails` 🔄

**⚠️ The "cart" pattern:** line items are built up in **server session state**
(`addtoCart` / `removeQItem` / `cartLoad`) before saving. This cannot survive a
stateless API. In the rebuild the client holds the draft lines and posts the whole
document in one request. **This is the single biggest behavioural change in the migration.**

---

## 5. Invoices

Same four-way split as quotations.

### Invoice (item) *(9)*
`InvoiceIndex` 🗒️ · `saveItemInv` 🔄 · `saveIcustomer` 🔄 · `addinvitemtoCart` 🔄 ·
`removeINVItem` 🔄 · `invitemcartLoad` 🔄 · `getAllItemsStk` 🔄 · `getCompanyData` 🔄 ·
`getCustomerProfit` 🔄

⚠️ **`getCustomerProfit` does not price anything**, despite living here and the name suggesting it. It
returns all ten `profit_percent` bands to fill the band dropdown on the add-customer form. The item
invoice screen leaves the rate blank for hand entry — auto-pricing is on quotations only. STATUS §8.

### ServiceInvoice *(6)*
`ServiceInvoiceIndex` 🗒️ · `saveSerInvoice` 🔄 · `addSertoCart` 🔄 · `removeSIItem` 🔄 ·
`getSerInvTotal` 🔄 · **`creditlimitcheck`** 🔄 ← *credit-limit enforcement*

### EditItemInvoice *(7)* / EditServiceInvoice *(7)*
`saveItemInv` / `saveSerInv` 🔄 · `addinvitemtoCart` 🔄 · `EIitemcartLoad` 🔄 ·
`removeINVItem` 🔄 · `getEInvoiceBr` 🔄 · `getEIInvoiceData` 🔄

### SearchInvoice *(15 — the largest controller)*
`SearchInvoiceIndex` 🗒️ · `getInvoiceData` 🔄 · `getInvoiceBr` 🔄 · `getEInvoiceBr` 🔄 ·
`updateSerInv` 🔄 · `getInvCompany` 🔄 · `delinvoice` 🔄 · `EIaddtoCart` 🔄 ·
`removeESerINVItem` 🔄 · `ESerInvLoad` 🔄 · `setselectedIno` / `setselectedEditIno` 🔄 ⚠️ ·
`generateIPDF` 📄 · `emailIPDF` ✉️ · `getInvoiceemails` 🔄

### DeletedInvoices *(3)*
`DeletedInvoicesIndex` 🗒️ · `getDelInvoiceData` 🔄 · `getDInvoiceBr` 🔄
*(soft-delete audit trail — keep this)*

---

## 6. Credit Notes

### CNote *(5)*
`CNoteIndex` 🗒️ · `saveCN` 🔄 · `addCNitemtoCart` 🔄 · `removeCNItem` 🔄 ·
**`getInvoiceDetail`** 🔄 ← *CN is raised against an existing invoice*

### SearchCN *(6)*
`SearchCNIndex` 🗒️ · `getCNData` 🔄 · `getCNBr` 🔄 · `delCN` 🔄 ·
`setselectedCNno` 🔄 ⚠️ · `downloadCN` 📄

---

## 7. Purchase Orders & Supplier Invoices

### PO *(6)*
`POIndex` 🗒️ · `savePO` 🔄 · `POaddtoCart` 🔄 · `removePOItem` 🔄 ·
`getSupData` 🔄 · `getCompanyData` 🔄

### SearchPO *(13)*
`SearchPOIndex` 🗒️ · `getPOData` 🔄 · `getPOBr` 🔄 · `getEPOBr` 🔄 · `updatePO` 🔄 ·
`getPOCompany` 🔄 · `EPOaddtoCart` 🔄 · `EPOinvcartLoad` 🔄 · `removePOItem` 🔄 ·
`setselectedpono` 🔄 ⚠️ · `generatePOPDF` 📄 · `emailPOPDF` ✉️ · `getPOemails` 🔄

### SupplierInvoices *(10)*
`SupplierInvoicesIndex` 🗒️ · `getSupInvoice` 🔄 · `getPendingSupInvoice` 🔄 ·
`getPaidSupInvoice` 🔄 · `saveSupInv` 🔄 · `updateSupInv` 🔄 · `deleteSupInv` 🔄 ·
`setselectedsupinv` 🔄 ⚠️ · `getCompanyData` 🔄 · **`getVats`** 🔄 ← *reads the VAT table; becomes settings*

---

## 8. Job Cards

### JobCard *(6)*
`JobCardIndex` 🗒️ · `getJobData` 🔄 · `getJobCard` 🔄 · `saveJobCard` 🔄 ·
**`closeJob`** 🔄 ← *status workflow* · `setselectedJno` 🔄 ⚠️

---

## 9. Payments, Cheques & Expenses

### CusPayments *(5)*
`CusPaymentsIndex` 🗒️ · `getPaymentData` 🔄 · `savePay` 🔄 · `deletepay` 🔄 ·
**`getCusOutInv`** 🔄 ← *lists open invoices to settle against*

### Cheque *(5)*
`ChequeIndex` 🗒️ · `getCheques` 🔄 · `saveCheque` 🔄 · `deletecheque` 🔄 · `printcheque` 📄
⚠️ Cheque printing overlays **pre-printed stationery** — must stay pixel-exact.

### CExpenses *(6)*
`CExpensesIndex` 🗒️ · `getExp` 🔄 · `saveExpense` 🔄 · `deleteExpense` 🔄 ·
`getExpCat` 🔄 · `saveExpenseCat` 🔄 *(expense categories — mini master-data)*

---

## 10. Reports  *(read-only; low risk; good early wins)*

Each follows an identical shape: Index 🗒️ + `get…Data` 🔄 + `Export…` 📊

| Report | Actions |
|---|---|
| SalesReport | `getSalesDataSum`, `ExportSalesData` |
| CustomerSales | `getCSalesData`, `ExportCSalesData` |
| CusOutstanding *(9 — biggest)* | `getOCusData`, `getCusOutDetail`, `getCusOutSum`, `ExportCustomerOutstanding`, `getOsCusemails`, `setselectedOsCus` ⚠️, **`emailOS`** ✉️, **`emailOSBulk`** ✉️ ← *bulk dunning emails* |
| CustomerVATR | `getCVatData`, `ExportCVATData` |
| SupplierVATR | `getSVatData`, `ExportSVATData` |
| SupplierPSum | `getSPData`, `ExportSPData` |
| SupplierPaymentReport | `getSupPayments`, `ExportSPayData` |
| CExpensesReport | `getExpRep`, `ExportExpData` |
| JobCardsReport | `getJDataF`, `ExportJobData` |
| ChequeRpt | `getChequeData`, `ExportChkData` |

---

## 11. Dashboards

Three near-identical controllers (3 actions each): **AdminDashboard**,
**UserDashboard**, **CustomerDashboard** — each `getChartData` + `getCardData`.
⚠️ Consolidate into **one dashboard endpoint** whose content varies by role.

---

## 12. Documents & Notes

### DocStore *(5)*
`DocStoreIndex` 🗒️ · `getDocData` 🔄 · `uploaddoc` 🔄 · `deldoc` 🔄 · `generateDocPDF` 📄
⚠️ File upload — PDFs stored as **BLOBs in MySQL** (`docstore.pdfdoc`). Move to object storage.

### Notes *(3)*
`NotesIndex` 🗒️ · `getNote` 🔄 · `saveNote` 🔄

---

## 13. Web Catalogue  *(possibly dead code — confirm before porting)*

### WCategory *(3)* / WProducts *(5)*
`saveCategory` · `getWCatData` · `saveProduct` · `delProduct` · `getWProdData`
⚠️ Last modified **March 2024** — over two years older than everything else, and
unreferenced elsewhere. **Ask whether this is still used.** If not, don't migrate it.

### Home *(3)*
`Index` · `About` · `Contact` — MVC scaffold leftovers. Delete.

---

## Cross-cutting issues to resolve during the rebuild

1. **The session "cart"** — quotations, invoices, CNs and POs all build line items in
   server session before saving. A stateless API can't do this. Client holds the draft;
   post the document whole. Affects ~10 controllers.
2. **`setselected*` actions** (~8 of them) stash the current record's ID in session so the
   next page can read it. Replace with URL parameters. These are also an **IDOR risk**
   today — nothing checks the user is allowed to see that record.
3. **Item/Service/Edit duplication** — 4 controllers per document type collapse to 1.
   This is where the 248 actions shrink substantially.
4. **`getCompanyData` appears in 6 controllers** — six copies of the same query.
   Becomes one settings endpoint.
5. **No delete for Item or Supplier** — decide whether that's intentional.
6. **Excel exports (11 of them)** all use ClosedXML and port to .NET Core with little change.

---

## Suggested build order

1. Auth & Users **+ Settings** *(security fix + the config layer everything needs)*
2. Master data — Customer, Supplier, Item, ItemStock *(locks in CRUD patterns)*
3. Dashboards *(fast visible win)*
4. Reports + Excel exports *(read-only, low risk)*
5. Quotations → Invoices → Credit Notes *(the bulk; kill the cart pattern here)*
6. POs → Supplier Invoices → Job Cards
7. Payments, Cheques, Expenses, DocStore, Notes
8. **PDF/print templates** *(deferred — contracts already extracted)*
