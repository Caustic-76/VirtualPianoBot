# VPB 6 Phase 5 · Studio Customisation

## Scope

Allow users to customise the visual identity and motion level of VPB 6 while leaving the validated playback pipeline unchanged.

## Included

- Four app themes: Dark, Midnight, Graphite, and Light.
- Five accent palettes: Lime, Cyan, Violet, Rose, and Amber.
- App-level DynamicResource brushes update immediately when a theme or accent changes.
- The falling-note visualiser adapts its canvas and lanes for Light mode; its PLAY LINE, current-position label, and velocity colour ramp use the selected accent.
- Reduce Motion disables ambient particle motion, hero pulses, and workspace page sweeps.
- Restore Defaults resets the theme to Dark, accent to Lime, and motion to enabled.
- Theme, accent, and motion preferences are stored in the existing settings file. Older settings load using defaults when the fields are missing.

## Verification checklist

1. Run `BUILD_RELEASE.bat` on Windows with the .NET 10 SDK.
2. Select each theme and ensure sidebar, cards, borders, type, and selected navigation remain readable.
3. Try all five accents. Confirm the logo mark, primary controls, motion effects, and falling-note play line follow the selected accent.
4. Load a MIDI in Performance Studio in Dark, Midnight, Graphite, then Light mode. Verify lane contrast and note visibility.
5. Enable Reduce Motion and navigate between workspaces. Ambient motion and page sweeps should stop without affecting playback or input state.
6. Disable Reduce Motion and verify the motion timer resumes.
7. Restart the app and confirm theme/accent/reduced-motion choices persist.
8. Import a folder twice to confirm Phase 3 copy-only, no-overwrite behaviour still holds.
9. Play a sparse MIDI and a dense MIDI to confirm playback timing and input dispatch are unaffected.

## Safety boundary

This phase changes appearance resources, settings, and the visualiser's presentation only. It does not change `StudioPlaybackEngine`, native input dispatch, the instrument mixer, MIDI import behaviour, or the permanent `%APPDATA%\VirtualPianoBot\midi` library location.
