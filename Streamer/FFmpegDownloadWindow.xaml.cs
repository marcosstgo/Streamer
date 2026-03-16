using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Streamer
{
    public partial class FFmpegDownloadWindow : Window
    {
        private const string VersionUrl = "https://www.gyan.dev/ffmpeg/builds/release-version";
        private const string DownloadUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";

        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(15) };
        private CancellationTokenSource? _cts;

        public bool DownloadCompleted { get; private set; }

        public FFmpegDownloadWindow()
        {
            InitializeComponent();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await FetchVersionAsync();
        }

        private async Task FetchVersionAsync()
        {
            try
            {
                var version = (await _http.GetStringAsync(VersionUrl)).Trim();
                VersionText.Text = version;
                DownloadBtn.IsEnabled = true;
                StatusText.Text = "Listo para descargar.";
            }
            catch
            {
                VersionText.Text = "No se pudo verificar";
                VersionText.Foreground = System.Windows.Media.Brushes.OrangeRed;
                DownloadBtn.IsEnabled = true;
                StatusText.Text = "No se pudo obtener la versión. Puedes continuar de todas formas.";
            }
        }

        private async void DownloadBtn_Click(object sender, RoutedEventArgs e)
        {
            DownloadBtn.IsEnabled = false;
            _cts = new CancellationTokenSource();

            try
            {
                await DownloadAndExtractAsync(_cts.Token);
                DownloadCompleted = true;
                StatusText.Visibility = System.Windows.Visibility.Collapsed;
                SuccessBanner.Visibility = System.Windows.Visibility.Visible;
                DownloadProgress.Value = 100;
                DownloadBtn.IsEnabled = false;
                CancelBtn.Content = "Cerrar";
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = "Descarga cancelada.";
                DownloadProgress.Value = 0;
                DownloadBtn.IsEnabled = true;
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error: {ex.Message}";
                DownloadBtn.IsEnabled = true;
            }
        }

        private async Task DownloadAndExtractAsync(CancellationToken ct)
        {
            var destDir = AppDomain.CurrentDomain.BaseDirectory ?? AppContext.BaseDirectory;
            var zipPath = Path.Combine(Path.GetTempPath(), "ffmpeg-essentials.zip");

            // Download with progress
            StatusText.Text = "Conectando con gyan.dev...";

            using var response = await _http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? -1L;
            using var srcStream = await response.Content.ReadAsStreamAsync(ct);
            using (var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true))
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

            // Extract
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = "Extrayendo ffmpeg.exe y ffprobe.exe...";
                DownloadProgress.Value = 99;
            });

            await Task.Run(() =>
            {
                using var zip = ZipFile.OpenRead(zipPath);
                foreach (var entry in zip.Entries)
                {
                    var name = Path.GetFileName(entry.FullName);
                    if (name is "ffmpeg.exe" or "ffprobe.exe")
                        entry.ExtractToFile(Path.Combine(destDir, name), overwrite: true);
                }
            }, ct);

            try { File.Delete(zipPath); } catch { }
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            Close();
        }
    }
}
