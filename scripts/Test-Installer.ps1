[CmdletBinding()]
param([Parameter(Mandatory)][string]$SetupPath)

$ErrorActionPreference = 'Stop'
$setup = (Resolve-Path -LiteralPath $SetupPath).Path
$root = Split-Path -Parent $PSScriptRoot
$appName = 'AKASS Optimizer'
$desktopPath = [Environment]::GetFolderPath('DesktopDirectory')
$programsPath = [Environment]::GetFolderPath('Programs')
if (!$desktopPath -or !$programsPath) { throw 'The test requires a Windows user profile with desktop and Start menu folders.' }
$desktopLink = Join-Path $desktopPath "$appName.lnk"
$startLink = Join-Path $programsPath "$appName.lnk"
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{F57BA096-F7CF-4595-9E62-C16926254F84}_is1'
if ((Test-Path -LiteralPath $uninstallKey) -or (Test-Path -LiteralPath $desktopLink) -or (Test-Path -LiteralPath $startLink)) {
    throw 'Run this check in a clean Windows profile without an existing AKASS installation or shortcuts.'
}

function Assert-AppIcon([string]$File) {
    $original = [System.Drawing.Icon]::new((Join-Path $root 'assets/akass.ico'), 32, 32)
    $actual = [System.Drawing.Icon]::ExtractAssociatedIcon($File)
    if ($null -eq $actual) {
        $original.Dispose()
        throw "No executable icon was found in $File."
    }
    $expectedBitmap = $original.ToBitmap()
    $actualBitmap = $actual.ToBitmap()
    $scaled = [System.Drawing.Bitmap]::new($actualBitmap, 32, 32)
    try {
        $difference = 0
        for ($y = 0; $y -lt 32; $y++) {
            for ($x = 0; $x -lt 32; $x++) {
                $expectedColor = $expectedBitmap.GetPixel($x, $y)
                $actualColor = $scaled.GetPixel($x, $y)
                if ($expectedColor.A -gt 0 -or $actualColor.A -gt 0) {
                    $difference += [Math]::Abs($expectedColor.A - $actualColor.A) +
                        [Math]::Abs($expectedColor.R - $actualColor.R) +
                        [Math]::Abs($expectedColor.G - $actualColor.G) +
                        [Math]::Abs($expectedColor.B - $actualColor.B)
                }
            }
        }
        # Shell icon extraction can resample or round alpha channels.
        if ($difference / (32 * 32 * 4 * 255) -gt 0.04) { throw "The AKASS icon is missing from $File." }
    } finally {
        $scaled.Dispose()
        $actualBitmap.Dispose()
        $expectedBitmap.Dispose()
        $actual.Dispose()
        $original.Dispose()
    }
}

function Invoke-Installer([string]$File, [string]$Arguments) {
    $process = Start-Process -FilePath $File -ArgumentList $Arguments -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -ne 0) { throw "$([IO.Path]::GetFileName($File)) returned exit code $($process.ExitCode)." }
}

Add-Type -AssemblyName System.Drawing
Assert-AppIcon $setup
Write-Output 'PASS: the setup EXE contains the AKASS icon.'

$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('AKASS-Installer-Test-' + [guid]::NewGuid().ToString('N'))
$installDir = Join-Path $testRoot 'app'
$null = New-Item -ItemType Directory -Path $testRoot
$installLog = Join-Path $testRoot 'setup.log'
$appExe = Join-Path $installDir 'AKASS.Optimizer.exe'
try {
    Invoke-Installer $setup ('/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /DIR="{0}" /LOG="{1}"' -f $installDir, $installLog)
    if (!(Test-Path -LiteralPath $appExe -PathType Leaf)) { throw 'The application executable was not installed.' }
    Assert-AppIcon $appExe
    if ($setup -notlike '*-framework48-*') {
        $frameTool = Join-Path $installDir 'tools/PresentMon/PresentMon.exe'
        if (!(Test-Path -LiteralPath $frameTool -PathType Leaf) -or
            !(Test-Path -LiteralPath (Join-Path $installDir 'LibreHardwareMonitorLib.dll') -PathType Leaf)) {
            throw 'The standard installer did not include its telemetry dependencies.'
        }
        Write-Output 'PASS: the standard installer includes PresentMon and LibreHardwareMonitor.'
    }
    $shell = New-Object -ComObject WScript.Shell
    foreach ($linkPath in @($desktopLink, $startLink)) {
        if (!(Test-Path -LiteralPath $linkPath -PathType Leaf)) { throw "The installer did not create $linkPath." }
        $shortcut = $shell.CreateShortcut($linkPath)
        if ($shortcut.TargetPath -ine $appExe -or $shortcut.WorkingDirectory -ine $installDir -or
            ($shortcut.IconLocation -replace ',\s*-?\d+$', '') -ine $appExe) {
            throw "Incorrect target, working directory, or icon for $linkPath."
        }
    }
    Write-Output 'PASS: the desktop and Start menu shortcuts target the branded installed app.'
} catch {
    if (Test-Path -LiteralPath $installLog) { Get-Content -LiteralPath $installLog -Tail 30 | Write-Output }
    throw
} finally {
    $uninstall = Get-ChildItem -LiteralPath $installDir -Filter 'unins*.exe' -File -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($uninstall) { Invoke-Installer $uninstall.FullName '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' }
}
if ((Test-Path -LiteralPath $desktopLink) -or (Test-Path -LiteralPath $startLink)) {
    throw 'Uninstallation left an AKASS shortcut behind.'
}
Write-Output 'PASS: uninstall removed both shortcuts.'
