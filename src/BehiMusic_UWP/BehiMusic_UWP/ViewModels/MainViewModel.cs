using BehiMusic_UWP.Models;
using BehiMusic_UWP.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace BehiMusic_UWP.ViewModels
{
    public sealed class MainViewModel : INotifyPropertyChanged
    {
        private readonly LibraryScanner _scanner = new LibraryScanner();
        private readonly SearchService _search = new SearchService();
        private readonly List<Song> _library = new List<Song>();
        private CancellationTokenSource _scanCancellation;
        private string _query = string.Empty;
        private string _sortField = "Title";
        private bool _sortAscending = true;
        private bool _isScanning;
        private int _scanProgress;
        private string _status = "Ready to scan your MP3 library";

        public ObservableCollection<Song> Songs { get; } = new ObservableCollection<Song>();
        public ObservableCollection<Song> RecentlyAdded { get; } = new ObservableCollection<Song>();
        public ObservableCollection<LibraryGroup> Albums { get; } = new ObservableCollection<LibraryGroup>();
        public ObservableCollection<LibraryGroup> HomeAlbums { get; } = new ObservableCollection<LibraryGroup>();
        public ObservableCollection<LibraryGroup> Artists { get; } = new ObservableCollection<LibraryGroup>();
        public ObservableCollection<LibraryGroup> AlbumArtists { get; } = new ObservableCollection<LibraryGroup>();
        public ObservableCollection<LibraryGroup> Genres { get; } = new ObservableCollection<LibraryGroup>();
        public ObservableCollection<LibraryGroup> Folders { get; } = new ObservableCollection<LibraryGroup>();
        public ObservableCollection<LibraryGroup> ReleaseTypes { get; } = new ObservableCollection<LibraryGroup>();

        public IReadOnlyList<Song> Library { get { return _library; } }
        public SearchService Search { get { return _search; } }
        public string Query { get { return _query; } }

        public string SortField
        {
            get { return _sortField; }
            set { if (_sortField != value) { _sortField = value; OnPropertyChanged(); ApplyFilterAndSort(); } }
        }

        public bool SortAscending
        {
            get { return _sortAscending; }
            set { if (_sortAscending != value) { _sortAscending = value; OnPropertyChanged(); ApplyFilterAndSort(); } }
        }

        public bool IsScanning
        {
            get { return _isScanning; }
            private set { if (_isScanning != value) { _isScanning = value; OnPropertyChanged(); } }
        }

        public int ScanProgress
        {
            get { return _scanProgress; }
            private set { if (_scanProgress != value) { _scanProgress = value; OnPropertyChanged(); } }
        }

        public string Status
        {
            get { return _status; }
            private set { if (_status != value) { _status = value; OnPropertyChanged(); } }
        }

        public async Task ScanAsync(bool validateCache = false)
        {
            if (IsScanning) return;
            _scanCancellation = new CancellationTokenSource();
            IsScanning = true;
            ScanProgress = 0;
            Status = "Finding MP3 files…";
            try
            {
                Progress<int> progress = new Progress<int>(value =>
                {
                    ScanProgress = value;
                    Status = (validateCache ? "Reading MP3 metadata… " : "Loading cached MP3 library… ") + value + "%";
                });
                IReadOnlyList<Song> songs = await _scanner.ScanAsync(progress, _scanCancellation.Token, validateCache);
                _library.Clear();
                _library.AddRange(songs);
                RebuildGroups();
                Replace(RecentlyAdded, _library.OrderByDescending(song => song.DateAdded).Take(20));
                ApplyFilterAndSort();
                Status = _library.Count == 0
                    ? "No MP3 files found"
                    : _library.Count + " MP3 songs indexed · " + _scanner.LastCacheHitCount + " loaded from cache";
            }
            catch (OperationCanceledException)
            {
                Status = "Scan cancelled";
            }
            finally
            {
                IsScanning = false;
                _scanCancellation.Dispose();
                _scanCancellation = null;
            }
        }

        public void SetQuery(string query, bool remember)
        {
            _query = query ?? string.Empty;
            OnPropertyChanged("Query");
            if (remember) _search.Remember(_query);
            ApplyFilterAndSort();
        }

        public IReadOnlyList<object> GetSuggestions(string query)
        {
            return _search.Suggest(_library, query);
        }

        private void ApplyFilterAndSort()
        {
            IEnumerable<Song> filtered = _search.Find(_library, _query);
            Func<Song, object> selector;
            switch (_sortField)
            {
                case "Artist": selector = song => song.DisplayArtist; break;
                case "Album": selector = song => song.DisplayAlbum; break;
                case "Year": selector = song => song.Year; break;
                case "Date added": selector = song => song.DateAdded; break;
                case "Duration": selector = song => song.Duration; break;
                default: selector = song => song.DisplayTitle; break;
            }

            filtered = _sortAscending ? filtered.OrderBy(selector) : filtered.OrderByDescending(selector);
            Replace(Songs, filtered);
        }

        private void RebuildGroups()
        {
            Replace(Albums, GroupBy(_library, song => song.DisplayAlbum, song => song.ReleaseType));
            Replace(HomeAlbums, Albums.Take(6));
            Replace(Artists, GroupBy(_library, song => song.DisplayArtist));
            Replace(AlbumArtists, GroupBy(_library, song => string.IsNullOrWhiteSpace(song.AlbumArtist) ? song.DisplayArtist : song.AlbumArtist));
            Replace(Genres, GroupBy(_library, song => string.IsNullOrWhiteSpace(song.Genre) ? "Unknown genre" : song.Genre));
            Replace(Folders, GroupBy(_library, song => string.IsNullOrWhiteSpace(song.Folder) ? "Music" : song.Folder));
            Replace(ReleaseTypes, GroupBy(_library, song => string.IsNullOrWhiteSpace(song.ReleaseType) ? "Unknown" : song.ReleaseType));
        }

        private static IEnumerable<LibraryGroup> GroupBy(IEnumerable<Song> source, Func<Song, string> key, Func<Song, string> releaseType = null)
        {
            return source.GroupBy(key, StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
                .Select(group => new LibraryGroup
                {
                    Name = group.Key,
                    SongCount = group.Count(),
                    Subtitle = group.Count() == 1 ? "1 song" : group.Count() + " songs",
                    ReleaseType = releaseType == null ? null : group.Select(releaseType).FirstOrDefault(value => value != "Unknown"),
                    RepresentativeSong = group.FirstOrDefault()
                });
        }

        private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
        {
            target.Clear();
            foreach (T item in items) target.Add(item);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
