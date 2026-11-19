# AKASS Optimizer user guide

Run `AKASS.Optimizer.exe` from the extracted Windows x64 package. Use the Dashboard to see system resources, or select **Analyze & Optimize** to inspect the machine and review the proposed cleanup in Smart Cleaner. Open **Settings** to choose English, Arabic or Turkish, adjust monitoring, and switch between dark, light and system themes.

## Smart Cleaner

| Category | Scope |
| --- | --- |
| User and Windows temporary files | Each file type is eligible. Manual cleanup considers user Temp files older than one hour and Windows Temp files older than one day. Scheduled cleanup keeps a narrower rule. |
| Windows Prefetch | Advanced, opt-in preview of `.pf` traces older than seven days. Initial app launches can be slower while Windows rebuilds them. |
| Chrome and Edge | `Cache_Data`, `Code Cache`, and `GPUCache` in conventional browser profiles. |
| Firefox | `cache2` under recognized profile locations. |
| Thumbnails | Disposable `thumbcache_*.db` files in the Windows thumbnail cache. Windows can regenerate them. |
| Error reports and crash dumps | Explicit advanced selection of eligible archived reports and dump files at least 14 days old. |
| Recycle Bin | Windows empties this account's Recycle Bin after a separate confirmation. |

Preview a category before cleaning. The browser must be closed to clean its cache. Saved passwords, cookies, sessions, history, bookmarks and complete browser profiles are outside cleanup scope. Cleanup permanently removes selected verified items. The results show file sizes removed and the observed system drive free-space change. Those values can differ because storage allocation and other disk activity affect free space. Windows Storage settings handle the Windows Update and Delivery Optimization categories. Windows uses Prefetch to help applications load.

Use **Flush DNS cache** from Smart Cleaner for network troubleshooting. Protected Windows directories may require starting AKASS as administrator.

## Memory and startup

The memory page combines available physical RAM, Windows memory load, commit and the Windows low-memory report. The memory breakdown shows reserved, in-use, modified, standby, and free RAM with cached and installed totals. Windows uses available capacity for caches that it can reclaim. A rise in available RAM alone does not measure speed. A process working set can include shared pages, so working set figures can overlap. Save your work before requesting a graceful close. The app measures available RAM and commit again after the request. The system tray can alert you to sustained pressure when enabled.

**Clear standby RAM** requests administrator approval and discards cached pages. **Compact AKASS heap** works on the app's own managed memory. Windows manages system-wide physical RAM and provides no general-purpose RAM defragmentation action for applications.

In Settings, enable **Automatic memory optimization** and choose a free-RAM threshold. The app waits for sustained pressure below that threshold and at least two minutes of user inactivity. It attempts one trim per pressure episode, at least ten minutes apart. It trims AKASS Optimizer's own working set by default. In the process table, you can explicitly choose a single eligible running app for the current session. The chosen app must still be a background app under your Windows account when the action runs. The result shows the target's working set and system available RAM before and after. Pages can return to RAM when needed. Automatic optimization can run in the tray with background monitoring enabled. **Optimize AKASS now** runs the self-only action immediately.

Open Startup Manager to inspect Run registrations and Startup folders. Use **Disable / restore** to back up or restore an eligible current-user shortcut. A backup remains on disk until restored. If a shortcut with the same name already exists in Startup, resolve the conflict in Windows before trying again. The **Windows Startup settings** button opens the system controls for other startup apps. Review and restore any remaining backed-up shortcuts before uninstall if you want them to launch again.

## Diagnostics, gaming and maintenance

Open **System Monitor** for CPU/GPU temperatures and displayed FPS. **Ctrl + Alt + F10** and the **Toggle overlay** button show or hide the top-right overlay. Keep AKASS running and use borderless or windowed mode for games. FPS follows the foreground graphics application and depends on Windows tracing permissions. Hardware sensors determine which temperatures are available. Some CPU sensors require the official PawnIO driver or elevated access. Dashes show missing readings. This telemetry requires the standard release rather than the Framework fallback.

Large Files lists candidates by size and file extension, lets you open their folders and exports a CSV. Use File Explorer to manage those files. Battery Health reports data Windows exposes and can create a battery report. SFC, DISM and DNS flush can prompt for administrator access. They run only after you choose a known operation and confirm it. DNS flush is for troubleshooting connectivity and name resolution.

Gaming Mode lets you select an existing Windows power plan for the session. Choose **End session** to restore the previous plan. The app also attempts restoration when it exits if the selected plan is still active. In Settings, choose a daily, weekly or monthly schedule only if you want unattended cleanup. It processes eligible current-user temporary files at least seven days old while you are signed in and connected to AC power. You can pause it or return to manual mode at any time. Maintenance History displays local activity and offers export and deletion controls.

To remove a portable copy, first disable its launch-at-sign-in setting and maintenance schedule if you enabled them. End any gaming session, restore startup shortcuts as desired, close the app and remove the extracted copy. The installed edition appears under Windows **Installed apps**.
