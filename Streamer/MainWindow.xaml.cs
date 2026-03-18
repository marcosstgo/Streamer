using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net.NetworkInformation;
using Streamer.Models;
using Streamer.Services;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualBasic;
using MessageBox = System.Windows.MessageBox;
using Button = System.Windows.Controls.Button;
using Forms = System.Windows.Forms;

namespace Streamer
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public ObservableCollection<Source> Sources { get; } = new ObservableCollection<Source>();
        private Source? selectedSource;
        private bool isOnlineMode = true;
        private string? selectedFolderPath;
        private string? selectedFilePath;
        private bool folderLoop = false;
        private bool folderRandom = false;

        public Source? SelectedSource
        {
            get => selectedSource;
            set
            {
                if (selectedSource == value) return;
                selectedSource = value;
                OnPropertyChanged();
            }
        }

        // Public wrapper to allow app-level handlers to perform safe cleanup.
        public void PerformShutdownCleanup()
        {
            try
            {
                // Ensure called on UI thread
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.Invoke(() => PerformShutdownCleanup());
                    return;
                }

                CleanUpFFmpegProcesses();
                try { if (_trayIcon != null) { _trayIcon.Visible = false; _trayIcon.Dispose(); _trayIcon = null; } } catch { }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PerformShutdownCleanup error: {ex}");
            }
        }

        public bool IsOnlineMode
        {
            get => isOnlineMode;
            set
            {
                if (isOnlineMode == value) return;
                isOnlineMode = value;
                OnPropertyChanged();
            }
        }

        public string? SelectedFolderPath
        {
            get => selectedFolderPath;
            set
            {
                if (selectedFolderPath == value) return;
                selectedFolderPath = value;
                OnPropertyChanged();
            }
        }

        public bool FolderLoop
        {
            get => folderLoop;
            set { if (folderLoop == value) return; folderLoop = value; OnPropertyChanged(); }
        }

        public bool FolderRandom
        {
            get => folderRandom;
            set { if (folderRandom == value) return; folderRandom = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        // FFmpeg runtime state
        private Process? ffmpegProcess;
        private CancellationTokenSource? ffmpegCts;

        // Overlay/logo settings
        private string _overlayPath = "";

        // Shared HttpClient for source validation to avoid socket exhaustion
        private static readonly HttpClient _sharedHttpClient = new HttpClient();

        // UI timer and state
        private readonly DispatcherTimer timer = new DispatcherTimer();
        private DateTime streamStartTime;
        private volatile int isStreamingInt = 0;
        private bool _uiReady = false;

        // Debounce timer for history save to avoid excessive disk writes
        private DispatcherTimer? _historySaveTimer;

        private readonly string appDataPath;

        // Tray & close behavior
        private Forms.NotifyIcon? _trayIcon;
        private bool _hasShownTrayTip = false;
        private bool _trayInitialized = false;
        // tray menu refs for safe disposal
        private Forms.ContextMenuStrip? _trayMenu;
        private Forms.ToolStripMenuItem? _trayOpenItem;
        private Forms.ToolStripMenuItem? _trayExitItem;
        private EventHandler? _trayDoubleClickHandler;
        private EventHandler? _trayOpenClickHandler;
        private EventHandler? _trayExitClickHandler;
        private bool _allowClose = false;
        private CloseAction _rememberedAction = CloseAction.Ask;
        private readonly string prefsPath;
        private string _currentTheme = "Pro";

        // ── Theme palette ──────────────────────────────────────────────────────
        private static System.Windows.Media.Color C(byte r, byte g, byte b) => System.Windows.Media.Color.FromRgb(r, g, b);

        private static readonly (string Key, System.Windows.Media.Color Pro, System.Windows.Media.Color Arc)[] _themeColors =
        {
            ("BgPrimary",        C(0x0F,0x17,0x20), C(0x13,0x08,0x10)),
            ("BgPanel",          C(0x14,0x1C,0x26), C(0x1A,0x0B,0x17)),
            ("BgPanelSecondary", C(0x1B,0x24,0x30), C(0x20,0x0E,0x1C)),
            ("TextPrimary",      C(0xE6,0xED,0xF3), C(0xF0,0xF0,0xFF)),
            ("TextSecondary",    C(0x9C,0xA6,0xB2), C(0x00,0xE5,0xFF)),
            ("TextMuted",        C(0x6B,0x76,0x83), C(0x4A,0x30,0x45)),
            ("AccentYellow",     C(0xF5,0xB7,0x2E), C(0x00,0xFF,0xFF)),
            ("AccentYellowHover",C(0xFF,0xC9,0x4A), C(0x40,0xFF,0xFF)),
            ("DangerRed",        C(0xC6,0x3D,0x3D), C(0xFF,0x17,0x44)),
            ("DangerRedHover",   C(0xE3,0x4D,0x4D), C(0xFF,0x4D,0x6A)),
            ("StatusGreen",      C(0x2E,0xCC,0x71), C(0x05,0xFF,0x74)),
            ("StatusGreenBright",C(0x36,0xE0,0x7F), C(0x40,0xFF,0x90)),
            ("InputBg",          C(0x0C,0x12,0x18), C(0x0D,0x06,0x0B)),
            ("BadgeBackground",  C(0xF5,0xB7,0x2E), C(0x5F,0xFF,0xFF)),
            ("BgCard",           C(0x14,0x1C,0x26), C(0x1A,0x0B,0x17)),
            ("BgCardHover",      C(0x17,0x20,0x29), C(0x22,0x0F,0x1F)),
            ("BgCardActive",     C(0x20,0x2A,0x34), C(0x2A,0x12,0x27)),
            ("Success",          C(0x2E,0xCC,0x71), C(0x05,0xFF,0x74)),
            ("Danger",           C(0xC6,0x3D,0x3D), C(0xFF,0x17,0x44)),
            ("Warning",          C(0xF5,0xB7,0x2E), C(0xFF,0xEA,0x00)),
            ("BgPanelHover",     C(0x17,0x20,0x29), C(0x22,0x0F,0x1F)),
            ("BgPanelActive",    C(0x20,0x2A,0x34), C(0x2A,0x12,0x27)),
            ("FocusBlue",        C(0x2D,0x9C,0xDB), C(0x5F,0xFF,0xFF)),
            ("SparklineBitrate", C(0xF5,0xB7,0x2E), C(0xFF,0xEA,0x00)),
            ("SparklineCPU",     C(0x36,0xE0,0x7F), C(0x05,0xFF,0x74)),
            ("SparklineBg",      C(0x17,0x1A,0x20), C(0x0D,0x06,0x0B)),
        };

        // FFmpeg progress parsing
        private static readonly Regex _regexBitrate = new(@"bitrate=\s*([\d.]+)kbits/s", RegexOptions.Compiled);
        private static readonly Regex _regexSpeed = new(@"speed=\s*([\d.]+)x", RegexOptions.Compiled);
        private static readonly Regex _regexFps = new(@"fps=\s*([\d.]+)", RegexOptions.Compiled);
        private static readonly Regex _regexFrame = new(@"frame=\s*(\d+)", RegexOptions.Compiled);
        private PerformanceCounter? _cpuCounter;

        // Per-process CPU tracking
        private DateTime _lastCpuCheck;
        private TimeSpan _lastCpuTime;

        // Frame-based watchdog — tracks real output frames to detect frozen streams.
        // Only fires after at least one frame has been produced (never during startup/connecting).
        private int _lastFrameCount = -1;
        private DateTime _lastFrameAdvancedUtc = DateTime.MinValue;
        private int _lastFfmpegExitCode = -1;

        // Circular buffer size for stderr/stdout to prevent unbounded memory growth
        private const int MaxStderrBufferLines = 200;
        private static readonly HashSet<string> StaticOverlayExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png",
            ".jpg",
            ".jpeg",
            ".bmp"
        };
        private string _overlayCachePath = string.Empty;

        // Capture mode
        private sealed class CaptureMonitorItem
        {
            public int Index { get; init; }
            public string Label { get; init; } = "";
            public override string ToString() => Label;
        }

        // Highlights structured log
        private StreamWriter? _hlLogWriter;
        private readonly object _hlLogLock = new object();
        private DateTime _hlSessionStart;
        private readonly List<string> _hlExtraFolders = new();

        private void HlLogOpen(string folder)
        {
            try
            {
                var logsDir = Path.Combine(appDataPath, "hl-logs");
                Directory.CreateDirectory(logsDir);
                var fileName = $"hl_{DateTime.Now:yyyyMMdd_HHmmss}.jsonl";
                _hlLogWriter = new StreamWriter(Path.Combine(logsDir, fileName), append: false, Encoding.UTF8) { AutoFlush = true };
                _hlSessionStart = DateTime.Now;
                HlLogWrite("session_start", new { folder, log_path = Path.Combine(logsDir, fileName) });
                Dispatcher.BeginInvoke(() => AddToHistory($"[HL] Log → {Path.Combine(logsDir, fileName)}"));
            }
            catch { }
        }

        private void HlLogClose(string reason)
        {
            try
            {
                HlLogWrite("session_end", new { reason, duration_s = (DateTime.Now - _hlSessionStart).TotalSeconds });
                lock (_hlLogLock) { _hlLogWriter?.Close(); _hlLogWriter = null; }
            }
            catch { }
        }

        private void HlLogWrite(string evt, object? data = null)
        {
            try
            {
                var entry = JsonSerializer.Serialize(new { t = DateTime.Now.ToString("O"), evt, data });
                lock (_hlLogLock) { _hlLogWriter?.WriteLine(entry); }
            }
            catch { }
        }

        // Reconnection settings for online mode
        private const int MaxReconnectAttempts = 5;
        private static readonly int[] ReconnectDelaysMs = { 2000, 4000, 8000, 16000, 30000 };
        // Capture mode reconnects faster — DXGI recovers in milliseconds, no need to wait 2s
        private static readonly int[] CaptureReconnectDelaysMs = { 500, 1000, 2000, 4000, 8000 };

        private static bool IsStaticOverlay(string overlayPath)
        {
            var extension = Path.GetExtension(overlayPath);
            return StaticOverlayExtensions.Contains(extension);
        }

        private string PrepareOverlayInput(string overlayPath, int overlaySize)
        {
            if (string.IsNullOrWhiteSpace(overlayPath) || !File.Exists(overlayPath) || !IsStaticOverlay(overlayPath))
                return overlayPath;

            try
            {
                overlaySize = Math.Clamp(overlaySize, 48, 128);
                var fileInfo = new FileInfo(overlayPath);
                var stamp = fileInfo.LastWriteTimeUtc.Ticks;
                var cacheKey = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes($"{overlayPath}|{stamp}|{overlaySize}")));
                var cachedPath = Path.Combine(_overlayCachePath, $"overlay_{cacheKey}_{overlaySize}.png");
                if (File.Exists(cachedPath))
                    return cachedPath;

                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(overlayPath, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();

                var scale = overlaySize / (double)Math.Max(1, bitmap.PixelWidth);
                var scaled = new System.Windows.Media.Imaging.TransformedBitmap(bitmap, new ScaleTransform(scale, scale));
                scaled.Freeze();

                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(scaled));
                using var stream = File.Create(cachedPath);
                encoder.Save(stream);
                return cachedPath;
            }
            catch
            {
                return overlayPath;
            }
        }

        private void AddOverlayInputArguments(ICollection<string> args, string overlayPath, int overlaySize)
        {
            if (string.IsNullOrWhiteSpace(overlayPath) || !File.Exists(overlayPath))
                return;

            var preparedOverlayPath = PrepareOverlayInput(overlayPath, overlaySize);

            // Static images are cheaper when FFmpeg loops the frame internally.
            if (IsStaticOverlay(preparedOverlayPath))
            {
                args.Add("-loop");
                args.Add("1");
            }

            args.Add("-i");
            args.Add(preparedOverlayPath);
        }

        // Detected hardware encoder (cached at startup). null = not available or not detected yet.
        private string? _detectedHwEncoder = null;

        // GPU usage: polled on background thread, read on UI thread
        private double _cachedGpuUsage = -1;
        private System.Threading.Timer? _gpuPollTimer;

        private enum CloseAction
        {
            Ask = 0,
            MinimizeToTray = 1,
            Exit = 2,
            Cancel = 3
        }

        public MainWindow()
        {
            InitializeComponent();

            // Set window icon from embedded resource
            try
            {
                Icon = new System.Windows.Media.Imaging.BitmapImage(
                    new Uri("pack://application:,,,/streamer.ico"));
            }
            catch { }

            // DataContext for bindings
            DataContext = this;

            // Set dynamic version in the yellow badge from assembly info
            try
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                if (version != null)
                {
                    VersionBadge.Text = version.Build > 0
                        ? $"v{version.Major}.{version.Minor}.{version.Build}"
                        : $"v{version.Major}.{version.Minor}";
                }
                else
                {
                    VersionBadge.Text = "v2.0.4";
                }
            }
            catch { }

            appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "CorilloStreamer"
            );
            _overlayCachePath = Path.Combine(appDataPath, "overlay-cache");

            prefsPath = Path.Combine(appDataPath, "prefs.json");

            // Offload disk IO (directory creation & loads) to background to avoid UI freeze.
            _ = InitializeAppDataAndLoadAsync();
            InitializeComponents();
            LoadThemeEarly();
            LoadLanguagePreference();

            // Wire UI events after InitializeComponent to avoid being called before controls are ready
            try
            {
                if (FindName("ModeOnline") is System.Windows.Controls.RadioButton modeOnline)
                    modeOnline.Checked += ModeOnline_Checked;
                if (FindName("ModeFolder") is System.Windows.Controls.RadioButton modeFolder)
                    modeFolder.Checked += ModeFolder_Checked;
                if (FindName("ModeFile") is System.Windows.Controls.RadioButton modeFile)
                    modeFile.Checked += ModeFile_Checked;
                if (FindName("ModeCapture") is System.Windows.Controls.RadioButton modeCapture)
                    modeCapture.Checked += ModeCapture_Checked;
            }
            catch { /* ignore if wiring fails */ }

            PopulateCaptureMonitors();

            // Load sources and check ffmpeg after window is loaded (single handler)
            this.Loaded += MainWindow_Loaded;

            this.Closing += MainWindow_Closing;

            // Hook minimize state changed to support minimize-to-tray
            this.StateChanged += MainWindow_StateChanged;

            // Load remembered choice will be done async in InitializeAppDataAndLoadAsync
        }

        private async Task InitializeAppDataAndLoadAsync()
        {
            try
            {
                // Create directories on a background thread
                await Task.Run(() =>
                {
                    Directory.CreateDirectory(appDataPath);
                    Directory.CreateDirectory(Path.Combine(appDataPath, "Favorites"));
                    Directory.CreateDirectory(_overlayCachePath);
                }).ConfigureAwait(false);

                // Load close preference on background thread (file IO)
                await Task.Run(() => LoadRememberedClosePreference()).ConfigureAwait(false);

                // Then load UI collections (on UI thread)
                await LoadFavoritesAsync().ConfigureAwait(true);
                await LoadHistoryAsync().ConfigureAwait(true);

                // Load persisted config (stream key etc.) if any
                await LoadConfigAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Initialization error: {ex.Message}");
            }
        }

        private string GetConfigPath()
        {
            return Path.Combine(appDataPath, "config.json");
        }

        private async Task LoadConfigAsync()
        {
            try
            {
                var cfg = GetConfigPath();
                if (!File.Exists(cfg)) return;

                var json = await File.ReadAllTextAsync(cfg).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(json)) return;

                try
                {
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("StreamKey", out var sk))
                    {
                        var encrypted = sk.GetString() ?? string.Empty;
                        var key = DecryptString(encrypted);
                        await Dispatcher.InvokeAsync(() =>
                        {
                            try { if (StreamKey != null) SetStreamKeyText(key); } catch { }
                        });
                    }
                }
                catch { /* ignore malformed config */ }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LoadConfigAsync error: {ex}");
            }
        }

        private async Task SaveConfigAsync()
        {
            try
            {
                var toSave = GetStreamKeyText();
                var encrypted = EncryptString(toSave);
                var cfg = new { StreamKey = encrypted };
                var json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
                var path = GetConfigPath();
                await File.WriteAllTextAsync(path, json).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SaveConfigAsync error: {ex}");
            }
        }

        // Simple encryption helpers using Aes with a static key+IV derived from a passphrase.
        // Note: This is a lightweight obfuscation. For production, use DPAPI or proper secure storage.
        private static readonly byte[] _aesKey = SHA256.HashData(Encoding.UTF8.GetBytes("CorilloStreamerSecretKey"));
        private static readonly byte[] _aesIV = MD5.HashData(Encoding.UTF8.GetBytes("CorilloStreamerIV"));

        private static string EncryptString(string plain)
        {
            try
            {
                if (string.IsNullOrEmpty(plain)) return string.Empty;
                var bytes = Encoding.UTF8.GetBytes(plain);
                var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(protectedBytes);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string DecryptString(string encrypted)
        {
            if (string.IsNullOrEmpty(encrypted)) return string.Empty;

            // Try DPAPI (preferred). If it fails, fallback to legacy AES decryption for older configs.
            try
            {
                var protectedBytes = Convert.FromBase64String(encrypted);
                var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                // Fallback to legacy AES-based decryption used in earlier releases
            }

            try
            {
                var bytes = Convert.FromBase64String(encrypted);
                using var aes = Aes.Create();
                aes.Key = _aesKey;
                aes.IV = _aesIV;
                using var ms = new MemoryStream(bytes);
                using var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
                using var sr = new StreamReader(cs, Encoding.UTF8);
                return sr.ReadToEnd();
            }
            catch
            {
                return string.Empty;
            }
        }

        private void InitializeComponents()
        {
            timer.Interval = TimeSpan.FromSeconds(1);
            timer.Tick += Timer_Tick;

            // Debounce timer: save history 3 seconds after last change
            _historySaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _historySaveTimer.Tick += (s, e) =>
            {
                _historySaveTimer.Stop();
                _ = SaveHistoryAsync();
            };

            // Init PerformanceCounter on background thread (can take 100-500ms)
            _ = Task.Run(() =>
            {
                try
                {
                    var counter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                    counter.NextValue();
                    _cpuCounter = counter;
                }
                catch { _cpuCounter = null; }
            });

            _lastCpuCheck = DateTime.UtcNow;
            _lastCpuTime = TimeSpan.Zero;
        }

        private async void CheckFFmpeg()
        {
            // Delegate to the async checker which uses bundled ffmpeg
            _ = CheckFFmpegAsync();
            // Detect available HW encoder in background
            _ = Task.Run(async () =>
            {
                try
                {
                    _detectedHwEncoder = await DetectHwEncoderAsync().ConfigureAwait(false);
                    if (_detectedHwEncoder != null)
                    {
                        var gpuLabel = _detectedHwEncoder switch
                        {
                            string e when e.Contains("nvenc") => "NVIDIA",
                            string e when e.Contains("qsv") => "Intel",
                            string e when e.Contains("amf") => "AMD",
                            _ => "GPU"
                        };
                        await Dispatcher.InvokeAsync(() =>
                        {
                            HardwareAccel.Content = $"Hardware Acceleration ({gpuLabel})";
                            HardwareAccel.IsChecked = true;
                            AddToHistory($"HW encoder detected: {_detectedHwEncoder} ({gpuLabel})");
                        });
                    }
                    else
                    {
                        await Dispatcher.InvokeAsync(() =>
                            HardwareAccel.Content = "Hardware Acceleration (No GPU)");
                        Debug.WriteLine("No HW encoder available, will use libx264.");
                    }
                }
                catch (Exception ex) { Debug.WriteLine($"HW encoder detection failed: {ex.Message}"); }
            });
        }

        private string GetFfmpegPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? string.Empty, "ffmpeg.exe");
        }

        private string GetFFprobePath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? string.Empty, "ffprobe.exe");
        }

        // Validate a single video file using ffprobe. Runs without touching UI thread.
        // Returns (ok, message) where message is non-null when a log entry should be recorded on the UI thread.
        private async Task<(bool ok, string? message)> ValidateVideoWithFFprobeAsync(string filePath)
        {
            try
            {
                var ffprobePath = GetFFprobePath();
                if (!File.Exists(ffprobePath))
                {
                    // ffprobe not available, skip validation
                    return (true, null);
                }

                var psi = new ProcessStartInfo
                {
                    FileName = ffprobePath,
                    Arguments = $"-v error -select_streams v:0 -show_entries stream=codec_name -of default=noprint_wrappers=1:nokey=1 \"{filePath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var proc = new Process { StartInfo = psi };
                proc.Start();

                var outputTask = proc.StandardOutput.ReadToEndAsync();
                var errorTask = proc.StandardError.ReadToEndAsync();
                var waitTask = proc.WaitForExitAsync();

                await Task.WhenAll(outputTask, errorTask, waitTask).ConfigureAwait(false);

                var output = await outputTask.ConfigureAwait(false);
                var error = await errorTask.ConfigureAwait(false);

                bool ok = proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(output);
                if (!ok)
                {
                    return (false, $"Skipped invalid video: {Path.GetFileName(filePath)}");
                }

                return (true, null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ffprobe validation error for {filePath}: {ex}");
                return (false, $"Skipped invalid video: {Path.GetFileName(filePath)}");
            }
        }

        private async Task CheckFFmpegAsync()
        {
            try
            {
                string ffmpegPath = GetFfmpegPath();

                if (!File.Exists(ffmpegPath))
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        UpdateFFmpegStatus(false);
                        StartButton.IsEnabled = false;
                        System.Windows.MessageBox.Show(Str.G("str_msg_ffmpeg_body").Replace("\\n", "\n"), Str.G("str_msg_ffmpeg_title"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    });
                    return;
                }

                // Try running ffmpeg -version non-blocking
                try
                {
                    var p = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = ffmpegPath,
                            Arguments = "-version",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            CreateNoWindow = true
                        }
                    };

                    p.Start();
                    string output = await p.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                    p.WaitForExit(3000);
                    bool detected = output.Contains("ffmpeg version", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(output);

                    await Dispatcher.InvokeAsync(() =>
                    {
                        UpdateFFmpegStatus(detected);
                        StartButton.IsEnabled = detected;
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error starting bundled ffmpeg: {ex}");
                    await Dispatcher.InvokeAsync(() =>
                    {
                        UpdateFFmpegStatus(false);
                        StartButton.IsEnabled = false;
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CheckFFmpegAsync error: {ex}");
                await Dispatcher.InvokeAsync(() => UpdateFFmpegStatus(false));
            }
        }

        private void UpdateFFmpegStatus(bool detected)
        {
            if (detected)
            {
                FFmpegIndicator.Fill = (SolidColorBrush)FindResource("Success");
                FFmpegStatusText.Text = Str.G("str_ffmpeg_detected");
                FFmpegPill.Cursor = System.Windows.Input.Cursors.Arrow;
                FFmpegPill.ToolTip = null;
            }
            else
            {
                FFmpegIndicator.Fill = (SolidColorBrush)FindResource("Danger");
                FFmpegStatusText.Text = Str.G("str_ffmpeg_not_found");
                FFmpegPill.Cursor = System.Windows.Input.Cursors.Hand;
                FFmpegPill.ToolTip = Str.G("str_ffmpeg_tooltip_dl");
            }
        }

        private void FFmpegPill_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Only open download window if FFmpeg is missing
            if (FFmpegStatusText.Text.Contains("No encontrado"))
            {
                var win = new FFmpegDownloadWindow { Owner = this };
                win.ShowDialog();
                if (win.DownloadCompleted)
                    _ = CheckFFmpegAsync();
            }
        }

        // ── Auto-update ───────────────────────────────────────────────────────
        private static readonly HttpClient _updateHttp = new() { Timeout = TimeSpan.FromSeconds(15) };
        private string? _latestVersion;
        private string? _latestDownloadUrl;

        private async Task CheckForUpdateAsync()
        {
            try
            {
                const string apiUrl = "https://api.github.com/repos/marcosstgo/Streamer/releases/latest";
                using var req = new HttpRequestMessage(HttpMethod.Get, apiUrl);
                req.Headers.UserAgent.ParseAdd("StreamerPro/2.1.0");
                using var resp = await _updateHttp.SendAsync(req).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return;

                var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var tagName = root.GetProperty("tag_name").GetString() ?? "";
                var latestVersion = tagName.TrimStart('v');
                var currentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

                // Find exe asset
                string? downloadUrl = null;
                if (root.TryGetProperty("assets", out var assets))
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        var name = asset.GetProperty("name").GetString() ?? "";
                        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = asset.GetProperty("browser_download_url").GetString();
                            break;
                        }
                    }
                }

                if (Version.TryParse(latestVersion, out var latest) &&
                    Version.TryParse(currentVersion, out var current) &&
                    latest > current &&
                    downloadUrl != null)
                {
                    _latestVersion = latestVersion;
                    _latestDownloadUrl = downloadUrl;

                    await Dispatcher.InvokeAsync(() =>
                    {
                        UpdateBadgeText.Text = $"v{latestVersion} disponible";
                        UpdateBadge.Visibility = Visibility.Visible;
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CheckForUpdateAsync error: {ex}");
            }
        }

        private void UpdateBadge_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_latestDownloadUrl == null) return;
            var win = new UpdateWindow(_latestVersion!, _latestDownloadUrl) { Owner = this };
            win.ShowDialog();
        }
        // ──────────────────────────────────────────────────────────────────────

        private async Task CheckServerStatusAsync()
        {
            try
            {
                string rtmpUrl = await Dispatcher.InvokeAsync(() => RTMPBase.Text ?? string.Empty);
                string host = string.Empty;

                try
                {
                    if (!string.IsNullOrWhiteSpace(rtmpUrl))
                    {
                        var cleaned = rtmpUrl.Trim().TrimEnd('/');
                        // Extract hostname from rtmp://host/path or similar
                        if (cleaned.Contains("://"))
                        {
                            var afterScheme = cleaned.Substring(cleaned.IndexOf("://") + 3);
                            host = afterScheme.Split('/', ':', '?')[0];
                        }
                        else
                        {
                            host = cleaned.Split('/', ':', '?')[0];
                        }
                    }
                }
                catch { }

                if (string.IsNullOrWhiteSpace(host))
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        ServerIndicator.Fill = (SolidColorBrush)FindResource("TextMuted");
                        ServerStatusText.Text = Str.G("str_server_default");
                        ServerLatencyText.Text = "--";
                    });
                    return;
                }

                var displayHost = host;

                // Ping the server to measure latency
                long latencyMs = -1;
                bool online = false;
                try
                {
                    using var ping = new Ping();
                    var reply = await ping.SendPingAsync(host, 3000);
                    if (reply.Status == IPStatus.Success)
                    {
                        latencyMs = reply.RoundtripTime;
                        online = true;
                    }
                }
                catch
                {
                    // Ping may be blocked; fallback to TCP connect on port 1935 (RTMP)
                    try
                    {
                        var sw = Stopwatch.StartNew();
                        using var tcp = new System.Net.Sockets.TcpClient();
                        var connectTask = tcp.ConnectAsync(host, 1935);
                        if (await Task.WhenAny(connectTask, Task.Delay(3000)) == connectTask && tcp.Connected)
                        {
                            sw.Stop();
                            latencyMs = sw.ElapsedMilliseconds;
                            online = true;
                        }
                    }
                    catch { }
                }

                await Dispatcher.InvokeAsync(() =>
                {
                    ServerStatusText.Text = displayHost;
                    if (online)
                    {
                        ServerIndicator.Fill = (SolidColorBrush)FindResource("Success");
                        ServerLatencyText.Text = $"{latencyMs}ms · Online";
                        ServerLatencyText.Foreground = (SolidColorBrush)FindResource("Success");
                    }
                    else
                    {
                        ServerIndicator.Fill = (SolidColorBrush)FindResource("Danger");
                        ServerLatencyText.Text = Str.G("str_server_offline");
                        ServerLatencyText.Foreground = (SolidColorBrush)FindResource("Danger");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CheckServerStatusAsync error: {ex}");
                await Dispatcher.InvokeAsync(() =>
                {
                    ServerIndicator.Fill = (SolidColorBrush)FindResource("Danger");
                    ServerStatusText.Text = Str.G("str_server_error");
                    ServerLatencyText.Text = "--";
                });
            }
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            bool isStreaming = System.Threading.Interlocked.CompareExchange(ref isStreamingInt, 0, 0) != 0;
            if (isStreaming)
            {
                var elapsed = DateTime.Now - streamStartTime;
                StreamTime.Text = $"Tiempo: {elapsed:hh\\:mm\\:ss}";
                try { StreamTimeCompact.Text = $"{elapsed:hh\\:mm\\:ss}"; } catch { }

                // Per-process CPU usage (more accurate than system-wide)
                try
                {
                    var proc = ffmpegProcess;
                    if (proc != null && !proc.HasExited)
                    {
                        var now = DateTime.UtcNow;
                        var cpuTime = proc.TotalProcessorTime;
                        var wallElapsed = (now - _lastCpuCheck).TotalMilliseconds;
                        if (wallElapsed > 0)
                        {
                            var cpuElapsed = (cpuTime - _lastCpuTime).TotalMilliseconds;
                            var cpuPercent = cpuElapsed / (wallElapsed * Environment.ProcessorCount) * 100.0;
                            MetricCpu.Text = $"{cpuPercent:F1}%";
                        }
                        _lastCpuCheck = now;
                        _lastCpuTime = cpuTime;

                        // Memory usage of FFmpeg process
                        try
                        {
                            var memMb = proc.WorkingSet64 / (1024.0 * 1024.0);
                            MetricMem.Text = $"{memMb:F1} MB";
                        }
                        catch { }

                        // GPU usage (read cached value from background poll)
                        try
                        {
                            var gpu = _cachedGpuUsage;
                            MetricGpu.Text = gpu >= 0 ? $"{gpu:F1}%" : "--";
                        }
                        catch { MetricGpu.Text = "--"; }
                    }
                    else if (_cpuCounter != null)
                    {
                        // Fallback to system-wide CPU
                        MetricCpu.Text = $"{_cpuCounter.NextValue():F0}%";
                    }
                }
                catch { }

                UpdateVideoHealthIndicator();
            }
        }

        /// <summary>
        /// Polls GPU utilization on a background thread every 2 seconds.
        /// Uses nvidia-smi for NVIDIA GPUs (fast and accurate), falls back to WMI query.
        /// Stores result in _cachedGpuUsage for the UI timer to read.
        /// </summary>
        private void StartGpuPolling()
        {
            if (_gpuPollTimer != null) return;
            _gpuPollTimer = new System.Threading.Timer(_ => PollGpuUsage(), null, 0, 2000);
        }

        private void StopGpuPolling()
        {
            _gpuPollTimer?.Dispose();
            _gpuPollTimer = null;
            _cachedGpuUsage = -1;
        }

        private void PollGpuUsage()
        {
            try
            {
                // NVIDIA: use nvidia-smi (instant, lightweight, accurate)
                if (_detectedHwEncoder != null && _detectedHwEncoder.Contains("nvenc"))
                {
                    var result = QueryNvidiaSmi();
                    if (result >= 0) { _cachedGpuUsage = result; return; }
                }

                // Fallback: not available
                _cachedGpuUsage = -1;
            }
            catch
            {
                _cachedGpuUsage = -1;
            }
        }

        /// <summary>
        /// Queries nvidia-smi for GPU utilization. Returns -1 if unavailable.
        /// </summary>
        private static double QueryNvidiaSmi()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "nvidia-smi",
                    Arguments = "--query-gpu=utilization.gpu --format=csv,noheader,nounits",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc == null) return -1;
                var output = proc.StandardOutput.ReadToEnd().Trim();
                _ = proc.StandardError.ReadToEnd(); // drain stderr
                if (!proc.WaitForExit(2000))
                {
                    try { proc.Kill(); } catch { }
                    return -1;
                }
                if (double.TryParse(output, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var val))
                    return val;
            }
            catch { }
            return -1;
        }

        private void ParseFFmpegProgress(string line)
        {
            try
            {
                // Update UI metrics
                var mBitrate = _regexBitrate.Match(line);
                if (mBitrate.Success)
                    Dispatcher.BeginInvoke(() => MetricBitrate.Text = $"{mBitrate.Groups[1].Value} kb/s");

                var mSpeed = _regexSpeed.Match(line);
                if (mSpeed.Success)
                    Dispatcher.BeginInvoke(() => MetricSpeed.Text = $"{mSpeed.Groups[1].Value}x");

                var mFps = _regexFps.Match(line);
                if (mFps.Success)
                    Dispatcher.BeginInvoke(() => MetricFps.Text = mFps.Groups[1].Value);

                // Frame count is the watchdog's heartbeat signal.
                // Only update when the frame counter actually advances — this guarantees:
                //   - No false positives during startup (frame=0 never triggers the watchdog)
                //   - Reliable freeze detection once the stream is running
                var mFrame = _regexFrame.Match(line);
                if (mFrame.Success && int.TryParse(mFrame.Groups[1].Value, out var fc) && fc > _lastFrameCount)
                {
                    _lastFrameCount = fc;
                    _lastFrameAdvancedUtc = DateTime.UtcNow;
                }
            }
            catch { }
        }

        private void ResetVideoHealthState()
        {
            _lastFrameCount = -1;
            _lastFrameAdvancedUtc = DateTime.MinValue;
            _watchdogKilledAt = DateTime.MinValue;
        }

        // Seconds without a new frame before the watchdog kills and restarts FFmpeg
        private const int WatchdogStallSeconds = 20;
        private DateTime _watchdogKilledAt = DateTime.MinValue;

        private void UpdateVideoHealthIndicator()
        {
            try
            {
                var now = DateTime.UtcNow;
                var success = (SolidColorBrush)FindResource("Success");
                var warning = (SolidColorBrush)FindResource("Warning");
                var danger  = (SolidColorBrush)FindResource("Danger");

                bool everSeenFrames = _lastFrameAdvancedUtc != DateTime.MinValue;
                double staleSec = everSeenFrames ? (now - _lastFrameAdvancedUtc).TotalSeconds : double.MaxValue;

                // UI indicator
                if (!everSeenFrames)
                {
                    // Startup / connecting — no frames yet, completely normal
                    VideoHealthIndicator.Fill = warning;
                    VideoHealthText.Text = Str.G("str_status_connecting");
                    VideoHealthText.Foreground = warning;
                }
                else if (staleSec < 5)
                {
                    VideoHealthIndicator.Fill = success;
                    VideoHealthText.Text = Str.G("str_status_live");
                    VideoHealthText.Foreground = success;
                }
                else if (staleSec < WatchdogStallSeconds)
                {
                    VideoHealthIndicator.Fill = warning;
                    VideoHealthText.Text = Str.G("str_status_unstable");
                    VideoHealthText.Foreground = warning;
                }
                else
                {
                    VideoHealthIndicator.Fill = danger;
                    VideoHealthText.Text = Str.G("str_status_frozen");
                    VideoHealthText.Foreground = danger;
                }

                // Watchdog: only fires once real frames were seen and then stalled.
                // The cooldown prevents repeated kills faster than one per WatchdogStallSeconds.
                if (everSeenFrames
                    && staleSec >= WatchdogStallSeconds
                    && (now - _watchdogKilledAt).TotalSeconds > WatchdogStallSeconds)
                {
                    var proc = ffmpegProcess;
                    if (proc != null && !proc.HasExited)
                    {
                        _watchdogKilledAt = now;
                        Dispatcher.BeginInvoke(() => AddToHistory("⚠ Watchdog: stream congelado — reiniciando FFmpeg..."));
                        try { proc.Kill(entireProcessTree: true); } catch { try { proc.Kill(); } catch { } }
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Returns true if the stderr line is a progress/stats line (should not clutter history).
        /// </summary>
        private static bool IsFFmpegProgressLine(string line)
        {
            // Progress lines typically start with "frame=", "size=", or contain "bitrate=" and "speed="
            if (string.IsNullOrWhiteSpace(line)) return false;
            var trimmed = line.TrimStart();
            return trimmed.StartsWith("frame=", StringComparison.Ordinal)
                || trimmed.StartsWith("size=", StringComparison.Ordinal)
                || (trimmed.Contains("bitrate=") && trimmed.Contains("speed="));
        }

        /// <summary>
        /// Adds a line to a circular buffer (Queue), keeping max capacity.
        /// </summary>
        private static void AddToCircularBuffer(Queue<string> buffer, string line, int maxLines)
        {
            buffer.Enqueue(line);
            while (buffer.Count > maxLines)
                buffer.Dequeue();
        }

        private async Task LoadAndValidateSourcesAsync()
        {
            try
            {
                var path = SourcesRepository.GetSourcesPath();

                await Dispatcher.InvokeAsync(() => SourceStatusText.Text = Str.G("str_loading_sources"));

                try
                {
                    // Ensure defaults file exists (may write file)
                    await SourcesRepository.EnsureDefaultSourcesAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error ensuring default sources: {ex}");
                    await Dispatcher.InvokeAsync(() => SourceStatusText.Text = $"Error creando defaults: {ex.Message}");
                }

                List<Source> list;
                try
                {
                    list = await SourcesRepository.LoadSourcesAsync();
                }
                catch (Exception ex)
                {
                    var message = $"Error cargando fuentes: {ex.Message} - Path: {path}";
                    Debug.WriteLine(message);
                    await Dispatcher.InvokeAsync(() => SourceStatusText.Text = message);
                    list = SourcesRepository.GetDefaultSources();
                }

                await Dispatcher.InvokeAsync(() => SourceStatusText.Text = Str.G("str_validating_sources"));

                var availability = await ValidateSourcesAsync(list).ConfigureAwait(false);

                // Apply results on UI thread
                await Dispatcher.InvokeAsync(() =>
                {
                    Sources.Clear();
                    foreach (var s in list)
                    {
                        if (availability.TryGetValue(s.Url, out var ok))
                            s.IsAvailable = ok;
                        else
                            s.IsAvailable = false;

                        Sources.Add(s);
                    }

                    // select first available or first - ensure we assign the instance from Sources so binding shows DisplayText
                    var firstAvailable = Sources.FirstOrDefault(x => x.IsAvailable) ?? Sources.FirstOrDefault();
                    SelectedSource = firstAvailable;
                    SourceStatusText.Text = Str.G("str_sources_ready");
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading sources: {ex}");
                await Dispatcher.InvokeAsync(() => SourceStatusText.Text = $"Error cargando fuentes: {ex.Message}");
            }
        }

        private async Task<Dictionary<string, bool>> ValidateSourcesAsync(IEnumerable<Source> list)
        {
            var results = new Dictionary<string, bool>();
            var http = _sharedHttpClient;

            var semaphore = new SemaphoreSlim(4); // limit ffprobe concurrency
            var tasks = list.Select(async s =>
            {
                await semaphore.WaitAsync().ConfigureAwait(false);
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    var req = new HttpRequestMessage(HttpMethod.Head, s.Url);
                    var res = await http.SendAsync(req, cts.Token).ConfigureAwait(false);
                    if (res.IsSuccessStatusCode)
                    {
                        return (s.Url, true);
                    }

                    req = new HttpRequestMessage(HttpMethod.Get, s.Url);
                    req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
                    res = await http.SendAsync(req, cts.Token).ConfigureAwait(false);
                    return (s.Url, res.IsSuccessStatusCode);
                }
                catch
                {
                    return (s.Url, false);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var resultsArr = await Task.WhenAll(tasks).ConfigureAwait(false);
            foreach (var (url, ok) in resultsArr)
                results[url] = ok;

            return results;
        }

        private async void StartStream_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(GetStreamKeyText()))
                {
                    System.Windows.MessageBox.Show(Str.G("str_msg_streamkey_required"), Str.G("str_msg_error"),
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string rtmpBase = RTMPBase.Text.TrimEnd('/');
                string streamKey = GetStreamKeyText().TrimStart('/');
                string rtmpUrl = $"{rtmpBase}/{streamKey}";
                string vBitrate = VideoBitrateManual.Text;
                string aBitrate = AudioBitrateManual.Text;

                var presetItem = PresetCombo.SelectedItem as ComboBoxItem;
                string preset = presetItem?.Content?.ToString() ?? "veryfast";

                string resolution = ResolutionManual.Text;
                string fps = FPSManual.Text;
                string overlayPath = _overlayPath;
                bool overlayActive = !string.IsNullOrWhiteSpace(overlayPath) && File.Exists(overlayPath);
                string overlayPos = ((OverlayPositionCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString()) ?? "W-w-10:H-h-10";
                int overlaySize = int.TryParse((OverlaySizeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var osz) ? osz : 130;

                // Cancel previous if any and create new CTS
                ffmpegCts?.Cancel();
                ffmpegCts = new CancellationTokenSource();

                // Sync mode flag from actual radio button state to avoid stale value
                IsOnlineMode = ModeOnline.IsChecked == true;

                if (IsOnlineMode)
                {
                    var sourceObj = SelectedSource;
                    if (sourceObj == null)
                    {
                        System.Windows.MessageBox.Show(Str.G("str_msg_select_source"), Str.G("str_msg_error"),
                                        MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    if (!sourceObj.IsAvailable)
                    {
                        System.Windows.MessageBox.Show(Str.G("str_msg_source_unavailable"), Str.G("str_msg_source_unavailable_title"),
                                        MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    string sourceUrl = sourceObj.Url;

                    // Build argument list safely and efficiently for online (single source) mode
                    var args = new List<string>();
                    bool hwAccelFlagOnline = HardwareAccel.IsChecked == true;
                    string? hwEncoder = null;
                    if (hwAccelFlagOnline)
                    {
                        if (_detectedHwEncoder != null)
                        {
                            hwEncoder = _detectedHwEncoder;
                            AddToHistory($"HWAccel: GPU encoding enabled - encoder: {hwEncoder}");
                        }
                        else
                        {
                            AddToHistory("HWAccel: no compatible GPU encoder found, using libx264");
                        }
                    }

                    args.AddRange(new[] { "-hide_banner", "-re", "-i", sourceUrl });
                    if (overlayActive) AddOverlayInputArguments(args, overlayPath, overlaySize);
                    args.AddRange(BuildEncodingArguments(
                        rtmpUrl: rtmpUrl,
                        preset: preset,
                        videoBitrate: vBitrate,
                        audioBitrate: aBitrate,
                        resolution: resolution,
                        fps: fps,
                        isFolderMode: false,
                        hwEncoder: hwEncoder,
                        hasOverlay: overlayActive,
                        overlayPreScaled: overlayActive && IsStaticOverlay(overlayPath),
                        overlayPos: overlayPos,
                        overlaySize: overlaySize
                    ));

                    if (ShowFFmpegCommand.IsChecked == true)
                    {
                        string display = string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
                        System.Windows.MessageBox.Show(display, "Comando FFmpeg",
                                      MessageBoxButton.OK, MessageBoxImage.Information);
                    }

                    // Start ffmpeg with auto-reconnect on failure and optional loop
                    var capturedArgs = args.ToList();
                    var capturedCts = ffmpegCts;
                    _ = Task.Run(async () =>
                    {
                        await Dispatcher.InvokeAsync(() => { System.Threading.Interlocked.Exchange(ref isStreamingInt, 1); streamStartTime = DateTime.Now; timer.Start(); UpdateStreamStatus(true); });
                        int attempt = 0;
                        try
                        {
                            while (!capturedCts.Token.IsCancellationRequested)
                            {
                                await ExecuteFFmpegAsync(capturedArgs, capturedCts.Token).ConfigureAwait(false);

                                if (capturedCts.Token.IsCancellationRequested) break;

                                // Check if Loop Infinito is enabled — if so, restart immediately (no backoff)
                                bool shouldLoop = await Dispatcher.InvokeAsync(() => LoopInfinite.IsChecked == true);
                                if (shouldLoop)
                                {
                                    await Dispatcher.InvokeAsync(() => AddToHistory("Loop: restarting source..."));
                                    attempt = 0; // reset reconnect counter
                                    continue;
                                }

                                // Not looping — treat as unexpected end, reconnect with backoff
                                attempt++;
                                if (attempt >= MaxReconnectAttempts)
                                {
                                    await Dispatcher.InvokeAsync(() => AddToHistory($"Max reconnect attempts ({MaxReconnectAttempts}) reached. Stopping."));
                                    break;
                                }

                                var delayMs = ReconnectDelaysMs[Math.Min(attempt - 1, ReconnectDelaysMs.Length - 1)];
                                await Dispatcher.InvokeAsync(() => AddToHistory($"Stream ended unexpectedly. Reconnecting in {delayMs / 1000}s (attempt {attempt}/{MaxReconnectAttempts})..."));
                                await Task.Delay(delayMs, capturedCts.Token).ConfigureAwait(false);
                            }
                        }
                        catch (OperationCanceledException) { }
                        finally
                        {
                            await Dispatcher.InvokeAsync(() => { System.Threading.Interlocked.Exchange(ref isStreamingInt, 0); timer.Stop(); UpdateStreamStatus(false); });
                        }
                    });

                    AddToHistory($"Stream iniciado: {sourceObj.Name} - {vBitrate}");
                }
                else if (ModeFile.IsChecked == true)
                {
                    // File mode - single local file
                    selectedFilePath = selectedFilePath ?? SelectedFileText.Text;
                    if (string.IsNullOrWhiteSpace(selectedFilePath) || !File.Exists(selectedFilePath))
                    {
                        System.Windows.MessageBox.Show(Str.G("str_msg_select_valid_file"), Str.G("str_msg_invalid_file_title"),
                                        MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    var args = new List<string>();
                    bool hwAccelFlagFile = HardwareAccel.IsChecked == true;
                    string? hwEncoderFile = (hwAccelFlagFile && _detectedHwEncoder != null) ? _detectedHwEncoder : null;
                    if (hwEncoderFile != null)
                    {
                        AddToHistory($"HWAccel: GPU encoding enabled - encoder: {hwEncoderFile}");
                    }

                    args.AddRange(new[] { "-hide_banner", "-fflags", "+genpts+discardcorrupt", "-avoid_negative_ts", "make_zero", "-re", "-i", selectedFilePath });
                    if (overlayActive) AddOverlayInputArguments(args, overlayPath, overlaySize);
                    args.AddRange(BuildEncodingArguments(
                        rtmpUrl: rtmpUrl,
                        preset: preset,
                        videoBitrate: vBitrate,
                        audioBitrate: aBitrate,
                        resolution: resolution,
                        fps: fps,
                        isFolderMode: false,
                        hwEncoder: hwEncoderFile,
                        hasOverlay: overlayActive,
                        overlayPreScaled: overlayActive && IsStaticOverlay(overlayPath),
                        overlayPos: overlayPos,
                        overlaySize: overlaySize
                    ));

                    if (ShowFFmpegCommand.IsChecked == true)
                    {
                        string display = string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
                        System.Windows.MessageBox.Show(display, "Comando FFmpeg",
                                      MessageBoxButton.OK, MessageBoxImage.Information);
                    }

                    var capturedArgs = args.ToList();
                    var capturedCts = ffmpegCts;
                    var fileName = Path.GetFileName(selectedFilePath);
                    _ = Task.Run(async () =>
                    {
                        await Dispatcher.InvokeAsync(() => { System.Threading.Interlocked.Exchange(ref isStreamingInt, 1); streamStartTime = DateTime.Now; timer.Start(); UpdateStreamStatus(true); });
                        int errorAttempt = 0;
                        try
                        {
                            while (!capturedCts.Token.IsCancellationRequested)
                            {
                                _lastFfmpegExitCode = -1;
                                await ExecuteFFmpegAsync(capturedArgs, capturedCts.Token).ConfigureAwait(false);

                                if (capturedCts.Token.IsCancellationRequested) break;

                                int exitCode = _lastFfmpegExitCode;
                                if (exitCode == 0)
                                {
                                    // File finished normally — apply loop setting
                                    errorAttempt = 0;
                                    bool shouldLoop = await Dispatcher.InvokeAsync(() => LoopInfinite.IsChecked == true);
                                    if (shouldLoop)
                                    {
                                        await Dispatcher.InvokeAsync(() => AddToHistory("Loop: restarting file..."));
                                        continue;
                                    }
                                    break;
                                }
                                else
                                {
                                    // FFmpeg exited with error (network drop, watchdog kill, etc.) — reconnect
                                    errorAttempt++;
                                    if (errorAttempt > MaxReconnectAttempts) break;
                                    int delayMs = ReconnectDelaysMs[Math.Min(errorAttempt - 1, ReconnectDelaysMs.Length - 1)];
                                    await Dispatcher.InvokeAsync(() => AddToHistory($"Reconectando... intento {errorAttempt}/{MaxReconnectAttempts}"));
                                    await Task.Delay(delayMs, capturedCts.Token).ConfigureAwait(false);
                                }
                            }
                        }
                        catch (OperationCanceledException) { }
                        finally
                        {
                            await Dispatcher.InvokeAsync(() => { System.Threading.Interlocked.Exchange(ref isStreamingInt, 0); timer.Stop(); UpdateStreamStatus(false); });
                        }
                    });

                    AddToHistory($"Stream iniciado: {fileName} - {vBitrate}");
                }
                else if (ModeCapture.IsChecked == true)
                {
                    // Capture mode
                    var monitorItem = CaptureMonitorCombo.SelectedItem as CaptureMonitorItem;
                    if (monitorItem == null)
                    {
                        System.Windows.MessageBox.Show(Str.G("str_msg_select_monitor"), Str.G("str_msg_monitor_required_title"), MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    int captureMonitorIdx = monitorItem.Index;
                    int captureFps = CaptureFps60.IsChecked == true ? 60 : 30;
                    bool captureAudio = CaptureAudioCheck.IsChecked == true;
                    string captureAudioDevice = CaptureAudioDeviceCombo.SelectedItem as string ?? "";
                    bool captureHw = HardwareAccel.IsChecked == true && _detectedHwEncoder != null;
                    string captureEncoder = captureHw ? _detectedHwEncoder! : "libx264";
                    // Downscale to profile resolution (e.g. 1920x1080) — same as OBS canvas output
                    string captureScale = string.IsNullOrWhiteSpace(resolution) ? "" : resolution.Replace("x", ":");
                    var captureArgs = BuildCaptureArgs(captureMonitorIdx, captureFps, captureAudio, captureAudioDevice, captureScale, rtmpUrl, vBitrate, aBitrate, captureEncoder);
                    var capturedCaptureCts = ffmpegCts!;

                    _ = Task.Run(async () =>
                    {
                        System.Threading.Interlocked.Exchange(ref isStreamingInt, 1);
                        await Dispatcher.InvokeAsync(() => { streamStartTime = DateTime.Now; timer.Start(); UpdateStreamStatus(true); });
                        int errorAttempt = 0;
                        try
                        {
                            while (!capturedCaptureCts.Token.IsCancellationRequested)
                            {
                                ResetVideoHealthState();
                                _lastFfmpegExitCode = -1;
                                await ExecuteFFmpegAsync(captureArgs, capturedCaptureCts.Token).ConfigureAwait(false);
                                if (capturedCaptureCts.Token.IsCancellationRequested) break;
                                int exitCode = _lastFfmpegExitCode;
                                if (exitCode != 0)
                                {
                                    errorAttempt++;
                                    if (errorAttempt > MaxReconnectAttempts) break;
                                    int delayMs = CaptureReconnectDelaysMs[Math.Min(errorAttempt - 1, CaptureReconnectDelaysMs.Length - 1)];
                                    await Dispatcher.InvokeAsync(() => AddToHistory($"Reconectando... intento {errorAttempt}/{MaxReconnectAttempts}"));
                                    await Task.Delay(delayMs, capturedCaptureCts.Token).ConfigureAwait(false);
                                }
                                else break;
                            }
                        }
                        catch (OperationCanceledException) { }
                        finally
                        {
                            await Dispatcher.InvokeAsync(() => { System.Threading.Interlocked.Exchange(ref isStreamingInt, 0); timer.Stop(); UpdateStreamStatus(false); });
                        }
                    });

                    AddToHistory($"🎮 Captura: Monitor {captureMonitorIdx + 1} @ {captureFps}fps → {vBitrate}{(captureAudio ? "" : " (sin audio)")}");
                }
                else
                {
                    // Folder mode
                    SelectedFolderPath = SelectedFolderPath ?? SelectedFolderText.Text;
                    bool isHighlightsMode = FolderHighlightsCheck?.IsChecked == true;
                    bool primaryFolderOk = !string.IsNullOrWhiteSpace(SelectedFolderPath) && Directory.Exists(SelectedFolderPath);
                    if (!primaryFolderOk)
                    {
                        // En Highlights mode, las carpetas extra son suficientes
                        if (!isHighlightsMode || _hlExtraFolders.Count == 0)
                        {
                            System.Windows.MessageBox.Show(Str.G("str_msg_select_valid_folder"), Str.G("str_msg_invalid_folder_title"),
                                            MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                        // Sin carpeta principal — usar primera carpeta extra como base
                        SelectedFolderPath = _hlExtraFolders.First();
                    }

                    // gather files
                    var exts = new[] { ".mp4", ".mkv", ".mov", ".avi", ".webm" };
                    var vids = Directory.EnumerateFiles(SelectedFolderPath)
                                .Where(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                                .ToList();

                    // Capture UI flags that will be needed after awaits to avoid cross-thread access
                    bool hwAccelFlag = HardwareAccel.IsChecked == true;
                    bool showCommandFlag = ShowFFmpegCommand.IsChecked == true;
                    bool capturedRandom = FolderRandom || (FolderRandomCheck?.IsChecked == true);
                    bool capturedWaitMode = FolderWaitCheck?.IsChecked == true;
                    bool capturedHighlights = FolderHighlightsCheck?.IsChecked == true;
                    string? hwEncoderHl = (hwAccelFlag && _detectedHwEncoder != null) ? _detectedHwEncoder : null;

                    if (capturedHighlights)
                    {
                        var hlCts = ffmpegCts;
                        var capturedHlFolders = new List<string> { SelectedFolderPath! }
                            .Concat(_hlExtraFolders.Where(Directory.Exists))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();
                        var hlExts = new[] { ".mp4", ".mkv", ".mov", ".avi", ".webm" };

                        // Streamer (permanente): lee MPEG-TS desde stdin, re-encoda, empuja a RTMP
                        var streamerArgs = new List<string>
                        {
                            "-hide_banner",
                            "-fflags", "+genpts+discardcorrupt",
                            "-avoid_negative_ts", "make_zero",
                            "-thread_queue_size", "512",
                            "-f", "mpegts", "-i", "pipe:0"
                        };
                        if (overlayActive) AddOverlayInputArguments(streamerArgs, overlayPath, overlaySize);
                        streamerArgs.AddRange(BuildEncodingArguments(
                            rtmpUrl: rtmpUrl,
                            preset: preset,
                            videoBitrate: vBitrate,
                            audioBitrate: aBitrate,
                            resolution: resolution,
                            fps: fps,
                            isFolderMode: true,
                            hwEncoder: hwEncoderHl,
                            hasOverlay: overlayActive,
                            overlayPreScaled: overlayActive && IsStaticOverlay(overlayPath),
                            overlayPos: overlayPos,
                            overlaySize: overlaySize
                        ));

                        _ = Task.Run(async () =>
                        {
                            System.Threading.Interlocked.Exchange(ref isStreamingInt, 1);
                            await Dispatcher.InvokeAsync(() => { streamStartTime = DateTime.Now; timer.Start(); UpdateStreamStatusWaiting(); });
                            HlLogOpen(string.Join(", ", capturedHlFolders));

                            var clipQueue = new Queue<string>();
                            string? lastPlayed = null;
                            bool isInRepeat = false;
                            var queueLock = new object();
                            Process? supplierProc = null;
                            Process? streamerProc = null;
                            Stream? streamerStdin = null;
                            bool streamerStarted = false;
                            var supplierKillCts = new CancellationTokenSource();

                            // Scan inicial — todas las carpetas, ordenado por fecha de creación
                            var initialClips = capturedHlFolders
                                .SelectMany(folder => Directory.EnumerateFiles(folder)
                                    .Where(f => hlExts.Contains(Path.GetExtension(f).ToLowerInvariant())))
                                .OrderBy(f => File.GetCreationTime(f))
                                .ToList();
                            foreach (var c in initialClips)
                                clipQueue.Enqueue(c);
                            HlLogWrite("scan_initial", new { clip_count = initialClips.Count, folders = capturedHlFolders.Count, files = initialClips.Select(Path.GetFileName).ToArray() });
                            if (initialClips.Count > 0)
                                await Dispatcher.InvokeAsync(() => AddToHistory($"Highlights: {initialClips.Count} clip(s) en cola ({capturedHlFolders.Count} carpeta(s))"));

                            // Watchers — uno por carpeta
                            var hlWatchers = capturedHlFolders.Select(folder => new FileSystemWatcher(folder)
                            {
                                EnableRaisingEvents = true,
                                NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime
                            }).ToList();

                            FileSystemEventHandler hlWatcherCreated = async (_, e) =>
                            {
                                if (!hlExts.Contains(Path.GetExtension(e.FullPath).ToLowerInvariant())) return;
                                var detectedAt = DateTime.Now;
                                await Task.Delay(2000).ConfigureAwait(false);
                                if (!File.Exists(e.FullPath)) return;
                                int queueCount;
                                lock (queueLock)
                                {
                                    var newTime = File.GetCreationTime(e.FullPath);
                                    var list = clipQueue.ToList();
                                    int idx = list.FindIndex(f => File.GetCreationTime(f) > newTime);
                                    if (idx < 0) list.Add(e.FullPath);
                                    else list.Insert(idx, e.FullPath);
                                    clipQueue.Clear();
                                    foreach (var f in list) clipQueue.Enqueue(f);
                                    queueCount = clipQueue.Count;
                                }
                                long sizeBytes = 0;
                                try { sizeBytes = new FileInfo(e.FullPath).Length; } catch { }
                                HlLogWrite("watcher_detected", new
                                {
                                    file = Path.GetFileName(e.FullPath),
                                    size_bytes = sizeBytes,
                                    delay_ms = (int)(DateTime.Now - detectedAt).TotalMilliseconds,
                                    queue_after = queueCount,
                                    state = isInRepeat ? "repeat" : (lastPlayed == null ? "waiting" : "playing"),
                                    will_interrupt = isInRepeat || lastPlayed == null
                                });
                                await Dispatcher.InvokeAsync(() => AddToHistory($"Highlights: nuevo clip → {Path.GetFileName(e.FullPath)}"));
                                // Solo interrumpir supplier si estamos en repeat o esperando
                                if (isInRepeat || lastPlayed == null)
                                    try { supplierKillCts.Cancel(); } catch (ObjectDisposedException) { }
                            };

                            foreach (var w in hlWatchers)
                                w.Created += hlWatcherCreated;

                            try
                            {
                                while (!hlCts!.Token.IsCancellationRequested)
                                {
                                    // Verificar que el streamer siga vivo
                                    if (streamerStarted && (streamerProc == null || streamerProc.HasExited))
                                    {
                                        int hlExitCode = -1;
                                        try { hlExitCode = streamerProc!.ExitCode; } catch { }
                                        HlLogWrite("streamer_exited_unexpectedly", new { exit_code = hlExitCode });
                                        await Dispatcher.InvokeAsync(() => AddToHistory($"⚠ Highlights: streamer caído (código {hlExitCode}) — reiniciando..."));
                                        // Matar supplier activo
                                        try { supplierProc?.Kill(entireProcessTree: true); } catch { }
                                        supplierProc = null;
                                        // Resetear estado del streamer para que se reinicie en el próximo clip
                                        streamerProc = null;
                                        streamerStdin = null;
                                        streamerStarted = false;
                                        ResetVideoHealthState();
                                        // Si teníamos un clip en repeat, volver a encolarlo para que retome rápido
                                        if (lastPlayed != null)
                                        {
                                            lock (queueLock)
                                            {
                                                var tmp = new Queue<string>(clipQueue);
                                                clipQueue.Clear();
                                                clipQueue.Enqueue(lastPlayed);
                                                foreach (var f in tmp) clipQueue.Enqueue(f);
                                            }
                                        }
                                        isInRepeat = false;
                                        // Breve pausa antes de reconectar
                                        await Dispatcher.InvokeAsync(UpdateStreamStatusWaiting);
                                        await Task.Delay(3000, hlCts!.Token).ConfigureAwait(false);
                                        continue;
                                    }

                                    string? clip = null;
                                    int queueSnapshot;
                                    lock (queueLock)
                                    {
                                        if (clipQueue.Count > 0) clip = clipQueue.Dequeue();
                                        queueSnapshot = clipQueue.Count;
                                    }

                                    // Renovar supplierKillCts para esta iteración
                                    try { supplierKillCts.Dispose(); } catch { }
                                    supplierKillCts = new CancellationTokenSource();
                                    var killLinked = CancellationTokenSource.CreateLinkedTokenSource(hlCts.Token, supplierKillCts.Token);

                                    try
                                    {
                                        if (clip != null)
                                        {
                                            lastPlayed = clip;
                                            isInRepeat = false;
                                            long clipSize = 0;
                                            try { clipSize = new FileInfo(clip).Length; } catch { }
                                            HlLogWrite("clip_play", new { file = Path.GetFileName(clip), size_bytes = clipSize, queue_remaining = queueSnapshot });

                                            // Matar supplier anterior si existe
                                            try { supplierProc?.Kill(entireProcessTree: true); } catch { }

                                            // Supplier: copia streams a MPEG-TS stdout, reproduce una vez
                                            var supArgs = new List<string>
                                            {
                                                "-hide_banner", "-fflags", "+genpts", "-avoid_negative_ts", "make_zero",
                                                "-re", "-i", clip, "-c", "copy", "-f", "mpegts", "pipe:1"
                                            };
                                            supplierProc = HlStartSupplier(supArgs);

                                            // Arrancar streamer en el primer clip
                                            if (!streamerStarted)
                                            {
                                                streamerProc = HlStartStreamer(streamerArgs, hlCts.Token);
                                                ffmpegProcess = streamerProc;
                                                streamerStdin = streamerProc.StandardInput.BaseStream;
                                                streamerStarted = true;
                                                HlLogWrite("streamer_started", null);
                                            }

                                            var clipStart = DateTime.Now;
                                            _ = HlRelayAsync(supplierProc.StandardOutput.BaseStream, streamerStdin!, hlCts.Token);
                                            await Dispatcher.InvokeAsync(() => UpdateStreamStatus(true));

                                            // Esperar que el clip termine naturalmente — no interrumpible por clip nuevo
                                            bool killed = false;
                                            try { await supplierProc.WaitForExitAsync(hlCts.Token).ConfigureAwait(false); }
                                            catch (OperationCanceledException) { killed = true; try { supplierProc?.Kill(entireProcessTree: true); } catch { } }
                                            HlLogWrite("clip_end", new { file = Path.GetFileName(clip), elapsed_ms = (int)(DateTime.Now - clipStart).TotalMilliseconds, exit_code = supplierProc?.ExitCode, was_killed = killed });
                                        }
                                        else if (lastPlayed != null)
                                        {
                                            isInRepeat = true;
                                            HlLogWrite("repeat_start", new { file = Path.GetFileName(lastPlayed) });

                                            // Matar supplier anterior
                                            try { supplierProc?.Kill(entireProcessTree: true); } catch { }

                                            // Supplier: loopea último clip hasta que llegue uno nuevo
                                            var supArgs = new List<string>
                                            {
                                                "-hide_banner", "-fflags", "+genpts", "-avoid_negative_ts", "make_zero",
                                                "-stream_loop", "-1", "-re", "-i", lastPlayed,
                                                "-c", "copy", "-f", "mpegts", "pipe:1"
                                            };
                                            supplierProc = HlStartSupplier(supArgs);
                                            _ = HlRelayAsync(supplierProc.StandardOutput.BaseStream, streamerStdin!, hlCts.Token);
                                            await Dispatcher.InvokeAsync(() => UpdateStreamStatus(true));

                                            // Esperar hasta que llegue clip nuevo (supplierKillCts) o el usuario pare
                                            bool repeatKilled = false;
                                            try { await supplierProc.WaitForExitAsync(killLinked.Token).ConfigureAwait(false); }
                                            catch (OperationCanceledException) { repeatKilled = true; try { supplierProc?.Kill(entireProcessTree: true); } catch { } }
                                            HlLogWrite("repeat_end", new { reason = hlCts.Token.IsCancellationRequested ? "stop" : "new_clip", was_killed = repeatKilled });
                                        }
                                        else
                                        {
                                            // Sin clips aún — esperar
                                            HlLogWrite("waiting_start", null);
                                            await Dispatcher.InvokeAsync(UpdateStreamStatusWaiting);
                                            try { await Task.Delay(Timeout.Infinite, killLinked.Token).ConfigureAwait(false); }
                                            catch (OperationCanceledException) { }
                                            HlLogWrite("waiting_end", new { reason = hlCts.Token.IsCancellationRequested ? "stop" : "clip_arrived" });
                                        }
                                    }
                                    finally { killLinked.Dispose(); }
                                }
                            }
                            catch (OperationCanceledException) { }
                            finally
                            {
                                try { supplierKillCts.Dispose(); } catch { }
                                try { supplierProc?.Kill(entireProcessTree: true); } catch { }
                                foreach (var w in hlWatchers) { try { w.EnableRaisingEvents = false; w.Dispose(); } catch { } }
                                // Cerrar stdin del streamer → FFmpeg recibe EOF → sale limpiamente
                                try { streamerStdin?.Close(); } catch { }
                                if (streamerProc != null && !streamerProc.HasExited)
                                {
                                    using var exitTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                                    try { await streamerProc.WaitForExitAsync(exitTimeout.Token).ConfigureAwait(false); }
                                    catch { try { streamerProc?.Kill(entireProcessTree: true); } catch { } }
                                }
                                HlLogClose(hlCts.Token.IsCancellationRequested ? "user_stop" : "streamer_exited");
                                System.Threading.Interlocked.Exchange(ref isStreamingInt, 0);
                                await Dispatcher.InvokeAsync(() => { timer.Stop(); UpdateStreamStatus(false); });
                            }
                        });

                        AddToHistory($"Highlights iniciado: {string.Join(" + ", capturedHlFolders.Select(Path.GetFileName))}");
                        return;
                    }

                    if (vids.Count == 0 && !capturedWaitMode)
                    {
                        System.Windows.MessageBox.Show(Str.G("str_msg_no_videos"), Str.G("str_msg_no_videos_title"),
                                        MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    if (capturedRandom && vids.Count > 0)
                        vids = vids.OrderBy(_ => Random.Shared.Next()).ToList();
                    string? hwEncoderFolder = (hwAccelFlag && _detectedHwEncoder != null) ? _detectedHwEncoder : null;

                    // Validate files with ffprobe asynchronously and build final list
                    var validationTasks = vids.Select(async v =>
                    {
                        var res = await ValidateVideoWithFFprobeAsync(v).ConfigureAwait(false);
                        return (path: v, result: res);
                    }).ToList();
                    var validationResults = await Task.WhenAll(validationTasks).ConfigureAwait(false);

                    var validVids = new List<string>();
                    var messages = new List<string>();
                    foreach (var vr in validationResults)
                    {
                        if (vr.result.ok) validVids.Add(vr.path);
                        if (!string.IsNullOrEmpty(vr.result.message)) messages.Add(vr.result.message!);
                    }

                    // Apply UI updates (history/logs and any message boxes) on UI thread
                    await Dispatcher.InvokeAsync(() =>
                    {
                        foreach (var m in messages)
                            AddToHistory(m);
                    });

                    if (validVids.Count == 0 && !capturedWaitMode)
                    {
                        await Dispatcher.InvokeAsync(() => System.Windows.MessageBox.Show(Str.G("str_msg_no_videos"), Str.G("str_msg_no_videos_title"), MessageBoxButton.OK, MessageBoxImage.Warning));
                        return;
                    }

                    // write playlist (FFmpeg concat demuxer expects lines like: file 'FULL_PATH')
                    var tempDir = Path.Combine(Path.GetTempPath(), "StreamerPro");
                    Directory.CreateDirectory(tempDir);
                    var playlist = Path.Combine(tempDir, "playlist.txt");
                    // Use UTF8 without BOM to avoid parsing issues
                    using (var sw = new StreamWriter(playlist, false, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
                    {
                        foreach (var v in validVids)
                        {
                            // Escape single quotes for concat demuxer by closing, inserting \' and reopening: e.g. a'b -> a'\'\''b
                            var escaped = v.Replace("'", "'\\'\\''");
                            // The required format is: file 'FULL_PATH'
                            sw.WriteLine($"file '{escaped}'");
                        }
                    }

                    // Preview first few lines of the generated playlist for debugging
                    try
                    {
                        AddToHistory($"Playlist path: {playlist}");
                        var preview = File.ReadLines(playlist).Take(10).ToList();
                        AddToHistory("Playlist preview:");
                        foreach (var pl in preview)
                            AddToHistory(pl);
                    }
                    catch (Exception ex)
                    {
                        AddToHistory($"Failed to read playlist preview: {ex.Message}");
                    }

                    // build args for concat (folder) mode
                    var args = new List<string>();
                    if (hwEncoderFolder != null)
                    {
                        AddToHistory($"HWAccel: GPU encoding enabled - encoder: {hwEncoderFolder}");
                    }

                    // Stabilize timestamps and concat demuxer behavior for heterogeneous inputs
                    // Keep +genpts; remove use_wallclock_as_timestamps which can speed up playback.
                    args.AddRange(new[] { "-hide_banner", "-fflags", "+genpts", "-avoid_negative_ts", "make_zero" });

                    args.AddRange(new[] { "-re", "-f", "concat", "-safe", "0", "-i", playlist });
                    if (overlayActive) AddOverlayInputArguments(args, overlayPath, overlaySize);

                    // rest of encoding options - use previously captured flags to avoid UI access off-thread
                    args.AddRange(BuildEncodingArguments(
                        rtmpUrl: rtmpUrl,
                        preset: preset,
                        videoBitrate: vBitrate,
                        audioBitrate: aBitrate,
                        resolution: resolution,
                        fps: fps,
                        isFolderMode: true,
                        hwEncoder: hwEncoderFolder,
                        hasOverlay: overlayActive,
                        overlayPreScaled: overlayActive && IsStaticOverlay(overlayPath),
                        overlayPos: overlayPos,
                        overlaySize: overlaySize
                    ));

                    // Optionally show FFmpeg command for folder (concat) mode
                    if (showCommandFlag)
                    {
                        string display = string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
                        System.Windows.MessageBox.Show(display, "Comando FFmpeg", MessageBoxButton.OK, MessageBoxImage.Information);
                    }

                    // Capture values needed inside Task.Run to avoid cross-thread access
                    var capturedCts = ffmpegCts;
                    var capturedArgs = args.ToList();
                    var capturedFolder = SelectedFolderPath;
                    var capturedExts = new[] { ".mp4", ".mkv", ".mov", ".avi", ".webm" };

                    // Start a background loop to run ffmpeg and optionally restart
                    _ = Task.Run(async () =>
                    {
                        System.Threading.Interlocked.Exchange(ref isStreamingInt, 1);
                        await Dispatcher.InvokeAsync(() => { streamStartTime = DateTime.Now; timer.Start(); UpdateStreamStatus(true); });
                        try
                        {
                            while (true)
                            {
                                // Re-scan folder — if empty and Modo Espera is on, poll every 5s until a video appears
                                try
                                {
                                    List<string> freshVids;
                                    while (true)
                                    {
                                        freshVids = Directory.EnumerateFiles(capturedFolder)
                                            .Where(f => capturedExts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                                            .ToList();
                                        if (freshVids.Count > 0 || !capturedWaitMode || capturedCts.Token.IsCancellationRequested)
                                            break;
                                        await Dispatcher.InvokeAsync(UpdateStreamStatusWaiting);
                                        await Task.Delay(5000, capturedCts.Token).ConfigureAwait(false);
                                    }
                                    if (capturedRandom)
                                        freshVids = freshVids.OrderBy(_ => Random.Shared.Next()).ToList();
                                    if (freshVids.Count > 0)
                                    {
                                        await Dispatcher.InvokeAsync(() => UpdateStreamStatus(true));
                                        bool loopPlaylist = await Dispatcher.InvokeAsync(() => FolderLoop || (FolderLoopCheck?.IsChecked == true));
                                        // When looping, repeat the playlist 999x inside the concat file so FFmpeg
                                        // stays connected to RTMP for hours without disconnecting.
                                        // This eliminates the viewer-side freeze that occurs on every FFmpeg restart.
                                        int repeatCount = loopPlaylist ? 999 : 1;
                                        using var sw = new System.IO.StreamWriter(playlist, false, new System.Text.UTF8Encoding(false));
                                        for (int r = 0; r < repeatCount; r++)
                                            foreach (var v in freshVids)
                                                sw.WriteLine($"file '{v.Replace("'", "'\\''")}'");
                                    }
                                }
                                catch (OperationCanceledException) { throw; }
                                catch { /* keep previous playlist if scan fails */ }

                                await ExecuteFFmpegAsync(capturedArgs, capturedCts.Token).ConfigureAwait(false);

                                if (capturedCts.Token.IsCancellationRequested) break;

                                bool shouldLoop = await Dispatcher.InvokeAsync(() => FolderLoop || (FolderLoopCheck?.IsChecked == true));
                                if (!shouldLoop && !capturedWaitMode) break;
                            }
                        }
                        catch (OperationCanceledException) { }
                        finally
                        {
                            await Dispatcher.InvokeAsync(() => { System.Threading.Interlocked.Exchange(ref isStreamingInt, 0); timer.Stop(); UpdateStreamStatus(false); });
                        }
                    });

                    AddToHistory($"Stream iniciado desde carpeta: {SelectedFolderPath}");
                }
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    System.Windows.MessageBox.Show($"Error: {ex.Message}", "Error",
                                  MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }

        private async void StopStream_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // cancel token to ask ffmpeg to stop (if we used it)
                ffmpegCts?.Cancel();

                // atomically take ownership and null the field
                var proc = Interlocked.Exchange(ref ffmpegProcess, null);
                if (proc != null && !proc.HasExited)
                {
                    try
                    {
                        // send a polite close (SIGINT) by writing q to stdin if available
                        if (!proc.HasExited)
                        {
                            try { proc.StandardInput.WriteLine("q"); } catch { }
                        }

                        // wait briefly for graceful exit
                        if (!proc.WaitForExit(2000))
                        {
                            // kill entire process tree (requires .NET 7+ on Windows)
                            try { proc.Kill(entireProcessTree: true); } catch { proc.Kill(); }
                        }
                    }
                    catch
                    {
                        try { proc.Kill(entireProcessTree: true); } catch { try { proc.Kill(); } catch { } }
                    }
                }
                proc?.Dispose();

                     System.Threading.Interlocked.Exchange(ref isStreamingInt, 0);
                timer.Stop();
                UpdateStreamStatus(false);

                AddToHistory("Stream detenido");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error deteniendo stream: {ex.Message}", "Error",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ExecuteFFmpegAsync(IEnumerable<string> args, CancellationToken ct)
        {
            var proc = new Process();
            ProcessJob? job = null;

            var ffmpegPath = GetFfmpegPath();
            proc.StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true
            };

            foreach (var a in args ?? Enumerable.Empty<string>())
                proc.StartInfo.ArgumentList.Add(a);

            // Reset per-process CPU tracking
            _lastCpuCheck = DateTime.UtcNow;
            _lastCpuTime = TimeSpan.Zero;

            ffmpegProcess = proc;

            // Circular buffers instead of unbounded StringBuilders
            var outputBuffer = new Queue<string>(MaxStderrBufferLines + 10);
            var errorBuffer = new Queue<string>(MaxStderrBufferLines + 10);

            var outputTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var errorTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            proc.OutputDataReceived += (s, e) =>
            {
                if (e.Data == null) { outputTcs.TrySetResult(true); return; }
                AddToCircularBuffer(outputBuffer, e.Data, MaxStderrBufferLines);
            };

            proc.ErrorDataReceived += (s, e) =>
            {
                if (e.Data == null) { errorTcs.TrySetResult(true); return; }
                AddToCircularBuffer(errorBuffer, e.Data, MaxStderrBufferLines);
                ParseFFmpegProgress(e.Data);

                // Only dispatch non-progress lines to UI history to reduce overhead
                if (!IsFFmpegProgressLine(e.Data))
                    Dispatcher.BeginInvoke(() => AddToHistory(e.Data));
            };

            try
            {
                proc.Start();
                try
                {
                    job = new ProcessJob();
                    job.AddProcess(proc);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to assign process to job: {ex}");
                    job?.Dispose();
                    job = null;
                }
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                // Use WaitForExitAsync (native .NET 8) instead of polling loop
                using (ct.Register(() =>
                {
                    try { if (!proc.HasExited) proc.StandardInput.WriteLine("q"); } catch { }
                }))
                {
                    try
                    {
                        await proc.WaitForExitAsync(ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // Graceful shutdown: give FFmpeg 300ms after 'q' was sent, then kill
                        try { if (!proc.WaitForExit(300)) proc.Kill(entireProcessTree: true); }
                        catch { try { proc.Kill(); } catch { } }
                    }
                }

                // Ensure async readers finish
                await Task.WhenAll(outputTcs.Task, errorTcs.Task).ConfigureAwait(false);

                int exitCode = -1;
                try { exitCode = proc.ExitCode; } catch { }
                _lastFfmpegExitCode = exitCode;

                await Dispatcher.InvokeAsync(() =>
                {
                    AddToHistory($"FFmpeg exited (code {exitCode})");

                    // Only show error popup if exit code is non-zero and not cancelled
                    if (exitCode != 0 && !ct.IsCancellationRequested)
                    {
                        var lastLines = errorBuffer.TakeLast(30).ToList();
                        if (lastLines.Any(l => !IsFFmpegProgressLine(l)))
                        {
                            var errorText = string.Join("\n", lastLines.Where(l => !IsFFmpegProgressLine(l)));
                            // Suppress popup for intentional exits (user sent q / Stop button)
                            bool isUserQuit = errorText.Contains("[q] command received") ||
                                             errorText.Contains("Exiting normally");
                            // Suppress popup for transient network errors — reconnect loop handles them
                            bool isNetworkError = errorText.Contains("-10053") || errorText.Contains("-10054") ||
                                                  errorText.Contains("-10061") || errorText.Contains("Connection reset") ||
                                                  errorText.Contains("Broken pipe") || errorText.Contains("Connection refused") ||
                                                  errorText.Contains("Network unreachable") ||
                                                  errorText.Contains("connection was aborted") ||
                                                  errorText.Contains("AcquireNextFrame") ||   // ddagrab: fullscreen exclusive game took over display
                                                  errorText.Contains("887a0026") ||           // DXGI_ERROR_ACCESS_LOST
                                                  errorText.Contains("connection was forcibly closed") ||
                                                  errorText.Contains("An existing connection was forcibly") ||
                                                  errorText.Contains("established connection was aborted") ||
                                                  errorText.Contains("WSAECONN") ||
                                                  errorText.Contains("Error writing interleaved packet") ||
                                                  errorText.Contains("Failed to write packet") ||
                                                  errorText.Contains("Input/output error");
                            if (!string.IsNullOrWhiteSpace(errorText) && !isNetworkError && !isUserQuit)
                                System.Windows.MessageBox.Show(errorText, $"FFmpeg Error (code {exitCode})",
                                    MessageBoxButton.OK, MessageBoxImage.Warning);
                            if (isNetworkError)
                                AddToHistory($"Network error (code {exitCode}) — reconnecting...");
                        }
                    }
                });
            }
            catch (OperationCanceledException) { /* cancellation */ }
            catch (Exception ex)
            {
                Logger.LogError($"FFmpeg error: {ex}");
                Dispatcher.BeginInvoke(() =>
                    System.Windows.MessageBox.Show($"Error en FFmpeg: {ex.Message}", "Error",
                                    MessageBoxButton.OK, MessageBoxImage.Error));
            }
            finally
            {
                Interlocked.CompareExchange(ref ffmpegProcess, null, proc);
                try { proc.Dispose(); } catch { }
                try { job?.Dispose(); } catch { }
            }
        }

        /// <summary>
        /// Detects available HW encoder by testing each one with a short null encode.
        /// Returns "h264_nvenc", "h264_qsv", "h264_amf", or null if none available.
        /// </summary>
        private async Task<string?> DetectHwEncoderAsync()
        {
            var encoders = new[] { "h264_nvenc", "h264_qsv", "h264_amf" };
            var ffmpegPath = GetFfmpegPath();
            foreach (var enc in encoders)
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = ffmpegPath,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                    psi.ArgumentList.Add("-f");
                    psi.ArgumentList.Add("lavfi");
                    psi.ArgumentList.Add("-i");
                    psi.ArgumentList.Add("nullsrc=s=256x256:d=1");
                    psi.ArgumentList.Add("-c:v");
                    psi.ArgumentList.Add(enc);
                    psi.ArgumentList.Add("-f");
                    psi.ArgumentList.Add("null");
                    psi.ArgumentList.Add("-");
                    using var p = Process.Start(psi);
                    if (p == null) continue;
                    // Drain stdout/stderr to prevent buffer deadlock
                    _ = p.StandardOutput.ReadToEndAsync();
                    _ = p.StandardError.ReadToEndAsync();
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await p.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                    if (p.ExitCode == 0) return enc;
                }
                catch { }
            }
            return null;
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // If already allowed to close, perform existing cleanup and allow
            if (_allowClose)
            {
                CleanUpFFmpegProcesses();
                try { _trayIcon?.Dispose(); _trayIcon = null; } catch { }
                return;
            }

            // Determine action based on remembered preference or ask the user
            var action = _rememberedAction;
            bool remember = false;
            if (action == CloseAction.Ask)
            {
                var res = ShowCloseDialog();
                action = res.action;
                remember = res.remember;
                if (remember && action != CloseAction.Ask)
                    SaveRememberedClosePreference(action);
            }

            switch (action)
            {
                case CloseAction.MinimizeToTray:
                    e.Cancel = true;
                    MinimizeToTray();
                    break;
                case CloseAction.Exit:
                    // allow close
                    e.Cancel = false;
                    _allowClose = true;
                    CleanUpFFmpegProcesses();
                    try
                    {
                        if (_trayIcon != null)
                        {
                            try { if (_trayDoubleClickHandler != null) _trayIcon.DoubleClick -= _trayDoubleClickHandler; } catch { }
                            try { if (_trayOpenItem != null && _trayOpenClickHandler != null) _trayOpenItem.Click -= _trayOpenClickHandler; } catch { }
                            try { if (_trayExitItem != null && _trayExitClickHandler != null) _trayExitItem.Click -= _trayExitClickHandler; } catch { }
                            try { if (_trayMenu != null) { _trayIcon.ContextMenuStrip = null; _trayMenu.Dispose(); _trayMenu = null; } } catch { }
                            try { _trayIcon.Visible = false; _trayIcon.Icon = null; _trayIcon.Dispose(); } catch { }
                            _trayIcon = null;
                        }
                    }
                    catch { }
                    break;
                default:
                    e.Cancel = true;
                    break;
            }
        }

        private void CleanUpFFmpegProcesses()
        {
            try
            {
                // Cancel any running ffmpeg and wait briefly
                ffmpegCts?.Cancel();
                var proc = Interlocked.Exchange(ref ffmpegProcess, null);
                if (proc != null && !proc.HasExited)
                {
                    try
                    {
                        if (!proc.WaitForExit(1500))
                        {
                            try { proc.Kill(entireProcessTree: true); } catch { try { proc.Kill(); } catch { } }
                        }
                    }
                    catch { try { proc.Kill(entireProcessTree: true); } catch { try { proc.Kill(); } catch { } } }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error cleaning ffmpeg processes: {ex}");
            }

            try { _cpuCounter?.Dispose(); _cpuCounter = null; } catch { }

            try
            {
                var playlist = Path.Combine(Path.GetTempPath(), "StreamerPro", "playlist.txt");
                if (File.Exists(playlist)) File.Delete(playlist);
            }
            catch { }
        }

        // ── Language methods ───────────────────────────────────────────────────

        private string _currentLang = "es";

        private void LoadLanguagePreference()
        {
            try
            {
                if (File.Exists(prefsPath))
                {
                    var json = File.ReadAllText(prefsPath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("Language", out var lp))
                    {
                        var l = lp.GetString();
                        if (l == "es" || l == "en") { ApplyLanguage(l); return; }
                    }
                }
            }
            catch { }
            // default: Spanish (already loaded via App.xaml)
            _currentLang = "es";
        }

        private void ApplyLanguage(string lang)
        {
            _currentLang = lang;
            var dicts = System.Windows.Application.Current.Resources.MergedDictionaries;
            var existing = dicts.FirstOrDefault(d =>
                d.Source?.OriginalString?.StartsWith("Strings.") == true);
            if (existing != null) dicts.Remove(existing);
            dicts.Insert(0, new System.Windows.ResourceDictionary
            {
                Source = new Uri($"Strings.{lang}.xaml", UriKind.Relative)
            });
        }

        private void LangToggleBtn_Click(object sender, RoutedEventArgs e)
        {
            var next = _currentLang == "es" ? "en" : "es";
            ApplyLanguage(next);
            try { WritePrefsField("Language", next); } catch { }
        }

        // ── Theme methods ──────────────────────────────────────────────────────

        private void LoadThemeEarly()
        {
            try
            {
                if (File.Exists(prefsPath))
                {
                    var json = File.ReadAllText(prefsPath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("Theme", out var tp))
                    {
                        var t = tp.GetString();
                        if (t == "Arc" || t == "Pro") { ApplyTheme(t); return; }
                    }
                }
            }
            catch { }
            ApplyTheme("Pro");
        }

        private void ApplyCardBorderThickness(DependencyObject parent, System.Windows.Thickness t)
        {
            var cardStyle = TryFindResource("CardStyle") as Style;
            if (cardStyle == null) return;
            ApplyCardBorderThicknessRecursive(parent, cardStyle, t);
        }

        private static void ApplyCardBorderThicknessRecursive(DependencyObject parent, Style cardStyle, System.Windows.Thickness t)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(parent))
            {
                if (child is not DependencyObject dep) continue;
                if (dep is System.Windows.Controls.Border b && b.Style == cardStyle)
                    b.BorderThickness = t;
                ApplyCardBorderThicknessRecursive(dep, cardStyle, t);
            }
        }

        private static LinearGradientBrush MakeArcStripeGradient() => new LinearGradientBrush(
            new GradientStopCollection
            {
                new GradientStop(C(0xD0,0x00,0x00), 0.00),
                new GradientStop(C(0xD0,0x00,0x00), 0.25),
                new GradientStop(C(0xFF,0xEA,0x00), 0.25),
                new GradientStop(C(0xFF,0xEA,0x00), 0.50),
                new GradientStop(C(0x05,0xFF,0x74), 0.50),
                new GradientStop(C(0x05,0xFF,0x74), 0.75),
                new GradientStop(C(0x5F,0xFF,0xFF), 0.75),
                new GradientStop(C(0x5F,0xFF,0xFF), 1.00),
            },
            new System.Windows.Point(0, 0),
            new System.Windows.Point(1, 0)
        );

        private void ApplyTheme(string theme)
        {
            _currentTheme = theme;
            bool isArc = theme == "Arc";
            var res = System.Windows.Application.Current.Resources;

            // Solid color palette tokens
            foreach (var (key, pro, arc) in _themeColors)
                res[key] = new SolidColorBrush(isArc ? arc : pro);

            // BorderBrushToken: 4-stripe Arc gradient or solid Pro color
            res["BorderBrushToken"] = isArc
                ? (System.Windows.Media.Brush)MakeArcStripeGradient()
                : new SolidColorBrush(C(0x2A,0x34,0x41));

            // Card borders: top-only in Arc (less is more), all sides in Pro
            var cardThickness = isArc
                ? new System.Windows.Thickness(0, 2, 0, 0)
                : new System.Windows.Thickness(1);
            ApplyCardBorderThickness(this, cardThickness);

            // StartButtonGradient
            if (res["StartButtonGradient"] is LinearGradientBrush sg && !sg.IsFrozen)
            {
                sg.GradientStops[0].Color = isArc ? C(0x00,0xFF,0xFF) : C(0xFF,0xC1,0x07);
                sg.GradientStops[1].Color = isArc ? C(0x00,0xCC,0xDD) : C(0xFF,0xB0,0x00);
            }

            // HeaderGradient — dark purple in Arc
            if (res["HeaderGradient"] is LinearGradientBrush hg && !hg.IsFrozen)
            {
                hg.GradientStops[0].Color = isArc ? C(0x13,0x08,0x10) : C(0x0F,0x13,0x17);
                hg.GradientStops[1].Color = isArc ? C(0x1A,0x0B,0x17) : C(0x14,0x17,0x1B);
            }

            // Window background
            Background = new SolidColorBrush(isArc ? C(0x13,0x08,0x10) : C(0x0F,0x17,0x20));

            // Theme toggle button
            try
            {
                if (isArc)
                {
                    ArcStripes.Visibility      = Visibility.Visible;
                    ThemeToggleBtn.Background  = new SolidColorBrush(C(0x1E,0x0D,0x1D));
                    ThemeToggleBtn.BorderBrush = MakeArcStripeGradient();
                    ThemeToggleBtn.Effect      = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color       = C(0x5F,0xFF,0xFF),
                        BlurRadius  = 14,
                        Opacity     = 0.55,
                        ShadowDepth = 0
                    };
                    ThemeToggleText.Text       = "PRO";
                    ThemeToggleText.Foreground = new SolidColorBrush(C(0xFF,0xFF,0xFF));
                }
                else
                {
                    ArcStripes.Visibility      = Visibility.Collapsed;
                    ThemeToggleBtn.Background  = new SolidColorBrush(C(0x14,0x1C,0x26));
                    ThemeToggleBtn.BorderBrush = new SolidColorBrush(C(0x2A,0x34,0x41));
                    ThemeToggleBtn.Effect      = null;
                    ThemeToggleText.Text       = "◈ ARC";
                    ThemeToggleText.Foreground = new SolidColorBrush(C(0x00,0xBF,0xBF));
                }
            }
            catch { /* controls may not be ready during early load */ }
        }

        private void ThemeToggleBtn_Click(object sender, RoutedEventArgs e)
        {
            var next = _currentTheme == "Pro" ? "Arc" : "Pro";
            ApplyTheme(next);
            SaveThemePreference(next);
        }

        private void SaveThemePreference(string theme)
        {
            try { WritePrefsField("Theme", theme); } catch { }
        }

        private Dictionary<string, string> ReadPrefsDict()
        {
            var dict = new Dictionary<string, string>();
            try
            {
                if (File.Exists(prefsPath))
                {
                    var json = File.ReadAllText(prefsPath);
                    using var doc = JsonDocument.Parse(json);
                    foreach (var prop in doc.RootElement.EnumerateObject())
                        if (prop.Value.ValueKind == JsonValueKind.String)
                            dict[prop.Name] = prop.Value.GetString() ?? "";
                }
            }
            catch { }
            return dict;
        }

        private void WritePrefsField(string key, string value)
        {
            Directory.CreateDirectory(appDataPath);
            var dict = ReadPrefsDict();
            dict[key] = value;
            File.WriteAllText(prefsPath, JsonSerializer.Serialize(dict));
        }

        private void LoadRememberedClosePreference()
        {
            try
            {
                if (File.Exists(prefsPath))
                {
                    var json = File.ReadAllText(prefsPath);
                    var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("CloseAction", out var ca))
                    {
                        var v = ca.GetString();
                        if (!string.IsNullOrEmpty(v) && Enum.TryParse<CloseAction>(v, out var parsed))
                            _rememberedAction = parsed;
                    }
                }
            }
            catch { }
        }

        private void SaveRememberedClosePreference(CloseAction action)
        {
            try { WritePrefsField("CloseAction", action.ToString()); } catch { }
        }

        private (CloseAction action, bool remember) ShowCloseDialog()
        {
            // Simple in-code dialog so we don't add XAML files. Owner is main window.
            var dlg = new Window()
            {
                Title = "Cerrar Streamer Pro",
                Width = 520,
                Height = 220,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow,
                Owner = this
            };

            var grid = new System.Windows.Controls.Grid();
            grid.Margin = new System.Windows.Thickness(12);
            dlg.Content = grid;
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });

            var header = new System.Windows.Controls.TextBlock
            {
                Text = "¿Qué deseas hacer?",
                FontWeight = FontWeights.Bold,
                Margin = new System.Windows.Thickness(0, 0, 0, 8)
            };
            grid.Children.Add(header);
            System.Windows.Controls.Grid.SetRow(header, 0);

            var stack = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Vertical };
            var rbMin = new System.Windows.Controls.RadioButton { Content = "Minimize to tray (recommended)", IsChecked = true, Margin = new System.Windows.Thickness(0, 4, 0, 4) };
            var rbExit = new System.Windows.Controls.RadioButton { Content = "Exit (close application)", Margin = new System.Windows.Thickness(0, 4, 0, 4) };
            var rbCancel = new System.Windows.Controls.RadioButton { Content = "Cancel", Margin = new System.Windows.Thickness(0, 4, 0, 4) };
            stack.Children.Add(rbMin);
            stack.Children.Add(rbExit);
            stack.Children.Add(rbCancel);

            var remember = new System.Windows.Controls.CheckBox { Content = "Remember my choice", Margin = new System.Windows.Thickness(0, 8, 0, 8) };
            stack.Children.Add(remember);

            grid.Children.Add(stack);
            System.Windows.Controls.Grid.SetRow(stack, 1);

            var buttons = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            var btnOk = new System.Windows.Controls.Button { Content = "OK", Width = 96, Margin = new System.Windows.Thickness(4) };
            var btnCancel = new System.Windows.Controls.Button { Content = "Cancel", Width = 96, Margin = new System.Windows.Thickness(4) };
            buttons.Children.Add(btnOk);
            buttons.Children.Add(btnCancel);
            grid.Children.Add(buttons);
            System.Windows.Controls.Grid.SetRow(buttons, 2);

            CloseAction result = CloseAction.Ask;

            btnOk.Click += (s, e) =>
            {
                if (rbMin.IsChecked == true) result = CloseAction.MinimizeToTray;
                else if (rbExit.IsChecked == true) result = CloseAction.Exit;
                else result = CloseAction.Cancel;
                dlg.DialogResult = true;
                dlg.Close();
            };

            btnCancel.Click += (s, e) =>
            {
                result = CloseAction.Cancel;
                dlg.DialogResult = false;
                dlg.Close();
            };

            dlg.ShowDialog();
            return (result, remember.IsChecked == true);
        }

        private void MinimizeToTray()
        {
            try
            {
                bool ok = EnsureTrayIcon();
                if (!ok)
                {
                    try
                    {
                        System.Windows.MessageBox.Show(Str.G("str_msg_tray_error"), Str.G("str_msg_tray_error_title"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    catch { }
                    return; // do not hide the window
                }

                // Ensure visible and hide window
                _trayIcon!.Visible = true;
                ShowInTaskbar = false;
                Hide();

                if (!_hasShownTrayTip)
                {
                    try
                    {
                        _trayIcon.BalloonTipTitle = "Streamer Pro";
                        _trayIcon.BalloonTipText = "Streamer Pro is running in the tray.";
                        _trayIcon.ShowBalloonTip(3000);
                        _hasShownTrayTip = true;
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MinimizeToTray failed: {ex}");
            }
        }

        private void RestoreFromTray()
        {
            try
            {
                ShowInTaskbar = true;
                Show();
                if (WindowState == WindowState.Minimized)
                    WindowState = WindowState.Normal;
                Activate();
                Topmost = true; Topmost = false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RestoreFromTray failed: {ex}");
            }
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            try
            {
                if (this.WindowState == WindowState.Minimized)
                {
                    MinimizeToTray();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"StateChanged handler error: {ex}");
            }
        }

        private void CleanupAndExit()
        {
            try
            {
                _allowClose = true;
                CleanUpFFmpegProcesses();
                try
                {
                    if (_trayIcon != null)
                    {
                        // unsubscribe handlers
                        try { if (_trayDoubleClickHandler != null) _trayIcon.DoubleClick -= _trayDoubleClickHandler; } catch { }
                        try { if (_trayOpenItem != null && _trayOpenClickHandler != null) _trayOpenItem.Click -= _trayOpenClickHandler; } catch { }
                        try { if (_trayExitItem != null && _trayExitClickHandler != null) _trayExitItem.Click -= _trayExitClickHandler; } catch { }

                        // dispose context menu explicitly
                        try { if (_trayMenu != null) { _trayIcon.ContextMenuStrip = null; _trayMenu.Dispose(); _trayMenu = null; } } catch { }

                        _trayIcon.Visible = false;
                        _trayIcon.Icon = null;
                        _trayIcon.Dispose();
                        _trayIcon = null;
                    }
                }
                catch { }
                System.Windows.Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CleanupAndExit failed: {ex}");
                try { System.Windows.Application.Current.Shutdown(); } catch { }
            }
        }

        private bool EnsureTrayIcon()
        {
            if (_trayInitialized) return true;
            try
            {
                if (_trayIcon != null)
                {
                    _trayInitialized = true;
                    return true;
                }

                _trayIcon = new Forms.NotifyIcon();

                // Load icon from embedded resource
                try
                {
                    var sri = System.Windows.Application.GetResourceStream(
                        new Uri("pack://application:,,,/streamer.ico"));
                    if (sri != null)
                        _trayIcon.Icon = new System.Drawing.Icon(sri.Stream);
                    else
                        _trayIcon.Icon = System.Drawing.SystemIcons.Application;
                }
                catch
                {
                    _trayIcon.Icon = System.Drawing.SystemIcons.Application;
                }

                _trayIcon.Text = "Streamer Pro";
                _trayIcon.Visible = true;

                var menu = new Forms.ContextMenuStrip();
                var openItem = new Forms.ToolStripMenuItem("Open");
                EventHandler openHandler = (s, e) => Dispatcher.Invoke(() => RestoreFromTray());
                openItem.Click += openHandler;
                var exitItem = new Forms.ToolStripMenuItem("Exit");
                EventHandler exitHandler = (s, e) => Dispatcher.Invoke(() => CleanupAndExit());
                exitItem.Click += exitHandler;
                menu.Items.Add(openItem);
                menu.Items.Add(exitItem);
                _trayIcon.ContextMenuStrip = menu;

                EventHandler doubleClickHandler = (s, e) => Dispatcher.Invoke(() => RestoreFromTray());
                _trayIcon.DoubleClick += doubleClickHandler;

                // keep references for safe disposal
                _trayMenu = menu;
                _trayOpenItem = openItem;
                _trayExitItem = exitItem;
                _trayDoubleClickHandler = doubleClickHandler;
                _trayOpenClickHandler = openHandler;
                _trayExitClickHandler = exitHandler;

                _trayInitialized = true;
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"EnsureTrayIcon failed: {ex}");
                return false;
            }
        }

        // Refactored helper: returns encoding/output arguments only. Does NOT include -re, -i, or hwaccel.
        private IEnumerable<string> BuildEncodingArguments(
            string rtmpUrl,
            string preset,
            string videoBitrate,
            string audioBitrate,
            string resolution,
            string fps,
            bool isFolderMode,
            string? hwEncoder = null,
            bool hasOverlay = false,
            bool overlayPreScaled = false,
            string overlayPos = "W-w-10:H-h-10",
            int overlaySize = 130)
        {
            var args = new List<string>(24);
            bool isHw = !string.IsNullOrEmpty(hwEncoder);

            // video encoding - select encoder based on detected HW encoder
            args.Add("-c:v");
            if (isHw)
            {
                args.Add(hwEncoder!);
                args.Add("-preset");

                if (hwEncoder!.Contains("nvenc"))
                {
                    // NVENC presets: p1 (fastest) to p7 (slowest)
                    var nvPreset = preset switch
                    {
                        "ultrafast" or "superfast" or "veryfast" => "p1",
                        "faster" => "p3",
                        "fast" => "p4",
                        "medium" => "p5",
                        _ => "p4"
                    };
                    args.Add(nvPreset);
                }
                else if (hwEncoder.Contains("qsv"))
                {
                    // QSV presets: veryfast, faster, fast, medium, slow, veryslow
                    args.Add(string.IsNullOrWhiteSpace(preset) ? "fast" : preset);
                }
                else if (hwEncoder.Contains("amf"))
                {
                    // AMF uses -quality instead of -preset
                    args.RemoveAt(args.Count - 1); // remove "-preset" we just added
                    args.Add("-quality");
                    var amfQuality = preset switch
                    {
                        "ultrafast" or "superfast" or "veryfast" => "speed",
                        "faster" or "fast" => "balanced",
                        "medium" => "quality",
                        _ => "balanced"
                    };
                    args.Add(amfQuality);
                }
                else
                {
                    args.Add(string.IsNullOrWhiteSpace(preset) ? "fast" : preset);
                }
            }
            else
            {
                args.Add("libx264");
                args.Add("-preset");
                args.Add(string.IsNullOrWhiteSpace(preset) ? "veryfast" : preset);
                args.Add("-tune");
                args.Add("zerolatency");
            }

            if (!string.IsNullOrWhiteSpace(videoBitrate))
            {
                args.Add("-b:v");
                args.Add(videoBitrate);

                // Constrain bitrate peaks for stable RTMP delivery
                args.Add("-minrate");
                args.Add(videoBitrate);
                args.Add("-maxrate");
                args.Add(videoBitrate);

                // Use a tighter VBV buffer for live streams so the encoder stays closer to target bitrate.
                var numericPart = new string(videoBitrate.Where(c => char.IsDigit(c)).ToArray());
                var suffix = new string(videoBitrate.Where(c => !char.IsDigit(c)).ToArray());
                if (int.TryParse(numericPart, out var bitrateValue))
                {
                    args.Add("-bufsize");
                    args.Add($"{bitrateValue}{suffix}");
                }
            }

            // Build video filter: scale (if resolution set) + overlay (if logo set)
            string? scaleFilter = null;

            if (!string.IsNullOrWhiteSpace(resolution))
            {
                var sep = resolution.Contains('x') ? 'x' : (resolution.Contains('X') ? 'X' : '\0');
                int w = 0, h = 0;
                if (sep != '\0')
                {
                    var parts = resolution.Split(sep);
                    if (parts.Length == 2 && int.TryParse(parts[0].Trim(), out w) && int.TryParse(parts[1].Trim(), out h))
                        scaleFilter = $"scale={w}:{h}:force_original_aspect_ratio=decrease,pad={w}:{h}:(ow-iw)/2:(oh-ih)/2,setsar=1";
                    else
                    {
                        args.Add("-s");
                        args.Add(resolution);
                    }
                }
                else
                {
                    args.Add("-s");
                    args.Add(resolution);
                }
            }

            overlaySize = Math.Clamp(overlaySize, 48, 128);
            if (hasOverlay)
            {
                // Static overlays are pre-scaled before FFmpeg starts so the filter only composites frames.
                string overlayFilter = overlayPreScaled
                    ? "[1:v]format=rgba[_logo]"
                    : $"[1:v]format=rgba,scale={overlaySize}:-1:flags=fast_bilinear[_logo]";
                string fc = scaleFilter != null
                    ? $"[0:v]{scaleFilter}[_base];{overlayFilter};[_base][_logo]overlay={overlayPos}:eval=init:repeatlast=1:format=auto[_out]"
                    : $"{overlayFilter};[0:v][_logo]overlay={overlayPos}:eval=init:repeatlast=1:format=auto[_out]";

                args.Add("-filter_complex");
                args.Add(fc);
                args.Add("-map");
                args.Add("[_out]");
                args.Add("-map");
                args.Add("0:a?");
            }
            else if (scaleFilter != null)
            {
                args.Add("-vf");
                args.Add(scaleFilter);
            }

            if (!string.IsNullOrWhiteSpace(fps))
            {
                args.Add("-r");
                args.Add(fps);
            }

            // Keyframe interval = 2s (required by RTMP/HLS servers)
            int gopSize = 60; // default for 30fps
            if (!string.IsNullOrWhiteSpace(fps) && int.TryParse(fps, out var fpsValue) && fpsValue > 0)
                gopSize = fpsValue * 2;
            args.Add("-g");
            args.Add(gopSize.ToString());
            if (!isHw)
            {
                // Keep GOP cadence deterministic so live platforms receive regular keyframes.
                var x264Params = $"keyint={gopSize}:min-keyint={gopSize}:scenecut=0:force-cfr=1";
                if (!string.IsNullOrWhiteSpace(videoBitrate))
                    x264Params += ":nal-hrd=cbr";
                args.Add("-x264-params");
                args.Add(x264Params);
            }
            else if (hwEncoder!.Contains("nvenc"))
            {
                args.Add("-tune");
                args.Add("ll");
                args.Add("-rc");
                args.Add("cbr");
                args.Add("-rc-lookahead");
                args.Add("0");
                args.Add("-forced-idr");
                args.Add("1");
            }
            else if (hwEncoder.Contains("amf"))
            {
                // AMF: enforce CBR mode explicitly (default is VBR)
                args.Add("-rc");
                args.Add("cbr");
            }
            else if (hwEncoder.Contains("qsv"))
            {
                // QSV: disable look-ahead for low-latency live streaming
                args.Add("-look_ahead");
                args.Add("0");
            }

            // Force keyframes at exact 2s intervals (critical for concat/playlist mode)
            if (isFolderMode)
            {
                args.Add("-force_key_frames");
                args.Add("expr:gte(t,n_forced*2)");
            }

            // Pixel format: yuv420p for broad compatibility (HW encoders accept and auto-convert)
            args.Add("-pix_fmt");
            args.Add("yuv420p");

            // audio encoding
            args.Add("-c:a");
            args.Add("aac");
            if (!string.IsNullOrWhiteSpace(audioBitrate))
            {
                args.Add("-b:a");
                args.Add(audioBitrate);
            }

            // In folder (concat) mode, force common audio layout/samplerate for robustness
            if (isFolderMode)
            {
                args.Add("-ac"); args.Add("2");
                args.Add("-ar"); args.Add("48000");
            }

            args.Add("-max_interleave_delta");
            args.Add("0");
            args.Add("-muxdelay");
            args.Add("0");
            args.Add("-muxpreload");
            args.Add("0");
            args.Add("-f");
            args.Add("flv");
            args.Add("-flvflags");
            args.Add("no_duration_filesize");
            args.Add("-rtmp_live");
            args.Add("live");
            args.Add(rtmpUrl);

            return args;
        }

        // ── Highlights helpers ──────────────────────────────────────────────

        /// <summary>Inicia el proceso FFmpeg permanente del streamer. Lee MPEG-TS de stdin, empuja a RTMP.</summary>
        private Process HlStartStreamer(IEnumerable<string> args, CancellationToken ct)
        {
            var proc = new Process();
            proc.StartInfo = new ProcessStartInfo
            {
                FileName = GetFfmpegPath(),
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var a in args)
                proc.StartInfo.ArgumentList.Add(a);

            _lastCpuCheck = DateTime.UtcNow;
            _lastCpuTime = TimeSpan.Zero;

            proc.ErrorDataReceived += (s, e) =>
            {
                if (e.Data == null) return;
                ParseFFmpegProgress(e.Data);
                if (!IsFFmpegProgressLine(e.Data))
                {
                    if (e.Data.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                        e.Data.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                        e.Data.Contains("Invalid", StringComparison.OrdinalIgnoreCase))
                        HlLogWrite("streamer_error", new { text = e.Data });
                    Dispatcher.BeginInvoke(() => AddToHistory(e.Data));
                }
            };

            proc.Start();
            proc.BeginErrorReadLine();

            // Al detener: cerrar stdin → FFmpeg recibe EOF → sale limpiamente
            ct.Register(() => { try { proc.StandardInput.Close(); } catch { } });

            return proc;
        }

        /// <summary>Inicia el proceso FFmpeg del supplier. Lee el clip, escribe MPEG-TS a stdout.</summary>
        private Process HlStartSupplier(IEnumerable<string> args)
        {
            var proc = new Process();
            proc.StartInfo = new ProcessStartInfo
            {
                FileName = GetFfmpegPath(),
                UseShellExecute = false,
                RedirectStandardInput = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var a in args)
                proc.StartInfo.ArgumentList.Add(a);

            // Drenar stderr para evitar bloqueo — solo loguear errores reales
            proc.ErrorDataReceived += (s, e) =>
            {
                if (e.Data == null || IsFFmpegProgressLine(e.Data)) return;
                Dispatcher.BeginInvoke(() => AddToHistory($"[supply] {e.Data}"));
            };

            proc.Start();
            proc.BeginErrorReadLine();

            return proc;
        }

        /// <summary>Copia bytes de source a destination hasta EOF, cancelación o error.</summary>
        private static async Task HlRelayAsync(Stream source, Stream destination, CancellationToken ct)
        {
            var buf = new byte[65536];
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    int n;
                    try { n = await source.ReadAsync(buf, 0, buf.Length, ct).ConfigureAwait(false); }
                    catch { break; }
                    if (n == 0) break; // supplier terminó
                    try { await destination.WriteAsync(buf, 0, n, CancellationToken.None).ConfigureAwait(false); }
                    catch { break; } // streamer cerrado
                }
            }
            catch { }
        }

        // ── Fin Highlights helpers ──────────────────────────────────────────

         private void UpdateStreamStatus(bool streaming)
         {
             // Always read isStreaming using Interlocked for thread safety
             bool isStreaming = System.Threading.Interlocked.CompareExchange(ref isStreamingInt, 0, 0) != 0;
             if (streaming)
            {
                StreamStatus.Fill = (SolidColorBrush)FindResource("Success");
                StreamStatusText.Text = Str.G("str_status_streaming");
                StreamStatusText.Foreground = (SolidColorBrush)FindResource("Success");
                StartButton.IsEnabled = false;
                StopButton.IsEnabled = true;
                try { StartBig.IsEnabled = false; } catch { }
                try { StopBig.IsEnabled = true; } catch { }
                try { VideoHealthIndicator.Visibility = Visibility.Visible; } catch { }
                try { VideoHealthText.Visibility = Visibility.Visible; } catch { }
                ResetVideoHealthState();
                try
                {
                    VideoHealthIndicator.Fill = (SolidColorBrush)FindResource("Warning");
                    VideoHealthText.Text = Str.G("str_status_connecting");
                    VideoHealthText.Foreground = (SolidColorBrush)FindResource("Warning");
                }
                catch { }
                MetricBitrate.Text = "--";
                MetricSpeed.Text = "--";
                MetricFps.Text = "--";
                MetricCpu.Text = "--";
                try { MetricGpu.Text = "--"; } catch { }
                StartGpuPolling();
            }
            else
            {
                StreamStatus.Fill = (SolidColorBrush)FindResource("Danger");
                StreamStatusText.Text = Str.G("str_status_stopped");
                StreamStatusText.Foreground = (SolidColorBrush)FindResource("Danger");
                StartButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                try { StartBig.IsEnabled = true; } catch { }
                try { StopBig.IsEnabled = false; } catch { }
                try { VideoHealthIndicator.Visibility = Visibility.Hidden; } catch { }
                try { VideoHealthText.Visibility = Visibility.Hidden; } catch { }
                ResetVideoHealthState();
                StreamTime.Text = "Tiempo: 00:00:00";
                try { StreamTimeCompact.Text = "00:00:00"; } catch { }
                MetricBitrate.Text = "--";
                MetricSpeed.Text = "--";
                MetricFps.Text = "--";
                MetricCpu.Text = "--";
                try { MetricGpu.Text = "--"; } catch { }
                StopGpuPolling();
            }
        }

        private void UpdateStreamStatusWaiting()
        {
            StreamStatus.Fill = (SolidColorBrush)FindResource("Warning");
            StreamStatusText.Text = Str.G("str_status_waiting");
            StreamStatusText.Foreground = (SolidColorBrush)FindResource("Warning");
            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            try { StartBig.IsEnabled = false; } catch { }
            try { StopBig.IsEnabled = true; } catch { }
            try { VideoHealthIndicator.Visibility = Visibility.Visible; } catch { }
            try { VideoHealthText.Visibility = Visibility.Visible; } catch { }
            ResetVideoHealthState();
            try
            {
                VideoHealthIndicator.Fill = (SolidColorBrush)FindResource("Warning");
                VideoHealthText.Text = Str.G("str_status_waiting");
                VideoHealthText.Foreground = (SolidColorBrush)FindResource("Warning");
            }
            catch { }
        }

        private void ProfileButton_Click(object sender, RoutedEventArgs e)
        {
            // Make profile buttons mutually exclusive and update related fields
            if (sender is Button button && button.Tag is string profile)
            {
                // Update fields based on profile (maintain existing behavior)
                switch (profile)
                {
                    case "480p":
                        VideoBitrateManual.Text = "1000k";
                        AudioBitrateManual.Text = "160k";
                        ResolutionManual.Text = "854x480";
                        FPSManual.Text = "30";
                        break;
                    case "720p":
                        VideoBitrateManual.Text = "2500k";
                        AudioBitrateManual.Text = "160k";
                        ResolutionManual.Text = "1280x720";
                        FPSManual.Text = "30";
                        break;
                    case "1080p":
                        VideoBitrateManual.Text = "4500k";
                        AudioBitrateManual.Text = "160k";
                        ResolutionManual.Text = "1920x1080";
                        FPSManual.Text = "30";
                        break;
                    case "1080p60":
                        VideoBitrateManual.Text = "5000k";
                        AudioBitrateManual.Text = "160k";
                        ResolutionManual.Text = "1920x1080";
                        FPSManual.Text = "60";
                        break;
                }

                // Maintain x264 preset selection if buttons correlate to presets
                // Map profile tags to preset choices when applicable
                try
                {
                    if (PresetCombo != null)
                    {
                        // heuristic: 720p and others likely want 'veryfast' or similar preset
                        switch (profile)
                        {
                            case "480p":
                                SelectPreset("veryfast");
                                break;
                            case "720p":
                                SelectPreset("veryfast");
                                break;
                            case "1080p":
                                SelectPreset("fast");
                                break;
                            case "1080p60":
                                SelectPreset("faster");
                                break;
                            default:
                                break;
                        }
                    }
                }
                catch { }

                // Update visual state: reset all cards, highlight selected
                var profileButtons = new[] { "Profile480p", "Profile720p", "Profile1080p", "Profile1080p60", "ProfileCustom" };
                foreach (var name in profileButtons)
                {
                    if (FindName(name) is Button b)
                    {
                        try { b.Style = (Style)FindResource("ProfileCardButton"); } catch { }
                    }
                }
                try { button.Style = (Style)FindResource("ProfileCardButtonActive"); } catch { }
            }
        }

        private void SelectPreset(string preset)
        {
            if (PresetCombo == null) return;
            for (int i = 0; i < PresetCombo.Items.Count; i++)
            {
                if (PresetCombo.Items[i] is ComboBoxItem item && item.Content?.ToString() == preset)
                {
                    PresetCombo.SelectedIndex = i;
                    break;
                }
            }
        }



        private void SaveFavorite_Click(object sender, RoutedEventArgs e)
        {
            string name = Interaction.InputBox(
                "Nombre para esta configuración:",
                "Guardar favorito",
                $"Stream {DateTime.Now:yyyyMMdd_HHmm}",
                -1, -1);

            if (!string.IsNullOrEmpty(name))
            {
                var config = new
                {
                    Name = name,
                    RtmpBase = RTMPBase.Text,
                    StreamKey = EncryptString(GetStreamKeyText()),
                    Source = SelectedSource?.Name,
                    VBitrate = VideoBitrateManual.Text,
                    ABitrate = AudioBitrateManual.Text,
                    Preset = (PresetCombo.SelectedItem as ComboBoxItem)?.Content?.ToString(),
                    Resolution = ResolutionManual.Text,
                    FPS = FPSManual.Text,
                    ForceYUV = ForceYUV.IsChecked,
                    DateSaved = DateTime.Now
                };

                string json = JsonSerializer.Serialize(config, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                string favPath = Path.Combine(appDataPath, "Favorites", $"{SanitizeFileName(name)}.json");
                File.WriteAllText(favPath, json);

                _ = LoadFavoritesAsync();
                MessageBox.Show(Str.G("str_msg_config_saved"), Str.G("str_msg_success"),
                              MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;

            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                sb.Append(invalid.Contains(c) ? '_' : c);
            }
            return sb.ToString();
        }

        private async Task LoadFavoritesAsync()
        {
            try
            {
                string favDir = Path.Combine(appDataPath, "Favorites");
                if (!Directory.Exists(favDir))
                {
                    await Task.Run(() => Directory.CreateDirectory(favDir)).ConfigureAwait(false);
                }

                // Enumerate files on background thread to reduce UI blocking
                List<string> files = await Task.Run(() =>
                    Directory.EnumerateFiles(favDir, "*.json").Select(f => Path.GetFileNameWithoutExtension(f) ?? string.Empty).ToList()
                ).ConfigureAwait(false);

                // Update UI in one dispatcher call
                await Dispatcher.InvokeAsync(() =>
                {
                    FavoritesList.Items.Clear();
                    foreach (var f in files)
                        FavoritesList.Items.Add(f);
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading favorites: {ex.Message}");
            }
        }

        private async Task LoadHistoryAsync()
        {
            try
            {
                string historyFile = Path.Combine(appDataPath, "history.json");
                if (File.Exists(historyFile))
                {
                    string json = await File.ReadAllTextAsync(historyFile).ConfigureAwait(false);
                    var history = JsonSerializer.Deserialize<List<string>>(json);
                    if (history != null)
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            HistoryList.Items.Clear();
                            foreach (var item in history)
                                HistoryList.Items.Add(item);
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading history: {ex.Message}");
            }
        }

        private async Task SaveHistoryAsync()
        {
            try
            {
                var history = new List<string>();
                // Access UI collection on dispatcher to build snapshot - only save last 20
                await Dispatcher.InvokeAsync(() =>
                {
                    var count = Math.Min(HistoryList.Items.Count, 20);
                    for (int i = 0; i < count; i++)
                    {
                        history.Add(HistoryList.Items[i]?.ToString() ?? string.Empty);
                    }
                });

                string json = JsonSerializer.Serialize(history);
                string historyFile = Path.Combine(appDataPath, "history.json");
                await File.WriteAllTextAsync(historyFile, json).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving history: {ex.Message}");
            }
        }

        private async void FavoritesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FavoritesList.SelectedItem != null)
            {
                try
                {
                    string name = FavoritesList.SelectedItem.ToString() ?? "";
                    string favPath = Path.Combine(appDataPath, "Favorites", $"{SanitizeFileName(name)}.json");

                    if (File.Exists(favPath))
                    {
                        string json = await File.ReadAllTextAsync(favPath).ConfigureAwait(false);
                        var config = JsonSerializer.Deserialize<Dictionary<string, object>>(json);

                        if (config != null)
                        {
                            await Dispatcher.InvokeAsync(() =>
                            {
                                if (config.ContainsKey("RtmpBase"))
                                {
                                    RTMPBase.Text = config["RtmpBase"]?.ToString() ?? "";
                                    SyncPlatformComboFromUrl();
                                }
                                if (config.ContainsKey("StreamKey"))
                                {
                                    var raw = config["StreamKey"]?.ToString() ?? "";
                                    var decrypted = DecryptString(raw);
                                    SetStreamKeyText(string.IsNullOrEmpty(decrypted) ? raw : decrypted);
                                }
                                if (config.ContainsKey("VBitrate"))
                                    VideoBitrateManual.Text = config["VBitrate"]?.ToString() ?? "2500k";
                                if (config.ContainsKey("ABitrate"))
                                    AudioBitrateManual.Text = config["ABitrate"]?.ToString() ?? "160k";
                                if (config.ContainsKey("Resolution"))
                                    ResolutionManual.Text = config["Resolution"]?.ToString() ?? "1280x720";
                                if (config.ContainsKey("FPS"))
                                    FPSManual.Text = config["FPS"]?.ToString() ?? "30";

                                if (config.ContainsKey("Preset") && PresetCombo.Items.Count > 0)
                                {
                                    string preset = config["Preset"]?.ToString() ?? "veryfast";
                                    for (int i = 0; i < PresetCombo.Items.Count; i++)
                                    {
                                        var item = PresetCombo.Items[i] as ComboBoxItem;
                                        if (item?.Content?.ToString() == preset)
                                        {
                                            PresetCombo.SelectedIndex = i;
                                            break;
                                        }
                                    }
                                }
                            });
                        }

                        await Dispatcher.InvokeAsync(() =>
                            MessageBox.Show(string.Format(Str.G("str_msg_config_loaded_fmt"), name), Str.G("str_msg_success"),
                                          MessageBoxButton.OK, MessageBoxImage.Information));
                    }
                }
                catch (Exception ex)
                {
                    await Dispatcher.InvokeAsync(() =>
                        MessageBox.Show($"{Str.G("str_msg_error")}: {ex.Message}", Str.G("str_msg_error"),
                                      MessageBoxButton.OK, MessageBoxImage.Error));
                }
            }
        }

        private void AddToHistory(string entry)
        {
            // Only marshal to the UI thread if required.
            if (!Dispatcher.CheckAccess())
            {
                // Use BeginInvoke to avoid blocking background threads
                Dispatcher.BeginInvoke(new Action(() => AddToHistory(entry)));
                return;
            }

            HistoryList.Items.Insert(0, $"{DateTime.Now:HH:mm:ss} - {entry}");
            while (HistoryList.Items.Count > 100)
            {
                HistoryList.Items.RemoveAt(HistoryList.Items.Count - 1);
            }

            // Log to file for diagnostics
            Logger.LogInfo(entry);

            // Debounce history save - restart timer on each new entry
            _historySaveTimer?.Stop();
            _historySaveTimer?.Start();
        }

        private void OpenSourcesButton_Click(object sender, RoutedEventArgs e)
        {
            var folder = SourcesRepository.GetAppFolder();
            try { Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true }); } catch (Exception ex) { MessageBox.Show(string.Format(Str.G("str_msg_open_folder_err_fmt"), ex.Message)); }
        }

        private void ModeOnline_Checked(object sender, RoutedEventArgs e)
        {
            IsOnlineMode = true;
        }

        private void ModeFolder_Checked(object sender, RoutedEventArgs e)
        {
            IsOnlineMode = false;
        }

        private void FolderHighlightsCheck_Changed(object sender, RoutedEventArgs e)
        {
            bool on = FolderHighlightsCheck?.IsChecked == true;
            if (FolderLoopCheck != null)   { if (on) FolderLoopCheck.IsChecked   = false; FolderLoopCheck.IsEnabled   = !on; }
            if (FolderRandomCheck != null) { if (on) FolderRandomCheck.IsChecked = false; FolderRandomCheck.IsEnabled = !on; }
            if (FolderWaitCheck != null)   { if (on) FolderWaitCheck.IsChecked   = false; FolderWaitCheck.IsEnabled   = !on; }
            if (HlExtraFoldersPanel != null)
                HlExtraFoldersPanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (HlPrimaryFolderHint != null)
                HlPrimaryFolderHint.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (!on)
            {
                _hlExtraFolders.Clear();
                HlFoldersList?.Children.Clear();
            }
        }

        private void HlAddFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog();
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var path = dlg.SelectedPath;
                if (!_hlExtraFolders.Contains(path, StringComparer.OrdinalIgnoreCase)
                    && !string.Equals(path, SelectedFolderPath, StringComparison.OrdinalIgnoreCase))
                {
                    _hlExtraFolders.Add(path);
                    AddHlFolderRow(path);
                }
            }
        }

        private void AddHlFolderRow(string path)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var txt = new TextBlock
            {
                Text = path,
                Foreground = (System.Windows.Media.Brush)FindResource("TextPrimary"),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = path
            };

            var btn = new Button
            {
                Content = "✕",
                Tag = path,
                Style = (Style)FindResource("DangerButton"),
                Padding = new Thickness(6, 2, 6, 2),
                FontSize = 11,
                Width = 28,
                Height = 28,
                Margin = new Thickness(6, 0, 0, 0),
                ToolTip = Str.G("str_tooltip_remove_folder")
            };
            btn.Click += HlRemoveFolderButton_Click;

            Grid.SetColumn(txt, 0);
            Grid.SetColumn(btn, 1);
            row.Children.Add(txt);
            row.Children.Add(btn);
            HlFoldersList.Children.Add(row);
        }

        private void HlRemoveFolderButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string path)
            {
                _hlExtraFolders.Remove(path);
                if (btn.Parent is Grid row)
                    HlFoldersList.Children.Remove(row);
            }
        }

        private void ModeFile_Checked(object sender, RoutedEventArgs e)
        {
            IsOnlineMode = false;
        }

        private void ModeCapture_Checked(object sender, RoutedEventArgs e)
        {
            IsOnlineMode = false;
        }

        private void PopulateCaptureMonitors()
        {
            try
            {
                CaptureMonitorCombo.Items.Clear();
                var screens = System.Windows.Forms.Screen.AllScreens;
                for (int i = 0; i < screens.Length; i++)
                {
                    var s = screens[i];
                    var label = $"Monitor {i + 1}  {s.Bounds.Width}×{s.Bounds.Height}{(s.Primary ? "  — Principal" : "")}";
                    CaptureMonitorCombo.Items.Add(new CaptureMonitorItem { Index = i, Label = label });
                }
                if (CaptureMonitorCombo.Items.Count > 0)
                    CaptureMonitorCombo.SelectedIndex = 0;
            }
            catch { }

            // Enumerate dshow audio devices for capture
            try
            {
                CaptureAudioDeviceCombo.Items.Clear();
                var ffmpegPath = GetFfmpegPath();
                using var proc = new Process();
                proc.StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                };
                proc.StartInfo.ArgumentList.Add("-list_devices");
                proc.StartInfo.ArgumentList.Add("true");
                proc.StartInfo.ArgumentList.Add("-f");
                proc.StartInfo.ArgumentList.Add("dshow");
                proc.StartInfo.ArgumentList.Add("-i");
                proc.StartInfo.ArgumentList.Add("dummy");
                proc.Start();
                var output = proc.StandardError.ReadToEnd();
                proc.WaitForExit();

                // Parse audio device names from dshow output
                var audioDevices = new List<string>();
                foreach (var line in output.Split('\n'))
                {
                    var m = System.Text.RegularExpressions.Regex.Match(line, @"""(.+?)"" \(audio\)");
                    if (m.Success) audioDevices.Add(m.Groups[1].Value);
                }

                foreach (var d in audioDevices)
                    CaptureAudioDeviceCombo.Items.Add(d);

                // Auto-select best device: prefer "stream" virtual device, then "system", then first
                if (audioDevices.Count > 0)
                {
                    var preferred = audioDevices.FirstOrDefault(d =>
                        d.IndexOf("stream", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        d.IndexOf("virtual", StringComparison.OrdinalIgnoreCase) >= 0)
                        ?? audioDevices.FirstOrDefault(d =>
                            d.IndexOf("system", StringComparison.OrdinalIgnoreCase) >= 0)
                        ?? audioDevices[0];
                    CaptureAudioDeviceCombo.SelectedItem = preferred;
                }
                else
                {
                    CaptureAudioCheck.IsChecked = false;
                    CaptureAudioCheck.IsEnabled = false;
                    CaptureAudioDeviceCombo.IsEnabled = false;
                }
            }
            catch { }
        }

        private List<string> BuildCaptureArgs(int monitorIdx, int fps, bool captureAudio, string captureAudioDevice, string scale, string rtmpUrl, string vBitrate, string aBitrate, string hwEncoder)
        {
            var args = new List<string> { "-hide_banner" };
            bool isHwEnc = hwEncoder.Contains("nvenc") || hwEncoder.Contains("amf") || hwEncoder.Contains("qsv");
            // scale filter: fit dentro del profile resolution manteniendo aspect ratio, pad con negro
            string scaleFilter = "";
            if (!string.IsNullOrWhiteSpace(scale))
            {
                var sp = scale.Split(':');
                if (sp.Length == 2 && int.TryParse(sp[0], out int sw) && int.TryParse(sp[1], out int sh))
                    scaleFilter = $",scale={sw}:{sh}:force_original_aspect_ratio=decrease,pad={sw}:{sh}:(ow-iw)/2:(oh-ih)/2,setsar=1";
                else
                    scaleFilter = $",scale={scale}";
            }

            // Init D3D11 device required by ddagrab
            args.AddRange(new[] { "-init_hw_device", "d3d11va=dda:0" });

            // Audio input via dshow (wasapi not available in this build)
            if (captureAudio)
                args.AddRange(new[] { "-f", "dshow", "-i", $"audio={captureAudioDevice}" });

            // Video: ddagrab → hwdownload → optional scale → encode
            // NVENC without scale: stay in D3D11 GPU memory (most efficient)
            // NVENC with scale + x264: download to CPU (bgra), scale, then encode
            // NVENC can encode from CPU frames; x264 always needs CPU frames
            if (isHwEnc && string.IsNullOrWhiteSpace(scale))
            {
                // Pure GPU path — no download, no scale
                args.AddRange(new[] {
                    "-filter_complex", $"ddagrab=output_idx={monitorIdx}:framerate={fps}:draw_mouse=1[cap]",
                    "-map", "[cap]"
                });
            }
            else
            {
                // CPU path: hwdownload → format → optional scale → encode (NVENC or x264)
                args.AddRange(new[] {
                    "-filter_complex", $"ddagrab=output_idx={monitorIdx}:framerate={fps}:draw_mouse=1[cap];[cap]hwdownload,format=bgra{scaleFilter}[out]",
                    "-map", "[out]"
                });
            }

            if (captureAudio)
                args.AddRange(new[] { "-map", "0:a" });

            // Video encode — encoder-specific params (preset names differ per vendor)
            if (hwEncoder.Contains("nvenc"))
                args.AddRange(new[] { "-c:v", hwEncoder, "-b:v", vBitrate, "-maxrate", vBitrate, "-preset", "p4", "-rc", "cbr" });
            else if (hwEncoder.Contains("amf"))
                args.AddRange(new[] { "-c:v", hwEncoder, "-b:v", vBitrate, "-maxrate", vBitrate, "-quality", "balanced", "-rc", "cbr" });
            else if (hwEncoder.Contains("qsv"))
                args.AddRange(new[] { "-c:v", hwEncoder, "-b:v", vBitrate, "-maxrate", vBitrate, "-preset", "fast" });
            else if (isHwEnc)
                args.AddRange(new[] { "-c:v", hwEncoder, "-b:v", vBitrate, "-maxrate", vBitrate });
            else
                args.AddRange(new[] { "-c:v", "libx264", "-b:v", vBitrate, "-preset", "veryfast", "-tune", "zerolatency" });

            // Audio encode
            if (captureAudio)
                args.AddRange(new[] { "-c:a", "aac", "-b:a", aBitrate });

            // Output
            args.AddRange(new[] { "-f", "flv", rtmpUrl });
            return args;
        }

        private async void CapturePreviewBtn_Click(object sender, RoutedEventArgs e)
        {
            var monitorItem = CaptureMonitorCombo.SelectedItem as CaptureMonitorItem;
            if (monitorItem == null) return;

            CapturePreviewBtn.IsEnabled = false;
            CapturePreviewBtn.Content = Str.G("str_capture_preview_btn");

            try
            {
                var previewPath = Path.Combine(Path.GetTempPath(), "streamerpro_preview.png");
                if (File.Exists(previewPath)) try { File.Delete(previewPath); } catch { }

                var previewArgs = new List<string>
                {
                    "-hide_banner",
                    "-init_hw_device", "d3d11va=dda:0",
                    "-filter_complex", $"ddagrab=output_idx={monitorItem.Index}:framerate=1[cap];[cap]hwdownload,format=bgra[out]",
                    "-map", "[out]",
                    "-vframes", "1",
                    "-f", "image2",
                    previewPath
                };

                var ffmpegPath = GetFfmpegPath();
                using var proc = new Process();
                proc.StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                };
                foreach (var arg in previewArgs)
                    proc.StartInfo.ArgumentList.Add(arg);
                proc.Start();
                await proc.WaitForExitAsync().ConfigureAwait(false);

                if (File.Exists(previewPath))
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        var bmp = new System.Windows.Media.Imaging.BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = new Uri(previewPath);
                        bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                        bmp.EndInit();

                        var img = new System.Windows.Controls.Image { Source = bmp, Stretch = System.Windows.Media.Stretch.Uniform };
                        var win = new Window
                        {
                            Title = $"{Str.G("str_msg_preview_title")} — {monitorItem.Label}",
                            Width = 960, Height = 560,
                            WindowStartupLocation = WindowStartupLocation.CenterScreen,
                            Background = System.Windows.Media.Brushes.Black,
                            Content = img
                        };
                        win.Show();
                    });
                }
                else
                {
                    await Dispatcher.InvokeAsync(() =>
                        System.Windows.MessageBox.Show(Str.G("str_msg_preview_fail"), Str.G("str_msg_preview_title"), MessageBoxButton.OK, MessageBoxImage.Warning));
                }
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                    System.Windows.MessageBox.Show($"Error: {ex.Message}", Str.G("str_msg_preview_title"), MessageBoxButton.OK, MessageBoxImage.Warning));
            }
            finally
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    CapturePreviewBtn.IsEnabled = true;
                    CapturePreviewBtn.Content = Str.G("str_preview_btn");
                });
            }
        }

        private void SelectFileButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Video files|*.mp4;*.mkv;*.mov;*.avi;*.webm|All files|*.*",
                Title = "Seleccionar archivo de video"
            };
            if (dlg.ShowDialog() == true)
            {
                selectedFilePath = dlg.FileName;
                if (SelectedFileText != null) SelectedFileText.Text = selectedFilePath;
            }
        }

        private void OverlayBrowseBtn_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Imágenes|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Todos los archivos|*.*",
                Title = "Seleccionar imagen de overlay"
            };
            if (dlg.ShowDialog() == true)
            {
                _overlayPath = dlg.FileName;
                OverlayPathText.Text = dlg.FileName;
                try
                {
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(dlg.FileName);
                    bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    OverlayPreviewImage.Source = bmp;
                    OverlayPreviewBorder.Visibility = Visibility.Visible;
                }
                catch { OverlayPreviewBorder.Visibility = Visibility.Collapsed; }
            }
        }

        private void OverlayClearBtn_Click(object sender, RoutedEventArgs e)
        {
            _overlayPath = "";
            OverlayPathText.Text = "";
            OverlayPreviewImage.Source = null;
            OverlayPreviewBorder.Visibility = Visibility.Collapsed;
        }

        private void SelectFolderButton_Click(object sender, RoutedEventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SelectFolderButton_Click(sender, e));
                return;
            }
            var dlg = new System.Windows.Forms.FolderBrowserDialog();
            var res = dlg.ShowDialog();
            if (res == System.Windows.Forms.DialogResult.OK)
            {
                var path = dlg.SelectedPath;
                SelectedFolderPath = path;
                // No actualizar SelectedFolderText.Text manualmente, el binding lo hará
            }
        }

        // Helper to wrap WPF window handle for WinForms dialogs
        private class WindowWrapper : System.Windows.Forms.IWin32Window
        {
            private readonly IntPtr _hwnd;
            public WindowWrapper(IntPtr handle) { _hwnd = handle; }
            IntPtr System.Windows.Forms.IWin32Window.Handle => _hwnd;
        }

        private async void ReloadSourcesButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadAndValidateSourcesAsync();
        }

        private async void MainWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            try
            {
                // Run initialization tasks - do NOT use ConfigureAwait(false) here
                // because EnsureWindowVisible and _uiReady must run on UI thread
                await LoadAndValidateSourcesAsync();
                CheckFFmpeg();
                await CheckServerStatusAsync();
                _ = CheckForUpdateAsync();

                // Ensure window is on-screen and visible (must be on UI thread)
                EnsureWindowVisible();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainWindow_Loaded error: {ex}");
            }
            finally
            {
                // Mark UI as ready so Checked handlers may safely access controls
                _uiReady = true;
            }
        }

        private void EnsureWindowVisible()
        {
            try
            {
                // Center on screen
                WindowStartupLocation = WindowStartupLocation.CenterScreen;

                // If minimized, restore
                if (WindowState == WindowState.Minimized)
                    WindowState = WindowState.Normal;

                // Ensure reasonable size
                if (double.IsNaN(Width) || Width <= 50)
                    Width = 1100;
                if (double.IsNaN(Height) || Height <= 50)
                    Height = 700;

                // Ensure visible in taskbar
                ShowInTaskbar = true;

                // Fix off-screen coordinates
                if (double.IsNaN(Left) || Left < -2000 || Left > 2000)
                {
                    Left = (SystemParameters.PrimaryScreenWidth - Width) / 2 + SystemParameters.VirtualScreenLeft;
                }
                if (double.IsNaN(Top) || Top < -2000 || Top > 2000)
                {
                    Top = (SystemParameters.PrimaryScreenHeight - Height) / 2 + SystemParameters.VirtualScreenTop;
                }

                // Bring to front
                Topmost = true;
                Activate();
                Focus();
                // Toggle Topmost off after bringing to front so it does not stay always on top
                Topmost = false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"EnsureWindowVisible error: {ex}");
            }
        }

        private void CorilloLink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AddToHistory($"Failed to open link: {ex.Message}");
            }
            e.Handled = true;
        }

        private async void StreamKey_LostFocus(object sender, RoutedEventArgs e)
        {
            // Sync between PasswordBox and TextBox
            SyncStreamKeyFromActive();
            await SaveConfigAsync().ConfigureAwait(false);
        }

        private void ToggleStreamKey_Click(object sender, RoutedEventArgs e)
        {
            if (StreamKey.Visibility == Visibility.Collapsed)
            {
                // Show plain text
                StreamKey.Text = StreamKeyHidden.Password;
                StreamKey.Visibility = Visibility.Visible;
                StreamKeyHidden.Visibility = Visibility.Collapsed;
                ToggleKeyBtn.Content = "🔒";
            }
            else
            {
                // Hide with password dots
                StreamKeyHidden.Password = StreamKey.Text;
                StreamKeyHidden.Visibility = Visibility.Visible;
                StreamKey.Visibility = Visibility.Collapsed;
                ToggleKeyBtn.Content = "👁";
            }
        }

        /// <summary>
        /// Gets the current stream key value from whichever control is active.
        /// </summary>
        private string GetStreamKeyText()
        {
            if (StreamKey.Visibility == Visibility.Visible)
                return StreamKey.Text;
            return StreamKeyHidden.Password;
        }

        /// <summary>
        /// Sets the stream key value in both controls.
        /// </summary>
        private void SetStreamKeyText(string value)
        {
            StreamKey.Text = value;
            StreamKeyHidden.Password = value;
        }

        /// <summary>
        /// Syncs the stream key from the currently visible control to the other.
        /// </summary>
        private void SyncStreamKeyFromActive()
        {
            if (StreamKey.Visibility == Visibility.Visible)
                StreamKeyHidden.Password = StreamKey.Text;
            else
                StreamKey.Text = StreamKeyHidden.Password;
        }

        private async void RTMPBase_LostFocus(object sender, RoutedEventArgs e)
        {
            // Sync platform combo when user manually edits the RTMP URL
            SyncPlatformComboFromUrl();
            await CheckServerStatusAsync().ConfigureAwait(false);
        }

        private void PlatformCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_uiReady) return;
            if (PlatformCombo.SelectedItem is ComboBoxItem item)
            {
                var url = item.Tag as string ?? "";
                var platform = item.Content?.ToString() ?? "";

                if (!string.IsNullOrEmpty(url))
                {
                    RTMPBase.Text = url;
                }
                else if (platform == "Kick")
                {
                    RTMPBase.Text = "";
                    AddToHistory("Kick: pega tu URL de ingest desde kick.com/dashboard/settings/stream");
                }
                else if (platform == "Personalizado")
                {
                    // Leave current URL as-is for custom
                }

                _ = CheckServerStatusAsync();
            }
        }

        /// <summary>
        /// Syncs the platform combo selection to match the current RTMP URL.
        /// </summary>
        private void SyncPlatformComboFromUrl()
        {
            if (PlatformCombo == null) return;
            var currentUrl = RTMPBase.Text.Trim();
            foreach (ComboBoxItem item in PlatformCombo.Items)
            {
                if (item.Tag is string tag && !string.IsNullOrEmpty(tag) && currentUrl == tag)
                {
                    PlatformCombo.SelectedItem = item;
                    return;
                }
            }
            // No match — select "Personalizado"
            PlatformCombo.SelectedIndex = PlatformCombo.Items.Count - 1;
        }

        private void DeleteFavorite_Click(object sender, RoutedEventArgs e)
        {
            if (FavoritesList.SelectedItems.Count == 0)
            {
                MessageBox.Show("Selecciona un favorito para eliminar", "Aviso",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var selected = FavoritesList.SelectedItems.Cast<object>().Select(i => i?.ToString() ?? "").ToList();
            var result = MessageBox.Show($"¿Eliminar {selected.Count} favorito(s)?", "Confirmar",
                          MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                foreach (var name in selected)
                {
                    try
                    {
                        string favPath = Path.Combine(appDataPath, "Favorites", $"{SanitizeFileName(name)}.json");
                        if (File.Exists(favPath))
                            File.Delete(favPath);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error deleting favorite '{name}': {ex.Message}");
                    }
                }

                _ = LoadFavoritesAsync();
            }
        }

        private void CreditsButton_Click(object sender, RoutedEventArgs e)
        {
            var credits = new CreditsWindow { Owner = this };
            credits.ShowDialog();
        }

    }
}



