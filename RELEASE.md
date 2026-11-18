# Build and publish AKASS Optimizer

## Windows x64 packages

Use PowerShell 7 on Windows x64. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and [Inno Setup 6](https://jrsoftware.org/isdl.php). Use a current stable release of each.

```powershell
.\scripts\Test-Desktop.ps1
.\scripts\New-Release.ps1
```

`artifacts/release` receives a self-contained portable ZIP, an Inno Setup executable and SHA-256 checksums. The installer runs per user and creates a branded Start menu entry and an optional desktop shortcut selected by default. It has an Apps & Features uninstall entry, preserves settings and startup shortcut backups, and requests removal of its own background registrations during uninstall. The setup launch option is unchecked by default.

To create the .NET Framework 4.8 portable ZIP and standalone EXE, run the following. Remove `-SkipInstaller` when Inno Setup is available and an installer is needed for this build.

```powershell
.\scripts\Test-Desktop.ps1 -FrameworkFallback
.\scripts\New-Release.ps1 -FrameworkFallback -SkipInstaller
```

## CI and versioning

Update `Directory.Build.props`, the fallback version metadata in `Program.cs`, `app.manifest` and `CHANGELOG.md`. Push the commit to GitHub and review the Windows build. Push an annotated tag matching the application version to publish the tested setup EXE, portable ZIP and checksum file under GitHub Releases. GitHub adds the source archives for the tagged commit.

```powershell
git tag -a v1.1.1 -m "AKASS Optimizer 1.1.1"
git push origin v1.1.1
```

A push to `main` whose commit message contains `[release]` can also publish the version in `Directory.Build.props`. The workflow creates the tag for that exact commit. If the release already exists, publishing stops. Upload additional assets through GitHub Releases after verifying their checksums.

## Authenticode and verification

Use a code-signing certificate assigned to the publisher and the Windows SDK SignTool. Sign the published app executable before building its packages, then sign the finished installer. Replace the timestamp URL with the RFC 3161 URL supplied by your certificate provider. Keep certificate credentials outside source control and CI logs.

```powershell
signtool sign /fd SHA256 /sha1 <certificate-thumbprint> /tr <timestamp-url> /td SHA256 <file>
signtool verify /pa /v <file>
Get-FileHash .\artifacts\release\* -Algorithm SHA256
```

For a signed standard release, use `dotnet publish` with the options in `New-Release.ps1` into a staging folder. Sign `AKASS.Optimizer.exe`, add the guide and notices as in the script, build the ZIP from that folder, and pass the same folder to `ISCC.exe` as `/DPackageDir=...`. Sign the resulting setup executable and regenerate checksums for the final ZIP and setup. The locally generated package is unsigned.

Use a publicly trusted code-signing certificate or [Microsoft Artifact Signing](https://learn.microsoft.com/windows/apps/package-and-deploy/code-signing-options) to identify the publisher in Windows. A newly signed download can still receive a SmartScreen reputation warning. The browser decides how it displays the EXE icon in its download history.

## Publish on akass.dev

1. Download and extract the expected CI artifact. Complete the validation in [TESTING.md](TESTING.md), including installing and uninstalling the exact setup file planned for release.
2. Put the approved packages and their SHA-256 file under a versioned path such as `/downloads/akass-optimizer/1.0.0/` on the `akass.dev` deployment. If the current Next.js app serves static assets from `public`, place the files under `public/downloads/akass-optimizer/1.0.0/` and deploy through the usual release process. An existing Nginx static download location can serve the same paths.
3. Link the setup, portable ZIP and checksum file from a product or download page. Show the version and runtime requirements. Offer the checksum and release notes alongside the files.
4. Download both packages using their final HTTPS URLs. Compare each remote download's SHA-256 against the posted file, then test the exact downloads in a clean Windows VM.
