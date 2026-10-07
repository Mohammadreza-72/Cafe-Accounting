# UI review

Review date: 2026-10-06. Baseline: v1.0.4. Scope: the Persian WPF desktop interface, first setup, cashier work, inventory work, and manager tasks.

Validation updated: 2026-10-07.

## Method

All three XAML files and their event handlers were reviewed. A separate QA host loaded the real application window and resources with synthetic products and a new database under `E:\CafeArian-qa`. It used the minimum window size of 1100 by 680 logical pixels. No employer data was entered into the UI host.

The Windows accessibility tree confirmed the dashboard, the F1 sales shortcut, the F3 customer shortcut, product stock labels, disabled-product explanations, and setting a synthetic customer name. Screenshot capture failed with `FrameArrived timed out` and `window capture timed out`; pointer input reported `coordinate input geometry is unavailable`. Tab focus did not produce a reliable observable change. Consequently, this review does **not** certify pixel appearance, complete mouse or keyboard workflows, high-DPI behavior, screen-reader speech, or task completion times. Source findings and runtime observations are distinguished below.

The business smoke suite and the disposable-backup field runner validate data behavior separately. Passing them is not evidence that the interface is easy for a new cashier.

The repeatable WPF runner in `Tests/Ui` loads the actual window, styles and event handlers in a separate process with a synthetic database on E:. It raises control events and measures layout in logical pixels; it does not drive the mouse, inspect rendered screenshots, or measure human task completion. The application and smoke projects exclude nested test files so copied XAML resources cannot break subsequent builds.

## Findings addressed

| ID | Priority | Evidence before change | User impact | Change |
| --- | --- | --- | --- | --- |
| U01 | High | App.xaml used white 14px text on `#AF7141`: calculated contrast 3.99:1. | Primary actions are harder to read. | Accent changed to `#8F552E`: 5.99:1 against white, 5.40:1 against the canvas. |
| U02 | Medium | Text input borders used the pale separator color. | Empty fields blend into white forms. | A separate input border color gives 3.64:1 contrast against white. |
| U03 | High | MainWindow showed product and purchase quick actions to cashiers, and a sale action to inventory staff. | A visible suggested action immediately produces a permission error. | Quick actions now follow the same roles as navigation. Service authorization remains in place. |
| U04 | Medium | The SettingsNav label was only “Backup”; the page also contains charges and printers. | Managers cannot predict where configuration lives. | The menu now says settings and backup. The catalog entry says products and warehouse, matching its purchase subpage. |
| U05 | High | Product tiles used a fixed-height plain string; long names did not wrap. | Similar items cannot be distinguished confidently. | Names wrap, price and quantity have separate lines, and the full name is available in the tooltip and accessible name. |
| U06 | Medium | Zero search results left an empty product area. | A cashier cannot tell whether there are no products or the search failed. | Separate messages explain an empty catalog and an unmatched query. |
| U07 | High | Expense, adjustment, category, and history search fields relied on hover tooltips. | New staff must guess what blank fields mean. | Visible labels added; input labels are also linked to WPF accessibility metadata. Login fields have explicit accessible names. |
| U08 | Medium | Read-only grids used small default styling and allowed multiple selection although commands operated on one row. | Dense rows and ambiguous selection increase mistakes. | Consistent text size, minimum row height, full-row single selection, alternating rows, and header sizing. |
| U09 | High | Purchase form had seven equal columns. Product and purchase forms could consume the entire constrained page height. | Controls become cramped and lower tables lose useful space. | Purchase fields use four columns. Catalog and purchasing have page scrolling and bounded tables. A full DPI pass remains pending. |
| U10 | Medium | Many selected-row commands returned silently when no row was selected. | Users interpret an inactive operation as broken. | Inline messages request a row or explain an already cancelled/empty item. |
| U11 | High | Purchases, expenses, stock adjustments, suppliers, accounts, and coupons cleared fields without a clear completion message. | Staff may repeat a successful action or doubt whether it saved. | A persistent page status reports completion; purchase-line status distinguishes adding a line from saving a purchase. |
| U12 | Medium | Routine invalid input was presented as an internal incident with an error ID and a modal dialog. | A fixable typing problem looks like a broken application. | Expected validation appears inline. Unexpected failures keep incident logging and the diagnostic dialog. |
| U13 | High | Closing the window discarded pending cart and purchase lines without a warning. | Staff lose unsubmitted work. | Closing warns when either draft contains lines; “No” is the default. Unsaved product/recipe/settings fields are not yet tracked. |
| U14 | High | Destructive Yes/No confirmations did not explicitly select the safe default. | Pressing Enter can accept an unintended destructive action. | Cancellation, deactivation, batch disposal, restore, and closing use “No” as the default. |
| U15 | Medium | Saving a batch-tracked product instructed every user to create a recipe. | A bought-in dated product appears to require production steps. | Guidance now distinguishes direct batch entry from recipe-based production. |
| U16 | Medium | Printer width used `int.TryParse` directly, unlike monetary and quantity fields. | Persian digits accepted elsewhere are rejected in this form. | Widths use the existing digit normalization before integer validation. |

