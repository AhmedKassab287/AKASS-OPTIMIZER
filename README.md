# AKASS Optimizer

AKASS Optimizer is a Windows desktop utility by Ahmed Kassab. It previews disk cleanup, monitors CPU and memory, and brings startup management and Windows diagnostics into one app. It works offline and stores preferences and activity history locally.

## Download for Windows x64

Open [GitHub Releases](https://github.com/AhmedKassab287/AKASS-OPTIMIZER/releases/latest) and expand **Assets**.

| Download | How to use it |
| --- | --- |
| `*-setup.exe` | Run the Windows installer. A desktop shortcut is selected by default, and the app is also available from the Start menu. Uninstall through **Settings → Apps → Installed apps**. |
| `*-portable.zip` | Extract the whole archive into a writable folder and run `AKASS.Optimizer.exe`. |
| `*-framework48-portable.exe` | Run the standalone app on a Windows x64 PC with .NET Framework 4.8 or 4.8.1. Use the matching ZIP if you also want the bundled documentation. |
| **Source code (zip)** | Extract the source or clone the repository. See [Build from source](#build-from-source). |
| `*-SHA256SUMS.txt` | Compare the listed SHA-256 value with your download using `Get-FileHash -Algorithm SHA256 <file>`. |

The standard `win-x64` installer and ZIP include the .NET desktop runtime. Downloads labeled `framework48` use the .NET Framework installed on your PC. Builds are unsigned unless the specific release says otherwise. See the [release notes](CHANGELOG.md) and [known limitations](KNOWN_LIMITATIONS.md).

## Update an existing copy

Run the new setup EXE over your existing installation. It reuses the existing install folder and keeps your preferences and activity history. Close AKASS Optimizer first if setup asks you to, including the copy in the system tray.

Starting with 1.3.0, open **Settings → Updates** or **About** and select **Check for updates**. The app also checks GitHub automatically while it is running. When a stable update is available, select **Install update**. AKASS downloads and verifies the installer, then closes so setup can update it and reopen the app. Windows may show its usual trust prompt. For a portable copy, open GitHub Releases from the same section and extract the latest ZIP.

## What it does

| Area | Available actions |
| --- | --- |
| Dashboard | View live CPU, RAM and storage metrics, review a health score and preview suggested cleanup. |
| Smart Cleaner | Scan eligible temporary files, browser caches and thumbnails. Review categories before cleaning. The Recycle Bin has a separate confirmation. |
| Memory Optimizer | Inspect RAM pressure and commit, free RAM from eligible background processes, and optionally clear the Windows standby cache. Automatic RAM optimization is available in Settings. |
| Startup Manager | Review Run registrations and Startup folders. Back up or restore eligible current-user shortcuts. |
| System Monitor | Follow CPU and memory history, disk and network rates, and running processes. |
| Large Files | Scan local folders or drives, filter by extension and size, and export results as CSV. |
| Windows Repair and Battery | Run fixed Windows repair and DNS commands, read charging status, and generate battery reports where supported. |
| Gaming Mode | Inspect usage and temporarily select an installed Windows power plan. |
| Scheduled Maintenance | Opt in to daily, weekly or monthly cleanup of eligible current-user temporary files. Pause, disable or review the schedule in Settings. |

Open **Settings** to change the language, theme, notification preferences and cleanup options. The app supports English, Arabic with right-to-left layout, and Turkish. Cleanup provides a preview. Some Windows repair commands require administrator approval. See [the user guide](docs/USER-GUIDE.md) for how each operation behaves.

## Build from source

Prerequisites are Windows x64, [PowerShell 7](https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-windows), and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). Open `AKASS.Optimizer.sln` in a Visual Studio version that supports .NET 10 or run:

```powershell
git clone https://github.com/AhmedKassab287/AKASS-OPTIMIZER.git
cd AKASS-OPTIMIZER
dotnet build .\AKASS.Optimizer.sln -c Release
dotnet run --project .\src\AKASS.Optimizer.App -c Release
```

Run the automated tests and desktop smoke checks, then build the release files. Install [Inno Setup 6](https://jrsoftware.org/isdl.php) for the setup executable.

```powershell
.\scripts\Test-Desktop.ps1
.\scripts\New-Release.ps1
```

On Windows with .NET Framework 4.8 or later, use the PowerShell 7 fallback if the .NET 10 SDK is unavailable.

```powershell
.\scripts\Test-Desktop.ps1 -FrameworkFallback
.\scripts\New-Release.ps1 -FrameworkFallback -SkipInstaller
```

Release files are written to `artifacts/release`. For automation, signing, and checksums see [the release guide](RELEASE.md). For implementation and test coverage see [architecture](ARCHITECTURE.md), [testing](TESTING.md), and [security and privacy](SECURITY.md).

## تشغيل البرنامج بالعربي

نزّل النسخة من [صفحة الإصدارات](https://github.com/AhmedKassab287/AKASS-OPTIMIZER/releases/latest). شغّل ملف التثبيت، أو فك ضغط النسخة المحمولة بالكامل وافتح `AKASS.Optimizer.exe`. لتغيير اللغة افتح **Settings → Language → العربية** واضغط **Save settings**. لتحميل السورس اختر **Source code (zip)** من نفس صفحة الإصدار.

## License and contact

See [LICENSE.txt](LICENSE.txt) for the publisher's terms and [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) for dependency notices. Contact Ahmed Kassab through [akass.dev](https://akass.dev).
