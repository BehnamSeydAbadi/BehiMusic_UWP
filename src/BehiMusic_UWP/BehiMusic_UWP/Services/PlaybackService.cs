using BehiMusic_UWP.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.Media.Playback;
using Windows.Media.Core;
using Windows.Media;
using Windows.Storage;
using Windows.UI.Core;
using Windows.UI.Xaml;

namespace BehiMusic_UWP.Services
{
    public sealed class PlaybackService
    {
        private static readonly PlaybackService _current = new PlaybackService();
        private readonly MediaPlayer _player;
        private readonly MediaPlaybackList _playbackList;
        private readonly ObservableCollection<Song> _queue = new ObservableCollection<Song>();
        private readonly CoreDispatcher _uiDispatcher;
        private readonly SystemMediaTransportControls _systemControls;
        private bool _stateRestoreAttempted;
        private bool _synchronizingQueue;

        private PlaybackService()
        {
            _uiDispatcher = Window.Current.Dispatcher;
            _player = new MediaPlayer();
            _player.AudioCategory = MediaPlayerAudioCategory.Media;
            // We handle transport requests below so a system button is never applied twice.
            _player.CommandManager.IsEnabled = false;
            _systemControls = _player.SystemMediaTransportControls;
            _systemControls.IsEnabled = true;
            _systemControls.IsPlayEnabled = true;
            _systemControls.IsPauseEnabled = true;
            _systemControls.IsNextEnabled = true;
            _systemControls.IsPreviousEnabled = true;
            _systemControls.ButtonPressed += SystemControls_ButtonPressed;
            _systemControls.ShuffleEnabledChangeRequested += SystemControls_ShuffleEnabledChangeRequested;
            _systemControls.AutoRepeatModeChangeRequested += SystemControls_AutoRepeatModeChangeRequested;
            _playbackList = new MediaPlaybackList();
            _playbackList.CurrentItemChanged += PlaybackList_CurrentItemChanged;
            _player.PlaybackSession.PlaybackStateChanged += PlaybackSession_PlaybackStateChanged;
            _player.Source = _playbackList;
        }

        public static PlaybackService Current { get { return _current; } }
        public MediaPlayer Player { get { return _player; } }
        public ObservableCollection<Song> Queue { get { return _queue; } }
        public Song CurrentSong { get; private set; }
        public bool ShuffleEnabled { get { return _playbackList.ShuffleEnabled; } }
        public RepeatMode RepeatMode { get; private set; }

        public event EventHandler CurrentSongChanged;
        public event EventHandler PlaybackStateChanged;
        public event EventHandler QueueChanged;

        public async Task SetQueueAndPlayAsync(IEnumerable<Song> songs, Song selected)
        {
            List<Song> items = songs.Where(song => song != null && song.File != null).ToList();
            if (items.Count == 0) return;

            _playbackList.Items.Clear();
            _queue.Clear();
            foreach (Song song in items)
            {
                _queue.Add(song);
                _playbackList.Items.Add(CreatePlaybackItem(song));
            }

            int index = selected == null ? 0 : items.IndexOf(selected);
            if (index < 0) index = 0;
            _playbackList.MoveTo((uint)index);
            RaiseQueueChanged();
            await Task.Yield();
            _player.Play();
        }

        public async Task PlayFilesAsync(IEnumerable<StorageFile> files)
        {
            List<Song> songs = new List<Song>();
            foreach (StorageFile file in files.Where(file => string.Equals(file.FileType, ".mp3", StringComparison.OrdinalIgnoreCase)))
            {
                Song song = await LibraryScanner.ReadSongAsync(file);
                if (song == null)
                {
                    song = new Song
                    {
                        File = file,
                        Path = file.Path,
                        FileName = file.Name,
                        Title = file.DisplayName,
                        ReleaseType = "Unknown"
                    };
                    song.NormalizedTitle = SearchService.Normalize(song.DisplayTitle);
                    song.SearchIndex = SearchService.Normalize(song.DisplayTitle + "\u001e" + song.FileName);
                }
                songs.Add(song);
            }
            if (songs.Count > 0) await SetQueueAndPlayAsync(songs, songs[0]);
        }

