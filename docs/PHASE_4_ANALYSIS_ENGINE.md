# VPB 6 Phase 4: Analysis Engine

Phase 4 connects the Analysis Studio to real MIDI data while preserving the permanent library at `%APPDATA%\\VirtualPianoBot\\midi`.

## Included
- Dependency-free MIDI parser for standard format 0/1 files.
- Tempo, note, duration, pitch and track extraction.
- Playability coverage against the proven C2-C7 keyboard envelope.
- Notes/sec and peak density.
- Typical note length, chord density and maximum overlap.
- Melody-candidate score and confidence heuristic.
- Difficulty classification.
- Recommended BPM, transpose and range mode.
- Difficult density sections and warnings.
- Analysis Studio UI and Apply Recommendation staging.
- Standalone analysis smoke test.

## Compatibility notes
The older VPB changelog identifies the C2-C7 range, richer track-analysis metrics, Smart Fit, chord grouping and physical-key conflict warnings as established behavior. This phase establishes the analysis service and keeps the playback-specific mapping controls for the Studio migration phase.

## Safety
No MIDI files are moved, deleted, overwritten or stored inside the installation directory by the analysis engine. Analysis reads the selected file only.
