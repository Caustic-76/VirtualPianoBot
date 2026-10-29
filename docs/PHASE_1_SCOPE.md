# Phase 1 scope and acceptance checklist

## Included

- [x] Target .NET 10 and Avalonia 12.1.4.
- [x] Application entry point and composition root.
- [x] MVVM view model, commands, and basic navigation.
- [x] Dashboard, MIDI Library, Analysis placeholder, and Settings shell.
- [x] Persistent per-user paths separated from the executable folder.
- [x] Append-only startup/error logs.
- [x] Atomic settings write with a previous-settings backup.
- [x] Recursive library scanner that ignores reparse points and continues after inaccessible paths.
- [x] Copy-only legacy MIDI migration with path guards and no-overwrite behavior.
- [x] Build and Windows x64 publish scripts.

## Intentionally not claimed as complete

- MIDI parsing, timing, note scheduling, or playback.
- Simulated keyboard input.
- Audio engine, separation, or waveform analysis.
- Playability scoring or recommendation logic.
- Auto-update and installer.

Those systems must be migrated and regression-tested, not faked in the new UI.

## Acceptance checks for the Windows build

1. `dotnet restore` completes.
2. `dotnet build -c Release` completes with no XAML-loading errors.
3. First launch creates the stable data directories under `%APPDATA%\VirtualPianoBot`.
4. Launch indexes existing `.mid` and `.midi` files without changing their contents.
5. A recognized old VPB version can contribute files to the permanent library, but source files stay in place.
6. If a destination filename already exists, its bytes are unchanged.
7. An unparseable settings JSON file is not deleted automatically.
8. Exceptions are written to the per-user log file.
9. Publishing as self-contained Windows x64 starts without needing Python or PySide6.
