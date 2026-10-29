# Phase 4 · Visualiser Engine

## Scope

Improve the falling-note visualiser's clarity and dense-MIDI performance without changing the playback scheduler or native keyboard dispatch.

## Delivered

- A sorted note cache plus prefix-maximum end-time index narrows each render to notes that can be visible. The reactive keyboard uses the same indexed approach for active-note highlights.
- Adaptive draw budgets: Auto, Detail, and Performance. Active notes are still drawn even when future notes are sampled in a dense section.
- Colour modes: Register (pitch register), Track (stable track palette), and Velocity (softness-to-strength gradient).
- Adjustable fall window: 4, 6, 8, or 10 seconds.
- Optional beat/bar grid derived from the file's source BPM. Bar labels use a conventional 4-beat measure as a visual guide, not a guarantee of MIDI time-signature metadata.
- Optional note-name labels on sufficiently tall notes.
- Both falling lanes and the reactive keyboard show transposed pitches and follow the selected performance track filter.
- Visualiser preferences are retained in the existing settings file.

## Windows smoke-test checklist

1. Build with `BUILD_RELEASE.bat` on a machine with the .NET 10 SDK.
2. Load a sparse MIDI. Verify note bars fall toward the PLAY LINE, keyboard highlights follow, and the current position remains seekable.
3. Switch colour mode among Register, Track, and Velocity.
4. Toggle Beat Grid and Note Names, and select 4-, 6-, 8-, and 10-second windows.
5. Switch render detail among Auto, Detail, and Performance on a dense MIDI. Confirm the UI remains responsive and active notes remain visible.
6. Change transpose and verify falling notes and the keyboard shift together.
7. Choose Best Piano, Best Melody, then Custom Selection. Confirm visual lanes and active keyboard keys follow the selected tracks, including the empty custom-selection state.
8. Restart the app and confirm visualiser preferences persist.
9. Import a MIDI folder twice and confirm existing files are skipped, not overwritten or removed.

## Safety boundary

This phase changes the visualiser controls, rendering path, and visualizer preferences only. It does not alter `StudioPlaybackEngine` scheduling, native keyboard-input dispatch, the permanent `%APPDATA%\\VirtualPianoBot\\midi` location, or copy-only import behaviour.
