using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Windows.Storage;
using Windows.UI.Xaml.Media;

namespace BehiMusic_UWP.Models
{
    public sealed class Song : INotifyPropertyChanged
    {
        private bool _isPlaying;
        private ImageSource _artwork;

        public StorageFile File { get; set; }
        public string Path { get; set; }
        public string FileName { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public string AlbumArtist { get; set; }
        public string Album { get; set; }
        public string Genre { get; set; }
        public string Folder { get; set; }
        public string ReleaseType { get; set; }
        public uint TrackNumber { get; set; }
        public uint Year { get; set; }
        public uint Bitrate { get; set; }
        public TimeSpan Duration { get; set; }
        public DateTimeOffset DateAdded { get; set; }
        public string NormalizedTitle { get; set; }
        public string SearchIndex { get; set; }
        public bool ArtworkLoadAttempted { get; set; }

        public ImageSource Artwork
        {
            get { return _artwork; }
            set { if (_artwork != value) { _artwork = value; OnPropertyChanged(); } }
        }

        public string DisplayTitle
        {
            get { return string.IsNullOrWhiteSpace(Title) ? FileName : Title; }
        }

        public string DisplayArtist
        {
            get { return string.IsNullOrWhiteSpace(Artist) ? "Unknown artist" : Artist; }
        }

        public string DisplayAlbum
        {
            get { return string.IsNullOrWhiteSpace(Album) ? "Unknown album" : Album; }
        }

        public string DurationText
        {
            get { return Duration.TotalHours >= 1 ? Duration.ToString(@"h\:mm\:ss") : Duration.ToString(@"m\:ss"); }
        }

        public bool IsPlaying
        {
            get { return _isPlaying; }
            set
            {
                if (_isPlaying == value) return;
                _isPlaying = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(propertyName));
        }

        public override string ToString()
        {
            return DisplayTitle + " — " + DisplayArtist;
        }
    }
}
