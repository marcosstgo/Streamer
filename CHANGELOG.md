# Changelog

## [3.0.1] - 2026-04-03

### Changed
- Reworked `Folder` mode around a Persistent Streamer Pipeline so clip rotation no longer depends on a fragile concat playlist timeline.
- Documented the new `Folder` architecture and linked it from the project documentation.

### Fixed
- Improved folder stability by keeping a persistent RTMP streamer alive while per-clip suppliers feed MPEG-TS into it.
- Fixed folder selection so the active visible folder is the one actually used at stream start.
- Fixed false FFmpeg popups caused by benign lag warnings and recoverable EOF-style muxer messages.
- Improved local logging so publish-folder builds can write a `streamer.log` next to the executable when possible.

## [3.0.0] - 2026-04-03

### Added
- Added Scheduled Start so Streamer Pro can automatically begin a stream at a chosen local date and time while the app remains open or minimized to tray.

### Changed
- Elevated the app's overall presentation with a refreshed control-room layout, improved hierarchy, refined ARC mode, and a polished Credits window.

### Fixed
- Scheduled start now uses its own countdown timer and avoids cross-thread config save issues when the scheduled launch triggers.

## [2.2.8] - 2026-04-03

### Changed
- Refreshed the visual design across the main window with a cleaner control-room layout, improved spacing, stronger hierarchy, and more polished action panels.
- Refined ARC mode so it keeps its identity while using more restrained accents and less noisy borders.
- Updated the Credits window with brighter iconography and a more polished presentation.

## [2.2.7] - 2026-04-03

### Changed
- GitHub Releases now build and attach the self-contained `Streamer.Pro.exe` asset automatically from GitHub Actions when a version tag is pushed.

### Fixed
- Added a common startup preflight so invalid RTMP URLs, missing FFmpeg binaries, missing stream key, and missing overlay files are caught before FFmpeg launches.
- Cleaned the remaining build warnings so the project now compiles with `0 warnings` and `0 errors`.

## [2.2.6] - 2026-04-03

### Changed
- Windows distribution now uses a self-contained single-file publish profile so users can run Streamer Pro without installing the .NET Desktop Runtime separately.
- Release and contributor docs now describe the real distribution model: `Streamer Pro.exe` is the main asset, while FFmpeg binaries are downloaded into the same folder when needed.

### Fixed
- FFmpeg detection now requires both `ffmpeg.exe` and `ffprobe.exe`, keeping the top-bar status aligned with the app's real dependency state.
- Capture mode now refreshes its monitor/audio device lists after installing FFmpeg, validates audio device selection before start, and performs a capture preflight before launching the stream.

## [2.2.5] - 2026-04-03

### Changed
- Release workflows now create draft releases with GitHub CLI and tolerate repos that rely on runtime FFmpeg download instead of committed binaries.

### Fixed
- The FFmpeg download pill now opens correctly regardless of the active UI language by checking the real binary state instead of matching Spanish text.

## [2.2.4] - 2026-04-03

### Changed
- FFmpeg binaries are now optional at build time so the app can rely on runtime download when they are missing.
- Force YUV420p and Save Logs now control the real streaming behavior instead of acting as visual-only options.

### Fixed
- Loop Infinito and Duración máx. now apply correctly across online, file, folder, and highlights modes.
- Overlay streaming now tolerates sources without audio instead of failing on strict audio mapping.
- Audio/video sync was improved for long-running streams with explicit CFR video normalization and audio resampling.
- Highlights mode now waits for files to stabilize before enqueuing them and ignores duplicate watcher events.




## [2.0.2] - 2025

### Added
- Stream Key hidden by default with PasswordBox (OBS-style) + show/hide toggle button (??/??)
- GPU% metric in status bar using nvidia-smi for real-time GPU utilization
- Loop Infinito now works in online mode (restarts source when video ends)
- GPU vendor label shown next to Hardware Acceleration checkbox (NVIDIA/Intel/AMD/No GPU)
- PasswordBoxStyle for dark theme consistency

### Changed
- Audio Bitrate standardized to 160k AAC across all quality profiles (Twitch standard)
- Removed 4K profile (not practical for streaming use case)
- GPU polling runs on background thread every 2s with cached value (no UI freeze)
- All threading issues fixed: ConfigureAwait, Dispatcher.Invoke ? BeginInvoke, async init
- PerformanceCounter and preferences loading moved off UI thread for faster startup
- DetectHwEncoderAsync drains stdout/stderr with 10s timeout to prevent hangs
- nvidia-smi query drains stderr and has 2s kill timeout

