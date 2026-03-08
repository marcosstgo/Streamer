using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Streamer.Models
{
    public class Source : INotifyPropertyChanged
    {
        private bool isAvailable;

        public string Name { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        public bool IsAvailable
        {
            get => isAvailable;
            set
            {
                if (isAvailable == value) return;
                isAvailable = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayText));
            }
        }

        public string DisplayText
        {
            get
            {
                var desc = string.IsNullOrWhiteSpace(Description) ? string.Empty : $" - {Description}";
                var avail = IsAvailable ? string.Empty : " (No disponible)";
                return $"{Name}{desc}{avail}";
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