        public void Play() { _player.Play(); }
        public void Pause() { _player.Pause(); }
        public void Next() { _playbackList.MoveNext(); }
        public void Previous() { _playbackList.MovePrevious(); }

        public void ToggleShuffle()
        {
            _playbackList.ShuffleEnabled = !_playbackList.ShuffleEnabled;
            _systemControls.ShuffleEnabled = _playbackList.ShuffleEnabled;
            RaiseQueueChanged();
        }

        public RepeatMode CycleRepeatMode()
        {
            if (RepeatMode == RepeatMode.Off) RepeatMode = RepeatMode.All;
            else if (RepeatMode == RepeatMode.All) RepeatMode = RepeatMode.One;
            else RepeatMode = RepeatMode.Off;

            _playbackList.AutoRepeatEnabled = RepeatMode != RepeatMode.Off;
            _systemControls.AutoRepeatMode = RepeatMode == RepeatMode.One
                ? MediaPlaybackAutoRepeatMode.Track
                : RepeatMode == RepeatMode.All ? MediaPlaybackAutoRepeatMode.List : MediaPlaybackAutoRepeatMode.None;
            RaiseQueueChanged();
            return RepeatMode;
        }

        public void AddToQueue(Song song)
        {
            if (song == null || song.File == null) return;
            bool wasEmpty = _queue.Count == 0;
            _queue.Add(song);
            _playbackList.Items.Add(CreatePlaybackItem(song));
            if (wasEmpty) _playbackList.MoveTo(0);
            RaiseQueueChanged();
        }

        public void PlayQueueSong(Song song)
        {
            int index = song == null ? -1 : _queue.IndexOf(song);
            if (index < 0 || index >= _playbackList.Items.Count) return;
            _playbackList.MoveTo((uint)index);
            _player.Play();
        }

        public void PlayNext(Song song) { AddToQueue(song); }

        public void RemoveAt(int index)
        {
            if (index < 0 || index >= _queue.Count) return;
            _queue.RemoveAt(index);
            _playbackList.Items.RemoveAt(index);
            if (_queue.Count == 0)
            {
                _player.Pause();
                ApplyCurrentSong(-1);
            }
            RaiseQueueChanged();
        }

        public void Remove(Song song)
        {
            if (song == null) return;
            RemoveAt(_queue.IndexOf(song));
        }

        public async Task SaveStateAsync()
        {
            try
            {
                await PlaybackStateStore.SaveAsync(new SavedPlaybackState
                {
                    QueuePaths = _queue.Where(song => !string.IsNullOrWhiteSpace(song.Path)).Select(song => song.Path).ToList(),
                    CurrentPath = CurrentSong == null ? null : CurrentSong.Path,
                    PositionTicks = _player.PlaybackSession.Position.Ticks,
                    ShuffleEnabled = ShuffleEnabled,
                    RepeatMode = (int)RepeatMode
                });
            }
            catch { }
        }

