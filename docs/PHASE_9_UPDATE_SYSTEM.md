# Phase 9: Update System and GitHub Releases

## Update flow

- Launcher controls: **CHECK FOR UPDATES** and **UPDATE SOURCE**.
- The chosen manifest URL is stored outside the install directory at `%LOCALAPPDATA%\VirtualPianoBot\update-settings.json`.
- Manifest/package URLs require HTTPS. The updater validates stable channel/version, package size, SHA-256 and a strict allow-list of application files.
- The apply process waits for VPB to exit, backs up known application files, replaces only the allow-listed files, validates the resulting files, relaunches VPB and attempts rollback if replacement fails.
- Logs and backups are under `%LOCALAPPDATA%\VirtualPianoBot\updates`. The updater never targets the MIDI library or Studio settings.

## GitHub Releases

The included `.github/workflows/release.yml` workflow builds and tests the solution on Windows when a release tag such as `v6.0.2` is pushed. It creates the Inno Setup installer and publishes these assets in the matching GitHub Release:

- `VirtualPianoBot-<version>-Setup.exe`
- `VirtualPianoBot-<version>-win-x64.zip`
- `VirtualPianoBot-Update.zip`
- `update-manifest.json`

The release manifest URL uses `https://github.com/OWNER/REPOSITORY/releases/latest/download/update-manifest.json`. The manifest's package URL is an absolute, version-pinned release URL for `VirtualPianoBot-Update.zip`. The fixed asset name keeps the workflow consistent, while the package URL is pinned to the exact version so a newer release published during a download cannot cause a manifest/package mismatch. Absolute URLs also avoid GitHub redirect-base ambiguity.

For this simple update route, the repository containing release assets must be public. The public repository may contain the source as well; if that is not desired, keep the source in a private repository and use a separate public release-only repository. Never embed a personal GitHub token in the launcher.

## Release sequence

1. Build `BUILD_RELEASE.bat` on Windows and test all regressions, including the repeated-chord case.
2. Ensure GitHub repository content is uploaded and the workflow is present at `.github/workflows/release.yml`.
3. Push a tag that matches `VERSION.txt`, prefixed with `v`. The included hotfix package uses `VERSION.txt = 6.0.2`, so the tag is `v6.0.2`.
4. GitHub Actions installs .NET 10, validates the tag, builds and tests the source, creates the installer, then publishes the update ZIPs and manifest.
5. Confirm all four assets exist on the published release and that the `update-manifest.json` package size and SHA-256 match `VirtualPianoBot-Update.zip`.
6. Install 6.0.2 manually once if using an older 6.0.0 build without the Phase 9 updater. Configure/check the update source if needed. Later updates should use the in-app updater.

## Data-safety contract

- Permanent MIDI library: `%APPDATA%\VirtualPianoBot\midi`.
- Settings and favourites: `%APPDATA%\VirtualPianoBot\settings.json` and the established VPB settings data directory.
- Update source: `%LOCALAPPDATA%\VirtualPianoBot\update-settings.json`.
- Update logs/backups: `%LOCALAPPDATA%\VirtualPianoBot\updates`.

The update package contains only `VPB6.exe`, `README.txt`, `updates.json`, and `runtime/VPB6Studio.exe`. It never includes or targets user MIDI/settings folders.

## Verification limitation

Static source checks are not a substitute for a successful Windows build. The new release workflow, live GitHub redirects, checksum verification, update install, UAC and rollback should be tested before treating the online updater as production-ready.
