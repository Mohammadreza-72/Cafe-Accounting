# Workflow review

This review checks the single-register, offline cafe workflow against the supplied project brief, service behavior, smoke scenarios, and current restaurant POS references. It separates defects from features that need a business decision.

## Tested flows

| Staff task | Expected behavior | Evidence and result |
| --- | --- | --- |
| Create a packaged drink | Save the item and initial stock together; sell it without a production batch. | Smoke test converts an unstocked batch item to ordinary stock, enters six units, checks the stock movement and balanced journal, and rejects a later conversion while stock exists. |
| Create a prepared drink | Buy ingredients, save a recipe, show the number that can be made, consume ingredients on sale, restore them on cancellation. | Smoke test covers ingredient purchases, recipe availability, cost, insufficient stock rollback, sale, and cancellation. Product form now links directly to recipe setup after save. |
| Create an expiring item | Require a dated batch; prefer the earliest valid expiry; reject expired stock. | Smoke test covers batch registration, FEFO sale, expiry exclusion, waste, and cancellation. |
| Register a customer | Accept common Persian mobile formats; show confirmation; avoid losing the name on a repeated save. | Smoke test registers a formatted number before a sale, saves a name, preserves it on blank-name upsert, and verifies sale totals. |
| Receive a purchase | Add stock once, maintain average cost, supplier debt and payment records. | Existing smoke tests cover multi-item purchase, settlement, role checks, and ledger balances. |
| Diagnose a zero-stock report | Identify missing batches, ordinary items with zero stock, and stock movement mismatches without changing data. | Diagnostics now surfaces each condition separately and points to the corrective workflow. |

The supplied employer backup was inspected read-only. Its inventory quantities and purchase/adjustment movements are all zero. The affected packaged drinks are configured as batch items without any batches. This explains the zero sellable quantity; changing the product type alone would not create stock. The original backup is not changed or published.

The field runner adds 29 checks against a disposable copy of that backup. It covers the failure before the fix, corrected stock entry, sales and cancellation, supplier debt, recipe consumption, charges, discount usage, roles, ledger balance, diagnostics and restore. Supply an E-drive work directory when testing on a space-constrained machine:

```powershell
dotnet run --project Tests/Field/CafeArian.Field.csproj -c Release -- "E:\private\backup.db" "E:\test-output"
```

The backup is not stored in this repository. Each run creates a new subfolder in the selected output directory and keeps its test copy and result backup there for local inspection.

## Usability review

The supplied employer screenshots and the WPF layout were reviewed for a cashier working in a short window and an owner setting up stock. The native window could not be controlled in this session, so these are layout and workflow findings, not a claim that a person completed a click-through session.

| Task | Observed friction | Change in this branch |
| --- | --- | --- |
| Close a sale | The checkout button sat at the bottom of a nested payment scroller and disappeared in a short window. | Keep the final total and checkout button in a fixed footer; scroll payment details separately. |
| Find why an item cannot be sold | A zero-stock product tile was disabled without an explanation. | Show a reason on the disabled tile for simple, prepared, and dated stock. |
| Add or inspect a product | Nine input columns compressed prices and product type; sellable stock appeared late in the table. | Use two readable form rows, put item type beside its name, and move sellable and recorded stock beside the name in the table. |
| Register a customer | Save fields and search shared an unlabeled row. | Label name, phone, and search visibly; separate registration from lookup. |
| Find the right stock action | Two navigation entries both said stock. | Name them "Define products" and "Purchases and warehouse" in Persian. |

A short-window acceptance pass should still be performed on the built installer by a cashier: add two items, enter a phone number and split payment, reach the fixed checkout button without scrolling, then locate the new customer. An owner should create a packaged drink with opening stock, a prepared drink with a recipe, and compare recorded stock with sellable stock. Observe time, mistaken clicks, error recovery, and whether the next action is clear. These human measures cannot be inferred from service tests.

## Comparison and next decisions

Odoo's [restaurant workflow](https://www.odoo.com/documentation/19.0/applications/sales/point_of_sale/restaurant.html) includes open table orders, kitchen or bar notifications, split bills and eat-in/takeaway tax choices. Its [daily POS workflow](https://www.odoo.com/documentation/19.0/applications/sales/point_of_sale/use.html) covers register opening/closing and returns. The open-source [Restro project](https://github.com/elitale/restro) also combines recipes, stock movements and a kitchen display. These are comparison points, not proof that every cafe needs every feature.

| Priority | Gap in this app | Cafe impact | Next step |
| --- | --- | --- | --- |
| High if tables are served | A sale is committed only at payment; there is no saved open order, table assignment or kitchen ticket. | Staff cannot hold and update an unpaid table order or send a preparation list. | Confirm whether this cafe serves tables. If yes, design draft orders and kitchen state before adding UI. |
| High for shift handover | No opening cash count, shift close count or discrepancy record. | A manager cannot reconcile the cash drawer against each cashier's shift. | Add shift sessions and explicit cash counts, keeping financial journal history immutable. |
| High for post-sale correction | Cancellation reverses the whole invoice, and physical refunds remain manual. | A single returned item needs a new sale and manual money reconciliation. | Define partial return, refund tender, and original-invoice linkage. |
| Medium | Units have labels but no conversion between purchase and recipe units. | Buying milk by liter and consuming it by milliliter requires manual conversion. | Add canonical stock units and validated conversion factors before changing existing quantities. |
| Medium | The prepared-item recipe is a separate save after the product. | Staff may create a product that still has zero available portions. | Keep the direct recipe action and diagnostic warning; consider an integrated editor if staff regularly create recipes. |
| Hardware dependent | Printing uses the Windows driver and configurable paper widths; no printer model is selected yet. | Exact receipt and label alignment remains unverified. | Calibrate with the actual printer models when available. |

The present release fixes the reported catalog, stock and customer path. Table orders, shift reconciliation, partial returns and unit conversions require data-model and acceptance decisions before implementation. Do not infer their financial behavior from a competitor's interface.
