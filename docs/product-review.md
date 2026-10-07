# Product review

Date: 2026-10-07. Reviewed commit: `b95e390a25d28bbcad8572e47653e6bf58549af4` on `main`, after PR #7.

Implementation follow-up: [Workflow fixes](workflow-fixes.md) records the corrections and their validation. The findings below describe the original reviewed commit; source line numbers refer to that baseline.

## Assessment

The app has a useful base for one offline register: Persian RTL forms, stock types, recipes, purchase debt, customer lookup, role checks, backups, and transaction rollback. It still has reproducible faults in stock entry, fractional consumption, and valuation. Correct these before calling the stock and profit workflow reliable for general cafe use.

The recent UI changes improve labels, feedback and checkout visibility. Daily payment work still requires too much scrolling, and the minimum-size cart cannot distinguish some similar product names. Table service and shift reconciliation remain product gaps.

## Evidence

This is a new scenario review, not a repetition of the earlier passing smoke results. An isolated runner called the current services and real WPF controls with generated accounts and synthetic databases on E:. No employer database was opened or changed in this review.

- Eight targeted logic probes reproduced the observations L01-L08 below.
- Real control events reproduced combined-recipe overcommit and cash autofill.
- WPF rendered sales at 1100x680 and 1360x820, plus catalog, recipes, purchases, finance, accounts, settings and diagnostics at 1100x680 logical pixels. The visible content of seven pages was inspected. Diagnostics rendering was captured during asynchronous loading and is not counted as completed visual validation.
- An attempted 1093x614 window was forced to 1100x680 by its minimum size.
- The previous smoke, 29 field and 16 UI checks remain useful baseline coverage. They did not cover these combined cases. They were not all rerun for this review.

The render host is not the installed executable and does not validate physical DPI, native window chrome, mouse/keyboard completion, printer output or human task times. Rendering the child content alone initially produced a mirrored RTL artifact; only the corrected whole-window renders were inspected as evidence. An initial async test-host failure was resolved by installing a WPF dispatcher synchronization context; it is not reported as a production defect.

## High priority logic findings

### L08: Fractional stock rejects a valid final portion

**Reproduced.** Create 0.3 units of an ingredient and a recipe consuming 0.1 units. Sell three portions individually. The first two succeed; the third reports insufficient stock, while the catalog still displays one sellable portion and 0.10 ingredient stock.

The database receives binary floating-point quantities, while stock checks use exact `Quantity >= $qty`. Display calculations convert these values back to decimal. See `Services/SaleService.cs:283-285` and `Services/ProductService.cs:77`.

**Cafe impact:** milk, coffee and syrup recipes can fail despite apparently sufficient stock. **Fix:** define canonical units and quantity precision; use consistent exact storage or validated quantization for all comparisons and movements. Preserve the no-overselling guarantee. Add sequences using 0.1, 0.2 and mixed recipes, with sale, production and cancellation.

### L01: A second stock addition fails and is blamed on the barcode

**Reproduced.** Create an ordinary product with one opening unit and a nonzero cost. Edit it and add another unit through the same stock-entry field. The operation fails with SQLite error 19 on `JournalEntries.ReferenceType, ReferenceId, EntryType`; the transaction rolls back and stock stays at one.

`ProductService.Save` uses the product ID as the unique reference for every `ProductOpening` journal (`Services/ProductService.cs:294`; `Data/Database.cs:279`). The form maps every SQLite constraint failure to a duplicate-barcode message (`MainWindow.xaml.cs:641`), sending the user to the wrong field.

**Fix:** give each stock-entry operation its own stable reference and preserve retries safely. Distinguish opening balance, purchased receipt and correction in the form; only diagnose barcode conflicts as barcode errors. Test a second entry and a no-stock edit separately.

### L02: Cancellation restores quantity with the wrong average cost

