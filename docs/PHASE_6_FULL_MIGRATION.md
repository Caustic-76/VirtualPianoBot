# Archived implementation notes · Keyboard Mapping Migration

> Historical design note. This file records earlier work on keyboard mapping that remains part of the application. In the final VPB6 roadmap, **Phase 6 Smart Automation was intentionally skipped**; this document does not mean the Smart Automation roadmap features are included in 6.0.0.


Phase 6 brings the proven VPB keyboard-mapping concepts into the C# Studio without modifying the permanent MIDI library.

## Included

- Version-independent `KeyboardMappingService`.
- Read-only reuse of the VPB 5 keymap when it is available in the preserved settings snapshot.
- Deterministic fallback QWERTY mapping for C2-C7.
- MIDI-to-key action planning with note-on and note-off actions.
- Transpose-aware mapping.
- Playback-speed-aware action timing.
- Playable/skipped note accounting.
- Unique mapped-key and conflict reporting.
- Studio input-plan dashboard showing the migration state.
- Playback engine can retain the prepared action plan independently of transport state.

## Safety boundary

Phase 6 prepares and validates keyboard actions but does not inject keystrokes into Windows. This keeps the native input adapter isolated until Phase 7 release hardening.

## Permanent MIDI library

The library remains `%APPDATA%\\VirtualPianoBot\\midi`. No migration or action-planning code moves, deletes, rewrites, or resets MIDI files.