## Page coverage

| Page | Review result and next concern |
| --- | --- |
| First setup and login | Clear initial-account distinction and field-level errors exist. Added accessible names. Password recovery and switching users still lack a dedicated workflow. Authentication dialogs were not automated. |
| Dashboard | Checked through the live accessibility tree. Role-inappropriate quick actions fixed. Long chart values, crowded header, and low-stock names still need visual measurement. |
| Quick sale | F1 observed at runtime. Fixed long product names and empty search feedback; v1.0.4 already keeps checkout outside the payment scroller. Card-only payment still requires typing the full amount; barcode-not-found feedback and category filtering would reduce friction. |
| Catalog | Product type, stock addition and automatic identifiers are documented. Page now scrolls, and the name column has a readable minimum. Large catalogs still need a search/filter bar. The form should disable stock entry for types that derive stock from recipes/batches. |
| Recipes | Formula is per one sold unit; quantity must use the ingredient's stored unit. Save feedback added. Selecting a recipe row does not fill the editor, and unit conversion remains manual. |
| Batches and expiry | Dates, production option, quantity and cost are present. Guidance fixed. The word “batch” still needs a novice-friendly explanation and the production/direct-entry modes should expose only relevant fields. |
| Purchasing and warehouse | Four-column form, explicit adjustment labels, success feedback, line total status, and scroll access added. Purchase-line correction still requires removal and re-entry. Supplier creation requires leaving the form, though the draft lines remain in memory. |
| Suppliers | Fields have visible labels and the saved supplier is selected for purchasing. There is no edit-existing-supplier flow; deactivation is the only row action. |
| Customers | F3 and setting a synthetic name observed through the live accessibility tree. The WPF runner verifies invalid-phone recovery and saving a Persian phone through the real form event. Registration/search are clearly separated. Invalid-mobile feedback now uses an error color. Mouse-driven registration and history browsing remain unverified. |
| Discounts | Fields identify amount versus percentage and optional limits/dates. Completion feedback and safe deactivation default added. Editing existing codes and date-format clarity remain gaps. |
| Invoices | Search now has a visible label and its toolbar wraps. No-selection and already-cancelled feedback improved. Status values are bound directly from the model; localization and an invoice-detail view need follow-up. |
| Finance and reports | Expense fields now have visible labels and save feedback. Reports still lack a date-range selector, export completion feedback is inconsistent, and report generation runs synchronously on the UI thread. |
| Bank accounts and terminals | Clear form fields and save feedback. Two columns may be narrow on small screens; account editing is absent. Payment controls should hide irrelevant bank selection for cash. |
| Users | Role and password requirements are visible; add feedback and safe deactivation default added. Reset-password form reuses the new-user password field, which can confuse a manager; a dedicated dialog would be clearer. Security settings were not changed through UI automation. |
| Settings and backup | Menu label now reveals its broader purpose. Persian printer widths accepted. Charge input should change its label with percentage/fixed mode. Database location has no user-facing move workflow. |
| Diagnostics | Checks, advice, errors and detail areas exist. Form-validation mistakes no longer flood the incident view. At minimum height the two grids plus detail panel still need a visual density pass. |

## Remaining priorities

