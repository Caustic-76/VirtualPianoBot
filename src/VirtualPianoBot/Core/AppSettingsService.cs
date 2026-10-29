using System.Text.Json;

namespace VirtualPianoBot.Core;

public sealed class AppSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly AppPaths _paths;
    private readonly AppLogger _logger;

    public AppSettingsService(AppPaths paths, AppLogger logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public AppSettings Load()
    {
        AppSettings settings;
        if (!File.Exists(_paths.SettingsFile))
        {
            _logger.Info("No VPB 6 settings were found; defaults will be used.");
            settings = new AppSettings();
        }
        else
        {
            try
            {
                var json = File.ReadAllText(_paths.SettingsFile);
                settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
                settings.RecentMidiPaths ??= [];
                settings.FavoriteMidiPaths ??= [];
                settings.LegacyV5Keymap ??= [];
                settings.AdditionalData ??= new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                settings.LastWorkspace = NormalizeWorkspace(settings.LastWorkspace);
                _logger.Info($"Settings loaded from '{_paths.SettingsFile}'.");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                // Do not delete, rename, or overwrite a settings file that cannot be read.
                _logger.Error("Settings could not be read. Defaults will be used; the original file was left untouched.", exception);
                return new AppSettings();
            }
        }

        if (!settings.LegacyV5ConfigImported)
        {
            TryImportLegacyConfigSnapshot(settings);
        }

        return settings;
    }

    private void TryImportLegacyConfigSnapshot(AppSettings settings)
    {
        // VPB 5 stored key mappings, custom presets, and app_settings together in
        // %APPDATA%\VirtualPianoBot\config.json. Read it for compatibility, but
        // never write to or rename the original file.
        var legacyConfigPath = Path.Combine(_paths.DataDirectory, "config.json");
        if (!File.Exists(legacyConfigPath)) return;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(legacyConfigPath));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                _logger.Warning("Legacy config.json was not a JSON object; it was left untouched for manual recovery.");
                return;
            }

            settings.LegacyV5ConfigSnapshot = root.Clone();
            if (root.TryGetProperty("app_settings", out var appSettings)
                && appSettings.ValueKind == JsonValueKind.Object)
            {
                settings.LegacyV5AppSettingsSnapshot = appSettings.Clone();
                if (appSettings.TryGetProperty("last_midi_dir", out var lastDirectory)
                    && lastDirectory.ValueKind == JsonValueKind.String)
                    settings.LegacyLastMidiDirectory = lastDirectory.GetString();

                if (settings.RecentMidiPaths.Count == 0
                    && appSettings.TryGetProperty("recent_midi_files", out var recent)
                    && recent.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in recent.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.String) continue;
                        var candidate = item.GetString();
                        if (string.IsNullOrWhiteSpace(candidate)) continue;
                        var extension = Path.GetExtension(candidate);
                        if (extension.Equals(".mid", StringComparison.OrdinalIgnoreCase)
                            || extension.Equals(".midi", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!settings.RecentMidiPaths.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                                settings.RecentMidiPaths.Add(candidate);
                            if (settings.RecentMidiPaths.Count >= 8) break;
                        }
                    }
                }
            }

            if (root.TryGetProperty("keymap", out var keymap) && keymap.ValueKind == JsonValueKind.Array)
            {
                settings.LegacyV5Keymap = keymap.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString() ?? string.Empty)
                    .ToList();
            }

            settings.LegacyV5ConfigImported = true;
            _logger.Info("Legacy VPB 5 config.json was read and preserved in the VPB 6 settings snapshot. The original config.json was not changed.");
            if (!Save(settings))
                _logger.Warning("Legacy config was loaded but the VPB 6 settings snapshot could not be saved. The original config.json remains untouched; import will be retried on the next launch.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.Error("Legacy config.json could not be imported. It was left untouched and can be retried later.", exception);
        }
    }

    public bool Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string? temporaryFile = null;

        try
        {
            _paths.EnsureDirectories();
            settings.SchemaVersion = Math.Max(1, settings.SchemaVersion);
            settings.LastWorkspace = NormalizeWorkspace(settings.LastWorkspace);
            temporaryFile = Path.Combine(_paths.DataDirectory, $"settings.{Guid.NewGuid():N}.tmp");
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(temporaryFile, json);

            if (File.Exists(_paths.SettingsFile))
            {
                // Retain one previous copy before replacing preferences.
                try { File.Copy(_paths.SettingsFile, _paths.SettingsFile + ".bak", overwrite: true); }
                catch (Exception backupException) when (backupException is IOException or UnauthorizedAccessException)
                {
                    _logger.Warning($"Settings backup could not be written: {backupException.Message}");
                }
            }

            File.Move(temporaryFile, _paths.SettingsFile, overwrite: true);
            _logger.Info("Settings saved.");
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.Error("Settings could not be saved.", exception);
            return false;
        }
        finally
        {
            if (temporaryFile is not null)
            {
                try { if (File.Exists(temporaryFile)) File.Delete(temporaryFile); }
                catch { /* Best-effort cleanup only. */ }
            }
        }
    }

    private static string NormalizeWorkspace(string? value) => value switch
    {
        "Dashboard" or "Library" or "Analysis" or "Settings" => value,
        _ => "Dashboard"
    };
}
