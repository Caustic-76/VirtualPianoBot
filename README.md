# Virtual Piano Bot 6.0.1 · Phase 9 Updater + Retrigger Hotfix

This package combines the Phase 9 update/distribution system with the proven repeated-note retrigger fix. The product version is **6.0.1** so the hotfix can be offered as an actual upgrade from 6.0.0.

## What changed

- Fixed shared note-off/note-on boundaries in keyboard mapping. Repeated same-pitch notes now explicitly emit key-up followed by key-down.
- Added a regression test for the exact repeated-chord pattern: three pitches repeated three times, with all nine note-ons and note-offs preserved.
- Retained the launcher’s **CHECK FOR UPDATES** and **UPDATE SOURCE** flow, HTTPS-only manifest and package handling, SHA-256 verification, archive allow-list and rollback-capable application step.
- Added GitHub Releases support and a GitHub Actions workflow. Pushing a matching version tag (for example `v6.0.1`) builds and tests the app, builds the installer and publishes the updater manifest and assets.

## Important: the first Phase 9 installation is manual

The earlier 6.0.0 stable package does not contain the Phase 9 updater. Install the 6.0.1 setup package once from the first GitHub Release. After that, the launcher can check for later stable releases. The new updater code must first pass the Windows build and release workflow before online updating should be treated as production-ready.

## Build locally on Windows

1. Extract this ZIP into a fresh folder.
2. Install the .NET 10 SDK.
3. Optional: set `VPB6_UPDATE_MANIFEST_URL` to a real HTTPS manifest before building if using a non-GitHub host.
4. Run `BUILD_RELEASE.bat`. The script restores and builds the solution, runs all regression suites including the retrigger case, validates the source, then stages the app payload, update package and manifest. If Inno Setup 6 is installed, it builds the installer too.

The previous `release` folder is kept until the replacement build has validated.

## GitHub Releases setup

A workflow is included at `.github/workflows/release.yml`. It expects the repository to be **public** so installed copies of VPB can fetch release assets anonymously. The workflow runs when you push a tag matching `VERSION.txt`. For this package, build and test on Windows first, then push tag `v6.0.1`. GitHub Actions will repeat the build and tests before creating the release.

For the updater to work, the release must include all of these assets from the same build:

- `VirtualPianoBot-6.0.1-Setup.exe` for new/manual installations
- `VirtualPianoBot-6.0.1-win-x64.zip` as the versioned update package archive
- `VirtualPianoBot-Update.zip` as the stable-name app-only package used by the updater
- `update-manifest.json` containing the version, package size, SHA-256 and release notes

The launcher manifest URL is `https://github.com/OWNER/REPOSITORY/releases/latest/download/update-manifest.json`. The update package URL in the manifest points to `https://github.com/OWNER/REPOSITORY/releases/download/v<version>/VirtualPianoBot-Update.zip`. The release workflow stamps the real `OWNER/REPOSITORY` automatically. Do not type an example URL into a real release.

### Public vs private source

A public repository exposes the source code as well as the release assets. Only choose that if you're comfortable making VPB's source public. The updater cannot anonymously download assets from a private repository. If you want private source, use a private source repository and a separate public release-only repository for the signed/verified packages and manifest; do not embed a personal access token in the application.

## User-data safety

The permanent MIDI library remains at `%APPDATA%\VirtualPianoBot\midi`. Studio settings and favourites remain under `%APPDATA%\VirtualPianoBot`, and the update source is saved under `%LOCALAPPDATA%\VirtualPianoBot\update-settings.json`. These are excluded from the update package and not targeted by the updater. Imports remain copy-only and no-overwrite.

## Roadmap

- Phases 1–5: Studio, falling notes, HUD, MIDI library, visualiser and customisation.
- Phase 6: intentionally skipped.
- Phase 7: reliability and safer release packaging.
- Phase 8: stable-release baseline.
- Phase 9: update client plus GitHub Release distribution.
