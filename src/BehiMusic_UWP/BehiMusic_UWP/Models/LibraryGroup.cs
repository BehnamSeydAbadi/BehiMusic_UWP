using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Windows.UI.Xaml.Media;

namespace BehiMusic_UWP.Models
{
    public sealed class LibraryGroup : INotifyPropertyChanged
    {
        private ImageSource _artwork;
        private double _coverSize = 112;
        public string Name { get; set; }
        public string Subtitle { get; set; }
        public int SongCount { get; set; }
        public string ReleaseType { get; set; }
        public Song RepresentativeSong { get; set; }
        public bool ArtworkLoadAttempted { get; set; }
        public ImageSource Artwork
        {
            get { return _artwork; }
            set { if (_artwork != value) { _artwork = value; OnPropertyChanged(); } }
        }
        public double CoverSize
        {
            get { return _coverSize; }
            set { if (Math.Abs(_coverSize - value) > 0.1) { _coverSize = value; OnPropertyChanged(); } }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
