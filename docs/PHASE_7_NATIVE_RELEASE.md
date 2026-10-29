# Native Windows Input · Implementation Reference

This document records the existing native-input and transport foundation that is included in the VPB 6 project. The current roadmap's Phase 7 is **Reliability & Release Candidate**; native keyboard input is an existing feature and is not being reimplemented by this phase.

## Native input safety boundary

- `NativeKeyboardInputService` uses Windows `SendInput`.
- Input starts **disarmed by default**.
- The Studio exposes an explicit **ARM INPUT** control.
- When armed, mapped actions are sent to the current foreground Windows application.
- `PANIC STOP` releases all tracked keys.
- Pausing, stopping, seeking, or disarming also releases tracked keys.
- Shift is reference-counted so overlapping shifted notes do not release Shift early.
- Only the mapped VPB key set is accepted: letters, digits, and `;`.

## Transport integration

The playback engine emits deterministic `PlaybackAction` events as the scaled transport crosses each action timestamp. Looping resets the action cursor, and seeking rebuilds the cursor using binary search. Phase 7 reliability work deliberately avoids changing the timing scheduler or native dispatch path.

## User-data protection

The permanent MIDI library remains at `%APPDATA%\VirtualPianoBot\midi`. Imports and legacy migration stay copy-only and do not overwrite collisions or delete source songs.
