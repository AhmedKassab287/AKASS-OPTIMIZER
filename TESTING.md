# Testing

## Reproduce

```powershell
.\scripts\Test-Desktop.ps1 -FrameworkFallback
```

Omit `-FrameworkFallback` for the .NET 10 SDK build. Use `-SkipDesktop` in a noninteractive Windows CI session. The suite writes evidence under `artifacts/validation`. File mutations use isolated disposable fixtures. The live check reads host metrics and uses isolated settings.

## Local result

| Check | Result |
| --- | --- |
| x64 WPF .NET Framework build | Compiled locally on Windows build 10.0.26300.0. |
| Automated checks | 16 passed, 0 failed, 1 skipped. |
| Symlink and junction fixtures | Skipped because the session could not create either. |
| WPF navigation, locales and themes | 108 page, locale and theme combinations rendered, including Arabic right-to-left layout. |
| Settings and scrolling | Saved Arabic and Turkish settings and reloaded them from disk. Verified option persistence, invalid input handling, clicking the scrollbar track and dragging the thumb. |
| Memory optimization | Tested pressure, idle and cooldown rules with synthetic inputs and verified real working-set trimming of the test process. The WPF check invoked the manual action and verified the measured history record. |
| Live dashboard | Ran without elevation and received five real samples in the local check. A short CPU and private-memory measurement is in `live-smoke.txt`. |

The automated suite covers memory pressure, commit and score logic, path validation, settings and history, startup parsing and enumeration, system metrics, active and stale processes, battery XML parsing, power plan enumeration, file discovery, CSV escaping, cancellation, verified deletion, locked and replaced files, deduplication, and scheduled age limits.

## Manual validation before a public release

- Install, upgrade, uninstall and reinstall through the actual setup executable. Run the same package on supported Windows 10 and Windows 11 x64 builds. Verify that shortcut backups and preferences remain available.
- Use a disposable Windows test account for Startup backup and restore and for Task Scheduler registration, execution, pause and removal. Verify the Windows logon option and system tray.
- On a disposable VM, check UAC cancellation and command results for SFC, DISM and DNS. Verify an actual battery report on a laptop.
- Check Arabic and Turkish text manually at 100%, 125% and 150% DPI. Test keyboard navigation, contrast and monitor changes.
- Sample idle and scanning performance over a longer run on different RAM sizes and drive types. Verify high-memory behavior and activity log bounds.

The installer, modern .NET 10 build, Windows 10, and the manual scenarios above are pending verification. CI is configured and requires a GitHub run to produce evidence.
