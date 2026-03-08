using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace Streamer
{
    public partial class CreditsWindow : Window
    {
        private readonly DispatcherTimer _scrollTimer;

        public CreditsWindow()
        {
            InitializeComponent();

            // Set version badge dynamically
            try
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                if (version != null)
                {
                    if (FindName("VersionText") is TextBlock vt)
                        vt.Text = $"v{version.Major}.{version.Minor}";
                }
            }
            catch { }

            BuildCreditsContent();

            _scrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            _scrollTimer.Tick += ScrollTimer_Tick;
            Loaded += (s, e) => StartScroll();
            Unloaded += (s, e) => StopScroll();
        }

        private void BuildCreditsContent()
        {
            CreditsPanel.Children.Clear();

            var accentBrush = (Brush)FindResource("AccentYellow");
            var textPrimary = (Brush)FindResource("TextPrimary");
            var textSecondary = (Brush)FindResource("TextSecondary");
            var textMuted = (Brush)FindResource("TextMuted");
            var cardBg = (Brush)FindResource("BgCard");
            var borderBrush = (Brush)FindResource("BorderBrushToken");

            // --- Development section ---
            AddSectionHeader("\U0001F468\u200D\U0001F4BB", "Desarrollo", accentBrush);
            AddCreditCard("Marcos Santiago", "Desarrollo & arquitectura", "\U0001F9D1\u200D\U0001F4BB", cardBg, borderBrush, textPrimary, textSecondary);
            AddCreditCard("GitHub Copilot", "Asistencia de codigo & pair programming", "\U0001F916", cardBg, borderBrush, textPrimary, textSecondary);
            AddCreditCard("ChatGPT", "Asistencia de diseno, UX & texto", "\U0001F4AC", cardBg, borderBrush, textPrimary, textSecondary);
            AddCreditCard("DeepSeek", "Investigacion & recursos", "\U0001F50D", cardBg, borderBrush, textPrimary, textSecondary);
            AddSpacer();

            // --- Technology section ---
            AddSectionHeader("\u26A1", "Tecnologias", accentBrush);
            AddCreditCard("FFmpeg", "Motor de procesamiento de audio/video", "\U0001F3A5", cardBg, borderBrush, textPrimary, textSecondary);
            AddCreditCard(".NET 8 / WPF", "Framework de aplicacion", "\U0001F3D7\uFE0F", cardBg, borderBrush, textPrimary, textSecondary);
            AddCreditCard("Visual Studio 2022", "Entorno de desarrollo (Community Edition)", "\U0001F4BB", cardBg, borderBrush, textPrimary, textSecondary);
            AddSpacer();

            // --- Libraries section ---
            AddSectionHeader("\U0001F4E6", "Librerias", accentBrush);
            AddPillRow(new[] { "System.Text.Json", "Microsoft.VisualBasic", "System.Security.Cryptography" }, accentBrush);
            AddSpacer();

            // --- Licenses section ---
            AddSectionHeader("\U0001F4C4", "Licencias", accentBrush);
            AddWrappedText("FFmpeg es software independiente distribuido bajo su propia licencia (LGPL/GPL). Consulta ffmpeg.org para detalles.", textMuted);
            AddWrappedText("Al usar componentes de terceros, asegurate de cumplir sus respectivas licencias.", textMuted);
            AddSpacer();

            // --- Thanks section ---
            AddSectionHeader("\U0001F64F", "Agradecimientos", accentBrush);
            AddWrappedText("A la comunidad de software libre, a todos los que contribuyen al ecosistema open source, y a los streamers que hacen posible la creacion de contenido.", textSecondary);
            AddSpacer();
            AddSpacer();

            // --- End mark ---
            CreditsPanel.Children.Add(new TextBlock
            {
                Text = "- Streamer Pro -",
                Foreground = textMuted,
                FontSize = 13,
                FontStyle = FontStyles.Italic,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 12, 0, 40)
            });
        }

        private void AddSectionHeader(string icon, string title, Brush accentBrush)
        {
            var sp = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 14, 0, 8)
            };

            sp.Children.Add(new TextBlock
            {
                Text = icon,
                FontSize = 18,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });

            sp.Children.Add(new TextBlock
            {
                Text = title.ToUpperInvariant(),
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = accentBrush,
                VerticalAlignment = VerticalAlignment.Center
            });

            CreditsPanel.Children.Add(sp);

            CreditsPanel.Children.Add(new Border
            {
                Height = 1,
                Background = accentBrush,
                Opacity = 0.3,
                Margin = new Thickness(0, 0, 0, 6),
                HorizontalAlignment = HorizontalAlignment.Stretch
            });
        }

        private void AddCreditCard(string name, string role, string icon, Brush cardBg, Brush borderBrush, Brush textPrimary, Brush textSecondary)
        {
            var border = new Border
            {
                Background = cardBg,
                CornerRadius = new CornerRadius(10),
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 3, 0, 3),
                SnapsToDevicePixels = true
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var iconBlock = new TextBlock
            {
                Text = icon,
                FontSize = 20,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(iconBlock, 0);
            grid.Children.Add(iconBlock);

            var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            textStack.Children.Add(new TextBlock
            {
                Text = name,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = textPrimary
            });
            textStack.Children.Add(new TextBlock
            {
                Text = role,
                FontSize = 12,
                Foreground = textSecondary,
                Margin = new Thickness(0, 2, 0, 0)
            });
            Grid.SetColumn(textStack, 1);
            grid.Children.Add(textStack);

            border.Child = grid;
            CreditsPanel.Children.Add(border);
        }

        private void AddPillRow(string[] items, Brush accentBrush)
        {
            var wrap = new WrapPanel
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 4, 0, 4)
            };

            foreach (var item in items)
            {
                var pill = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(30, 245, 183, 46)),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(10, 4, 10, 4),
                    Margin = new Thickness(0, 0, 6, 6)
                };
                pill.Child = new TextBlock
                {
                    Text = item,
                    FontSize = 12,
                    Foreground = accentBrush,
                    FontWeight = FontWeights.Medium
                };
                wrap.Children.Add(pill);
            }

            CreditsPanel.Children.Add(wrap);
        }

        private void AddWrappedText(string text, Brush foreground)
        {
            CreditsPanel.Children.Add(new TextBlock
            {
                Text = text,
                Foreground = foreground,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 4),
                LineHeight = 18
            });
        }

        private void AddSpacer()
        {
            CreditsPanel.Children.Add(new Border { Height = 8 });
        }

        private void StartScroll()
        {
            CreditsScroll.ScrollToVerticalOffset(0);
            _scrollTimer.Start();
        }

        private void StopScroll()
        {
            _scrollTimer.Stop();
        }

        private void ScrollTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                if (CreditsScroll.VerticalOffset >= CreditsScroll.ScrollableHeight)
                {
                    _scrollTimer.Stop();
                    return;
                }

                CreditsScroll.ScrollToVerticalOffset(CreditsScroll.VerticalOffset + 0.6);
            }
            catch { }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try { DragMove(); } catch { }
        }
    }
}
