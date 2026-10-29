# VPB 6 Phase 8 - Direct Studio Playback

Performance Studio no longer depends on the Analysis workspace. Selecting a MIDI loads a lightweight playback source directly from the MIDI file: note events, timing, tempo, track count and pitch bounds. The expensive playability/density/melody/difficulty analysis remains optional.

## Behaviour
- Select a MIDI -> it is immediately loaded into Performance Studio.
- Press PLAY without visiting Analysis.
- ARM INPUT is still required before native Windows keyboard events are sent.
- Analysis remains available as a separate optional workspace.
- Studio transport speed is applied by the transport engine, so keyboard action timestamps remain in source MIDI time.
- Persistent MIDI storage remains `%APPDATA%\VirtualPianoBot\midi`.
