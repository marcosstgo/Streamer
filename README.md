<p align="center">
  <img src="https://corillo.live/assets/streamer-pro/screenshot-main.jpg" alt="Streamer Pro v2.1.3" width="860"/>
</p>

# Streamer Pro

**Advanced Windows streaming engine powered by FFmpeg**

Stream to Corillo, Twitch, Kick, YouTube, or any custom RTMP endpoint — from local playlists, online sources, or direct screen capture.

🔗 **[corillo.live/streamer-pro](https://corillo.live/streamer-pro)** · **[Download latest](https://github.com/marcosstgo/Streamer/releases/latest)**

---

## Features

- **4 Source Modes** — Online URL, Folder Playlist, Local File, Screen Capture (NVIDIA, BETA)
- **Multi-Platform RTMP** — Corillo, Twitch, Kick, YouTube, or any custom endpoint
- **Quality Profiles** — 480p, 720p, 1080p, 1080p 60fps, or fully custom bitrate/resolution
- **Highlight Mode** — Watches SteelSeries Moments / NVIDIA Highlights folders and streams clips in real time
- **Hardware Acceleration** — NVIDIA NVENC for GPU encoding
- **Logo Overlay** — Custom watermark burned in by FFmpeg on every frame
- **Auto-Recovery** — Frame-based watchdog restarts stream automatically on freeze
- **Auto-Update** — Detects new versions on startup and updates with one click
- **FFmpeg Downloader** — Downloads FFmpeg automatically on first run, no manual install needed

---

## Installation

1. Download `Streamer Pro.exe` from [Releases](https://github.com/marcosstgo/Streamer/releases/latest)
2. Place it in a dedicated folder (recommended: `C:\streamerpro`)
3. Run it from that folder
4. If `ffmpeg.exe` and `ffprobe.exe` are missing, Streamer Pro will offer to download them automatically into the same folder

**Requirements:** Windows 10/11 · NVIDIA GPU (optional, for Capture mode)

FFmpeg is required for streaming, but it is not bundled in the main GitHub Release asset because the binaries are large. The app handles downloading them on demand.

---

## Build from source

```
git clone https://github.com/marcosstgo/Streamer.git
cd Streamer
dotnet build Streamer/Streamer.csproj -c Release
dotnet publish Streamer/Streamer.csproj /p:PublishProfile="Properties\PublishProfiles\Release-win-x64.pubxml"
```

For the full release process, asset upload, and auto-update validation, see [`RELEASE.md`](RELEASE.md).

---

## Credits

Developed by **Marcos Santiago** · Part of the [CORILLO](https://corillo.live) streaming ecosystem
Built with **Claude Code** by Anthropic · Powered by [FFmpeg](https://ffmpeg.org/)

---

*Hecho con amor en Puerto Rico* 🇵🇷
