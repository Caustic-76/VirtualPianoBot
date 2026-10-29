using System.Text.Json;

namespace VirtualPianoBot.Core;

public sealed class LegacyMidiMigrationService
{
    private readonly AppPaths _paths;
    private readonly AppLogger _logger;
    private readonly AppSettings _settings;

    public LegacyMidiMigrationService(AppPaths paths, AppLogger logger, AppSettings settings)
    {
        _paths = paths;
        _logger = logger;
        _settings = settings;
    }

    /// <summary>
    /// Performs an idempotent, copy-only migration from older VPB libraries.
    /// This is intentionally safe to run on every launch: existing destination
    /// files are never overwritten and source files are never deleted.
    /// </summary>
    public MidiImportResult MigrateKnownLegacyFolders(string currentInstallDirectory)
    {
        _paths.EnsureDirectories();
        var destination = Path.GetFullPath(_paths.MidiDirectory);
        var result = new MidiImportResult();

        foreach (var sourceFolder in FindKnownLegacyMidiFolders(currentInstallDirectory, destination))
        {
            var source = Path.GetFullPath(sourceFolder);
            _logger.Info($"Checking recognized legacy MIDI folder: {source}");

            foreach (var file in MidiLibraryService.EnumerateMidiFilePaths(source))
            {
                try
                {
                    var relative = Path.GetRelativePath(source, file);
                    var target = Path.GetFullPath(Path.Combine(destination, relative));
                    if (!IsWithinDirectory(target, destination))
                    {
                        result.Skipped++;
                        continue;
                    }

                    if (File.Exists(target))
                    {
                        result.Skipped++;
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    try
                    {
                        File.Copy(file, target, overwrite: false);
                        result.Copied++;
                    }
                    catch (IOException) when (File.Exists(target))
                    {
                        result.Skipped++;
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    result.Failures.Add($"{file}: {exception.Message}");
                }
            }
        }

        if (result.Copied > 0 || result.Skipped > 0 || result.HasFailures)
        {
            _logger.Info($"Legacy MIDI migration finished. {result} Existing library files were not overwritten and source files were kept.");
        }
        else
        {
            _logger.Info("Legacy MIDI migration found no importable legacy files. It remains safe to retry on a later launch.");
        }

        foreach (var failure in result.Failures)
            _logger.Warning($"Legacy migration: {failure}");

        return result;
    }

    private IReadOnlyList<string> FindKnownLegacyMidiFolders(string currentInstallDirectory, string destination)
    {
        var candidates = new List<string>();
        var seen = new HashSet<string>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);

        void AddCandidate(string? folder, bool requireMidiFiles = true)
        {
            if (string.IsNullOrWhiteSpace(folder)) return;
            string resolved;
            try { resolved = Path.GetFullPath(Environment.ExpandEnvironmentVariables(folder)); }
            catch { return; }
            if (!Directory.Exists(resolved) || PathsOverlap(resolved, destination)) return;
            if (seen.Contains(resolved)) return;
            if (requireMidiFiles && !MidiLibraryService.EnumerateMidiFilePaths(resolved).Any()) return;
            seen.Add(resolved);
            candidates.Add(resolved);
        }

        // 1. Explicit path carried forward from the old VPB configuration.
        AddCandidate(_settings.LegacyLastMidiDirectory);

        // 2. Recent MIDI paths carried forward from the old configuration.
        foreach (var recentPath in _settings.RecentMidiPaths)
        {
            try
            {
                var expanded = Environment.ExpandEnvironmentVariables(recentPath);
                if (File.Exists(expanded))
                    AddCandidate(Path.GetDirectoryName(Path.GetFullPath(expanded)));
                else if (Directory.Exists(expanded))
                    AddCandidate(expanded);
            }
            catch { /* A stale recent path is harmless. */ }
        }

        string currentRoot;
        try { currentRoot = Path.GetFullPath(currentInstallDirectory); }
        catch { return candidates; }

        // 3. Recover the Python/Windows Store redirected APPDATA location.
        // Older VPB builds were Python applications. When Python 3.x came from
        // the Microsoft Store, its APPDATA-backed data can live under:
        //   %LOCALAPPDATA%\Packages\PythonSoftwareFoundation.Python.*\LocalCache\Roaming\VirtualPianoBot\midi
        // VPB 6 is native .NET, so Environment.SpecialFolder.ApplicationData
        // resolves to the normal %APPDATA% location instead. This explicit
        // discovery bridges those two locations without moving or deleting the
        // original collection.
        foreach (var redirectedRoot in FindPythonRedirectedRoamingRoots())
            AddCandidate(Path.Combine(redirectedRoot, "midi"));

        // 4. Search sibling VPB installs, including their config.json files.
        // Older builds could remember a custom MIDI directory instead of using
        // their own <install>\midi folder. That is the case the first Phase 1
        // migration missed.
        var parentInfo = Directory.GetParent(currentRoot);
        for (var depth = 0; depth < 4 && parentInfo is not null; depth++, parentInfo = parentInfo.Parent)
        {
            IEnumerable<string> siblingDirectories;
            try { siblingDirectories = Directory.EnumerateDirectories(parentInfo.FullName).ToArray(); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var sibling in siblingDirectories)
            {
                if (PathsEqual(sibling, currentRoot)) continue;
                var directoryName = Path.GetFileName(sibling);
                if (!directoryName.StartsWith("VirtualPianoBot_v", StringComparison.OrdinalIgnoreCase)
                    && !directoryName.StartsWith("VirtualPianoBot-v", StringComparison.OrdinalIgnoreCase))
                    continue;

                AddCandidate(Path.Combine(sibling, "midi"));
                ImportLegacyConfigPaths(Path.Combine(sibling, "config.json"), path => AddCandidate(path));
                ImportLegacyConfigPaths(Path.Combine(sibling, "app", "config.json"), path => AddCandidate(path));
                DiscoverMidiFoldersInsideLegacyInstall(sibling, path => AddCandidate(path));
            }
        }

        // 5. Search the user's common VPB download/install area for old
        // version folders. This is deliberately constrained, rather than
        // scanning the whole profile for arbitrary MIDI files.
        foreach (var root in GetCommonLegacyRoots(currentRoot))
        {
            IEnumerable<string> directories;
            try { directories = Directory.EnumerateDirectories(root, "VirtualPianoBot_v*", SearchOption.TopDirectoryOnly).ToArray(); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var directory in directories)
            {
                AddCandidate(Path.Combine(directory, "midi"));
                ImportLegacyConfigPaths(Path.Combine(directory, "config.json"), path => AddCandidate(path));
                ImportLegacyConfigPaths(Path.Combine(directory, "app", "config.json"), path => AddCandidate(path));
                DiscoverMidiFoldersInsideLegacyInstall(directory, path => AddCandidate(path));
            }
        }

        return candidates;
    }


    private void DiscoverMidiFoldersInsideLegacyInstall(string installDirectory, Action<string?> addCandidate)
    {
        if (!Directory.Exists(installDirectory)) return;

        var ignoredNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".venv", "venv", "__pycache__", "bin", "obj", "logs", "node_modules" };

        var pending = new Stack<(string Directory, int Depth)>();
        pending.Push((installDirectory, 0));
        var visited = new HashSet<string>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);

