# VPB 6.0.2 · Update-Path Test Release

This test release validates the in-app update path from installed version 6.0.1 to 6.0.2.

## Visible test marker

The launcher subtitle includes **UPDATE TEST** after the 6.0.2 version and Performance Studio label. This makes the updated payload easy to identify after installation.

## Expected release assets

- `VirtualPianoBot-6.0.2-Setup.exe` for manual installation
- `VirtualPianoBot-6.0.2-win-x64.zip` as the versioned Windows application package
- `VirtualPianoBot-Update.zip` as the stable-name app-only update payload
- `update-manifest.json` with package version, HTTPS URL, byte size, SHA-256 and release notes

## Test procedure

1. Keep the existing 6.0.1 installation.
2. Open the launcher and click **CHECK FOR UPDATES**.
3. Confirm the offered version is 6.0.2.
4. Approve the update and allow download verification and installation to finish.
5. Confirm the launcher reopens and its subtitle contains **UPDATE TEST**.
6. Verify the MIDI library, favourites and Studio settings remain present.
7. Confirm the update log records success. If a step fails, retain the log and rollback backup for diagnosis.

## Safety and verification

The updater requires HTTPS, checks SHA-256 and package size, validates the archive against its app-only allow-list, and preserves application files for rollback. The permanent MIDI library remains at `%APPDATA%\VirtualPianoBot\midi`.

GitHub Actions builds the full solution, runs source preflight and regression suites, compiles the Inno Setup installer, and only then publishes the release assets. CI success validates build and packaging; the actual in-app update and rollback still need confirmation on Windows.

Phase 6 Smart Automation remains intentionally skipped.
