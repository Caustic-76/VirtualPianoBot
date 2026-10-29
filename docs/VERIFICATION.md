# VPB 6 verification and release note

The Phase 7 Release Candidate 1 passed the user's Windows acceptance run flawlessly. Phase 9 adds an online update client and package generator. The new launcher and update path require Windows compile and runtime checks before online update hosting is enabled.

Static preflight covers:

- Well-formed XML in all `.axaml`, project, props and manifest files.
- All solution project paths resolve.
- Stable `6.0.1` version metadata matches across application, launcher, installer and local update manifest.
- The local update configuration is packaged into both the staged app payload and the installer.
- The updater requires HTTPS, verifies SHA-256, validates a strict ZIP file allow-list, and includes a rollback apply script.
- Release publishing creates an app-only ZIP and corresponding `update-manifest.json`.
- Online updates remain disabled until a real HTTPS `manifestUrl` is configured.
- The version-independent `%APPDATA%\VirtualPianoBot\midi` path contract is present.
- MIDI imports and legacy migration remain copy-only and do not overwrite or delete source songs.
- Regression suites cover MIDI persistence/no-overwrite behavior, analysis, and keyboard mapping.

The artifact-generation environment does not include the .NET 10 SDK or Windows PowerShell, so it cannot compile or execute the updater. On Windows with the .NET 10 SDK, run `BUILD_RELEASE.bat`; optionally install Inno Setup 6 to create the installer. Test a disposable update manifest and rollback before enabling a public endpoint.

- Repeated chord regression: three pitches repeated three times preserve nine note-ons and nine note-offs; retrigger boundaries emit release before press.
