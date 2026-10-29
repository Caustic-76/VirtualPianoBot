# User-data safety contract

## Protected locations

The MIDI collection must remain at `%APPDATA%\VirtualPianoBot\midi` across VPB versions. Do not place the only copy of user songs inside a version-named install folder.

## Allowed operations in Phase 1

- Create missing data directories.
- Enumerate `.mid` and `.midi` files read-only.
- Copy recognized legacy files into the permanent library if the destination path does not exist.
- Save application settings through a temporary file and replace operation, retaining a `.bak` copy when a prior settings file exists.
- Append log entries.

## Forbidden operations

- Emptying or resetting the MIDI folder.
- Moving or deleting source MIDI files during migration.
- Overwriting a MIDI destination file silently.
- Following directory symlinks/reparse points during library scans.
- Searching arbitrary user folders for legacy data. Only the current install's `midi` folder and neighboring VPB-version folders with recognizable VPB markers are candidates.
- Treating a failed scan or settings parse as permission to delete user data.

## Migration collision policy

Relative paths beneath the legacy MIDI folder are preserved. If a target already exists, it is skipped and left byte-for-byte untouched. Source files are always retained. A clean first-run migration is marked complete in version-independent settings so deleted songs are not silently restored from an old install on every launch. If migration reports unreadable files, the next launch can retry. Where multiple older versions have the same relative path, the most recently modified recognized version is examined first; no existing destination is replaced.