**Reproduced.** Start with one unit costing 100 tomans; sell it. Receive one unit costing 300, then cancel the original sale. Stock becomes two units at average cost 300, giving inventory value 600. The inventory-account journal delta is 400. The cancellation movement records unit cost zero.

`CancelSale` restores quantities without the original movement cost or a weighted average update (`Services/SaleService.cs:325-353`). The journal reversal restores the original cost, so the two records disagree.

**Cafe impact:** later cost of sales and estimated profit can be wrong even though every journal balances. **Fix:** restore original per-movement costs and recompute the stock valuation consistently. Add inventory-value reconciliation, not only debit/credit and quantity checks. Cover ordinary stock, recipe ingredients and batches separately.

### L03: Changing a unit silently changes the meaning of existing stock

**Reproduced.** Create one liter of milk and a 0.25-liter recipe. Edit the ingredient unit to milliliter. The system accepts it, retains stock `1` and recipe quantity `0.25`, and still claims four portions.

`UnitName` is freely updated with no conversion or history check (`Services/ProductService.cs:213`). **Fix:** prevent changing the base unit after stock/recipe use, or require an explicit conversion that updates dependent quantities and costs with an audit record. A label edit cannot represent liters-to-milliliters conversion safely.

## Other logic and report findings

| ID | Priority | Evidence | User impact and correction |
| --- | --- | --- | --- |
| L04 | Medium | Calling the actual PDF numeric formatter with `0.25` returns `0`; `Services/ReportService.cs:119-123` formats all numbers as `N0`. | Fractional stock appears absent in PDF. Use quantity precision per column and keep integer money formatting separate. Excel retains numeric fractions. |
| L05 | Medium | After cancelling the only sale, the payment export still has its positive payment row and no status/refund column; `Services/ReportService.cs:155-156`. | A user summing the payment sheet cannot reconcile it with completed sales. Keep original payments traceable, but show cancellation/refund state and an explicit net amount. |
| L06 | Medium | Saving the same supplier invoice `duplicate-001` twice creates two purchases through `RecordPurchase`. | Accidental re-entry doubles stock and debt. Warn for repeated nonblank supplier/document references and provide a deliberate override for legitimate reuse. Test UI retries independently of this service-level reproduction. |
| L07 | Medium | A prepared drink with four portions available exports inventory quantity `0`; `Services/ReportService.cs:151-152` reads raw inventory. | The generic quantity column repeats the owner's zero-stock confusion. Export product type, unit, recorded quantity and sellable quantity separately; apply the same expiry rules as the app. |

## UI and UX findings

| ID | Priority | Evidence | Recommended behavior |
| --- | --- | --- | --- |
| U01 | Medium | Two drinks each need the same last unit of an ingredient. Both can be added to the actual cart; checkout rejects the combination. `MainWindow.xaml.cs:323-326` only groups the same finished product. | Compute availability against combined ingredient demand in the current cart and identify the affected lines before payment. The final transaction correctly rejects overselling and must keep that protection. |
| U02 | High for cashier workflow | In both sales renders, payment amount fields sit below discount, coupon, charges and customer details. At 1100x680 even customer entry is below the visible payment area. A 100,000 order with 40,000 card autofills 60,000 cash. | Put cash/card/transfer selection and paid total above optional details. Offer full-amount tender actions and an explicit split-payment mode. Require the cashier to see the split before committing. Cash autofill itself is current policy, not proof of an incorrect transaction. |
| U03 | Medium | The minimum 1100x680 exceeds roughly 1093x614 logical pixels on a 1366x768 display at 125%, before taskbar/chrome. Attempting the smaller logical size was coerced to the minimum. `MainWindow.xaml:5`. | Add a compact sidebar and responsive layouts, then validate on real 125%/150% displays. Lowering the minimum alone is insufficient. |
| U04 | Medium | At 1100x680, cart lines for two drinks whose names differ at the end look the same because the product column clips them. Tiles show full names, but the cart has no equivalent detail affordance. `MainWindow.xaml:194`. | Wrap the cart name, provide a full-name tooltip/detail panel and include distinguishing SKU/variant details. Verify two similar products can be identified before removal or quantity edits. |
| U05 | Medium | At minimum height, the product form takes almost the whole page and only roughly one table row is initially visible. Selecting stock and returning to form fields requires scrolling. | Use a searchable list with a separate editor panel or compact edit dialog. Keep name, type, price and stock task prominent; place identifiers, labels and category administration in secondary controls. |

