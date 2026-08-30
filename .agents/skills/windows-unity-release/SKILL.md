---
name: windows-unity-release
description: Build and publish Jumping Ninja Android APK and Windows ZIP releases from Windows when a developer requests a GitHub tag or Release.
---

# Windows Unity Release

Use the repository script instead of reconstructing Unity, Git, and GitHub CLI commands.

## Prepare

1. Read `GAME_STATUS.md`. Confirm the requested tag, whether phone testing is required, and that publishing to GitHub is authorized.
2. For a new game version, update Unity Version Name, Android Version Code, and their checks in `Assets/Editor/V1ProjectSetup.cs` and both release builders before releasing.
3. Create `ReleaseNotes/<tag>.md` with player-visible changes, Android and Windows download details, signing facts, test scope, and any known limitations.
4. Commit and push all intended release changes. Preserve unrelated work and leave the tree clean; the script never commits or stashes changes automatically.

Do not delete `Library` during a normal release. Reuse Unity's incremental cache; clear it only after logs provide concrete evidence that the cache is corrupt.

## Publish

Run from the repository root:

```powershell
.\scripts\release-windows.ps1 <tag> .\ReleaseNotes\<tag>.md
```

For example:

```powershell
.\scripts\release-windows.ps1 v1.0.4 .\ReleaseNotes\v1.0.4.md
```

The script locates the project Unity version and uses separate Unity processes for each target, as required by Unity 6. It builds the Android APK with the committed build profile, builds the Windows x64 player and compresses it as ZIP, calculates both SHA-256 hashes, pushes the current branch and annotated tag, creates the GitHub Release, uploads both artifacts, and verifies the published release. Tags containing `-` are marked as prereleases. It uses `GH_TOKEN`, an existing `gh` login, or the Git Credential Manager credential without printing the token.

To intentionally republish an existing APK under another tag, skip Unity and name the exact source commit:

```powershell
.\scripts\release-windows.ps1 v1.0.3-test .\ReleaseNotes\v1.0.3-test.md `
  -SkipBuild -ApkPath .\Builds\JumpingNinja-v1.0.3.apk `
  -WindowsZipPath .\Builds\JumpingNinja-v1.0.3-Windows.zip -TargetCommit v1.0.3
```

Use this mode only when the new Release must contain byte-for-byte identical output. Compare the reported SHA-256 with the source release.

## Finish

Open the reported Release URL and confirm both asset states are `uploaded`. Record the tag, URL, both hashes, signing types, and skipped tests in `GAME_STATUS.md`, then commit and push that documentation update.

The script refuses to overwrite an existing Release or move a conflicting tag. If upload fails after the tag is pushed, fix the cause and rerun with the same tag; do not create a replacement tag. Debug signing is acceptable until the developer requests a persistent release keystore, but note that debug-signed installs cannot later be upgraded in place by a differently signed APK.
