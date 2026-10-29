using VirtualPianoBot.Core;

namespace VirtualPianoBot.Services;

/// <summary>Composition root for the first VPB 6 foundation milestone.</summary>
public static class AppServices
{
    private static bool _initialized;

    public static AppPaths Paths { get; private set; } = null!;
    public static AppLogger Logger { get; private set; } = null!;
    public static AppSettingsService SettingsService { get; private set; } = null!;
    public static MidiLibraryService LibraryService { get; private set; } = null!;
    public static LegacyMidiMigrationService LegacyMigrationService { get; private set; } = null!;
    public static AppSettings Settings { get; private set; } = null!;

    public static void Initialize()
    {
        if (_initialized) return;

        Paths = new AppPaths();
        Paths.EnsureDirectories();
        Logger = new AppLogger(Paths);
        SettingsService = new AppSettingsService(Paths, Logger);
        Settings = SettingsService.Load();
        LibraryService = new MidiLibraryService(Paths, Logger);
        LegacyMigrationService = new LegacyMidiMigrationService(Paths, Logger, Settings);
        _initialized = true;
    }
}
