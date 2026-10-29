# Virtual Piano Bot 6.0.1 · Playback Hotfix and Update Distribution

## Playback fix

- Fixed repeated notes that begin at the exact same timestamp another note of the same mapped key ends. The planner now emits key-up followed by key-down so the new note-on is not swallowed.
- Added regression coverage for three repeated three-note chords: all nine note-ons, all nine note-offs, and each shared boundary's release-before-press order are checked.

## Update and distribution

- Retained the Phase 9 HTTPS update client, SHA-256 verification, strict app-only package validation and rollback apply process.
- Added GitHub Releases support with stable latest-release URLs for the update manifest and app-only update ZIP.
- Added a GitHub Actions workflow that builds the full solution, runs all regression suites, creates the installer, and publishes the versioned package, stable update asset and manifest when a matching `v<version>` tag is pushed.
- Online updates remain unavailable until this source has been published to a public GitHub repository and the first release asset set is successfully built.

## Existing features retained

- Falling-note Performance Studio, instrument selection, Smart Fit / Exact Pitch, HUD, MIDI Library 2.0, visualiser controls, Studio themes and accent colours.
- Phase 6 Smart Automation remains intentionally skipped.

## Data safety

The permanent MIDI library remains at `%APPDATA%\VirtualPianoBot\midi`. The update ZIP contains only the known application files. The installer and updater do not include or replace the user library or settings.

## Verification

The source package includes a repeated-chord regression test and GitHub release workflow. Compile and run the Windows build before tagging `v6.0.1`; the GitHub Actions workflow is designed to repeat those checks before publishing any release.