Other source-confirmed friction remains: recipe-row selection does not prefill its editor; supplier/account editing has no dedicated path; date display depends on Windows culture; raw invoice status values remain English; reports have no date-range selector. These are follow-up items, not newly measured human usability failures.

## Cafe workflow fit

| Staff need | Current fit | Required next step |
| --- | --- | --- |
| Sell packaged items at one register | Basic flow exists; L01 affects replenishment through the catalog. | Separate receipt/purchase from product editing while keeping the same workspace. |
| Prepare drinks from ingredients | Recipe consumption exists; L08 and U01 affect service under load. | Fix precision and cart-level ingredient availability, then add base-unit guidance. |
| Receive dated stock from a supplier | Direct batch entry records stock; multi-line purchase only accepts types 1 and 2. Batch receipts use a separate accounting reference. | Design one receipt that links supplier, payment/debt, batches and expiry without entering stock twice. |
| Correct a sale after preparation | Cancellation reverses the whole sale and restores ingredients; physical refund is manual. | Separate void-before-preparation from return-after-preparation, decide restock versus waste, and link refund tender to the original sale. Prepared milk/coffee cannot automatically be assumed reusable. |
| Keep a table order open | No persisted unpaid order/table/kitchen workflow. | Add only if table service is required: saved order, preparation state, table transfer and payment. |
| Hand over a shift | No opening drawer, counted closing cash or discrepancy record. | Add cashier sessions with expected versus counted tender totals and an owner review. |
| Understand profit and stock | Basic reports exist; L02/L04/L05/L07 reduce consistency. | Fix source calculations and labels before adding more charts. |

For comparison, [Odoo's restaurant workflow](https://www.odoo.com/documentation/19.0/applications/sales/point_of_sale/restaurant.html) documents floors, tables and order handling; its [POS workflow](https://www.odoo.com/documentation/19.0/applications/sales/point_of_sale/use.html) covers register operations. [ERPNext stock reconciliation](https://docs.frappe.io/erpnext/stock-reconciliation) explicitly separates opening-stock and reconciliation purposes and includes quantity and valuation. These are workflow references; they do not determine this cafe's return, tax or stock policies.

## Delivery order

1. Fix L08, L01, L02 and L03 with regression cases and safe existing-data handling. Add a quantity-and-value consistency check to diagnostics.
2. Reconcile exported quantities, cancelled payments and supplier invoice re-entry. Preserve historical records.
3. Simplify checkout: tender first, optional details second; handle shared ingredients and similar names before payment.
4. Validate small-screen layouts, keyboard routes and Persian dates using the installed executable. Observe a cashier completing a sale and an owner receiving stock, without coaching; record mistakes and recovery rather than inventing a usability score.
5. Define shift close, dated purchases and returns, then table service if needed.

## Local reproduction

The review harness is saved at `E:/CafeArian-qa/review-20261007/Review.csproj` with its source beside it. It creates a fresh synthetic database per run. It intentionally records current failures; a successful runner exit does not mean the product is defect-free.

```powershell
$env:TEMP = 'E:/project/Cafe-Accounting-ariyan/artifacts/test-temp'
$env:TMP = $env:TEMP
dotnet run --project E:/CafeArian-qa/review-20261007/Review.csproj -c Release -- --logic-only
dotnet run --project E:/CafeArian-qa/review-20261007/Review.csproj -c Release
```

Local logs and corrected renders are retained under `artifacts/product-review/` (ignored by Git). This review adds documentation; it does not claim the findings are fixed or publish a new build.
