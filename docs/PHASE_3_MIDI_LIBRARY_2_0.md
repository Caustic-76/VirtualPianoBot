# VPB6 Phase 3 · MIDI Library 2.0

## Included

- Persistent favourites stored in the version-independent `%APPDATA%\\VirtualPianoBot\\settings.json` file by library-relative path.
- Search by MIDI title, filename, or relative folder path.
- Quick filters for all MIDI files, favourites, and files modified in the last 30 days.
- Sorting by recently modified, title A-Z, largest file first, or folder.
- Folder import using the native Avalonia folder picker.
- Copy-only import that preserves folder structure and never overwrites an existing destination file.
- Import summary reporting copied, skipped, and failed items.
- Selected MIDI details with actions to favourite it, open its file location, or move to Performance Studio.
- Collection totals for file count, favourites, recent files, and total library size.

## Data safety

- The permanent MIDI library remains `%APPDATA%\\VirtualPianoBot\\midi`.
- No delete, move, rename, or overwrite action is introduced.
- Import keeps source files untouched, skips files whose destination already exists, and rejects source/destination paths that overlap.
- Library scanning continues to skip symbolic links and filesystem reparse points.
- Favourites are stored as relative paths in the existing settings file. Settings backup/atomic-save behaviour remains in place.
- Playback scheduling, falling-notes rendering, instrument selection, and Phase 2 HUD telemetry are not intentionally changed.

## Build and smoke test

Run `BUILD_RELEASE.bat` on Windows with the .NET 10 SDK. Verify: library scan, folder import, duplicate-file skip, Favorites filter across app restart, all sort modes, selected MIDI loading into Studio, and the Phase 2 HUD. The editing environment used to prepare this source did not have the .NET SDK installed, so a successful compile is not claimed here.
