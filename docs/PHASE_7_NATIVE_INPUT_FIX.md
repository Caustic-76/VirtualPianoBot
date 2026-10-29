# VPB 6 Native Input Fix

## Problem

The first Phase 7 native keyboard adapter used a `SendInput` structure whose union was only the size of `KEYBDINPUT` (24 bytes on x64). Windows expects the native `INPUT` union to be 32 bytes on x64, making `INPUT` 40 bytes total.

That meant VPB could reach `SendInput`, but Windows could reject the input packet because `cbSize` was incorrect.

## Fix

`NativeKeyboardInputService` now:

- Uses a 32-byte explicit `INPUT` union.
- Reports partial/failed `SendInput` calls through `LastError`.
- Releases Shift as well as tracked piano keys during panic/stop/disarm.
- Keeps the existing ARM / DISARM safety model.

## Windows integrity note

`SendInput` cannot inject into a target application running at a higher integrity level than VPB. If the target app is elevated, run VPB at the same integrity level. Normal non-elevated applications should work with the default VPB launch mode.
