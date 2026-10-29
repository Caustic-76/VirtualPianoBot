using System.Text.Json;
using System.Text.Json.Serialization;

namespace VirtualPianoBot.Core;

/// <summary>
/// User preferences that are safe to carry across application updates.
/// Keep this model version-independent and avoid installation-relative paths.
/// </summary>
public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public string Theme { get; set; } = "Dark";
    public string AccentColor { get; set; } = "#B8FF65";
    public bool ReduceMotion { get; set; }
    public string LastWorkspace { get; set; } = "Dashboard";
    public string? LastOpenedMidiPath { get; set; }
    public bool LegacyMidiMigrationCompleted { get; set; }
    public bool LegacyV5ConfigImported { get; set; }
    public string? LegacyLastMidiDirectory { get; set; }
    public List<string> LegacyV5Keymap { get; set; } = [];
    public JsonElement? LegacyV5ConfigSnapshot { get; set; }
    public JsonElement? LegacyV5AppSettingsSnapshot { get; set; }
    public List<string> RecentMidiPaths { get; set; } = [];
    public List<string> FavoriteMidiPaths { get; set; } = [];
    public double StudioVisualizerLookAheadSeconds { get; set; } = 6.0;
    public string StudioVisualizerColorMode { get; set; } = "Register";
    public string StudioVisualizerDetailMode { get; set; } = "Auto";
    public bool StudioVisualizerShowBeatGrid { get; set; } = true;
    public bool StudioVisualizerShowNoteLabels { get; set; }

    // Carry forward fields introduced by future builds that an older executable
    // does not know about, rather than discarding them on the next settings save.
    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalData { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
