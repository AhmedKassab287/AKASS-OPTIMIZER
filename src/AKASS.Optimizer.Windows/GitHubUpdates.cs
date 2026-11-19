using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Akass.Optimizer;

public sealed class GitHubUpdate
{
    private const string Repository = "AhmedKassab287/AKASS-OPTIMIZER";
    public const string ReleasesPage = "https://github.com/" + Repository + "/releases/latest";
    private const string ReleaseApi = "https://api.github.com/repos/" + Repository + "/releases/latest";
    private const string DownloadRoot = "https://github.com/" + Repository + "/releases/download/";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{F57BA096-F7CF-4595-9E62-C16926254F84}_is1";
    private const long MaximumInstallerBytes = 512L * 1024 * 1024;
    private const int MaximumMetadataBytes = 1024 * 1024;
    private static readonly HttpClient Client = CreateClient();

    private readonly Uri download;
    private readonly string digest;
    private readonly long size;
    public Version Version { get; }

    private GitHubUpdate(Version version, Uri download, string digest, long size)
    {
        Version = version;
        this.download = download;
        this.digest = digest;
        this.size = size;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AKASS-Optimizer");
        return client;
    }

    public static bool IsInstalled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
            if (key?.GetValue("InstallLocation") is not string location || !Path.IsPathRooted(location)) return false;
            return string.Equals(Path.GetFullPath(location).TrimEnd('\\'),
                Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            return false;
        }
    }

    public static async Task<GitHubUpdate?> CheckAsync(Version installed, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleaseApi);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var body = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var metadata = new MemoryStream();
        await CopyBoundedAsync(body, metadata, MaximumMetadataBytes, timeout.Token).ConfigureAwait(false);
        metadata.Position = 0;
        return ReadRelease(metadata, installed);
    }

    internal static GitHubUpdate? ReadRelease(Stream metadata, Version installed)
    {
        var release = (Release?)new DataContractJsonSerializer(typeof(Release)).ReadObject(metadata);
        string? tag = release?.Tag;
        if (release == null || release.Draft || release.Prerelease || tag == null || !tag.StartsWith("v", StringComparison.Ordinal) ||
            !Version.TryParse(tag.Substring(1), out var version) || version.Build < 0 || version.Revision >= 0 ||
            !string.Equals(tag, "v" + version, StringComparison.Ordinal)) return null;

        var installedVersion = new Version(installed.Major, installed.Minor, Math.Max(0, installed.Build));
        if (version <= installedVersion) return null;
        string name = "AKASS-Optimizer-" + version + "-win-x64-setup.exe";
        string url = DownloadRoot + tag + "/" + name;
        var assets = release.Assets?.Where(asset => string.Equals(asset?.Name, name, StringComparison.Ordinal)).ToArray();
        if (assets == null || assets.Length != 1 || assets[0] is not { } setup ||
            setup.State != "uploaded" || setup.Url != url || setup.Size <= 0 || setup.Size > MaximumInstallerBytes ||
            setup.Digest is not { Length: 71 } hash || !hash.StartsWith("sha256:", StringComparison.Ordinal) ||
            !hash.Skip(7).All(Uri.IsHexDigit))
            throw new InvalidDataException("GitHub did not provide a verifiable Windows x64 installer for this release.");

        return new GitHubUpdate(version, new Uri(url), hash.Substring(7), setup.Size);
    }

    public async Task<string> DownloadAsync(string stateDirectory, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        using var response = await OpenDownloadAsync(timeout.Token).ConfigureAwait(false);
        if (response.Content.Headers.ContentLength is long length && length != size)
            throw new InvalidDataException("The installer download has an unexpected size.");
        using var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        return await StageAsync(source, stateDirectory, timeout.Token).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> OpenDownloadAsync(CancellationToken token)
    {
        var next = download;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            var response = await Client.GetAsync(next, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (response.IsSuccessStatusCode) return response;
            using (response)
            {
                if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308)) response.EnsureSuccessStatusCode();
                var location = response.Headers.Location;
                if (location == null || !Uri.TryCreate(next, location, out var redirect) || redirect.Scheme != Uri.UriSchemeHttps ||
                    !redirect.IsDefaultPort || redirect.UserInfo.Length != 0 ||
                    redirect.Host is not ("github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com"))
                    throw new InvalidDataException("GitHub returned an unexpected installer download location.");
                next = redirect;
            }
        }
        throw new InvalidDataException("The installer download had too many redirects.");
    }

    internal async Task<string> StageAsync(Stream source, string stateDirectory, CancellationToken token)
    {
        string directory = Path.Combine(stateDirectory, "Updates");
        Directory.CreateDirectory(directory);
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The update directory cannot be a link.");
        foreach (string oldFile in Directory.EnumerateFiles(directory, "AKASS-Update-*"))
        {
            if (File.GetLastWriteTimeUtc(oldFile) < DateTime.UtcNow.AddDays(-7)) TryDelete(oldFile);
        }

        string path = Path.Combine(directory, $"AKASS-Update-{Version}-{Guid.NewGuid():N}.exe");
        string partial = path + ".download";
        try
        {
            using (var file = new FileStream(partial, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, true))
            {
                await CopyBoundedAsync(source, file, size, token).ConfigureAwait(false);
                if (file.Length != size) throw new InvalidDataException("The installer download is incomplete.");
                file.Position = 0;
                using var sha256 = SHA256.Create();
                var actual = BitConverter.ToString(sha256.ComputeHash(file)).Replace("-", "");
                if (!string.Equals(actual, digest, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The installer SHA-256 did not match the GitHub release.");
            }
            // Preserve Windows internet-download trust checks when the staged installer is opened.
            FileSafety.MarkInternetDownload(partial, download);
            token.ThrowIfCancellationRequested();
            File.Move(partial, path);
            return path;
        }
        finally { TryDelete(partial); }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static async Task CopyBoundedAsync(Stream source, Stream destination, long maximumBytes, CancellationToken token)
    {
        var buffer = new byte[81920];
        long total = 0;
        int count;
        while ((count = await source.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) > 0)
        {
            total += count;
            if (total > maximumBytes) throw new InvalidDataException("The GitHub download exceeds its expected size.");
            await destination.WriteAsync(buffer, 0, count, token).ConfigureAwait(false);
        }
    }

    [DataContract]
    private sealed class Release
    {
        [DataMember(Name = "tag_name")] public string? Tag { get; set; }
        [DataMember(Name = "draft")] public bool Draft { get; set; }
        [DataMember(Name = "prerelease")] public bool Prerelease { get; set; }
        [DataMember(Name = "assets")] public Asset?[]? Assets { get; set; }
    }

    [DataContract]
    private sealed class Asset
    {
        [DataMember(Name = "name")] public string? Name { get; set; }
        [DataMember(Name = "browser_download_url")] public string? Url { get; set; }
        [DataMember(Name = "state")] public string? State { get; set; }
        [DataMember(Name = "digest")] public string? Digest { get; set; }
        [DataMember(Name = "size")] public long Size { get; set; }
    }
}
