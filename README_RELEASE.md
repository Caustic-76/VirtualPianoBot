# VPB 6.0.1 · Release & Verification Notes

This is a source/build package combining the Phase 9 updater and the repeated-note retrigger hotfix. It is **not precompiled binaries**. The .NET 10 Windows build must pass before publishing a release.

## Build and local checks

Run `BUILD_RELEASE.bat` on Windows with the .NET 10 SDK installed. It runs the preflight, builds the complete solution and executes the MIDI-library safety, MIDI-analysis and keyboard-mapping regression suites. The foundation suite now includes the repeated-chord retrigger test. Install Inno Setup 6 if you also want the setup executable.

The output includes:

- `release\app\VPB6.exe`
- `release\app\runtime\VPB6Studio.exe`
- `release\VirtualPianoBot-6.0.1-win-x64.zip`
- `release\update-manifest.json`
- `release\VirtualPianoBot-Update.zip` when GitHub mode is configured
- `release\VirtualPianoBot-6.0.1-Setup.exe` when Inno Setup 6 is installed

## GitHub release workflow

The `.github/workflows/release.yml` workflow is triggered by a pushed tag that exactly matches the `VERSION.txt` value with a `v` prefix. It builds/tests with .NET 10, creates the installer and attaches the release assets using the built-in `GITHUB_TOKEN`/`gh` client. No personal access token is embedded in VPB.

The repository needs to be public for this simple anonymous updater design. The manifest URL and stable update-package URL are stamped from `github.repository`. After the first successful release, install the 6.0.1 setup executable once and use **CHECK FOR UPDATES** for subsequent stable releases.

## What is tested by source checks

- Stable product version metadata matches `VERSION.txt`.
- The repeated-chord regression checks confirm all note-ons and note-offs across shared timing boundaries and verify release-before-press ordering.
- Library import and migration remain copy-only/no-overwrite.
- The updater requires HTTPS, validates SHA-256 and package size, enforces an archive allow-list, and performs application-file rollback on failure.
- The release workflow publishes matching versioned setup, ZIP and manifest assets.

## Verification limitation

The generation environment does not include the .NET 10 SDK or Windows PowerShell. This ZIP has passed archive and static-structure checks, but the C# build, GitHub Actions workflow, UAC flow, live download, update install and rollback must be verified on Windows before enabling public updates.
