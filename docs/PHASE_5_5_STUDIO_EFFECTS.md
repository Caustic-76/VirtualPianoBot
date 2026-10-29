# VPB 6 Phase 5.5 - Studio Effects

Phase 5.5 is the visual performance pass on top of the Phase 5 transport.

## Included
- Animated atmospheric scan bands.
- Beat-style grid pulses and corner markers.
- Reactive playhead glow.
- Active-note energy trails and pulses.
- Live reactive piano keyboard driven by currently active MIDI pitches.
- 60 FPS visual animation loop with reduced idle motion when paused.
- Existing transport, analysis, and persistent MIDI library remain unchanged.

The effects are intentionally rendered as non-interactive overlays so they cannot intercept transport controls.

Persistent MIDI storage remains `%APPDATA%\\VirtualPianoBot\\midi`.
