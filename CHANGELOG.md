# Changelog

## [1.5] - Unreleased
### Added
- Minimize to tray support and reliable tray icon lifecycle.
- App-level global exception handlers and safe shutdown cleanup.
- Process job assignment to ensure ffmpeg child processes are terminated if the app crashes.
- Shared HttpClient and limited concurrency for source validation.
- Non-blocking UI history updates and other stability improvements.
- Basic file logging to `%AppData%/CorilloStreamer/streamer.log`.
- Explicit disposal and unsubscription of tray menu handlers.

### FFmpeg binaries included

- ffmpeg.exe SHA256: `5AF82A0D4FE2B9EAE211B967332EA97EDFC51C6B328CA35B827E73EAC560DC0D`
- ffprobe.exe SHA256: `192A1D6899059765AC8C39764FC3148D4E6049955956DC2029F81F4BD6A8972D`

### Fixed
- Align "Loop" and "Random" checkboxes in folder mode UI.
- Ensure BUILD and version updated to 1.5.

### Notes
- Build target: .NET 8 (WPF)
- Binaries: ffmpeg.exe and ffprobe.exe are included as content and copied to output.

