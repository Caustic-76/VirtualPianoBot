# Virtual Piano Bot 6.0.2 · In-App Update Test

## Purpose

A small test release to validate the complete automatic update path from 6.0.1 to 6.0.2.

## Visible confirmation

- The launcher subtitle now displays **6.0.2 · PERFORMANCE STUDIO · UPDATE TEST** so the updated payload is easy to identify.
- Updated launcher, Studio assembly metadata, installer output name and local update metadata to 6.0.2.

## Test scope

- Publish an app-only update ZIP and checksum manifest.
- From an existing 6.0.1 install, discover 6.0.2 via the latest-release manifest.
- Confirm HTTPS package download, SHA-256 verification, archive validation, application replacement, relaunch and update logging.
- Verify that the MIDI library, favourites and Studio preferences remain untouched.

## Unchanged

- No changes to playback scheduling, keyboard event dispatch, MIDI mapping or persistent MIDI-library logic.
- Phase 6 Smart Automation remains skipped.
