using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net.Http;
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

        // Shared HttpClient for source validation to avoid socket exhaustion
        private static readonly HttpClient _sharedHttpClient = new HttpClient();

        // UI timer and state
        private readonly DispatcherTimer timer = new DispatcherTimer();
        private DateTime streamStartTime;
        private bool isStreaming = false;
        private bool _uiReady = false;

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

            // DataContext for bindings
            DataContext = this;

            // Set dynamic title with version info from assembly
            try
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                if (version != null)
                {
                    TitleText.Text = $"Streamer Pro v{version.Major}.{version.Minor}";
                }
                else
                {
                    TitleText.Text = "Streamer Pro v1.5";
                }
            }
            catch { }

            appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "CorilloStreamer"
            );

            prefsPath = Path.Combine(appDataPath, "prefs.json");

            // Offload disk IO (directory creation & loads) to background to avoid UI freeze.
            _ = InitializeAppDataAndLoadAsync();
            InitializeComponents();

            // Wire UI events after InitializeComponent to avoid being called before controls are ready
            try
            {
                if (FindName("ModeOnline") is System.Windows.Controls.RadioButton modeOnline)
                    modeOnline.Checked += ModeOnline_Checked;
                if (FindName("ModeFolder") is System.Windows.Controls.RadioButton modeFolder)
                    modeFolder.Checked += ModeFolder_Checked;
            }
            catch { /* ignore if wiring fails */ }

            // Load sources and check ffmpeg after window is loaded (single handler)
            this.Loaded += MainWindow_Loaded;

            this.Closing += MainWindow_Closing;

            // Hook minimize state changed to support minimize-to-tray
            this.StateChanged += MainWindow_StateChanged;

            // Load remembered choice if exists
            LoadRememberedClosePreference();
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
                }).ConfigureAwait(false);

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
                            try { if (StreamKey != null) StreamKey.Text = key; } catch { }
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
                var toSave = StreamKey?.Text ?? string.Empty;
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
        }

        private async void CheckFFmpeg()
        {
            // Delegate to the async checker which uses bundled ffmpeg
            _ = CheckFFmpegAsync();
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
                        System.Windows.MessageBox.Show("No se encontró ffmpeg.exe en la carpeta de la aplicación. Vuelve a instalar o copia ffmpeg.exe junto al .exe.", "FFmpeg no encontrado", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                FFmpegStatusText.Text = "FFmpeg: Detectado";
            }
            else
            {
                FFmpegIndicator.Fill = (SolidColorBrush)FindResource("Danger");
                FFmpegStatusText.Text = "FFmpeg: No detectado";
            }
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            if (isStreaming)
            {
                var elapsed = DateTime.Now - streamStartTime;
                StreamTime.Text = $"Tiempo: {elapsed:hh\\:mm\\:ss}";
            }
        }

        private async Task LoadAndValidateSourcesAsync()
        {
            try
            {
                var path = SourcesRepository.GetSourcesPath();

                await Dispatcher.InvokeAsync(() => SourceStatusText.Text = "Cargando fuentes...");

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

                await Dispatcher.InvokeAsync(() => SourceStatusText.Text = "Validando fuentes...");

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
                    SourceStatusText.Text = "Listo";
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
                if (string.IsNullOrWhiteSpace(StreamKey.Text))
                {
                    System.Windows.MessageBox.Show("Stream key es requerido", "Error",
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string rtmpBase = RTMPBase.Text.TrimEnd('/');
                string streamKey = StreamKey.Text.TrimStart('/');
                string rtmpUrl = $"{rtmpBase}/{streamKey}";
                string vBitrate = VideoBitrateManual.Text;
                string aBitrate = AudioBitrateManual.Text;

                var presetItem = PresetCombo.SelectedItem as ComboBoxItem;
                string preset = presetItem?.Content?.ToString() ?? "veryfast";

                string resolution = ResolutionManual.Text;
                string fps = FPSManual.Text;

                // Cancel previous if any and create new CTS
                ffmpegCts?.Cancel();
                ffmpegCts = new CancellationTokenSource();

                if (IsOnlineMode)
                {
                    var sourceObj = SelectedSource;
                    if (sourceObj == null)
                    {
                        System.Windows.MessageBox.Show("Selecciona una fuente de video", "Error",
                                        MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    if (!sourceObj.IsAvailable)
                    {
                        System.Windows.MessageBox.Show("La fuente seleccionada no está disponible. Elige otra o recarga las fuentes.", "Fuente no disponible",
                                        MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    string sourceUrl = sourceObj.Url;

                    // Build argument list safely and efficiently for online (single source) mode
                    var args = new List<string>();
                    bool hwAccelFlagOnline = HardwareAccel.IsChecked == true;
                    if (hwAccelFlagOnline)
                    {
                        AddToHistory("HWAccel: enabled (online mode)");
                        args.AddRange(new[] { "-hwaccel", "auto" });
                    }

                    args.AddRange(new[] { "-re", "-i", sourceUrl });
                    args.AddRange(BuildEncodingArguments(
                        sourceUrl: sourceUrl,
                        rtmpUrl: rtmpUrl,
                        preset: preset,
                        videoBitrate: vBitrate,
                        audioBitrate: aBitrate,
                        resolution: resolution,
                        fps: fps,
                        forceYuv: ForceYUV.IsChecked == true,
                        isFolderMode: false
                    ));

                    if (ShowFFmpegCommand.IsChecked == true)
                    {
                        string display = string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
                        System.Windows.MessageBox.Show(display, "Comando FFmpeg",
                                      MessageBoxButton.OK, MessageBoxImage.Information);
                    }

                    // Start ffmpeg asynchronously (do not await to keep UI responsive)
                    _ = ExecuteFFmpegAsync(args, ffmpegCts.Token);

                    isStreaming = true;
                    streamStartTime = DateTime.Now;
                    timer.Start();
                    UpdateStreamStatus(true);

                    AddToHistory($"Stream iniciado: {sourceObj.Name} - {vBitrate}");
                }
                else
                {
                    // Folder mode
                    SelectedFolderPath = SelectedFolderPath ?? SelectedFolderText.Text;
                    if (string.IsNullOrWhiteSpace(SelectedFolderPath) || !Directory.Exists(SelectedFolderPath))
                    {
                        System.Windows.MessageBox.Show("Selecciona una carpeta válida con archivos de video.", "Carpeta inválida",
                                        MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    // gather files
                    var exts = new[] { ".mp4", ".mkv", ".mov", ".avi", ".webm" };
                    var vids = Directory.EnumerateFiles(SelectedFolderPath)
                                .Where(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                                .ToList();

                    if (vids.Count == 0)
                    {
                        System.Windows.MessageBox.Show("La carpeta no contiene archivos de video soportados.", "Sin videos",
                                        MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    if (FolderRandom || FolderRandomCheck.IsChecked == true)
                    {
                        var rnd = new Random();
                        vids = vids.OrderBy(_ => rnd.Next()).ToList();
                    }

                    // Capture UI flags that will be needed after awaits to avoid cross-thread access
                    bool forceYuvFlag = ForceYUV.IsChecked == true;
                    bool hwAccelFlag = HardwareAccel.IsChecked == true;

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

                    if (validVids.Count == 0)
                    {
                        await Dispatcher.InvokeAsync(() => System.Windows.MessageBox.Show("No valid videos found in the selected folder.", "Sin videos válidos", MessageBoxButton.OK, MessageBoxImage.Warning));
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
                    if (hwAccelFlag)
                    {
                        AddToHistory("HWAccel: enabled (folder mode)");
                        args.AddRange(new[] { "-hwaccel", "auto" });
                    }

                    // Stabilize timestamps and concat demuxer behavior for heterogeneous inputs
                    // Keep +genpts; remove use_wallclock_as_timestamps which can speed up playback.
                    args.AddRange(new[] { "-fflags", "+genpts", "-avoid_negative_ts", "make_zero" });

                    args.AddRange(new[] { "-re", "-f", "concat", "-safe", "0", "-i", playlist });
                    // Reset timestamps for concat input to avoid playback speed issues
                    args.AddRange(new[] { "-reset_timestamps", "1" });

                    // rest of encoding options - use previously captured flags to avoid UI access off-thread
                    args.AddRange(BuildEncodingArguments(
                        sourceUrl: "ignored",
                        rtmpUrl: rtmpUrl,
                        preset: preset,
                        videoBitrate: vBitrate,
                        audioBitrate: aBitrate,
                        resolution: resolution,
                        fps: fps,
                        forceYuv: forceYuvFlag,
                        isFolderMode: true
                    ));

                    // Start a background loop to run ffmpeg and optionally restart
                    _ = Task.Run(async () =>
                    {
                        // mark streaming state (field only) and update UI safely
                        isStreaming = true;
                        await Dispatcher.InvokeAsync(() => { streamStartTime = DateTime.Now; timer.Start(); UpdateStreamStatus(true); });
                        try
                        {
                            while (true)
                            {
                                // run ffmpeg process (no direct UI access inside)
                                await ExecuteFFmpegAsync(args, ffmpegCts.Token).ConfigureAwait(false);

                                // After each run, decide whether to loop by querying UI-bound flags on the UI thread
                                bool shouldLoop = await Dispatcher.InvokeAsync(() => FolderLoop || (FolderLoopCheck?.IsChecked == true));

                                if (!shouldLoop || ffmpegCts.Token.IsCancellationRequested)
                                    break;
                            }
                        }
                        catch (OperationCanceledException) { }
                        finally
                        {
                            await Dispatcher.InvokeAsync(() => { isStreaming = false; timer.Stop(); UpdateStreamStatus(false); });
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

                isStreaming = false;
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
            // Assign process to a job so child processes are killed if the app exits/crashes
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
            {
                proc.StartInfo.ArgumentList.Add(a);
            }

            // publish reference once
            ffmpegProcess = proc;

            var outputSb = new StringBuilder();
            var errorSb = new StringBuilder();

            var outputTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var errorTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            proc.OutputDataReceived += (s, e) =>
            {
                if (e.Data == null) outputTcs.TrySetResult(true);
                else
                {
                    outputSb.AppendLine(e.Data);
                    // Append to UI safely
                    Dispatcher.BeginInvoke(() => AddToHistory(e.Data));
                }
            };
            proc.ErrorDataReceived += (s, e) =>
            {
                if (e.Data == null) errorTcs.TrySetResult(true);
                else
                {
                    errorSb.AppendLine(e.Data);
                    Dispatcher.BeginInvoke(() => AddToHistory(e.Data));
                }
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

                // monitor cancellation
                using (ct.Register(() =>
                {
                    try { if (!proc.HasExited) proc.StandardInput.WriteLine("q"); } catch { }
                }))
                {
                    // Wait for exit or cancellation
                    while (!proc.HasExited)
                    {
                        if (ct.IsCancellationRequested)
                        {
                            // give it a moment, then kill tree
                            try { if (!proc.WaitForExit(1500)) proc.Kill(entireProcessTree: true); } catch { try { proc.Kill(); } catch { } }
                            break;
                        }
                        await Task.Delay(200, ct).ConfigureAwait(false);
                    }
                }

                // ensure readers finish
                await Task.WhenAll(outputTcs.Task, errorTcs.Task).ConfigureAwait(false);

                string output = outputSb.ToString();
                string error = errorSb.ToString();

                Dispatcher.Invoke(() =>
                {
                    if (!string.IsNullOrEmpty(output))
                        AddToHistory("FFmpeg: Completado");

                    if (!string.IsNullOrEmpty(error) && !error.Contains("Press") && !error.Contains("KB"))
                        System.Windows.MessageBox.Show(error, "FFmpeg Output",
                                        MessageBoxButton.OK, MessageBoxImage.Information);
                });
            }
            catch (OperationCanceledException)
            {
                // cancellation requested
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                    System.Windows.MessageBox.Show($"Error en FFmpeg: {ex.Message}", "Error",
                                    MessageBoxButton.OK, MessageBoxImage.Error));
            }
            finally
            {
                // Clear the shared reference only if it still points to this process
                Interlocked.CompareExchange(ref ffmpegProcess, null, proc);

                Dispatcher.Invoke(() =>
                {
                    isStreaming = false;
                    timer.Stop();
                    UpdateStreamStatus(false);
                });

                try { proc.Dispose(); } catch { }
                try { job?.Dispose(); } catch { }
            }
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
            try
            {
                Directory.CreateDirectory(appDataPath);
                var obj = new { CloseAction = action.ToString() };
                File.WriteAllText(prefsPath, JsonSerializer.Serialize(obj));
            }
            catch { }
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
                        System.Windows.MessageBox.Show("No se pudo inicializar el icono de la bandeja del sistema. Streamer Pro permanecerá visible.", "Error al inicializar la bandeja", MessageBoxButton.OK, MessageBoxImage.Warning);
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

                // Try load custom icon, fall back to SystemIcons.Application
                try
                {
                    var icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? string.Empty, "streamerpro.ico");
                    if (!File.Exists(icoPath))
                    {
                        // fallback file name used previously
                        icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? string.Empty, "streamer.ico");
                    }

                    if (File.Exists(icoPath))
                    {
                        _trayIcon.Icon = new System.Drawing.Icon(icoPath);
                    }
                    else
                    {
                        _trayIcon.Icon = System.Drawing.SystemIcons.Application;
                    }
                }
                catch (Exception)
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

        private void ExecuteFFmpeg(string command)
        {
            // Back-compat: allow callers to pass a single command string
            var args = new List<string>();
            if (!string.IsNullOrWhiteSpace(command))
            {
                var trimmed = command.StartsWith("ffmpeg ") ? command.Substring(7) : command;
                // naive split on spaces for legacy strings (best-effort)
                args.AddRange(trimmed.Split(' ').Where(s => !string.IsNullOrEmpty(s)));
            }

            ExecuteFFmpeg(args);
        }

        private void ExecuteFFmpeg(IEnumerable<string> args)
        {
            var proc = new Process();
            var ffmpegPath = GetFfmpegPath();
            proc.StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            // Fill ArgumentList (safer than building one large string)
            foreach (var a in args ?? Enumerable.Empty<string>())
            {
                proc.StartInfo.ArgumentList.Add(a);
            }

            // publish reference once
            ffmpegProcess = proc;

            var outputSb = new StringBuilder();
            var errorSb = new StringBuilder();

            proc.OutputDataReceived += (s, e) => { if (e.Data != null) outputSb.AppendLine(e.Data); };
            proc.ErrorDataReceived += (s, e) => { if (e.Data != null) errorSb.AppendLine(e.Data); };

            try
            {
                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                proc.WaitForExit();

                string output = outputSb.ToString();
                string error = errorSb.ToString();

                Dispatcher.Invoke(() =>
                {
                    if (!string.IsNullOrEmpty(output))
                        AddToHistory("FFmpeg: Completado");

                    if (!string.IsNullOrEmpty(error) && !error.Contains("Press") && !error.Contains("KB"))
                        System.Windows.MessageBox.Show(error, "FFmpeg Output",
                                        MessageBoxButton.OK, MessageBoxImage.Information);
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                    System.Windows.MessageBox.Show($"Error en FFmpeg: {ex.Message}", "Error",
                                    MessageBoxButton.OK, MessageBoxImage.Error));
            }
            finally
            {
                // Clear the shared reference only if it still points to this process
                Interlocked.CompareExchange(ref ffmpegProcess, null, proc);

                Dispatcher.Invoke(() =>
                {
                    isStreaming = false;
                    timer.Stop();
                    UpdateStreamStatus(false);
                });

                try { proc.Dispose(); } catch { }
            }
        }

        // Refactored helper: returns encoding/output arguments only. Does NOT include -re, -i, or hwaccel.
        private IEnumerable<string> BuildEncodingArguments(
            string sourceUrl,
            string rtmpUrl,
            string preset,
            string videoBitrate,
            string audioBitrate,
            string resolution,
            string fps,
            bool forceYuv,
            bool isFolderMode)
        {
            var args = new List<string>(20);

            // video encoding
            args.Add("-c:v");
            args.Add("libx264");
            args.Add("-preset");
            args.Add(string.IsNullOrWhiteSpace(preset) ? "veryfast" : preset);

            if (!string.IsNullOrWhiteSpace(videoBitrate))
            {
                args.Add("-b:v");
                args.Add(videoBitrate);
            }

            // If a resolution is provided, apply a safe filter that preserves aspect ratio
            // and pads (letterbox) to the exact output size to avoid stretching.
            if (!string.IsNullOrWhiteSpace(resolution))
            {
                // parse WxH
                var sep = resolution.Contains('x') ? 'x' : (resolution.Contains('X') ? 'X' : '\0');
                int w = 0, h = 0;
                if (sep != '\0')
                {
                    var parts = resolution.Split(sep);
                    if (parts.Length == 2 && int.TryParse(parts[0].Trim(), out w) && int.TryParse(parts[1].Trim(), out h))
                    {
                        var vf = $"scale={w}:{h}:force_original_aspect_ratio=decrease,pad={w}:{h}:(ow-iw)/2:(oh-ih)/2,setsar=1";
                        args.Add("-vf");
                        args.Add(vf);
                    }
                    else
                    {
                        // fallback to simple size flag
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

            if (!string.IsNullOrWhiteSpace(fps))
            {
                args.Add("-r");
                args.Add(fps);
            }

            // Keep yuv420p as default pixel format for broad compatibility
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

            // output format and url
            args.Add("-f");
            args.Add("flv");
            args.Add(rtmpUrl);

            return args;
        }

        private void UpdateStreamStatus(bool streaming)
        {
            if (streaming)
            {
                StreamStatus.Fill = (SolidColorBrush)FindResource("Success");
                StreamStatusText.Text = "Transmitiendo";
                StreamStatusText.Foreground = (SolidColorBrush)FindResource("Success");
                StartButton.IsEnabled = false;
                StopButton.IsEnabled = true;
            }
            else
            {
                StreamStatus.Fill = (SolidColorBrush)FindResource("Danger");
                StreamStatusText.Text = "Detenido";
                StreamStatusText.Foreground = (SolidColorBrush)FindResource("Danger");
                StartButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                StreamTime.Text = "Tiempo: 00:00:00";
            }
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
                        AudioBitrateManual.Text = "96k";
                        ResolutionManual.Text = "854x480";
                        FPSManual.Text = "30";
                        break;
                    case "720p":
                        VideoBitrateManual.Text = "2500k";
                        AudioBitrateManual.Text = "128k";
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
                        AudioBitrateManual.Text = "192k";
                        ResolutionManual.Text = "1920x1080";
                        FPSManual.Text = "60";
                        break;
                    case "4k":
                        VideoBitrateManual.Text = "16000k";
                        AudioBitrateManual.Text = "256k";
                        ResolutionManual.Text = "3840x2160";
                        FPSManual.Text = "30";
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
                            case "4k":
                                SelectPreset("faster");
                                break;
                            default:
                                break;
                        }
                    }
                }
                catch { }

                // Update visual state: make clicked button primary and others secondary
                var profileButtons = new[] { "Profile480p", "Profile720p", "Profile1080p", "Profile1080p60", "Profile4k", "ProfileCustom" };
                foreach (var name in profileButtons)
                {
                    if (FindName(name) is Button b)
                    {
                        try
                        {
                            b.Style = (Style)FindResource("SecondaryButton");
                        }
                        catch { }
                    }
                }

                // Apply primary style to clicked button
                try { button.Style = (Style)FindResource("PrimaryButton"); } catch { }
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

        private void ProfileCustom_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Puedes editar los valores manualmente en los campos inferiores",
                          "Perfil personalizado",
                          MessageBoxButton.OK,
                          MessageBoxImage.Information);
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
                    StreamKey = StreamKey.Text,
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
                MessageBox.Show("Configuración guardada", "Éxito",
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
                // Access UI collection on dispatcher to build snapshot
                await Dispatcher.InvokeAsync(() =>
                {
                    foreach (var item in HistoryList.Items)
                    {
                        history.Add(item?.ToString() ?? string.Empty);
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

        private void FavoritesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FavoritesList.SelectedItem != null)
            {
                try
                {
                    string name = FavoritesList.SelectedItem.ToString() ?? "";
                    string favPath = Path.Combine(appDataPath, "Favorites", $"{SanitizeFileName(name)}.json");

                    if (File.Exists(favPath))
                    {
                        string json = File.ReadAllText(favPath);
                        var config = JsonSerializer.Deserialize<Dictionary<string, object>>(json);

                        if (config != null)
                        {
                            if (config.ContainsKey("RtmpBase"))
                                RTMPBase.Text = config["RtmpBase"]?.ToString() ?? "";
                            if (config.ContainsKey("StreamKey"))
                                StreamKey.Text = config["StreamKey"]?.ToString() ?? "";
                            if (config.ContainsKey("VBitrate"))
                                VideoBitrateManual.Text = config["VBitrate"]?.ToString() ?? "2500k";
                            if (config.ContainsKey("ABitrate"))
                                AudioBitrateManual.Text = config["ABitrate"]?.ToString() ?? "128k";
                            if (config.ContainsKey("Resolution"))
                                ResolutionManual.Text = config["Resolution"]?.ToString() ?? "1280x720";
                            if (config.ContainsKey("FPS"))
                                FPSManual.Text = config["FPS"]?.ToString() ?? "30";

                            // Buscar preset en combo
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
                        }

                        MessageBox.Show($"Configuración '{name}' cargada", "Éxito",
                                      MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error cargando configuración: {ex.Message}", "Error",
                                  MessageBoxButton.OK, MessageBoxImage.Error);
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
            while (HistoryList.Items.Count > 20)
            {
                HistoryList.Items.RemoveAt(HistoryList.Items.Count - 1);
            }

            // Save history asynchronously - don't block UI
            _ = SaveHistoryAsync();
        }

        private void OpenSourcesButton_Click(object sender, RoutedEventArgs e)
        {
            var folder = SourcesRepository.GetAppFolder();
            try { Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true }); } catch (Exception ex) { MessageBox.Show($"No se pudo abrir la carpeta: {ex.Message}"); }
        }

        private void ModeOnline_Checked(object sender, RoutedEventArgs e)
        {
            if (!_uiReady) return;

            IsOnlineMode = true;
            // show/hide folder controls
            if (FindName("FolderControls") is FrameworkElement folderControls)
                folderControls.Visibility = Visibility.Collapsed;

            if (FindName("SourceCombo") is System.Windows.Controls.ComboBox sc)
                sc.IsEnabled = true;
        }

        private void ModeFolder_Checked(object sender, RoutedEventArgs e)
        {
            if (!_uiReady) return;

            IsOnlineMode = false;
            if (FindName("FolderControls") is FrameworkElement folderControls)
                folderControls.Visibility = Visibility.Visible;

            if (FindName("SourceCombo") is System.Windows.Controls.ComboBox sc)
                sc.IsEnabled = false;
        }

        private void SelectFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog();
            var res = dlg.ShowDialog();
            if (res == System.Windows.Forms.DialogResult.OK)
            {
                SelectedFolderPath = dlg.SelectedPath;
                if (SelectedFolderText != null) SelectedFolderText.Text = SelectedFolderPath;
            }
        }

        private async void ReloadSourcesButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadAndValidateSourcesAsync();
        }

        private void SourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }

        private async void MainWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            try
            {
                // Run initialization tasks that require the UI
                await LoadAndValidateSourcesAsync().ConfigureAwait(false);
                await CheckFFmpegAsync().ConfigureAwait(false);

                // Ensure window is on-screen and visible
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
            // Save the stream key when the user leaves the textbox
            await SaveConfigAsync().ConfigureAwait(false);
        }
    }
}