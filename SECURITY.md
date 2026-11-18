# Security and privacy

AKASS Optimizer starts under the current user's privileges. For SFC, DISM and DNS troubleshooting it uses a UAC-approved helper with fixed command choices and a process-verified pipe. Command output is limited in memory and can be saved by the user. DISM repair may use Windows Update for repair sources depending on system policy. Primary features operate offline.

Cleanup starts with a bounded scan of approved local roots. It excludes reparse points, hard-linked files and ambiguous paths. Before deletion it opens the directory chain, verifies the final path and file identity, and deletes via an opened handle. New, changed or locked targets can be skipped. The configured roots are validated again at execution. Browser deletions are skipped while the browser process is detected. The Recycle Bin uses Windows Shell APIs and its own confirmation. Scheduled cleanup is opt-in with a stricter age threshold and current-user scope.

Process actions recheck the process identity, interactive window, session, ownership and executable scope. They request a normal window close after user confirmation. Startup changes made in the app are limited to reversible moves for eligible current-user shortcuts and the application's own opt-in startup entry. Use Windows Settings to manage other registrations. The gaming session records the prior power plan and restores it if the app's selected plan is still active.

Automatic memory optimization is opt-in. It trims the app's own working set unless the user explicitly selects a single eligible app for that session. Each external trim uses one process handle and checks the process start time, user, session, foreground status, elevation, critical status and recent CPU use. It also checks for sustained pressure and user inactivity. Memory activity stays in local history.

The app uses no account, analytics or remote storage. Settings and up to 250 recent activity records live under `%LOCALAPPDATA%\AKASS\Optimizer`. Records older than 90 days are pruned on subsequent writes. Exported reports can contain system details or file paths. Settings, logs and disabled shortcuts are retained when uninstalling to preserve recovery options. Use Maintenance History to clear the activity log.

For a security report, contact the publisher privately through [akass.dev](https://akass.dev). Include the app version, Windows build and a minimal reproduction using disposable data. Avoid attaching real personal data.
