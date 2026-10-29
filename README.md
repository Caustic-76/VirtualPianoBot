# Virtual Piano Bot 6.0.2 · In-App Update Test

This is a small test release to validate the complete VPB update path from **6.0.1 to 6.0.2**.

## What changed in 6.0.2

- The launcher version banner now shows **UPDATE TEST** next to version 6.0.2. After an in-app update, this is a visible confirmation that the new launcher payload was installed.
- Updated launcher, Studio assembly, installer and update metadata to 6.0.2.
- No playback scheduler or MIDI mapping changes are part of this test release.

## Test the automatic updater

1. Keep the working **VPB 6.0.1** installation.
2. Click **CHECK FOR UPDATES** in the launcher.
3. Confirm the available update is **6.0.2** and approve it.
4. Let VPB download the package, verify its SHA-256 checksum, apply the app-only update and relaunch.
5. Confirm the launcher banner says **6.0.2 · PERFORMANCE STUDIO · UPDATE TEST**.
6. Open the MIDI library and confirm your songs, favourites and Studio preferences remain intact.

If the update fails, save the message and log path shown by VPB. Do not delete the updater's rollback backup.

## User-data safety

The permanent MIDI library remains at `%APPDATA%\VirtualPianoBot\midi`. Studio settings and favourites remain under `%APPDATA%\VirtualPianoBot`, and the update source is saved under `%LOCALAPPDATA%\VirtualPianoBot\update-settings.json`. The update ZIP is an app-only payload and excludes user data.

The GitHub Actions workflow builds the .NET 10 solution, runs regression checks, builds the installer, and publishes the setup package, portable ZIP, app-only update ZIP and `update-manifest.json` when a pushed tag matches `VERSION.txt`.
