# Changelog


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

