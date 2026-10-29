# VPB 6 Phase 4.5 - Analysis Visualisation

Adds a dedicated visual layer on top of the Phase 4 analysis engine.

## Included
- GPU-rendered density timeline with 48 analysis buckets.
- Peak-density trace and pressure bars.
- Difficult-section guide markers.
- Pitch-range envelope from lowest to highest MIDI note.
- Playable coverage indicator.
- Empty state when no analysis result exists.
- Uses the real `MidiAnalysisResult` produced by Phase 4.

## Safety
- No MIDI files are moved, deleted, overwritten or migrated by the visualiser.
- Persistent library location remains `%APPDATA%\\VirtualPianoBot\\midi`.
