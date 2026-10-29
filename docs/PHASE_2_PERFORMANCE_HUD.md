# Phase 2: Performance HUD

## Included

- [x] Live HUD over the falling-note canvas.
- [x] Effective tempo calculated from source BPM and playback speed.
- [x] Rolling one-second note-on rate and session peak rate.
- [x] Active voice count and compact active-pitch/chord readout.
- [x] Note-on event count, selected track count, input-armed state, and playback progress.
- [x] HUD visibility toggle in the Studio transport.
- [x] HUD telemetry clears on MIDI load, restart, stop, and panic; pause/seek clear active voices and the rolling rate.
- [x] Playback scheduler and native input dispatch policy left unchanged.
- [x] Engine change notifications are marshalled to the Avalonia UI thread before updating bound properties.

## Windows verification

1. Build with `BUILD_RELEASE.bat`.
2. Open a MIDI and verify the HUD starts at zero and reads READY.
3. Play a sparse MIDI and a dense MIDI. Confirm live rate, peak rate, active voices, note-ons, progress, and tempo update.
4. Pause, seek, restart, stop, and use PANIC. Confirm telemetry behaves as described.
5. Toggle HUD visibility without changing playback.
6. Confirm ARM INPUT / VISUAL ONLY reflects the current input state.
7. Confirm the library at `%APPDATA%\VirtualPianoBot\midi` remains present and unchanged.

The repository/container does not include the .NET SDK, so final compilation must be confirmed on the Windows development machine.
