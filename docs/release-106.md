# Release 1.0.6

## Changes

- Open existing installations directly without a password. Fresh installations ask only for a name. Settings can change it.
- Keep business data, old user identities, and historical references during upgrade. Old credential columns remain for database compatibility and are never used for authentication.
- Add water to a recipe with one button. Unmeasured ingredients display "به مقدار لازم" and do not consume stock or add cost.
- Keep measured ingredients as the limit for available portions. Water does not remove a milk, coffee, or syrup stock limit.
- Label recipe steps and ingredient units. Keep the ingredient list visible in compact windows while the editor scrolls.
- Prevent cancellation of water-only sales from creating stock. Keep the fallback for historical sales that lacked inventory movements.

## Validation

- Smoke scenarios passed with an isolated database and Persian culture.
- 43 workflow checks cover accounting, quantities, shared materials, water-only sales and production, mixed recipes, cancellation, and returning to measured water.
- 45 WPF UI checks cover name-only setup, editing the name, direct restart, water entry, repeated entry, selection, and compact recipe layout.
- 27 field checks passed on a disposable copy of the employer backup. A snapshot of every business row stayed unchanged when adopting and renaming the local profile. The original backup stayed untouched.

UI checks render WPF controls and invoke their actions. They do not represent a physical 125% or 150% monitor test. Receipt and label hardware remain untested.

## Installation

Use `cafe-arian-setup-1.0.6.exe` to upgrade. It keeps the database and backups. The installer provides a destination choice and Start-menu and optional desktop shortcuts.

The release asset includes `SHA256SUMS.txt`. The uploaded installer size and digest must match the local file before publication.
