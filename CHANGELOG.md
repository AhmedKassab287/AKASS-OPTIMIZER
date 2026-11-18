# Release notes

## 1.1.1

- The installer creates an AKASS Optimizer desktop shortcut by default. You can clear the shortcut option during setup.
- The desktop and Start menu shortcuts use the AKASS application icon.
- Release builds verify the setup icon, shortcut target, and shortcut removal during uninstall.

## 1.1.0

- Added a rounded scrollbar for long pages and scrollable controls in the dark and light themes.
- Added opt-in automatic memory optimization, an available-memory threshold, an idle requirement and a cooldown. The Memory Optimizer page shows the latest working-set and available-memory measurements.
- Added one optional, explicitly approved background app per AKASS session. The default target is AKASS Optimizer.
- Expanded desktop checks to cover all pages in the three languages and themes, including settings saves, validation, and scrollbar track and thumb behavior.

## 1.0.0

- Added a WPF dashboard with measured CPU, physical RAM, storage and maintenance health.
- Added cleanup previews, explicit category confirmation, file identity checks, cancellation, and separate Recycle Bin confirmation.
- Added memory pressure and process analysis, graceful close requests, and before and after measurements.
- Added startup inspection, reversible current-user shortcuts, large-file scanning and CSV export, Windows repairs, battery reporting, and temporary power-plan changes.
- Added opt-in scheduled maintenance, a system tray, local activity history, dark and light themes, and English, Arabic and Turkish interfaces.
- Added the Visual Studio solution, Windows CI packaging, automated checks and release scripts. The locally verified portable package uses .NET Framework 4.8.
