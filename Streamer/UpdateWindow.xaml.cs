using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Streamer
{
    public partial class UpdateWindow : Window
    {
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(10) };
        private CancellationTokenSource? _cts;
        private readonly string _newVersion;
        private readonly string _downloadUrl;

        public UpdateWindow(string newVersion, string downloadUrl)
        {
            InitializeComponent();
            _newVersion = newVersion;
            _downloadUrl = downloadUrl;

            var current = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";
            CurrentVersionText.Text = $"v{current}";
            NewVersionText.Text = $"v{newVersion}";
        }

        private async void UpdateBtn_Click(object sender, RoutedEventArgs e)
        {
            UpdateBtn.IsEnabled = false;
            CancelBtn.IsEnabled = false;
            _cts = new CancellationTokenSource();

            try
            {
                await DownloadAndApplyAsync(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = "Actualización cancelada.";
                DownloadProgress.Value = 0;
                UpdateBtn.IsEnabled = true;
                CancelBtn.IsEnabled = true;
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error: {ex.Message}";
                UpdateBtn.IsEnabled = true;
                CancelBtn.IsEnabled = true;
            }
        }

        private async Task DownloadAndApplyAsync(CancellationToken ct)
        {
            // Determine paths — Environment.ProcessPath is reliable for single-file apps
            var currentExe = Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName
                ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Streamer Pro.exe");
            var tempExe = Path.Combine(Path.GetTempPath(), $"StreamerPro-update-{_newVersion}.exe");

            // Download
            StatusText.Text = "Conectando con GitHub...";
            using var req = new HttpRequestMessage(HttpMethod.Get, _downloadUrl);
            req.Headers.UserAgent.ParseAdd("StreamerPro/updater");
            using var response = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? -1L;
            using var srcStream = await response.Content.ReadAsStreamAsync(ct);
            using (var fileStream = new FileStream(tempExe, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true))
            {
                var buffer = new byte[65536];
                long downloaded = 0;
                int read;

                while ((read = await srcStream.ReadAsync(buffer, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                    downloaded += read;

                    if (total > 0)
                    {
                        var pct = (double)downloaded / total * 100;
                        var mb = downloaded / 1_048_576.0;
                        var totalMb = total / 1_048_576.0;
                        Dispatcher.Invoke(() =>
                        {
                            DownloadProgress.Value = pct;
                            StatusText.Text = $"Descargando... {mb:F1} MB / {totalMb:F1} MB";
                        });
                    }
                }
            }

            Dispatcher.Invoke(() =>
            {
                DownloadProgress.Value = 99;
                StatusText.Visibility = Visibility.Collapsed;
                SuccessBanner.Visibility = Visibility.Visible;
            });

            // Small delay so user sees the success banner
            await Task.Delay(1500, ct);

            // Write a batch script to replace the exe and relaunch
            var batchPath = Path.Combine(Path.GetTempPath(), "streamerpro_update.bat");
            File.WriteAllText(batchPath,
                $"@echo off\r\n" +
                $"ping 127.0.0.1 -n 6 > nul\r\n" +
                $":retry\r\n" +
                $"move /Y \"{tempExe}\" \"{currentExe}\"\r\n" +
                $"if errorlevel 1 (ping 127.0.0.1 -n 3 > nul & goto retry)\r\n" +
                $"start \"\" \"{currentExe}\"\r\n" +
                $"del \"%~f0\"\r\n");

            var psi = new ProcessStartInfo("cmd.exe", $"/c \"{batchPath}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(psi);

            // Close application
            System.Windows.Application.Current.Dispatcher.Invoke(() => System.Windows.Application.Current.Shutdown());
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            Close();
        }
    }
}