        while (pending.Count > 0)
        {
            var (directory, depth) = pending.Pop();
            if (depth > 5) continue;

            string resolved;
            try { resolved = Path.GetFullPath(directory); }
            catch { continue; }
            if (!visited.Add(resolved)) continue;

            // Only promote directories that actually contain MIDI files. This
            // keeps the migration targeted and avoids copying unrelated data.
            if (MidiLibraryService.EnumerateMidiFilePaths(resolved).Count > 0)
                addCandidate(resolved);

            IEnumerable<string> children;
            try { children = Directory.EnumerateDirectories(resolved).ToArray(); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var child in children)
            {
                var name = Path.GetFileName(child);
                if (ignoredNames.Contains(name)) continue;
                pending.Push((child, depth + 1));
            }
        }
    }

    private void ImportLegacyConfigPaths(string configPath, Action<string?> addCandidate)
    {
        if (!File.Exists(configPath)) return;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(configPath));
            var root = document.RootElement;
            if (root.TryGetProperty("app_settings", out var appSettings)
                && appSettings.ValueKind == JsonValueKind.Object)
            {
                if (appSettings.TryGetProperty("last_midi_dir", out var lastDir)
                    && lastDir.ValueKind == JsonValueKind.String)
                    addCandidate(lastDir.GetString());

                if (appSettings.TryGetProperty("recent_midi_files", out var recent)
                    && recent.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in recent.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.String) continue;
                        var path = item.GetString();
                        if (string.IsNullOrWhiteSpace(path)) continue;
                        try
                        {
                            var expanded = Environment.ExpandEnvironmentVariables(path);
                            if (File.Exists(expanded))
                                addCandidate(Path.GetDirectoryName(Path.GetFullPath(expanded)));
                        }
                        catch { /* Stale legacy history is harmless. */ }
                    }
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.Warning($"Could not inspect legacy config '{configPath}': {exception.Message}");
        }
    }

    private static IEnumerable<string> FindPythonRedirectedRoamingRoots()
    {
        var roots = new HashSet<string>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);

        if (!OperatingSystem.IsWindows()) return roots;

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData)) return roots;

        var packagesRoot = Path.Combine(localAppData, "Packages");
        if (!Directory.Exists(packagesRoot)) return roots;

        IEnumerable<string> packageDirectories;
        try
        {
            packageDirectories = Directory.EnumerateDirectories(
                packagesRoot,
                "PythonSoftwareFoundation.Python.*",
                SearchOption.TopDirectoryOnly).ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return roots;
        }

        foreach (var packageDirectory in packageDirectories)
        {
            var redirectedRoot = Path.Combine(
                packageDirectory, "LocalCache", "Roaming", "VirtualPianoBot");

            if (Directory.Exists(Path.Combine(redirectedRoot, "midi")))
                roots.Add(Path.GetFullPath(redirectedRoot));
        }

        return roots;
    }

    private static IEnumerable<string> GetCommonLegacyRoots(string currentInstallDirectory)
    {
        var roots = new HashSet<string>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            roots.Add(Path.Combine(userProfile, "Downloads"));
            roots.Add(Path.Combine(userProfile, "Documents"));
            roots.Add(Path.Combine(userProfile, "Desktop"));
        }

        try
        {
            var current = new DirectoryInfo(currentInstallDirectory);
            for (var i = 0; i < 3 && current is not null; i++, current = current.Parent)
                roots.Add(current.FullName);
        }
        catch { }

        return roots.Where(Directory.Exists);
    }

    private static bool PathsOverlap(string first, string second) =>
        IsWithinDirectory(first, second) || IsWithinDirectory(second, first);

    private static bool IsWithinDirectory(string path, string directory)
    {
        var relative = Path.GetRelativePath(directory, path);
        return relative == "." || (!Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !relative.StartsWith("../", StringComparison.Ordinal));
    }

    private static bool PathsEqual(string first, string second) =>
        string.Equals(Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
