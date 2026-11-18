# Build a local x64 executable against the installed .NET Framework 4.8/4.8.1.
# Requires PowerShell 7 with its bundled Roslyn assemblies. The primary build uses .NET 10.
param([string]$OutputFolder)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
if (!$OutputFolder) { $OutputFolder = Join-Path $root 'artifacts/framework-x64' }
$OutputFolder = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputFolder)
New-Item -ItemType Directory -Path $OutputFolder -Force | Out-Null
if (!(Test-Path "$PSHOME/Microsoft.CodeAnalysis.CSharp.dll")) { throw 'PowerShell 7 Roslyn assemblies are required.' }
Add-Type -Path "$PSHOME/Microsoft.CodeAnalysis.dll"
Add-Type -Path "$PSHOME/Microsoft.CodeAnalysis.CSharp.dll"
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$assemblies = @('mscorlib','System','System.Core','System.Xml','System.Runtime.Serialization',
    'System.Configuration','System.Drawing','System.Windows.Forms','Microsoft.CSharp',
    'Facades/System.ValueTuple','WPF/WindowsBase','WPF/PresentationCore','WPF/PresentationFramework','System.Xaml')
$references = [Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]]::new()
foreach ($assembly in $assemblies) {
    $path = Join-Path $framework ($assembly + '.dll')
    if (Test-Path $path) { $references.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($path)) }
}
$parse = [Microsoft.CodeAnalysis.CSharp.CSharpParseOptions]::Default.WithLanguageVersion(
    [Microsoft.CodeAnalysis.CSharp.LanguageVersion]::Latest).WithPreprocessorSymbols([string[]]@('NETFRAMEWORK'))
function Build-Local($name, $file, $kind, $sources, $icon, $manifest) {
    $syntax = [Collections.Generic.List[Microsoft.CodeAnalysis.SyntaxTree]]::new()
    foreach ($source in $sources) {
        $syntax.Add([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText(
            [IO.File]::ReadAllText($source.FullName), $parse, $source.FullName, [Text.Encoding]::UTF8))
    }
    $options = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new($kind)
    $options = $options.WithPlatform([Microsoft.CodeAnalysis.Platform]::X64).
        WithOptimizationLevel([Microsoft.CodeAnalysis.OptimizationLevel]::Release).
        WithNullableContextOptions([Microsoft.CodeAnalysis.NullableContextOptions]::Enable).
        WithDeterministic($true)
    $compilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create($name, $syntax, $references, $options)
    $target = [IO.File]::Create($file)
    $iconFile = if ($icon) { [IO.File]::OpenRead($icon) }
    $manifestFile = if ($manifest) { [IO.File]::OpenRead($manifest) }
    try {
        $resources = $compilation.CreateDefaultWin32Resources($true, $false, $manifestFile, $iconFile)
        try { $result = $compilation.Emit($target, $null, $null, $resources) }
        finally { $resources.Dispose() }
    } finally {
        $target.Dispose()
        if ($iconFile) { $iconFile.Dispose() }
        if ($manifestFile) { $manifestFile.Dispose() }
    }
    $result.Diagnostics | Where-Object Severity -In 'Error','Warning' | ForEach-Object ToString
    if (!$result.Success) { Remove-Item -LiteralPath $file; throw "Build failed for $name." }
}
$core = @(Get-ChildItem "$root/src/AKASS.Optimizer.Core/*.cs","$root/src/AKASS.Optimizer.Windows/*.cs")
$app = @(Get-ChildItem "$root/src/AKASS.Optimizer.App/*.cs")
$tests = @(Get-ChildItem "$root/tests/AKASS.Optimizer.Tests/*.cs")
Build-Local 'AKASS.Optimizer' "$OutputFolder/AKASS.Optimizer.exe" WindowsApplication ($core + $app) "$root/assets/akass.ico" "$root/src/AKASS.Optimizer.App/app.manifest"
Copy-Item -LiteralPath "$root/src/AKASS.Optimizer.App/Framework.config" -Destination "$OutputFolder/AKASS.Optimizer.exe.config" -Force
Build-Local 'AKASS.Optimizer.Tests' "$OutputFolder/AKASS.Optimizer.Tests.exe" ConsoleApplication ($core + $tests) $null $null
Copy-Item -LiteralPath "$root/src/AKASS.Optimizer.App/Framework.config" -Destination "$OutputFolder/AKASS.Optimizer.Tests.exe.config" -Force
Write-Output $OutputFolder
