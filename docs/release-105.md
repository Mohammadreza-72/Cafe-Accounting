# Release 1.0.5

This release packages the workflow fixes from PR #8 and adjusts window sizing for display scaling.

## Changes

The executable declares Per Monitor V2 awareness. The login and main windows fit the current monitor's work area at startup and after DPI changes. The main window can shrink to 900x450 logical pixels. This prevents the default 1360x820 window from extending outside a smaller logical work area.

The DPI declaration follows [Microsoft's WPF guidance](https://github.com/microsoft/WPF-Samples/blob/main/PerMonitorDPI/readme.md). Work area measurements exclude the taskbar and convert physical pixels to WPF units with the window's current DPI.

## Installation evidence

On 2026-10-07 the interactive Inno installer upgraded the existing 1.0.4 installation at `E:/CafeArian` to 1.0.5. Its log records successful completion, the executable, uninstaller, Start menu shortcut and desktop shortcut. Both shortcuts target `E:/CafeArian/CafeArian.exe`. The installed executable's SHA-256 matched the published build locally.

The installer launched the app with an isolated test database on E:. A first-run setup window was reported, followed by the responding main window. The supplied employer backup retained its original SHA-256. Test databases, credentials and native install logs are not release assets.

## Validation

- The preceding workflow commit passed Smoke, 33 workflow, 33 WPF UI and 29 employer-backup field checks.
- Window sizing changes passed 39 WPF UI checks, including 900x450 and 911x485 logical sizes, startup work-area constraints and login sizing.
- The actual installed app was measured on a 1920x1080 display with a 1920x1040 work area at 96 DPI (100%). Its main window was responsive and inside that work area.
- Native UI capture failed with `FrameArrived timed out` and related window capture errors. Installer steps and main-window appearance were not independently screenshot-verified by the agent.
- Physical 125%/150% tests require changing Windows Scale and remain pending. The corresponding compact WPF geometry tests are not physical DPI tests.

## Download

[Windows installer](https://github.com/Mohammadreza-72/Cafe-Accounting/releases/download/v1.0.5/cafe-arian-setup-1.0.5.exe).

The package includes the .NET runtime. Authenticode signing requires a publisher certificate; the locally built package is unsigned. Existing accounts and data are retained when upgrading.
