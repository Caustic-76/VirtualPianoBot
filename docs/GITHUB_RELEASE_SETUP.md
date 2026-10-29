# Set up automatic VPB6 releases on GitHub

This repository is prepared for **public GitHub Releases**. A public release is needed because installed copies of VPB fetch the manifest and package without signing in. Only publish the source publicly if you are comfortable with others viewing it. If not, keep the source private and use a separate public repository for release assets.

## 1. Create the repository

1. Create a repository named `VirtualPianoBot` on GitHub.
2. Choose **Public** only if you are happy for the source to be public.
3. For the simplest first upload, do not pre-create a README, `.gitignore` or license because this source package already contains project files.
4. On Windows, install GitHub Desktop, clone the empty repository, then copy the contents of this source package into the cloned folder. Keep the `.github/workflows/release.yml` directory in place.
5. Open the solution/build locally and run `BUILD_RELEASE.bat`. Fix any Windows build failures before creating the first tag.
6. Commit and push the source to the repository's default branch.

## 2. Publish the first automated release

The source package uses `VERSION.txt = 6.0.1`. Create and push the matching tag from a terminal opened in the cloned repository:

```powershell
git tag v6.0.1
git push origin v6.0.1
```

That tag triggers `.github/workflows/release.yml`. GitHub Actions installs .NET 10, validates the tag/version match, runs the full solution build and regression checks, creates the Inno Setup installer, and publishes the release assets only if the preceding steps succeed.

The release should contain:

- `VirtualPianoBot-6.0.1-Setup.exe`
- `VirtualPianoBot-6.0.1-win-x64.zip`
- `VirtualPianoBot-Update.zip`
- `update-manifest.json`

If the GitHub Actions run fails, no release is published by that workflow. Read the failing build/test step before trying again.

## 3. Install the updater-enabled build

The older 6.0.0 stable build predates the Phase 9 updater. Install the 6.0.1 setup package once. Its `updates.json` is configured by the release workflow with the repository's public latest-release manifest URL. Later releases can be checked from the launcher with **CHECK FOR UPDATES**.

The configured URL pattern is:

`https://github.com/OWNER/REPOSITORY/releases/latest/download/update-manifest.json`

The manifest references the version-pinned package at:

`https://github.com/OWNER/REPOSITORY/releases/download/v<version>/VirtualPianoBot-Update.zip`

The actual owner and repository name are filled in by GitHub Actions; replace the placeholders only when diagnosing the URL, not in the checked-in source.

## 4. Publish future versions

For a future hotfix, update the version consistently across `VERSION.txt`, both project files, launcher display metadata, application manifest, `updates.json`, and `installer/VPB6.iss`. Update release notes, build/test locally, commit and push the source, then create a tag whose name is `v` plus the version (for example `v6.0.2`). The workflow builds and publishes the update assets again. The updater's package checksum is regenerated from that exact build.

Do not publish a tag that does not match `VERSION.txt`. The workflow rejects version/tag mismatches.
