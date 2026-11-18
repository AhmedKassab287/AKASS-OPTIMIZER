[CmdletBinding()]
param([switch]$FrameworkFallback, [switch]$SkipDesktop)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$evidence = (New-Item -ItemType Directory -Force (Join-Path $root 'artifacts/validation')).FullName
if ($FrameworkFallback) {
    & (Join-Path $PSScriptRoot 'Build-DesktopFallback.ps1')
    $app = Join-Path $root 'artifacts/framework-x64/AKASS.Optimizer.exe'
    $tests = Join-Path $root 'artifacts/framework-x64/AKASS.Optimizer.Tests.exe'
} else {
    & dotnet build (Join-Path $root 'AKASS.Optimizer.sln') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'The .NET build failed.' }
    $app = Join-Path $root 'src/AKASS.Optimizer.App/bin/Release/net10.0-windows/AKASS.Optimizer.exe'
    $tests = Join-Path $root 'tests/AKASS.Optimizer.Tests/bin/Release/net10.0-windows/AKASS.Optimizer.Tests.exe'
}
& $tests | Tee-Object -FilePath (Join-Path $evidence 'automated-tests.txt')
if ($LASTEXITCODE -ne 0) { throw 'Automated tests failed.' }

if (!$SkipDesktop) {
    foreach ($mode in '--smoke-ui', '--smoke-live') {
        $process = Start-Process -FilePath $app -ArgumentList $mode, ('"' + $evidence + '"') -Wait -PassThru -WindowStyle Hidden
        if ($process.ExitCode -ne 0) { throw "Desktop check $mode failed. See $evidence." }
    }
    Get-Content (Join-Path $evidence 'ui-smoke.txt'), (Join-Path $evidence 'live-smoke.txt')
}
