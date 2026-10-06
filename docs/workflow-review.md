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
