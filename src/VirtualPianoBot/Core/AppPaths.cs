namespace VirtualPianoBot.Core;

/// <summary>
/// Stable, version-independent locations for VPB user data.
/// The MIDI library deliberately lives outside the installation directory.
/// </summary>
public sealed class AppPaths
{
    public AppPaths(string? applicationDataRoot = null)
    {
        var root = string.IsNullOrWhiteSpace(applicationDataRoot)
            ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            : applicationDataRoot;

        if (string.IsNullOrWhiteSpace(root))
        {
            root = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        DataDirectory = Path.GetFullPath(Path.Combine(root, "VirtualPianoBot"));
        MidiDirectory = Path.Combine(DataDirectory, "midi");
        LogsDirectory = Path.Combine(DataDirectory, "logs");
        SettingsFile = Path.Combine(DataDirectory, "settings.json");
    }

    public string DataDirectory { get; }
    public string MidiDirectory { get; }
    public string LogsDirectory { get; }
    public string SettingsFile { get; }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(MidiDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}
