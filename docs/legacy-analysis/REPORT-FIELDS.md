# Report Field Contracts

Extracted mechanically from the twelve `.rpt` files via the Crystal SDK
(`_migration-tools/RptDump.cs` → `_migration-tools/rpt-dump.json`).
This is the complete input contract for rebuilding the documents — nothing else
is in those files. **Zero formulas exist in any report**: every value is either a
pushed parameter, a detail-table column, or static text.

Geometry note: the reports are **Letter (8.5in / 612pt)**, not A4.

---

## The `_SN` / `_ST` split is about VAT, not branding

| | `_SN` | `_ST` |
|---|---|---|
| Entity | Smart Net (PVT) LTD | Smart Technologies |
| VAT registered | **Yes** — carries `vat`, `vatper`, `invtotal`, `vatno` | **No** — those parameters are absent entirely |
| Payment block | — | Bank/cheque details (Sampath Bank – Kohuwala) |

So this is **one template per document** plus a company profile carrying
`is_vat_registered`. It is *not* twelve designs.

---

## Shared header fields (present on nearly every document)

| Field | Meaning |
|---|---|
| `date` | Document date |
| `qno` | Document number (invoice / quotation / CN / PO no — reused for all) |
| `client` | Customer or supplier name |
| `address` | Their address |
| `contactP` | Contact person |
| `preparedby` | Logged-in user's name |
| `pono` | Customer's PO reference |

## Shared money fields

| Field | Meaning |
|---|---|
| `tot` | Subtotal (before discount/VAT) |
| `idisc` / `discountper` | Discount percent |
| `totafterdisc` / `totad` | Total after discount |
| `vatper` | VAT label string, e.g. `"VAT(5%)"` — **a display string, not a rate** |
| `vat` | VAT amount |
| `invtotal` | Grand total including VAT |
| `paid` | Amount paid |
| `balance` | Balance due |
| `vatno` | Customer VAT/TRN, pre-formatted as `"Bill To : VAT Number - …"` |

⚠️ All parameters are **pre-formatted strings** (`StringField`), including money.
The C# controller does the formatting. In the rebuild these should be typed
(`decimal`, `DateOnly`) and formatted at render time.

---

## Per-document contracts

### Invoice — `Invoice_SN` / `Invoice_SN_TAX` / `Invoice_ST`

**Detail columns (5):** `Item No`, `Description`, `Quantity`, `Rate`, `Total`

| Variant | Parameters |
|---|---|
| `Invoice_SN` (16) | date, qno, client, address, contactP, tot, preparedby, pono, paid, balance, vat, invtotal, vatper, vatno, idisc, totafterdisc |
| `Invoice_SN_TAX` (17) | *as above* + `custelephone` |
| `Invoice_ST` (12) | date, qno, client, address, contactP, tot, preparedby, pono, paid, balance, idisc, totafterdisc *(no VAT fields)* |

**Static labels:** TAX INVOICE / INVOICE · Date · Invoice No · Client · Address ·
Contact Person · PO Number · Prepared By · Signature · Goods Received By · NIC ·
Authorized Signature

`Invoice_SN_TAX` additionally hardcodes the **supplier block** — Supplier's TIN,
Supplier's Name, Address, Telephone No, Date of Supply, and the purchaser
equivalents. These become company settings.

---

### Quotation — `Quotation_SN` / `Quotation_ST`

**Detail columns (5):** `Item No`, `Description`, `Quantity`, `Rate`, `Total`

| Variant | Parameters |
|---|---|
| `Quotation_SN` (13) | date, qno, client, address, contactP, tot, preparedby, vat, vatper, invtotal, **qvalidity**, **discountper**, **totad** |
| `Quotation_ST` (10) | date, qno, client, address, contactP, tot, preparedby, qvalidity, discountper, totad |

**Unique field:** `qvalidity` — quotation validity period.
**Static:** QUOTATION · Quotation No · Bill To · (ST also carries the bank block)

---

### Credit Note — `CN_SN` / `CN_ST`

**Detail columns (5):** `Item No`, `Description`, `Quantity`, `Rate`, `Total`

| Variant | Parameters |
|---|---|
| `CN_SN` (12) | date, qno, client, address, contactP, tot, preparedby, pono, vat, invtotal, vatper, vatno |
| `CN_ST` (8) | date, qno, client, address, contactP, tot, preparedby, pono |

**Static:** CREDIT NOTE · Credit Note No · (no paid/balance — a CN has no payment block)

---

### Purchase Order — `PO_SN` / `PO_ST`

**Detail columns (5):** `Item No`, `Description`, `Quantity`, `Rate`, `Total`

| Variant | Parameters |
|---|---|
| `PO_SN` (10) | date, qno, client, address, contactP, tot, preparedby, vat, vatper, invtotal |
| `PO_ST` (7) | date, qno, client, address, contactP, tot, preparedby |

Note `client` here holds the **supplier** name (label reads "Supplier :").
**Static:** PURCHASE ORDER · PO No · Supplier

---

### Job Sheet — `Job_SN` / `Job_ST`

**Detail columns (3):** `ItemDesc`, `Qty`, `Serial`  ← *different from the others*

| Variant | Parameters |
|---|---|
| `Job_SN` (8) | date, **jno**, client, contactP, preparedby, totafterdisc, **faultdesc**, **remarks** |
| `Job_ST` (8) | date, jno, client, address, contactP, preparedby, faultdesc, remarks |

**Unique fields:** `jno` (job number), `faultdesc` (fault description), `remarks`,
and a `Serial` column on the line items.
**Static:** JOB SHEET · Job No · Fault Description · Remarks · Customer Details ·
Goods Collected By · Customer Signature · NIC

⚠️ `Job_SN` has **no `address`** parameter while `Job_ST` does — likely an
oversight in the original. Include it in the rebuild.

---

### Cheque — `cheque.rpt`

**No parameters.** Everything arrives as detail-table columns (9):

| Column | Meaning |
|---|---|
| `Pay` | Payee name |
| `Amount` | Amount in figures |
| `AmountText` | Amount in words (generated by `AmountToText.cs`) |
| `DayDigitOne`, `DayDigitTwo` | Date day, split into individual digits |
| `MonthDigitOne`, `MonthDigitTwo` | Month digits |
| `YearDigitOne`, `YearDigitTwo` | Year digits |

The date is split per-digit because it prints into the boxed date grid on a
pre-printed cheque. **This one must stay pixel-accurate** — it overlays physical
stationery, so it's the single report where "modernize" is the wrong instinct.
Measure a real cheque before rebuilding.

---

## Images

Every report except `cheque.rpt` embeds **2 images**: a letterhead across the top
and a footer/signature graphic. These become `companies.logo_url` and
`companies.stamp_url` — they are not recoverable from the `.rpt` binary and will
need re-supplying as image files.

---

## Things to fix in the rebuild rather than reproduce

1. **`vatper` is a formatted string** (`"VAT(5%)"`), so an invoice structurally
   cannot express mixed VAT rates. If a zero-rated or exempt line ever appears
   alongside a standard-rated one, today's document is wrong. Rebuild with a
   per-line tax rate and a grouped VAT summary.
2. **`vatno` ships pre-decorated** as `"Bill To : VAT Number - …"` — presentation
   baked into data. Pass the raw number.
3. **Money arrives as strings**, so rounding decisions are already frozen upstream
   and invisible to the template.
4. **Company identity is hardcoded** inside `Invoice_SN_TAX` (name, TIN, address,
   phone) and the bank block inside the `_ST` files. → `companies` table.
5. **Reports are Letter, not A4.** Decide deliberately which you want.