        public async Task RestoreStateAsync(IEnumerable<Song> library)
        {
            if (_stateRestoreAttempted || _queue.Count > 0) return;
            _stateRestoreAttempted = true;
            SavedPlaybackState state = await PlaybackStateStore.LoadAsync();
            if (state == null || state.QueuePaths == null || state.QueuePaths.Count == 0) return;

            Dictionary<string, Song> byPath = library.Where(song => !string.IsNullOrWhiteSpace(song.Path))
                .GroupBy(song => song.Path, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            List<Song> restored = state.QueuePaths.Where(byPath.ContainsKey).Select(path => byPath[path]).ToList();
            if (restored.Count == 0) return;

            _playbackList.Items.Clear();
            _queue.Clear();
            foreach (Song song in restored)
            {
                _queue.Add(song);
                _playbackList.Items.Add(CreatePlaybackItem(song));
            }

            int index = restored.FindIndex(song => string.Equals(song.Path, state.CurrentPath, StringComparison.OrdinalIgnoreCase));
            _playbackList.MoveTo((uint)Math.Max(0, index));
            _playbackList.ShuffleEnabled = state.ShuffleEnabled;
            _systemControls.ShuffleEnabled = state.ShuffleEnabled;
            RepeatMode restoredRepeat = Enum.IsDefined(typeof(RepeatMode), state.RepeatMode) ? (RepeatMode)state.RepeatMode : RepeatMode.Off;
            SetRepeatMode(restoredRepeat);
            RaiseQueueChanged();

            await Task.Delay(300);
            if (state.PositionTicks > 0) _player.PlaybackSession.Position = TimeSpan.FromTicks(state.PositionTicks);
        }

        private void SetRepeatMode(RepeatMode mode)
        {
            RepeatMode = mode;
            _playbackList.AutoRepeatEnabled = mode != RepeatMode.Off;
            _systemControls.AutoRepeatMode = mode == RepeatMode.One
                ? MediaPlaybackAutoRepeatMode.Track
                : mode == RepeatMode.All ? MediaPlaybackAutoRepeatMode.List : MediaPlaybackAutoRepeatMode.None;
            RaiseQueueChanged();
        }

        private async void SystemControls_ButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
        {
            await _uiDispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (args.Button == SystemMediaTransportControlsButton.Play) Play();
                else if (args.Button == SystemMediaTransportControlsButton.Pause) Pause();
                else if (args.Button == SystemMediaTransportControlsButton.Next) Next();
                else if (args.Button == SystemMediaTransportControlsButton.Previous) Previous();
            });
        }

