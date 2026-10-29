using System.Text;
using VirtualPianoBot.Core;
using VirtualPianoBot.Analysis;
using VirtualPianoBot.Studio;

var failures = new List<string>();
var testRoot = Path.Combine(Path.GetTempPath(), "VPB6-FoundationChecks-" + Guid.NewGuid().ToString("N"));

void Check(bool condition, string description)
{
    Console.WriteLine($"{(condition ? "PASS" : "FAIL")}  {description}");
    if (!condition) failures.Add(description);
}

try
{
    var appDataRoot = Path.Combine(testRoot, "AppData");
    var paths = new AppPaths(appDataRoot);
    paths.EnsureDirectories();
    var logger = new AppLogger(paths);
    var library = new MidiLibraryService(paths, logger);

    Check(paths.MidiDirectory == Path.Combine(appDataRoot, "VirtualPianoBot", "midi"),
        "MIDI path is stable and version-independent.");
    Check(Directory.Exists(paths.MidiDirectory) && Directory.Exists(paths.LogsDirectory),
        "Data and log directories are created.");

    var importSource = Path.Combine(testRoot, "import-source");
    Directory.CreateDirectory(Path.Combine(importSource, "Composer", "Album"));
    File.WriteAllBytes(Path.Combine(importSource, "Composer", "Album", "Song.mid"), [1, 2, 3, 4]);
    File.WriteAllBytes(Path.Combine(importSource, "ignored.txt"), [9, 9]);
    var import = library.ImportFolder(importSource);
    Check(import.Copied == 1 && import.Failures.Count == 0,
        "Folder import copies supported MIDI files only.");
    Check(File.ReadAllBytes(Path.Combine(paths.MidiDirectory, "Composer", "Album", "Song.mid")).SequenceEqual(new byte[] { 1, 2, 3, 4 }),
        "Import preserves file bytes and subfolder layout.");

    File.WriteAllBytes(Path.Combine(paths.MidiDirectory, "Composer", "Album", "Song.mid"), [8, 8, 8]);
    var collisionImport = library.ImportFolder(importSource);
    Check(collisionImport.Skipped == 1,
        "Import skips a filename collision rather than overwriting it.");
    Check(File.ReadAllBytes(Path.Combine(paths.MidiDirectory, "Composer", "Album", "Song.mid")).SequenceEqual(new byte[] { 8, 8, 8 }),
        "Existing destination bytes remain unchanged after a collision.");

    var rejectedSelfImport = false;
    try { _ = library.ImportFolder(paths.MidiDirectory); }
    catch (InvalidOperationException) { rejectedSelfImport = true; }
    Check(rejectedSelfImport, "Import refuses to recursively import the permanent library into itself.");

    File.WriteAllBytes(Path.Combine(paths.MidiDirectory, "second.MIDI"), [5]);
    File.WriteAllBytes(Path.Combine(paths.MidiDirectory, "not-a-midi.wav"), [6]);
    var scanned = library.Scan();
    Check(scanned.Count == 2,
        "Library scan finds .mid and .midi recursively and ignores unrelated extensions.");

    var legacyRoot = Path.Combine(testRoot, "installs");
    var currentInstall = Path.Combine(legacyRoot, "VirtualPianoBot_v6");
    var legacyInstall = Path.Combine(legacyRoot, "VirtualPianoBot_v5_1_0");
    Directory.CreateDirectory(Path.Combine(currentInstall, "app"));
    Directory.CreateDirectory(Path.Combine(legacyInstall, "app"));
    Directory.CreateDirectory(Path.Combine(legacyInstall, "midi"));
    File.WriteAllText(Path.Combine(legacyInstall, "app", "virtual_piano_bot.py"), "marker");
    File.WriteAllBytes(Path.Combine(legacyInstall, "midi", "Legacy.mid"), [1, 3, 3, 7]);
    File.WriteAllBytes(Path.Combine(legacyInstall, "midi", "Composer.mid"), [4, 4, 4]);
    File.WriteAllBytes(Path.Combine(paths.MidiDirectory, "Composer.mid"), [7, 7, 7]);

    var migration = new LegacyMidiMigrationService(paths, logger, new AppSettings()).MigrateKnownLegacyFolders(currentInstall);
    Check(migration.Copied >= 1,
        "Recognized sibling VPB legacy installation is migrated by copying.");
    Check(File.Exists(Path.Combine(legacyInstall, "midi", "Legacy.mid")),
        "Legacy source files remain in their original folder.");
    Check(File.ReadAllBytes(Path.Combine(paths.MidiDirectory, "Legacy.mid")).SequenceEqual(new byte[] { 1, 3, 3, 7 }),
        "Legacy file contents are copied correctly.");
    Check(File.ReadAllBytes(Path.Combine(paths.MidiDirectory, "Composer.mid")).SequenceEqual(new byte[] { 7, 7, 7 }),
        "Migration never overwrites a destination collision.");

    var configuredLegacyFolder = Path.Combine(testRoot, "legacy-configured-midi");
    Directory.CreateDirectory(configuredLegacyFolder);
    File.WriteAllBytes(Path.Combine(configuredLegacyFolder, "Configured.mid"), [9, 8, 7]);
    var configuredSettings = new AppSettings { LegacyLastMidiDirectory = configuredLegacyFolder };
    var configuredMigration = new LegacyMidiMigrationService(paths, logger, configuredSettings)
        .MigrateKnownLegacyFolders(currentInstall);
    Check(configuredMigration.Copied == 1,
        "Configured legacy MIDI directory is discovered and migrated.");
    Check(File.Exists(Path.Combine(configuredLegacyFolder, "Configured.mid")),
        "Configured legacy MIDI source remains untouched.");

    var settingsService = new AppSettingsService(paths, logger);
    var legacyConfigPath = Path.Combine(paths.DataDirectory, "config.json");
    var legacyConfig = Encoding.UTF8.GetBytes("{\"keymap\":[\"A\",\"S\"],\"custom_presets\":{\"demo\":{}},\"app_settings\":{\"speed\":\"125\",\"last_midi_dir\":\"C:\\\\Songs\\\\MIDI\",\"recent_midi_files\":[\"C:\\\\Songs\\\\song.mid\"]}}");
    File.WriteAllBytes(legacyConfigPath, legacyConfig);
    var importedSettings = settingsService.Load();
    Check(importedSettings.LegacyV5ConfigImported && importedSettings.LegacyV5Keymap.Count == 2,
        "Legacy VPB 5 config is imported into a preserved VPB 6 snapshot.");
    Check(importedSettings.LegacyV5ConfigSnapshot?.GetRawText().Contains("custom_presets", StringComparison.Ordinal) == true,
        "Legacy custom presets remain present in the preserved config snapshot.");
    Check(File.ReadAllBytes(legacyConfigPath).SequenceEqual(legacyConfig),
        "Original VPB 5 config.json bytes remain unchanged by import.");

    importedSettings.StudioVisualizerLookAheadSeconds = 10;
    importedSettings.StudioVisualizerColorMode = "Velocity";
    importedSettings.StudioVisualizerDetailMode = "Performance";
    importedSettings.StudioVisualizerShowBeatGrid = false;
    importedSettings.StudioVisualizerShowNoteLabels = true;
    Check(settingsService.Save(importedSettings),
        "Visualiser preferences can be saved through the existing settings service.");
    var visualizerSettings = settingsService.Load();
    Check(visualizerSettings.StudioVisualizerLookAheadSeconds == 10
        && visualizerSettings.StudioVisualizerColorMode == "Velocity"
        && visualizerSettings.StudioVisualizerDetailMode == "Performance"
        && !visualizerSettings.StudioVisualizerShowBeatGrid
        && visualizerSettings.StudioVisualizerShowNoteLabels,
        "Visualiser preferences survive a settings-file round trip.");

    visualizerSettings.Theme = "Midnight";
    visualizerSettings.AccentColor = "#B69CFF";
    visualizerSettings.ReduceMotion = true;
    Check(settingsService.Save(visualizerSettings),
        "Theme, accent, and reduce-motion preferences can be saved.");
    var appearanceSettings = settingsService.Load();
    Check(appearanceSettings.Theme == "Midnight"
        && appearanceSettings.AccentColor == "#B69CFF"
        && appearanceSettings.ReduceMotion,
        "Studio customisation preferences survive a settings-file round trip.");

    // Regression: repeated chord notes that touch end-to-start must emit both a
    // key-up and a key-down at each shared boundary. Previously the mutually
    // exclusive isUp/isDown branch dropped the successor note-on.
    var repeatedChordNotes = new List<MidiNote>();
    foreach (var pitch in new[] { 55, 58, 62 })
    {
        for (var repetition = 0; repetition < 3; repetition++)
        {
            var start = repetition * 100.0;
            repeatedChordNotes.Add(new MidiNote(start, start + 100.0, pitch, 100, 0, 0));
        }
    }
    var repeatedChordResult = new MidiAnalysisResult(
        "repeated-chord-regression.mid", repeatedChordNotes.Count, 300, 120, 1,
        55, 62, 9, 9, 100, 1, 3, 1, 0, 0, 1,
        "Moderate", "Synthetic repeated-chord regression sample", "100%", 120, 0,
        "Exact Pitch", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<double>(), repeatedChordNotes);
    var repeatedChordPlan = new KeyboardMappingService().BuildPlan(repeatedChordResult, 0, "Exact Pitch");
    Check(repeatedChordPlan.Actions.Count(action => action.IsNoteOn) == 9,
        "Repeated chord plan emits every note-on across exact end/start boundaries.");
    Check(repeatedChordPlan.Actions.Count(action => !action.IsNoteOn) == 9,
        "Repeated chord plan emits every matching note-off.");
    foreach (var boundary in new[] { 100.0, 200.0 })
    {
        foreach (var pitch in new[] { 55, 58, 62 })
        {
            var boundaryActions = repeatedChordPlan.Actions
                .Where(action => action.TimeMs == boundary && action.Pitch == pitch)
                .ToList();
            Check(boundaryActions.Count == 2
                && !boundaryActions[0].IsNoteOn
                && boundaryActions[1].IsNoteOn,
                $"Pitch {pitch} retriggers as key-up/key-down at {boundary:0} ms.");
        }
    }

    var corruptSettings = Encoding.UTF8.GetBytes("{ this is not json }");
    File.WriteAllBytes(paths.SettingsFile, corruptSettings);
    _ = settingsService.Load();
    Check(File.ReadAllBytes(paths.SettingsFile).SequenceEqual(corruptSettings),
        "Unreadable VPB 6 settings are not deleted or rewritten by loading.");
}
catch (Exception exception)
{
    Console.WriteLine("FATAL  " + exception);
    failures.Add("Unexpected test harness exception: " + exception.Message);
}
finally
{
    try { if (Directory.Exists(testRoot)) Directory.Delete(testRoot, recursive: true); }
    catch { /* Temporary test cleanup only. */ }
}

Console.WriteLine();
Console.WriteLine(failures.Count == 0
    ? "All VPB 6 foundation safety checks passed."
    : $"{failures.Count} foundation check(s) failed.");
return failures.Count == 0 ? 0 : 1;
