# Release notes

## 1.2.0

- Manual cleanup scans all eligible file types in approved user and Windows Temp folders. Advanced cleanup previews old Prefetch traces. Smart Cleaner includes a separate DNS cache action.
- Memory Optimizer shows hardware-reserved, in-use, modified, standby, free and cached RAM. Manual actions include trimming AKASS or a selected idle background application, clearing the standby list with elevation, and compacting the AKASS managed heap.
- System Monitor shows CPU/GPU temperature and foreground-app displayed FPS. Toggle the top-right overlay with Ctrl+Alt+F10 or the System Monitor button. Borderless/windowed games can show the overlay.
- Standard installer and portable package include LibreHardwareMonitor and Intel PresentMon 2.6.0. Sensor and frame data availability depends on hardware and Windows tracing permissions.

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
