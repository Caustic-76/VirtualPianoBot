# VPB 6.0.1 Final Release Checklist

**RC1 status:** the user reports that RC1 passed flawlessly on Windows. Phase 6 Smart Automation was intentionally skipped. Phase 9 adds new updater code, so compile and validate it on Windows before enabling online update hosting.

## A. Final stable build

- [ ] Run `scripts\Validate-Release.ps1`; all source preflight checks pass.
- [ ] Run `BUILD_RELEASE.bat` with the .NET 10 SDK installed.
- [ ] Confirm the full solution builds without errors and review any warnings.
- [ ] Confirm MIDI persistence/no-overwrite regression checks pass.
- [ ] Confirm MIDI analysis regression checks pass.
- [ ] Confirm keyboard mapping/legacy mapping regression checks pass.
- [ ] Confirm `release\app\VPB6.exe` and `release\app\runtime\VPB6Studio.exe` exist and are non-empty.
- [ ] If using Inno Setup, confirm `release\VirtualPianoBot-6.0.1-Setup.exe` is produced.
- [ ] Confirm the launcher displays `6.0.1` and `updates.json` identifies the stable channel and configured (or intentionally blank) HTTPS manifest URL.
- [ ] Confirm `release\VirtualPianoBot-6.0.1-win-x64.zip` and `release\update-manifest.json` are generated and the manifest SHA-256 matches the ZIP.
- [ ] If online updating is enabled, host both files at the configured HTTPS URL and verify the installed launcher reads the test manifest.

## B. Protected user data

- [ ] Confirm `%APPDATA%\VirtualPianoBot\midi` still contains the pre-update collection.
- [ ] Rescan the library and verify expected nested songs appear.
- [ ] Import a sample folder twice; the second import skips collisions and does not overwrite files.
- [ ] Confirm original imported source files remain untouched.
- [ ] Back up the MIDI library and verify nested paths are preserved.
- [ ] Confirm a backup destination inside the permanent MIDI library is rejected.
- [ ] Back up settings and confirm `settings.json` is unchanged.

## C. Final smoke test

- [ ] Launch Studio via the installed launcher.
- [ ] Confirm a sparse MIDI and a dense/fast MIDI play as expected.
- [ ] Check play, pause, resume, stop, restart, seek, loop and countdown.
- [ ] Confirm falling notes, HUD, instrument selections, range mode and visualiser preferences respond correctly.
- [ ] Confirm themes, accent, Reduce Motion and favourites persist across restart.
- [ ] Confirm native keyboard input starts disarmed, and pause/stop/PANIC releases held keys.
- [ ] Confirm install and uninstall do not touch `%APPDATA%\VirtualPianoBot\midi`.

## D. Updater acceptance

- [ ] Test a manifest with the current version and confirm the launcher reports up to date.
- [ ] Test a newer test version and verify the release notes prompt and SHA-256 check.
- [ ] Test an incorrect checksum and confirm no installed files change.
- [ ] In a disposable install, simulate a failed replacement and verify rollback restores the previous application files.
- [ ] Confirm the updater waits for Studio to close and keeps backups/logs under `%LOCALAPPDATA%\VirtualPianoBot\updates`.
- [ ] Verify the MIDI library and settings are not in the package or replacement list.

## E. Release decision

- [ ] Save the final build log and note any accepted warnings.
- [ ] Keep a copy of the installer and release hash.
- [ ] Mark final stable packaging complete only after the versioned `6.0.1` build and installer pass.

- [ ] Repeated-chord regression passes: all note-ons and note-offs are emitted at shared end/start boundaries.
- [ ] GitHub Release contains Setup.exe, versioned app ZIP, VirtualPianoBot-Update.zip, and update-manifest.json.
