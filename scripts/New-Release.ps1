[CmdletBinding()]
param(
    [switch]$FrameworkFallback,
    [switch]$SkipInstaller,
    [string]$InnoCompiler,
    [string]$ExpectedTag
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$version = [string]([xml](Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Expected an application version in major.minor.patch format.' }
if ($ExpectedTag -and $ExpectedTag -cne "v$version") { throw "The tag must match v$version." }
$artifacts = (New-Item -ItemType Directory -Path (Join-Path $root 'artifacts') -Force).FullName
$release = (New-Item -ItemType Directory -Path (Join-Path $artifacts 'release') -Force).FullName
$work = (New-Item -ItemType Directory -Path (Join-Path $artifacts ('package-' + [Guid]::NewGuid().ToString('N'))) -Force).FullName
$packageSuffix = if ($FrameworkFallback) { 'win-x64-framework48' } else { 'win-x64' }
$base = "AKASS-Optimizer-$version-$packageSuffix"
try {
    if ($FrameworkFallback) {
        $fallback = Join-Path $artifacts "framework-x64-$version"
        & (Join-Path $PSScriptRoot 'Build-DesktopFallback.ps1') -OutputFolder $fallback
        Copy-Item -LiteralPath (Join-Path $fallback 'AKASS.Optimizer.exe'), (Join-Path $fallback 'AKASS.Optimizer.exe.config') -Destination $work
    } else {
        & dotnet publish (Join-Path $root 'src/AKASS.Optimizer.App/AKASS.Optimizer.App.csproj') -c Release -r win-x64 `
            --self-contained true -p:PublishTrimmed=false -p:DebugType=none -o $work
        if ($LASTEXITCODE -ne 0) { throw 'Publishing the .NET desktop application failed.' }
        $presentMon = New-Item -ItemType Directory -Path (Join-Path $work 'tools/PresentMon')
        $presentMonExe = Join-Path $presentMon.FullName 'PresentMon.exe'
        $presentMonDownload = 'https://github.com/GameTechDev/PresentMon/releases/download/v2.6.0/PresentMon-2.6.0-x64.exe'
        Invoke-WebRequest -Uri $presentMonDownload -OutFile $presentMonExe
        if ((Get-FileHash -LiteralPath $presentMonExe -Algorithm SHA256).Hash -ine
            'b2a706bc6ad475749e3b7e3409263aa1e6906d45bdcf993f6dbc0f660188f1af') {
            throw 'PresentMon did not match the pinned release digest.'
        }
        Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/GameTechDev/PresentMon/v2.6.0/LICENSE.txt' -OutFile (Join-Path $presentMon.FullName 'LICENSE.txt')
        Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/LibreHardwareMonitor/LibreHardwareMonitor/v0.9.6/LICENSE' -OutFile (Join-Path $work 'LibreHardwareMonitor-LICENSE.txt')
    }
    Copy-Item -LiteralPath (Join-Path $root 'LICENSE.txt'), (Join-Path $root 'THIRD-PARTY-NOTICES.txt'),
        (Join-Path $root 'CHANGELOG.md'), (Join-Path $root 'docs/USER-GUIDE.md') -Destination $work

    $portable = Join-Path $release "$base-portable.zip"
    Compress-Archive -Path (Join-Path $work '*') -DestinationPath $portable -CompressionLevel Optimal -Force
    $binaries = @($portable)
    if ($FrameworkFallback) {
        $standalone = Join-Path $release "$base-portable.exe"
        Copy-Item -LiteralPath (Join-Path $work 'AKASS.Optimizer.exe') -Destination $standalone -Force
        $binaries += $standalone
    }
    if (!$SkipInstaller) {
        if (!$InnoCompiler) { $InnoCompiler = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source }
        if (!$InnoCompiler) { $InnoCompiler = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe' }
        if (!(Test-Path -LiteralPath $InnoCompiler -PathType Leaf)) { throw 'Inno Setup 6 is required for the installer. The portable archive was built.' }
        $definitions = @("/DAppVersion=$version", "/DPackageDir=$work", "/DReleaseDir=$release", "/DPackageSuffix=$packageSuffix")
        if ($FrameworkFallback) { $definitions += '/DFrameworkFallback=1' }
        & $InnoCompiler @definitions (Join-Path $root 'installer/AKASS.Optimizer.iss')
        $setup = Join-Path $release "$base-setup.exe"
        if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $setup -PathType Leaf)) { throw 'The installer build failed.' }
        $binaries += $setup
    }
    $binaries | ForEach-Object {
        $digest = (Get-FileHash -Algorithm SHA256 -LiteralPath $_).Hash.ToLowerInvariant()
        "$digest  $([IO.Path]::GetFileName($_))"
    } | Set-Content -LiteralPath (Join-Path $release "$base-SHA256SUMS.txt") -Encoding ascii
    Get-Item -LiteralPath $binaries | Select-Object Name, Length
} finally {
    # Resolve both directories before recursively deleting this invocation's private staging folder.
    $resolvedWork = (Resolve-Path -LiteralPath $work).Path
    $resolvedArtifacts = (Resolve-Path -LiteralPath $artifacts).Path.TrimEnd('\')
    if ([IO.Path]::GetDirectoryName($resolvedWork) -ieq $resolvedArtifacts -and
        [IO.Path]::GetFileName($resolvedWork) -cmatch '^package-[a-f0-9]{32}$') {
        Remove-Item -LiteralPath $resolvedWork -Recurse -Force
    }
}
