# Virtual Piano Bot 6 - Phase 3.5 Motion & Navigation

## Goal
Turn the Phase 3 interface into a living music-studio UI with visible ambient motion and animated workspace transitions.

## Added
- Animated ambient orb drift
- Floating particle field
- Stronger hero glow and pulse rings
- More visible spectrum movement
- Animated scan-line emphasis
- Page-change slide/fade transition
- Animated transition sweep/glow
- Cubic easing for workspace movement
- Motion continues independently of navigation
- Effects remain non-interactive

## Navigation animation
Switching Dashboard, MIDI Library, Analysis Studio, or Settings now triggers:
1. Workspace lifts slightly and fades down
2. Neon sweep crosses the workspace
3. Workspace slides in from the right
4. Opacity and position ease back to normal

## Compatibility
- Keeps Phase 3 commands and ViewModel navigation
- Keeps the persistent MIDI library path unchanged
- Does not alter MIDI files or migration behavior
- Uses the existing 16 ms motion timer

## Build
Run the existing `scripts/Build-VPB6.ps1` script from the project root.
