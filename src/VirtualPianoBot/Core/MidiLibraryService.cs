namespace VirtualPianoBot.Core;

/// <summary>
/// Read-only library scanner plus a copy-only import operation.
/// Scanning never renames, deletes, edits, or relocates user files.
/// </summary>
public sealed class MidiLibraryService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mid", ".midi"
    };

    private readonly AppPaths _paths;
    private readonly AppLogger _logger;

    public MidiLibraryService(AppPaths paths, AppLogger logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public IReadOnlyList<MidiLibraryItem> Scan()
    {
        _paths.EnsureDirectories();
        var paths = EnumerateMidiFilePaths(_paths.MidiDirectory);
        var items = new List<MidiLibraryItem>(paths.Count);
        foreach (var path in paths)
        {
            try { items.Add(new MidiLibraryItem(path, _paths.MidiDirectory)); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                _logger.Warning($"A MIDI file could not be indexed: {path}. {exception.Message}");
            }
        }

        return items
            .OrderByDescending(item => item.ModifiedAt)
            .ThenBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Recursively discovers MIDI files without traversing symbolic links / reparse points.
    /// Access failures are isolated to the affected directory or file.
    /// </summary>
    public static List<string> EnumerateMidiFilePaths(string rootDirectory)
    {
        var results = new List<string>();
        if (string.IsNullOrWhiteSpace(rootDirectory) || !Directory.Exists(rootDirectory)) return results;

        string root;
        try { root = Path.GetFullPath(rootDirectory); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return results;
        }

        var pending = new Stack<string>();
        pending.Push(root);
        var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            IEnumerable<string> entries;
            try { entries = Directory.EnumerateFileSystemEntries(directory).ToArray(); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                try
                {
                    var attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) continue;

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        pending.Push(entry);
                        continue;
                    }

                    if (!SupportedExtensions.Contains(Path.GetExtension(entry))) continue;
                    var fullPath = Path.GetFullPath(entry);
                    if (seen.Add(fullPath)) results.Add(fullPath);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    // Continue scanning the rest of the collection.
                }
            }
        }

        results.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(
            Path.GetRelativePath(root, left), Path.GetRelativePath(root, right)));
        return results;
    }

    /// <summary>
    /// Copy-only import. Existing destination files are never overwritten.
    /// Folder structure under the selected source is retained.
    /// </summary>
    public MidiImportResult ImportFolder(string sourceDirectory)
    {
        if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
            throw new DirectoryNotFoundException("Choose an existing folder containing MIDI files.");

        var source = Path.GetFullPath(sourceDirectory);
        var destination = Path.GetFullPath(_paths.MidiDirectory);
        if (PathsOverlap(source, destination))
            throw new InvalidOperationException("The selected folder overlaps the permanent VPB MIDI library. Choose a separate source folder.");

        _paths.EnsureDirectories();
        var result = new MidiImportResult();

        foreach (var file in EnumerateMidiFilePaths(source))
        {
            try
            {
                var relative = Path.GetRelativePath(source, file);
                if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                {
                    result.Skipped++;
                    continue;
                }

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
                result.Failures.Add($"{Path.GetFileName(file)}: {exception.Message}");
            }
        }

        _logger.Info($"MIDI folder import completed. {result}");
        foreach (var failure in result.Failures) _logger.Warning(failure);
        return result;
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
}