1. **Small screens and DPI:** 1100 by 680 is the enforced logical minimum. A physical 1366 by 768 screen at 125% scaling offers roughly 1093 by 614 logical pixels before other desktop space. A responsive sidebar and page layouts are still needed; lowering the minimum alone would hide content.
2. **Faster tender entry:** offer “all cash / all card / all transfer”, then expose split payment as an option. Keep the final amount visible and validate the tender sum before committing.
3. **Dates and numbers:** display culture currently depends on Windows. The live QA host showed Gregorian dates. Define a consistent Persian display policy while preserving invariant database dates, and verify DatePicker behavior explicitly.
4. **Edit and recover:** add explicit edit modes for supplier/account/customer details, recipe-row prefill, and purchase-line correction. Protect unsaved forms when changing selection or leaving a page.
5. **Cafe service model:** unpaid orders, table assignment, kitchen/bar preparation state, shift close and partial returns remain product workflow gaps. Their absence matters more than cosmetic polish for table-service cafes.
6. **Human acceptance:** observe one new cashier and one owner performing the tasks below. Record completion, assistance, wrong selections, recovery and elapsed time; no such measurements were invented in this review.

## Automated checks

The 2026-10-07 run passed the business smoke suite, all 29 disposable-backup checks, and all 16 WPF control checks. The original employer backup had the same SHA-256 before and after testing. Private databases remain outside Git.

The WPF checks cover role-aware dashboard actions, accessible labels, long product names, cart totals, fixed checkout placement at 1100 by 680 and 1360 by 820 logical pixels, empty search feedback, invalid and Persian customer phones, missing selection, batch guidance, purchase validation, diagnostic noise, and Persian printer widths. These are control-event integration tests. Close-confirmation interaction, actual printer output, full keyboard/mouse paths and visual DPI acceptance remain pending.

Run sequentially from the repository root on Windows with .NET 10. Keep temporary files on E: when C: has limited space:

```powershell
$env:TEMP = Join-Path (Get-Location) 'artifacts/test-temp'
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Force -Path $env:TEMP | Out-Null
dotnet run --project Tests/CafeArian.Smoke.csproj -c Release
dotnet run --project Tests/Ui/CafeArian.Ui.csproj -c Release -- artifacts/ui-tests
dotnet run --project Tests/Field/CafeArian.Field.csproj -c Release -- 'E:\path\source-backup.db' artifacts/field-runs
dotnet build CafeArian.csproj -c Release --no-restore
git diff --check
```

The field runner expects the supplied four-product backup, copies it before testing, and never edits the source. Do not substitute a working production database. The UI runner uses only generated accounts and synthetic data.

## Acceptance tasks

- At 1366 by 768 and 125% scaling: enter the application, reach every sidebar item, read every primary action, and verify no field or confirmation is clipped.
- Sell two similarly named products, increase/decrease quantity, scan an unknown barcode, apply an invalid discount, recover, and complete card-only and split payments.
- Register an invalid then valid customer phone; find that customer and its invoices; repeat the phone without creating a duplicate.
- Create a packaged item with opening stock; create a prepared item from two ingredients using a fractional quantity; explain recorded versus sellable stock without help.
- Receive a two-line supplier purchase, correct a quantity before saving, settle part of its debt, and find the remaining balance.
- Enter a dated batch, distinguish bought-in versus produced stock, and locate expired stock without accidentally selling it.
- Leave a cart and a purchase draft open, try to close, choose No, and verify the draft is still available.
- As cashier and inventory roles, check that suggested navigation only leads to allowed tasks.
- Find printer and charge settings from the sidebar; enter Persian digits; make a backup and locate the confirmation.

## References

The contrast calculation uses the [W3C relative-luminance method and 4.5:1 normal-text benchmark](https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum.html). This is a design benchmark, not a claim of full WCAG conformance for this native application. [Visible labels](https://www.w3.org/WAI/WCAG22/Understanding/labels-or-instructions.html) and [Microsoft dialog guidance](https://learn.microsoft.com/en-us/windows/win32/uxguide/win-dialog-box) informed form and confirmation changes. The prior [workflow comparison](workflow-review.md) covers Odoo, ERPNext and Restro.
