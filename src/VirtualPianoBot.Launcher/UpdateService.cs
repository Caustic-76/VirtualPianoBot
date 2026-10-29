using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VirtualPianoBot.Launcher;

internal sealed class LocalUpdateConfiguration
{
    public string Channel { get; set; } = "stable";
    public string Version { get; set; } = "6.0.2";
    public string ManifestUrl { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

internal sealed class UserUpdateConfiguration
{
    public string ManifestUrl { get; set; } = string.Empty;
}

internal sealed class RemoteUpdateManifest
{
    public int SchemaVersion { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string PackageUrl { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long? PackageSizeBytes { get; set; }
    public string ReleaseNotes { get; set; } = string.Empty;
    public string PublishedAtUtc { get; set; } = string.Empty;
}

internal sealed record CheckedUpdate(
    LocalUpdateConfiguration Local,
    RemoteUpdateManifest Remote,
    Uri ManifestUri,
    Uri PackageUri,
    Version CurrentVersion,
    Version LatestVersion);

internal sealed record PreparedUpdate(string JobRoot, string PayloadRoot, string ApplyScriptPath, string BackupRoot, string LogPath, string Version);

internal static class UpdateService
{
    private const long MaximumPackageBytes = 750L * 1024 * 1024;
    private const int MaximumManifestBytes = 256 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly HashSet<string> AllowedPackageFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "VPB6.exe",
        "README.txt",
        "updates.json",
        "runtime/VPB6Studio.exe"
    };

    public static LocalUpdateConfiguration LoadLocalConfiguration(string appRoot)
    {
        var path = Path.Combine(appRoot, "updates.json");
        if (!File.Exists(path))
            throw new FileNotFoundException("The local update configuration (updates.json) is missing.", path);

        var local = JsonSerializer.Deserialize<LocalUpdateConfiguration>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("The local update configuration is empty or invalid.");

        if (string.IsNullOrWhiteSpace(local.Version) || !Version.TryParse(local.Version, out _))
            throw new InvalidDataException("The installed VPB version in updates.json is invalid.");
        if (string.IsNullOrWhiteSpace(local.Channel))
            throw new InvalidDataException("The update channel in updates.json is missing.");

        var userConfigPath = GetUserUpdateConfigurationPath();
        if (File.Exists(userConfigPath))
        {
            try
            {
                var userConfig = JsonSerializer.Deserialize<UserUpdateConfiguration>(File.ReadAllText(userConfigPath), JsonOptions)
                    ?? throw new InvalidDataException("The user update-source configuration is empty.");
                local.ManifestUrl = userConfig.ManifestUrl ?? string.Empty;
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("The user update-source file is invalid. Open UPDATE SOURCE to replace it.", ex);
            }
        }

        return local;
    }

    public static void SaveManifestUrl(string manifestUrl)
    {
        var value = manifestUrl.Trim();
        if (value.Length > 2048)
            throw new InvalidDataException("The update manifest URL is too long.");
        var path = GetUserUpdateConfigurationPath();
        if (string.IsNullOrWhiteSpace(value))
        {
            if (File.Exists(path)) File.Delete(path);
            return;
        }
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !IsSecureHttpUri(uri))
            throw new InvalidDataException("The update manifest URL must be an absolute HTTPS URL without embedded credentials.");

        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var tempPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            var config = new UserUpdateConfiguration { ManifestUrl = value };
            File.WriteAllText(tempPath, JsonSerializer.Serialize(config, JsonOptions), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch (IOException) { }
            }
        }
    }

    private static string GetUserUpdateConfigurationPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VirtualPianoBot", "update-settings.json");

    public static async Task<CheckedUpdate> CheckAsync(LocalUpdateConfiguration local)
    {
        if (string.IsNullOrWhiteSpace(local.ManifestUrl))
            throw new UpdateNotConfiguredException("Online updates are not configured yet. click UPDATE SOURCE and enter the HTTPS address of your hosted update-manifest.json.");
        if (!Uri.TryCreate(local.ManifestUrl, UriKind.Absolute, out var manifestUri) || !IsSecureHttpUri(manifestUri))
            throw new InvalidDataException("The update manifest URL must be an absolute HTTPS URL without embedded credentials.");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var response = await Http.GetAsync(manifestUri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri is not Uri finalManifestUri || !IsSecureHttpUri(finalManifestUri))
            throw new InvalidDataException("The update manifest redirected to an insecure or invalid URL. HTTPS is required.");
        if (response.Content.Headers.ContentLength is long length && length > MaximumManifestBytes)
            throw new InvalidDataException("The online update manifest is unexpectedly large.");

        var json = await ReadBoundedTextAsync(response.Content, MaximumManifestBytes, timeout.Token);

        var remote = JsonSerializer.Deserialize<RemoteUpdateManifest>(json, JsonOptions)
            ?? throw new InvalidDataException("The online update manifest is empty or invalid.");
        if (remote.SchemaVersion != 1)
            throw new InvalidDataException($"Unsupported update manifest schema: {remote.SchemaVersion}.");
        if (!string.Equals(remote.Channel, local.Channel, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"The online manifest is for the '{remote.Channel}' channel, but this installation uses '{local.Channel}'.");
        if (!Version.TryParse(remote.Version, out var latestVersion))
            throw new InvalidDataException("The online manifest contains an invalid version number.");
        if (!Version.TryParse(local.Version, out var currentVersion))
            throw new InvalidDataException("The installed version number is invalid.");
        if (string.IsNullOrWhiteSpace(remote.PackageUrl))
            throw new InvalidDataException("The online manifest has no update package URL.");
        if (string.IsNullOrWhiteSpace(remote.Sha256) || remote.Sha256.Length != 64 || !remote.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("The online manifest does not contain a valid SHA-256 checksum.");
        if (remote.PackageSizeBytes is < 1 or > MaximumPackageBytes)
            throw new InvalidDataException("The package size in the online manifest is invalid or exceeds the 750 MB limit.");
        if (remote.ReleaseNotes is null)
            remote.ReleaseNotes = string.Empty;
        else if (remote.ReleaseNotes.Length > 12000)
            remote.ReleaseNotes = remote.ReleaseNotes[..12000];

        if (!Uri.TryCreate(finalManifestUri, remote.PackageUrl, out var packageUri) || !IsSecureHttpUri(packageUri))
            throw new InvalidDataException("The update package URL must resolve to an HTTPS URL without embedded credentials.");

        return new CheckedUpdate(local, remote, finalManifestUri, packageUri, currentVersion, latestVersion);
    }

    public static async Task<PreparedUpdate> DownloadAndPrepareAsync(CheckedUpdate update, string appRoot)
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var updatesRoot = Path.Combine(localData, "VirtualPianoBot", "updates");
        var jobRoot = Path.Combine(updatesRoot, "pending", $"{update.Remote.Version}-{Guid.NewGuid():N}");
        var payloadRoot = Path.Combine(jobRoot, "payload");
        Directory.CreateDirectory(payloadRoot);

        var packagePath = Path.Combine(jobRoot, "update-package.zip");
        using (var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15)))
        using (var response = await Http.GetAsync(update.PackageUri, HttpCompletionOption.ResponseHeadersRead, timeout.Token))
        {
            response.EnsureSuccessStatusCode();
            if (response.RequestMessage?.RequestUri is not Uri finalPackageUri || !IsSecureHttpUri(finalPackageUri))
                throw new InvalidDataException("The update package redirected to an insecure or invalid URL. HTTPS is required.");
            if (response.Content.Headers.ContentLength is long length && (length <= 0 || length > MaximumPackageBytes))
                throw new InvalidDataException("The update package size is invalid or exceeds the 750 MB limit.");

            await using var source = await response.Content.ReadAsStreamAsync(timeout.Token);
            await using var target = new FileStream(packagePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            var buffer = new byte[81920];
            long total = 0;
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), timeout.Token);
                if (read == 0) break;
                total += read;
                if (total > MaximumPackageBytes)
                    throw new InvalidDataException("The update package exceeded the 750 MB download limit.");
                await target.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
            }
            await target.FlushAsync(timeout.Token);
            if (total <= 0)
                throw new InvalidDataException("The downloaded update package is empty.");
            if (update.Remote.PackageSizeBytes.HasValue && total != update.Remote.PackageSizeBytes.Value)
                throw new InvalidDataException("The downloaded package size does not match the online manifest.");
        }

        string actualHash;
        await using (var packageStream = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
        {
            using var sha = SHA256.Create();
            actualHash = Convert.ToHexString(await sha.ComputeHashAsync(packageStream)).ToLowerInvariant();
        }
        var expectedHash = update.Remote.Sha256.ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(actualHash), Encoding.ASCII.GetBytes(expectedHash)))
            throw new InvalidDataException("The update package checksum does not match the manifest. VPB was not changed.");

        ValidateArchive(packagePath);
        ZipFile.ExtractToDirectory(packagePath, payloadRoot, overwriteFiles: false);
        ValidatePayload(payloadRoot);
        PreserveUpdateConfiguration(Path.Combine(payloadRoot, "updates.json"), update.Local, update.Remote);

        var backupParent = Path.Combine(localData, "VirtualPianoBot", "updates", "backups");
        var backupRoot = Path.Combine(backupParent, $"{update.Local.Version}-to-{update.Remote.Version}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(backupParent);
        var logPath = Path.Combine(jobRoot, "update.log");
        var scriptPath = Path.Combine(jobRoot, "apply-update.ps1");
        await File.WriteAllTextAsync(scriptPath, ApplyUpdateScript, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        // Confirm the target path is absolute before generating the external apply step.
        _ = Path.GetFullPath(appRoot);
        return new PreparedUpdate(jobRoot, payloadRoot, scriptPath, backupRoot, logPath, update.Remote.Version);
    }

    public static void StartApplyScript(PreparedUpdate prepared, string appRoot, int launcherProcessId)
    {
        var systemRoot = Environment.GetEnvironmentVariable("WINDIR");
        if (string.IsNullOrWhiteSpace(systemRoot))
            systemRoot = Directory.GetParent(Environment.SystemDirectory)?.FullName ?? @"C:\Windows";
        var powershell = Path.Combine(systemRoot, "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(powershell))
            throw new FileNotFoundException("Windows PowerShell was not found, so the downloaded update is ready but could not be installed automatically.", powershell);

        var needsElevation = !CanWriteToDirectory(appRoot);
        var arguments = "-NoProfile -ExecutionPolicy Bypass -File " + Quote(prepared.ApplyScriptPath)
            + " -LauncherPid " + launcherProcessId
            + " -TargetRoot " + Quote(Path.GetFullPath(appRoot))
            + " -StagedRoot " + Quote(Path.GetFullPath(prepared.PayloadRoot))
            + " -BackupRoot " + Quote(Path.GetFullPath(prepared.BackupRoot))
            + " -LogPath " + Quote(Path.GetFullPath(prepared.LogPath));

        var startInfo = new ProcessStartInfo
        {
            FileName = powershell,
            Arguments = arguments,
            UseShellExecute = true,
            WorkingDirectory = prepared.JobRoot,
            WindowStyle = ProcessWindowStyle.Normal
        };
        if (needsElevation) startInfo.Verb = "runas";

        _ = Process.Start(startInfo) ?? throw new InvalidOperationException("Windows PowerShell could not be started.");
    }

    private static void PreserveUpdateConfiguration(string path, LocalUpdateConfiguration local, RemoteUpdateManifest remote)
    {
        var node = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
            ?? throw new InvalidDataException("The downloaded package's updates.json is invalid.");
        node["channel"] = remote.Channel;
        node["version"] = remote.Version;
        node["manifestUrl"] = local.ManifestUrl;
        node["status"] = "update-client-enabled";
        node["message"] = string.IsNullOrWhiteSpace(local.ManifestUrl)
            ? "Online update checks are not configured. Set manifestUrl to your hosted update manifest."
            : "Online update checks are configured.";
        File.WriteAllText(path, node.ToJsonString(JsonOptions), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void ValidateArchive(string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var totalUncompressed = 0L;
        foreach (var entry in archive.Entries)
        {
            var raw = entry.FullName.Replace('\\', '/');
            var relative = raw.TrimEnd('/');
            var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            var dosAttributes = entry.ExternalAttributes & 0xFFFF;
            var isDirectory = raw.EndsWith("/", StringComparison.Ordinal)
                || (dosAttributes & (int)FileAttributes.Directory) != 0
                || unixType == 0x4000;
            if (string.IsNullOrWhiteSpace(relative)) continue;
            if (relative.StartsWith("/", StringComparison.Ordinal) || relative.Contains(':'))
                throw new InvalidDataException("The update package contains an absolute or invalid path.");
            if (relative.Split('/').Any(part => part is ".." or "." or ""))
                throw new InvalidDataException("The update package contains path traversal segments.");
            if (isDirectory)
            {
                if (!string.Equals(relative, "runtime", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Unexpected directory in update package: {relative}");
                continue;
            }
            if (!AllowedPackageFiles.Contains(relative))
                throw new InvalidDataException($"Unexpected file in update package: {relative}");
            if (!seen.Add(relative))
                throw new InvalidDataException($"Duplicate path in update package: {relative}");
            if (unixType == 0xA000)
                throw new InvalidDataException("Symbolic links are not allowed in update packages.");
            totalUncompressed += entry.Length;
            if (totalUncompressed > 1_500L * 1024 * 1024)
                throw new InvalidDataException("The expanded update package exceeds the safety limit.");
        }

        foreach (var required in new[] { "VPB6.exe", "README.txt", "updates.json", "runtime/VPB6Studio.exe" })
        {
            if (!seen.Contains(required))
                throw new InvalidDataException($"The update package is missing required file: {required}");
        }
    }

    private static void ValidatePayload(string payloadRoot)
    {
        foreach (var relative in AllowedPackageFiles)
        {
            var path = Path.GetFullPath(Path.Combine(payloadRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
            var root = Path.GetFullPath(payloadRoot) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                throw new InvalidDataException($"The extracted update payload is incomplete or contains an unsafe path: {relative}");
            if (new FileInfo(path).Length <= 0)
                throw new InvalidDataException($"The extracted update payload contains an empty file: {relative}");
        }
    }


    private static async Task<string> ReadBoundedTextAsync(HttpContent content, int maximumBytes, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var bufferStream = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0) break;
            if (bufferStream.Length + read > maximumBytes)
                throw new InvalidDataException("The online update manifest is unexpectedly large.");
            await bufferStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return Encoding.UTF8.GetString(bufferStream.ToArray()).TrimStart('\uFEFF');
    }

    private static bool IsSecureHttpUri(Uri uri) => uri.IsAbsoluteUri
        && uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        && string.IsNullOrEmpty(uri.UserInfo);

    private static bool CanWriteToDirectory(string directory)
    {
        try
        {
            var probe = Path.Combine(directory, ".vpb-update-probe-" + Guid.NewGuid().ToString("N"));
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            try { File.Delete(probe); } catch (IOException) { }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private const string ApplyUpdateScript = """
        param(
            [Parameter(Mandatory=$true)][int]$LauncherPid,
            [Parameter(Mandatory=$true)][string]$TargetRoot,
            [Parameter(Mandatory=$true)][string]$StagedRoot,
            [Parameter(Mandatory=$true)][string]$BackupRoot,
            [Parameter(Mandatory=$true)][string]$LogPath
        )
        $ErrorActionPreference = 'Stop'
        $files = @('VPB6.exe', 'README.txt', 'updates.json', 'runtime\VPB6Studio.exe')
        $originals = @{}
        $touched = New-Object 'System.Collections.Generic.List[string]'
        function Write-UpdateLog([string]$Message) {
            $line = ('[{0}] {1}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Message)
            try { Add-Content -LiteralPath $LogPath -Value $line -Encoding UTF8 } catch { }
            Write-Host $line
        }
        try {
            $TargetRoot = [IO.Path]::GetFullPath($TargetRoot)
            $StagedRoot = [IO.Path]::GetFullPath($StagedRoot)
            $BackupRoot = [IO.Path]::GetFullPath($BackupRoot)
            foreach ($relative in $files) {
                if (-not (Test-Path -LiteralPath (Join-Path $StagedRoot $relative) -PathType Leaf)) {
                    throw "Staged update is missing required file: $relative"
                }
            }
            Write-UpdateLog 'Update downloaded and verified. Waiting for VPB to close.'
            while (Get-Process -Id $LauncherPid -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 300 }
            while (Get-Process -Name 'VPB6Studio' -ErrorAction SilentlyContinue) {
                Write-UpdateLog 'Waiting for open Performance Studio window(s) to close.'
                Start-Sleep -Seconds 2
            }
            New-Item -ItemType Directory -Path $BackupRoot -Force | Out-Null
            foreach ($relative in $files) {
                $destination = Join-Path $TargetRoot $relative
                $source = Join-Path $StagedRoot $relative
                $backup = Join-Path $BackupRoot $relative
                $originals[$relative] = Test-Path -LiteralPath $destination -PathType Leaf
                if ($originals[$relative]) {
                    New-Item -ItemType Directory -Path (Split-Path -Parent $backup) -Force | Out-Null
                    Copy-Item -LiteralPath $destination -Destination $backup -Force
                }
            }
            foreach ($relative in $files) {
                $destination = Join-Path $TargetRoot $relative
                $source = Join-Path $StagedRoot $relative
                New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
                $touched.Add($relative)
                Copy-Item -LiteralPath $source -Destination $destination -Force
                if ((Get-Item -LiteralPath $source).Length -ne (Get-Item -LiteralPath $destination).Length) {
                    throw "Post-copy size validation failed for $relative"
                }
            }
            if (-not (Test-Path -LiteralPath (Join-Path $TargetRoot 'VPB6.exe') -PathType Leaf) -or
                -not (Test-Path -LiteralPath (Join-Path $TargetRoot 'runtime\VPB6Studio.exe') -PathType Leaf)) {
                throw 'Post-update executable validation failed.'
            }
            Write-UpdateLog 'Update installed successfully. The rollback backup has been preserved.'
            Start-Process -FilePath (Join-Path $TargetRoot 'VPB6.exe') -WorkingDirectory $TargetRoot
            exit 0
        }
        catch {
            Write-UpdateLog ('Update failed: ' + $_.Exception.Message)
            for ($i = $touched.Count - 1; $i -ge 0; $i--) {
                $relative = $touched[$i]
                $destination = Join-Path $TargetRoot $relative
                $backup = Join-Path $BackupRoot $relative
                try {
                    if ($originals.ContainsKey($relative) -and $originals[$relative] -and (Test-Path -LiteralPath $backup -PathType Leaf)) {
                        Copy-Item -LiteralPath $backup -Destination $destination -Force
                        Write-UpdateLog "Restored previous file: $relative"
                    } elseif (Test-Path -LiteralPath $destination -PathType Leaf) {
                        Remove-Item -LiteralPath $destination -Force
                        Write-UpdateLog "Removed incomplete new file: $relative"
                    }
                } catch { Write-UpdateLog ('Rollback warning for ' + $relative + ': ' + $_.Exception.Message) }
            }
            Write-UpdateLog 'Rollback finished. User data and the MIDI library were not targeted.'
            if (Test-Path -LiteralPath (Join-Path $TargetRoot 'VPB6.exe') -PathType Leaf) {
                try { Start-Process -FilePath (Join-Path $TargetRoot 'VPB6.exe') -WorkingDirectory $TargetRoot } catch { }
            }
            exit 1
        }
        """;
}

internal sealed class UpdateNotConfiguredException : InvalidOperationException
{
    public UpdateNotConfiguredException(string message) : base(message) { }
}