        private async void SystemControls_ShuffleEnabledChangeRequested(SystemMediaTransportControls sender, ShuffleEnabledChangeRequestedEventArgs args)
        {
            await _uiDispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                _playbackList.ShuffleEnabled = args.RequestedShuffleEnabled;
                _systemControls.ShuffleEnabled = _playbackList.ShuffleEnabled;
                RaiseQueueChanged();
            });
        }

        private async void SystemControls_AutoRepeatModeChangeRequested(SystemMediaTransportControls sender, AutoRepeatModeChangeRequestedEventArgs args)
        {
            await _uiDispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                SetRepeatMode(args.RequestedAutoRepeatMode == MediaPlaybackAutoRepeatMode.Track
                    ? RepeatMode.One
                    : args.RequestedAutoRepeatMode == MediaPlaybackAutoRepeatMode.List ? RepeatMode.All : RepeatMode.Off));
        }

        public void Move(int oldIndex, int newIndex)
        {
            if (oldIndex < 0 || oldIndex >= _queue.Count || newIndex < 0 || newIndex >= _queue.Count || oldIndex == newIndex)
                return;

            Song song = _queue[oldIndex];
            MediaPlaybackItem item = _playbackList.Items[oldIndex];
            _queue.RemoveAt(oldIndex);
            _playbackList.Items.RemoveAt(oldIndex);
            _queue.Insert(newIndex, song);
            _playbackList.Items.Insert(newIndex, item);
            RaiseQueueChanged();
        }

        public void SynchronizeQueueMove(int oldIndex, int newIndex)
        {
            if (oldIndex < 0 || oldIndex >= _playbackList.Items.Count || newIndex < 0 || newIndex >= _playbackList.Items.Count || oldIndex == newIndex)
                return;

            Song current = CurrentSong;
            TimeSpan position = _player.PlaybackSession.Position;
            bool wasPlaying = _player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;
            _synchronizingQueue = true;
            MediaPlaybackItem item = _playbackList.Items[oldIndex];
            _playbackList.Items.RemoveAt(oldIndex);
            _playbackList.Items.Insert(newIndex, item);
            int currentIndex = current == null ? -1 : _queue.IndexOf(current);
            if (currentIndex >= 0) _playbackList.MoveTo((uint)currentIndex);
            _synchronizingQueue = false;
            ApplyCurrentSong(currentIndex);
            if (position > TimeSpan.Zero) _player.PlaybackSession.Position = position;
            if (wasPlaying) _player.Play();
            RaiseQueueChanged();
        }

        private static MediaPlaybackItem CreatePlaybackItem(Song song)
        {
            MediaPlaybackItem item = new MediaPlaybackItem(MediaSource.CreateFromStorageFile(song.File));
            MediaItemDisplayProperties display = item.GetDisplayProperties();
            display.Type = Windows.Media.MediaPlaybackType.Music;
            display.MusicProperties.Title = song.DisplayTitle;
            display.MusicProperties.Artist = song.DisplayArtist;
            display.MusicProperties.AlbumTitle = song.DisplayAlbum;
            item.ApplyDisplayProperties(display);
            return item;
        }

        private int IndexOf(MediaPlaybackItem item)
        {
            if (item == null) return -1;
            for (int i = 0; i < _playbackList.Items.Count; i++)
                if (ReferenceEquals(_playbackList.Items[i], item)) return i;
            return -1;
        }

        private async void PlaybackList_CurrentItemChanged(MediaPlaybackList sender, CurrentMediaPlaybackItemChangedEventArgs args)
        {
            if (_synchronizingQueue) return;
            if (RepeatMode == RepeatMode.One && args.Reason == MediaPlaybackItemChangedReason.EndOfStream && args.OldItem != null)
            {
                int repeatedIndex = IndexOf(args.OldItem);
                if (repeatedIndex >= 0)
                {
                    _playbackList.MoveTo((uint)repeatedIndex);
                    _player.Play();
                    return;
                }
            }

            int index = IndexOf(args.NewItem);
            if (_uiDispatcher.HasThreadAccess)
                ApplyCurrentSong(index);
            else
                await _uiDispatcher.RunAsync(CoreDispatcherPriority.Normal, () => ApplyCurrentSong(index));
        }

        private void ApplyCurrentSong(int index)
        {
            foreach (Song song in _queue) song.IsPlaying = false;
            CurrentSong = index >= 0 && index < _queue.Count ? _queue[index] : null;
            if (CurrentSong != null) CurrentSong.IsPlaying = true;
            UpdateSystemDisplay();
            EventHandler handler = CurrentSongChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void PlaybackSession_PlaybackStateChanged(MediaPlaybackSession sender, object args)
        {
            if (sender.PlaybackState == MediaPlaybackState.Playing) _systemControls.PlaybackStatus = MediaPlaybackStatus.Playing;
            else if (sender.PlaybackState == MediaPlaybackState.Paused) _systemControls.PlaybackStatus = MediaPlaybackStatus.Paused;
            else if (sender.PlaybackState == MediaPlaybackState.None) _systemControls.PlaybackStatus = MediaPlaybackStatus.Closed;
            else _systemControls.PlaybackStatus = MediaPlaybackStatus.Changing;
            EventHandler handler = PlaybackStateChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void RaiseQueueChanged()
        {
            EventHandler handler = QueueChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void UpdateSystemDisplay()
        {
            if (CurrentSong == null) return;
            _systemControls.DisplayUpdater.Type = MediaPlaybackType.Music;
            _systemControls.DisplayUpdater.MusicProperties.Title = CurrentSong.DisplayTitle;
            _systemControls.DisplayUpdater.MusicProperties.Artist = CurrentSong.DisplayArtist;
            _systemControls.DisplayUpdater.MusicProperties.AlbumTitle = CurrentSong.DisplayAlbum;
            _systemControls.DisplayUpdater.Update();
        }
    }
}
