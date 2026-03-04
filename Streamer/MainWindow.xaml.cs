using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualBasic;
using System.Text;

namespace Streamer
{
    public partial class MainWindow : Window
    {
        private Process? ffmpegProcess;
        private DispatcherTimer timer = new DispatcherTimer();
        private DateTime streamStartTime;
        private bool isStreaming = false;
        private readonly string appDataPath;

        public MainWindow()
        {
            InitializeComponent();

            appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "CorilloStreamer"
            );
            Directory.CreateDirectory(appDataPath);
            Directory.CreateDirectory(Path.Combine(appDataPath, "Favorites"));

            InitializeComponents();
            CheckFFmpeg();
            LoadFavorites();
            LoadHistory();
        }

        private void InitializeComponents()
        {
            timer.Interval = TimeSpan.FromSeconds(1);
            timer.Tick += Timer_Tick!;
        }

        private async void CheckFFmpeg()
        {
            await Task.Run(() =>
            {
                try
                {
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "ffmpeg",
                            Arguments = "-version",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            CreateNoWindow = true
                        }
                    };

                    process.Start();
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(3000);

                    bool detected = output.Contains("ffmpeg version", StringComparison.OrdinalIgnoreCase);

                    Dispatcher.Invoke(() => UpdateFFmpegStatus(detected));
                }
                catch
                {
                    Dispatcher.Invoke(() => UpdateFFmpegStatus(false));
                }
            });
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

        private void StartStream_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(StreamKey.Text))
                {
                    MessageBox.Show("Stream key es requerido", "Error",
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string rtmpBase = RTMPBase.Text.TrimEnd('/');
                string streamKey = StreamKey.Text.TrimStart('/');
                string rtmpUrl = $"{rtmpBase}/{streamKey}";

                string source = GetSelectedSource();
                string vBitrate = VideoBitrateManual.Text;
                string aBitrate = AudioBitrateManual.Text;

                var presetItem = PresetCombo.SelectedItem as ComboBoxItem;
                string preset = presetItem?.Content?.ToString() ?? "veryfast";

                string resolution = ResolutionManual.Text;
                string fps = FPSManual.Text;

                string sourceUrl = GetSourceUrl(source);

                string ffmpegCmd = $"ffmpeg -re -i \"{sourceUrl}\" " +
                                  $"-c:v libx264 -preset {preset} -b:v {vBitrate} " +
                                  $"-c:a aac -b:a {aBitrate} " +
                                  $"-f flv \"{rtmpUrl}\"";

                if (ForceYUV.IsChecked == true)
                    ffmpegCmd += " -pix_fmt yuv420p";

                if (HardwareAccel.IsChecked == true)
                    ffmpegCmd = " -hwaccel auto " + ffmpegCmd;

                if (ShowFFmpegCommand.IsChecked == true)
                {
                    MessageBox.Show(ffmpegCmd, "Comando FFmpeg",
                                  MessageBoxButton.OK, MessageBoxImage.Information);
                }

                Task.Run(() => ExecuteFFmpeg(ffmpegCmd));

                isStreaming = true;
                streamStartTime = DateTime.Now;
                timer.Start();
                UpdateStreamStatus(true);

                AddToHistory($"Stream iniciado: {source} - {vBitrate}");
            }           
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StopStream_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // atomically take ownership and null the field
                var proc = Interlocked.Exchange(ref ffmpegProcess, null);
                if (proc != null && !proc.HasExited)
                {
                    proc.Kill();
                    proc.WaitForExit(3000);
                }
                proc?.Dispose();

                isStreaming = false;
                timer.Stop();
                UpdateStreamStatus(false);

                AddToHistory("Stream detenido");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error deteniendo stream: {ex.Message}", "Error",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExecuteFFmpeg(string command)
        {
            Process proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = command.StartsWith("ffmpeg ") ? command.Substring(7) : command,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            // publish reference once
            ffmpegProcess = proc;

            var outputSb = new StringBuilder();
            var errorSb = new StringBuilder();

            proc.OutputDataReceived += (s, e) => { if (e.Data != null) outputSb.AppendLine(e.Data); };
            proc.ErrorDataReceived  += (s, e) => { if (e.Data != null) errorSb.AppendLine(e.Data); };

            try
            {
                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                proc.WaitForExit();

                string output = outputSb.ToString();
                string error  = errorSb.ToString();

                Dispatcher.Invoke(() =>
                {
                    if (!string.IsNullOrEmpty(output))
                        AddToHistory("FFmpeg: Completado");

                    if (!string.IsNullOrEmpty(error) && !error.Contains("Press") && !error.Contains("KB"))
                        MessageBox.Show(error, "FFmpeg Output",
                                        MessageBoxButton.OK, MessageBoxImage.Information);
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                    MessageBox.Show($"Error en FFmpeg: {ex.Message}", "Error",
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
            if (sender is Button button && button.Tag is string profile)
            {
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
                    Source = GetSelectedSource(),
                    VBitrate = VideoBitrateManual.Text,
                    ABitrate = AudioBitrateManual.Text,
                    Preset = (PresetCombo.SelectedItem as ComboBoxItem)?.Content?.ToString(),
                    Resolution = ResolutionManual.Text,
                    FPS = FPSManual.Text,
                    ForceYUV = ForceYUV.IsChecked,
                    DateSaved = DateTime.Now
                };

                string json = System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });

                string favPath = Path.Combine(appDataPath, "Favorites", $"{SanitizeFileName(name)}.json");
                File.WriteAllText(favPath, json);

                LoadFavorites();
                MessageBox.Show("Configuración guardada", "Éxito",
                              MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }

        private void LoadFavorites()
        {
            try
            {
                string favDir = Path.Combine(appDataPath, "Favorites");
                if (Directory.Exists(favDir))
                {
                    var files = Directory.GetFiles(favDir, "*.json");
                    FavoritesList.Items.Clear();
                    foreach (var file in files)
                    {
                        FavoritesList.Items.Add(Path.GetFileNameWithoutExtension(file));
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading favorites: {ex.Message}");
            }
        }

        private void LoadHistory()
        {
            try
            {
                string historyFile = Path.Combine(appDataPath, "history.json");
                if (File.Exists(historyFile))
                {
                    string json = File.ReadAllText(historyFile);
                    var history = System.Text.Json.JsonSerializer.Deserialize<List<string>>(json);
                    if (history != null)
                    {
                        HistoryList.Items.Clear();
                        foreach (var item in history)
                        {
                            HistoryList.Items.Add(item);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading history: {ex.Message}");
            }
        }

        private void SaveHistory()
        {
            try
            {
                var history = new List<string>();
                foreach (var item in HistoryList.Items)
                {
                    history.Add(item?.ToString() ?? "");
                }

                string json = System.Text.Json.JsonSerializer.Serialize(history);
                string historyFile = Path.Combine(appDataPath, "history.json");
                File.WriteAllText(historyFile, json);
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
                        var config = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(json);

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
            Dispatcher.Invoke(() =>
            {
                HistoryList.Items.Insert(0, $"{DateTime.Now:HH:mm:ss} - {entry}");
                while (HistoryList.Items.Count > 20)
                {
                    HistoryList.Items.RemoveAt(HistoryList.Items.Count - 1);
                }
                SaveHistory();
            });
        }

        private string GetSelectedSource()
        {
            var item = SourceCombo.SelectedItem as ComboBoxItem;
            return item?.Content?.ToString() ?? "Tears of Steel - Sci-fi corto (open)";
        }

        private string GetSourceUrl(string sourceName)
        {
            if (sourceName.Contains("Tears of Steel"))
                return "https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/TearsOfSteel.mp4";
            else if (sourceName.Contains("Big Buck Bunny"))
                return "https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/BigBuckBunny.mp4";
            else if (sourceName.Contains("Elephant's Dream"))
                return "https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/ElephantsDream.mp4";
            else if (sourceName.Contains("Sintel"))
                return "https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/Sintel.mp4";
            else if (sourceName.Contains("For Bigger Blazes"))
                return "https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/ForBiggerBlazes.mp4";
            else if (sourceName.Contains("For Bigger Escape"))
                return "https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/ForBiggerEscape.mp4";
            else if (sourceName.Contains("For Bigger Fun"))
                return "https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/ForBiggerFun.mp4";
            else if (sourceName.Contains("For Bigger Joyrides"))
                return "https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/ForBiggerJoyrides.mp4";
            else
                return "https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/TearsOfSteel.mp4";
        }

        // Método temporal para depuración - lo puedes borrar después
        


        private void SourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }
    }
}