### Fixed
- HW Acceleration: removed -hwaccel auto (caused DXVA2 filter incompatibility), GPU only used for encoding
- HW encoder: auto-detect GPU vendor, vendor-specific presets, fallback to libx264
- Video filters (-vf scale/pad) now work correctly with HW acceleration
- Pixel format always yuv420p for maximum compatibility
- Stream Key encrypted with DPAPI in favorites (was plain text)
- Favorites loading uses async file IO (was blocking UI thread)
- Source DisplayText fixed corrupted character
- Empty event handlers removed from XAML and code-behind
- History debounced (saves 3s after last change instead of every entry)
- History shows 100 entries in UI, persists last 20 to disk
- Logger integrated into FFmpeg flow for diagnostics
- AppData unified to CorilloStreamer (was split with StreamerPro)
- Old synchronous ExecuteFFmpeg methods removed

## [2.0.1] - 2025

### Added
- Complete UI redesign: dark premium dashboard matching professional mockup
- New color palette with tokens: BgPrimary (#0F1720), BgPanel (#141C26), AccentYellow (#F5B72E), etc.
- Custom CheckBox and RadioButton templates with dark theme and yellow accent
- Rounded button templates (PrimaryButton, DangerButton, BigPrimaryButton, BigDangerButton)
- StatusPill style for transmitting/stopped indicator
- BadgeStyle for version badge in header
- Auto-reconnect with exponential backoff for online streaming (max 5 attempts: 2s, 4s, 8s, 16s, 30s)
- Hardware encoder detection (h264_nvenc, h264_qsv, h264_amf) with automatic fallback
- Per-process CPU and memory tracking for FFmpeg (instead of system-wide)
- Circular buffer for stderr/stdout to prevent unbounded memory growth in long streams
- Keyframe interval (-g) set to 2 seconds for RTMP server compatibility
- Rate control: -maxrate and -bufsize for stable RTMP bitrate delivery
- FLV flags: -flvflags no_duration_filesize to suppress FFmpeg warnings
- DetectHwEncoderAsync helper for probing GPU encoder availability
- IsFFmpegProgressLine filter and AddToCircularBuffer utility methods
- Professional status bar with inline metrics (Bitrate, FPS, CPU, MEM, Speed)
- Server status indicator with connectivity and latency in header
- Action buttons column with Iniciar Stream and Detener plus live timer
- Typography tokens (TitleFontSize, SectionTitleFontSize, LabelFontSize, etc.)
- Dark scrollbar styles matching the theme
- SparklinePolyline style for real-time graphs
- Two-column layout: Config + Advanced Options side by side
- Profiles row with 6 pill buttons (480p, 720p, 1080p, 1080p60, 4K, Personalizado)
- Favorites and History sections with InputBg backgrounds
- Credits window with proper emoji rendering using XML character entities
- Comprehensive README with badges, screenshots, feature documentation

### Changed
- Window background updated to #0F1720
- Window width increased to 1080px for better breathing room
- Outer padding increased to 20px
- Card border radius standardized to 10px
- Input height standardized to 34px
- All font sizes aligned to design tokens (14px labels, 16px section titles, 26px title)
- Profile labels simplified (e.g. "1080p - Alto" instead of "1080p - 4500k")
- Header simplified: removed heavy CardShadow, added border
- Footer redesigned as single status bar with inline metrics and action buttons
- Credits footer text uses XML entities for cross-encoding compatibility
- ExecuteFFmpegAsync now uses WaitForExitAsync (native .NET 8) instead of 200ms polling loop
- Stderr UI dispatching reduced: progress lines (frame=, size=) are filtered out from history
- MessageBox after FFmpeg exit only shows last 30 non-progress error lines on non-zero exit code
- StringBuilders replaced with circular Queue buffers (max 200 lines) for memory efficiency
- HW acceleration now switches encoder to h264_nvenc with mapped NVENC presets
- BuildEncodingArguments includes -maxrate, -bufsize, -g, and -flvflags for RTMP optimization

### Fixed
- StreamTimeCompact timer now syncs with main StreamTime
- StartBig/StopBig buttons now properly enable/disable with stream state
- Heart and satellite emoji in CreditsWindow render correctly (XML char entities)
- Duplicate x:Name conflicts resolved (MetricBitrate, MetricFps, etc.)
- File encoding issues with emoji characters resolved
- CPU metric now tracks FFmpeg process specifically instead of system-wide PerformanceCounter
- Memory metric (MEM) now shows FFmpeg working set in real-time
- Online stream no longer silently dies on server disconnect (auto-reconnect)

## [1.5] - Previous
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

