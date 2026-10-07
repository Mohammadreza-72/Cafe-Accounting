# Workflow fixes

Corrections from the [product review](product-review.md), implemented on 2026-10-07.

## Changes

| Findings | Correction |
| --- | --- |
| L01 | Each positive catalog stock entry uses its own movement ID for the journal. Only a barcode constraint is reported as a barcode conflict. Product edits without stock do not add another entry. |
| L02 | Cancellation restores the original movement cost and recalculates the weighted average. Batch sale, return and disposal recalculate the value of remaining batches. Repeated disposal after a return uses a new movement reference. |
| L03 | A used base unit cannot be relabeled. Unused products can still correct their unit. Unit conversion requires a separate product. |
| L04, L07 | Inventory exports distinguish recorded stock from sellable portions and include type and unit. PDF quantity columns retain fractional digits. |
| L05 | Payment exports retain the original amount and show cancellation status and accounting net. This does not record or perform a physical refund. |
| L06 | Repeated nonblank supplier invoice references require an explicit confirmation. Matching normalizes Persian/Arabic digits and case. Accepted duplicates have an audit record. |
| L08 | Stock operations validate six decimal places and use the same precision in SQL writes and comparisons. Unsupported new precision is rejected. Old finer stock is blocked with guidance before a write. |
| U01 | Cart validation aggregates shared ingredient demand before adding a drink and before checkout. Transactional stock checks remain in place. |
| U02 | Full cash, card and transfer modes fill the total automatically. Split tender is explicit, the payment summary stays beside checkout, and optional details are collapsed. F4 expands the discount section. |
| U03, U04 | The sidebar and brand panel shrink on smaller windows. Cart names wrap and have full-name tooltips. Payment selection and checkout are checked at four logical window sizes. |
| U05 | A searchable catalog fills the page. Selecting a product opens a separate editor. Returning to the list allows the same product to be opened again. Derived stock entry is disabled. |

Selecting a recipe row also fills its ingredient and quantity fields. Diagnostics now compare stock value with movement value and flag legacy precision. Historical quantities, journals and cancellation costs are not rewritten automatically.

## Validation

All runners use disposable databases and temporary files on E:. The field runner copies the supplied employer backup and verifies that the original file is unchanged.

Verified results on 2026-10-07:

- Smoke suite: passed, including Persian culture, reports, backups and migrations.
- Workflow suite: 33 checks passed.
- WPF UI suite: 33 checks passed.
- Employer backup field suite: 29 checks passed; original file unchanged.
- Release configuration build and whitespace check: passed.

```powershell
$env:TEMP = 'E:/project/Cafe-Accounting-ariyan/artifacts/test-temp'
$env:TMP = $env:TEMP
dotnet run --project Tests/CafeArian.Smoke.csproj -c Release
dotnet run --project Tests/Workflow/CafeArian.Workflow.csproj -c Release -- E:/project/Cafe-Accounting-ariyan/artifacts/workflow-tests
dotnet run --project Tests/Ui/CafeArian.Ui.csproj -c Release -- E:/project/Cafe-Accounting-ariyan/artifacts/ui-tests
dotnet run --project Tests/Field/CafeArian.Field.csproj -c Release -- '<source-backup.db>' E:/project/Cafe-Accounting-ariyan/artifacts/field-tests
```

Workflow checks cover repeated receipts, cancellation after a price change, recipe returns, batch valuation and repeated disposal, fractional consumption and production, overselling rejection, used units, supplier reference duplicates, PDF/Excel output, balanced journals and legacy diagnostics.

UI checks use the application's WPF resources and real control events with a dispatcher synchronization context. Whole-window renders cover sales at 960x560, 1093x600, 1100x680 and 1360x820 logical pixels, plus the compact catalog and split payment. Generated images and databases stay under ignored `artifacts/`.

## Limits

- This validates source and WPF control behavior. It is not an installed-executable test, physical 125%/150% DPI test, printer test or observed cashier usability study.
- The native duplicate-invoice confirmation is reviewed in code; rejection, rollback and the explicit override are tested at service level.
- Old stock valuation differences need owner review and an explicit correction. Diagnostics explain the difference without changing records.
- Shift closing, tables, partial refunds, unified supplier purchases with expiry batches, supplier/account editing and report date ranges remain separate product work.
- These changes were subsequently packaged in 1.0.5. See [Release 1.0.5](release-105.md) for installation and DPI validation.
