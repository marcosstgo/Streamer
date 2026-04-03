Contributing and Release instructions
====================================

Release process
---------------

The current release process is documented in `RELEASE.md`.

The current technical notes for the stabilized folder playlist engine are documented in `docs/FOLDER_MODE.md`.

Important updates since `v2.2.5`:

1. `ffmpeg.exe` and `ffprobe.exe` are optional at build time
2. `ffmpeg.exe` and `ffprobe.exe` are still required for the product to work correctly
3. They are not bundled in the main GitHub Release asset because of binary size and distribution constraints
4. The app downloads FFmpeg automatically at runtime when needed and stores the binaries next to `Streamer Pro.exe`
5. GitHub Releases publish `Streamer.Pro.exe` as the main asset
6. Draft release creation is automated through GitHub Actions

FFmpeg verification
-------------------

Use the provided script to verify local FFmpeg binaries against the expected SHA256 values:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File "tools/verify-ffmpeg.ps1"
```

For CI or clean clones where FFmpeg binaries are intentionally absent, use:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File "tools/verify-ffmpeg.ps1" -AllowMissing
```

License
-------

See `LICENSE-FFMPEG.txt` for license obligations related to FFmpeg binaries.
