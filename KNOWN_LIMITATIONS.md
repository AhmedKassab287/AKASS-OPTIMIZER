# Known limitations

- The demonstrated Windows x64 package uses .NET Framework 4.8 or later. The .NET 10 SDK and Inno Setup compiler were unavailable on the test machine, so the standard self-contained build and the installer remain unverified.
- Startup Registry Run values and common Startup entries are visible as registrations. Use Windows Startup Settings for their enabled state and changes. In-app backup and restore cover eligible current-user `.lnk` entries. Publisher details come from executable version metadata and can be blank.
- Browser cleanup covers conventional Chrome, Edge and Firefox profile paths and the supported disposable cache subfolders. Custom profile directories outside known roots are excluded. Internet Explorer and other browsers are outside scope.
- Windows Update and Delivery Optimization cleanup open Windows Storage settings. Thumbnail cache files in use may be skipped. Recycle Bin totals come from Windows before and after and may change if another process modifies its contents.
- Memory pressure uses available RAM, commit headroom, Windows memory-load reporting and the Windows low-memory signal. Automatic working-set trimming is limited to AKASS by default or one explicitly approved idle background app per session. Paging-rate analytics and automated closing are outside scope. CPU temperature and drive media type have no reliable hardware source in this release.
- Repairs capture output and exit status at completion. While a repair runs, progress is indeterminate. The generated Windows battery report depends on Windows and hardware support.
- The UI is localized for the three supported languages. Windows tool output, some detailed runtime errors, and some exported diagnostic fields can remain in their original language.
- The portable archive requires extraction of the complete contents. Independent Windows 10, DPI, installer, scheduled task, live battery and elevated repair validation are pending.
