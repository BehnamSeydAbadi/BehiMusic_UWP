using BehiMusic_UWP.Models;
using BehiMusic_UWP.Services;
using BehiMusic_UWP.ViewModels;
using System;
using System.Collections.Generic;
using Windows.Media.Playback;
using Windows.Foundation;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;

namespace BehiMusic_UWP
{
    public sealed partial class MainPage : Page
    {
        private readonly DispatcherTimer _positionTimer = new DispatcherTimer();
        private readonly DispatcherTimer _toastTimer = new DispatcherTimer();
        private bool _updatingPosition;
        private Song _draggedQueueSong;
        private int _draggedQueueOldIndex = -1;
        private Storyboard _nowPlayingStoryboard;
        private bool _draggingNowPlaying;
        private readonly Stack<int> _pivotHistory = new Stack<int>();
        private int _lastPivotIndex = -1;
        private bool _restoringPivotState;
        private Song _chosenSearchSuggestion;
        public MainViewModel ViewModel { get; } = new MainViewModel();
        public PlaybackService Playback { get { return PlaybackService.Current; } }
        public object Queue { get { return Playback.Queue; } }
        public Song CurrentSong { get { return Playback.CurrentSong; } }

        public MainPage()
        {
            InitializeComponent();
            DataContext = this;
            Loaded += MainPage_Loaded;
            SizeChanged += MainPage_SizeChanged;
            Playback.CurrentSongChanged += Playback_CurrentSongChanged;
            Playback.PlaybackStateChanged += Playback_PlaybackStateChanged;
            Playback.QueueChanged += Playback_QueueChanged;
            _positionTimer.Interval = TimeSpan.FromMilliseconds(500);
            _positionTimer.Tick += PositionTimer_Tick;
            _positionTimer.Start();
            _toastTimer.Interval = TimeSpan.FromSeconds(2);
            _toastTimer.Tick += ToastTimer_Tick;
            SystemNavigationManager.GetForCurrentView().BackRequested += MainPage_BackRequested;
            SongsList.ContainerContentChanging += SongList_ContainerContentChanging;
            RecentSongsList.ContainerContentChanging += SongList_ContainerContentChanging;
            QueueList.ContainerContentChanging += SongList_ContainerContentChanging;
            HomeAlbumsGrid.ContainerContentChanging += GroupGrid_ContainerContentChanging;
            AlbumsGrid.ContainerContentChanging += GroupGrid_ContainerContentChanging;
            ArtistsGrid.ContainerContentChanging += GroupGrid_ContainerContentChanging;
            AlbumArtistsGrid.ContainerContentChanging += GroupGrid_ContainerContentChanging;
            GenresGrid.ContainerContentChanging += GroupGrid_ContainerContentChanging;
            FoldersGrid.ContainerContentChanging += GroupGrid_ContainerContentChanging;
            ReleaseTypesGrid.ContainerContentChanging += GroupGrid_ContainerContentChanging;
        }

        private async void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (ViewModel.Library.Count == 0) await ViewModel.ScanAsync();
            await Playback.RestoreStateAsync(ViewModel.Library);
            UpdatePlaybackAccents();
        }

        private async void Scan_Click(object sender, RoutedEventArgs e) { await ViewModel.ScanAsync(true); }

        private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
            sender.ItemsSource = ViewModel.GetSuggestions(sender.Text);
            ViewModel.SetQuery(sender.Text, false);
        }

        private async void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            Song chosenSong = args.ChosenSuggestion as Song;
            string query = args.ChosenSuggestion as string ?? args.QueryText;
            ViewModel.SetQuery(query, true);
            if (chosenSong != null && chosenSong != _chosenSearchSuggestion)
                await Playback.SetQueueAndPlayAsync(ViewModel.Songs, chosenSong);
            _chosenSearchSuggestion = null;
        }

        private async void SearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            Song song = args.SelectedItem as Song;
            if (song == null) return;

            _chosenSearchSuggestion = song;
            sender.Text = song.DisplayTitle;
            ViewModel.SetQuery(song.DisplayTitle, true);
            await Playback.SetQueueAndPlayAsync(ViewModel.Songs, song);
        }

        private async void SongsList_ItemClick(object sender, ItemClickEventArgs e)
        {
            Song song = e.ClickedItem as Song;
            if (song != null) await Playback.SetQueueAndPlayAsync(ViewModel.Songs, song);
        }

        private void PlayNext_Click(object sender, RoutedEventArgs e)
        {
            FrameworkElement element = sender as FrameworkElement;
            Song song = element == null ? null : element.DataContext as Song;
            if (song != null)
            {
                Playback.AddToQueue(song);
                ShowToast("Added to queue");
            }
        }

        private void Group_ItemClick(object sender, ItemClickEventArgs e)
        {
            LibraryGroup group = e.ClickedItem as LibraryGroup;
            if (group == null) return;
            SearchBox.Text = group.Name;
            ViewModel.SetQuery(group.Name, true);
            LibraryPivot.SelectedIndex = 1;
        }

        private async void SongList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue) return;
            try { await ArtworkService.LoadAsync(args.Item as Song); }
            catch { }
        }

        private async void GroupGrid_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue) return;
            try { await ArtworkService.LoadAsync(args.Item as LibraryGroup); }
            catch { }
        }

        private void SortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBoxItem item = SortCombo.SelectedItem as ComboBoxItem;
            if (item != null) ViewModel.SortField = item.Content.ToString();
        }

        private void SortDirection_Changed(object sender, RoutedEventArgs e)
        {
            bool ascending = SortDirectionButton.IsChecked == true;
            SortDirectionButton.Content = ascending ? "Ascending" : "Descending";
            ViewModel.SortAscending = ascending;
        }

        private void GridDensity_SelectionChanged(object sender, SelectionChangedEventArgs e) { UpdateGridWidths(); }
        private void GroupGrid_Loaded(object sender, RoutedEventArgs e) { UpdateGridWidth(sender as GridView); }
        private void MainPage_SizeChanged(object sender, SizeChangedEventArgs e) { UpdateGridWidths(); }

        private void UpdateGridWidths()
        {
            UpdateGridWidth(HomeAlbumsGrid); UpdateGridWidth(AlbumsGrid); UpdateGridWidth(ArtistsGrid); UpdateGridWidth(AlbumArtistsGrid);
            UpdateGridWidth(GenresGrid); UpdateGridWidth(FoldersGrid); UpdateGridWidth(ReleaseTypesGrid);
        }

        private void UpdateGridWidth(GridView grid)
        {
            if (grid == null || grid.ItemsPanelRoot == null) return;
            ItemsWrapGrid panel = grid.ItemsPanelRoot as ItemsWrapGrid;
            if (panel == null) return;
            ComboBoxItem item = GridDensityCombo == null ? null : GridDensityCombo.SelectedItem as ComboBoxItem;
            int density = 3;
            if (grid != HomeAlbumsGrid && grid != AlbumsGrid && item != null)
                int.TryParse(item.Content.ToString(), out density);
            if (density < 1) density = 3;
            double availableWidth = grid.ActualWidth > 0 ? grid.ActualWidth - 4 : ActualWidth - 36;
            panel.ItemWidth = Math.Max(96, Math.Floor(availableWidth / density));
            double coverSize = Math.Max(72, panel.ItemWidth - 12);
            foreach (LibraryGroup group in grid.Items) group.CoverSize = coverSize;
        }

        private void PlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (Playback.Player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing) Playback.Pause();
            else Playback.Play();
        }

        private void Previous_Click(object sender, RoutedEventArgs e) { Playback.Previous(); }
        private void Next_Click(object sender, RoutedEventArgs e) { Playback.Next(); }
        private void Shuffle_Click(object sender, RoutedEventArgs e) { Playback.ToggleShuffle(); UpdatePlaybackAccents(); }
        private void Repeat_Click(object sender, RoutedEventArgs e) { Playback.CycleRepeatMode(); UpdatePlaybackAccents(); }
        private async void OpenQueue_Click(object sender, RoutedEventArgs e)
        {
            NowPlayingOverlay.Visibility = Visibility.Collapsed;
            QueueSplitView.IsPaneOpen = true;
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, FocusCurrentQueueSong);
        }

        private void CloseQueue_Click(object sender, RoutedEventArgs e)
        {
            QueueSplitView.IsPaneOpen = false;
            NowPlayingOverlay.Visibility = Visibility.Visible;
        }

        private void QueueSplitView_PaneClosed(SplitView sender, object args)
        {
            NowPlayingOverlay.Visibility = Visibility.Visible;
        }

        private void Navigation_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            int index;
            if (button != null && int.TryParse(Convert.ToString(button.Tag), out index)) LibraryPivot.SelectedIndex = index;
        }

        private void LibraryPivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int currentIndex = LibraryPivot.SelectedIndex;
            if (_lastPivotIndex < 0)
                _lastPivotIndex = currentIndex;
            else if (currentIndex != _lastPivotIndex)
            {
                if (!_restoringPivotState && (_pivotHistory.Count == 0 || _pivotHistory.Peek() != _lastPivotIndex))
                    _pivotHistory.Push(_lastPivotIndex);
                _lastPivotIndex = currentIndex;
            }

            Button[] buttons = { HomeNavButton, SongsNavButton, AlbumsNavButton, ArtistsNavButton, MoreNavButton };
            for (int i = 0; i < buttons.Length; i++) if (buttons[i] != null) buttons[i].Opacity = i == LibraryPivot.SelectedIndex ? 1 : 0.52;
        }

        private async void QuickPick_Click(object sender, RoutedEventArgs e)
        {
            FrameworkElement element = sender as FrameworkElement;
            string action = element == null ? null : Convert.ToString(element.Tag);
            if (action == "Songs") LibraryPivot.SelectedIndex = 1;
            else if (action == "Albums") LibraryPivot.SelectedIndex = 2;
            else if (action == "Folders") LibraryPivot.SelectedIndex = 4;
            else if (action == "Shuffle" && ViewModel.Songs.Count > 0)
            {
                await Playback.SetQueueAndPlayAsync(ViewModel.Songs, ViewModel.Songs[0]);
                if (!Playback.ShuffleEnabled) Playback.ToggleShuffle();
                Playback.Next();
                UpdatePlaybackAccents();
            }
        }

        private void OpenNowPlaying_Click(object sender, RoutedEventArgs e) { ShowNowPlaying(); }
        private void CloseNowPlaying_Click(object sender, RoutedEventArgs e) { HideNowPlaying(); }

        private void MiniPlayerBar_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (HasInteractiveParent(e.OriginalSource as DependencyObject, MiniPlayerBar)) return;
            ShowNowPlaying();
            e.Handled = true;
        }

        private static bool HasInteractiveParent(DependencyObject source, DependencyObject boundary)
        {
            DependencyObject current = source;
            while (current != null && current != boundary)
            {
                if (current is ButtonBase || current is Slider) return true;
                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }

        private void NowPlayingOverlay_ManipulationStarted(object sender, ManipulationStartedRoutedEventArgs e)
        {
            if (HasInteractiveParent(e.OriginalSource as DependencyObject, NowPlayingOverlay))
            {
                _draggingNowPlaying = false;
                return;
            }

            StopNowPlayingAnimation();
            _draggingNowPlaying = true;
            e.Handled = true;
        }

        private void NowPlayingOverlay_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
        {
            if (!_draggingNowPlaying) return;
            NowPlayingTransform.Y = Math.Max(0, e.Cumulative.Translation.Y);
            e.Handled = true;
        }

        private void NowPlayingOverlay_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
        {
            if (!_draggingNowPlaying) return;
            _draggingNowPlaying = false;
            double dismissDistance = Math.Min(150, Math.Max(90, RootLayout.ActualHeight * 0.15));
            bool fastDownwardFlick = e.Velocities.Linear.Y > 0.45;
            if (NowPlayingTransform.Y >= dismissDistance || fastDownwardFlick)
                HideNowPlaying();
            else
                RestoreNowPlayingPosition();
            e.Handled = true;
        }

        private void RestoreNowPlayingPosition()
        {
            StopNowPlayingAnimation();
            _nowPlayingStoryboard = CreateNowPlayingAnimation(NowPlayingTransform.Y, 0, 180, EasingMode.EaseOut);
            Storyboard restoreStoryboard = _nowPlayingStoryboard;
            restoreStoryboard.Completed += (animation, args) =>
            {
                if (_nowPlayingStoryboard != restoreStoryboard) return;
                restoreStoryboard.Stop();
                NowPlayingTransform.Y = 0;
                _nowPlayingStoryboard = null;
            };
            restoreStoryboard.Begin();
        }

        private void ShowNowPlaying()
        {
            StopNowPlayingAnimation();
            double sourceY = GetMiniPlayerTop();
            NowPlayingTransform.Y = sourceY;
            NowPlayingOverlay.Opacity = 1;
            NowPlayingOverlay.IsHitTestVisible = true;
            NowPlayingOverlay.Visibility = Visibility.Visible;

            _nowPlayingStoryboard = CreateNowPlayingAnimation(sourceY, 0, 300, EasingMode.EaseOut);
            Storyboard openingStoryboard = _nowPlayingStoryboard;
            openingStoryboard.Completed += (animation, args) =>
            {
                if (_nowPlayingStoryboard != openingStoryboard) return;
                openingStoryboard.Stop();
                NowPlayingTransform.Y = 0;
                _nowPlayingStoryboard = null;
            };
            openingStoryboard.Begin();
        }

        private void HideNowPlaying()
        {
            if (NowPlayingOverlay.Visibility != Visibility.Visible) return;
            StopNowPlayingAnimation();
            NowPlayingOverlay.IsHitTestVisible = false;
            _nowPlayingStoryboard = CreateNowPlayingAnimation(NowPlayingTransform.Y, GetMiniPlayerTop(), 250, EasingMode.EaseIn);
            Storyboard closingStoryboard = _nowPlayingStoryboard;
            closingStoryboard.Completed += (animation, args) =>
            {
                if (_nowPlayingStoryboard != closingStoryboard) return;
                closingStoryboard.Stop();
                NowPlayingOverlay.Visibility = Visibility.Collapsed;
                NowPlayingOverlay.IsHitTestVisible = true;
                NowPlayingTransform.Y = 0;
                _nowPlayingStoryboard = null;
            };
            closingStoryboard.Begin();
        }

        private Storyboard CreateNowPlayingAnimation(double from, double to, int durationMilliseconds, EasingMode easingMode)
        {
            DoubleAnimation slide = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = TimeSpan.FromMilliseconds(durationMilliseconds),
                EasingFunction = new CubicEase { EasingMode = easingMode },
                EnableDependentAnimation = true
            };
            Storyboard.SetTarget(slide, NowPlayingTransform);
            Storyboard.SetTargetProperty(slide, "Y");
            Storyboard storyboard = new Storyboard();
            storyboard.Children.Add(slide);
            return storyboard;
        }

        private double GetMiniPlayerTop()
        {
            try
            {
                Point topLeft = MiniPlayerBar.TransformToVisual(RootLayout).TransformPoint(new Point());
                if (topLeft.Y > 0) return topLeft.Y;
            }
            catch { }
            return Math.Max(120, RootLayout.ActualHeight - 140);
        }

        private void StopNowPlayingAnimation()
        {
            if (_nowPlayingStoryboard == null) return;
            double currentY = NowPlayingTransform.Y;
            _nowPlayingStoryboard.Stop();
            NowPlayingTransform.Y = currentY;
            _nowPlayingStoryboard = null;
        }

        private void MainPage_BackRequested(object sender, BackRequestedEventArgs e)
        {
            if (NowPlayingOverlay.Visibility == Visibility.Visible)
            {
                HideNowPlaying();
                e.Handled = true;
                return;
            }

            if (QueueSplitView.IsPaneOpen)
            {
                QueueSplitView.IsPaneOpen = false;
                NowPlayingOverlay.Visibility = Visibility.Visible;
                e.Handled = true;
                return;
            }

            if (!string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                SearchBox.Text = string.Empty;
                ViewModel.SetQuery(string.Empty, false);
                e.Handled = true;
                return;
            }

            if (_pivotHistory.Count > 0)
            {
                RestorePivotState(_pivotHistory.Pop());
                e.Handled = true;
                return;
            }

            if (LibraryPivot.SelectedIndex != 0)
            {
                RestorePivotState(0);
                e.Handled = true;
            }
        }

        private void RestorePivotState(int index)
        {
            _restoringPivotState = true;
            LibraryPivot.SelectedIndex = index;
            _restoringPivotState = false;
        }

        private void PositionTimer_Tick(object sender, object e)
        {
            TimeSpan duration = Playback.Player.PlaybackSession.NaturalDuration;
            TimeSpan position = Playback.Player.PlaybackSession.Position;
            _updatingPosition = true;
            PositionSlider.Maximum = Math.Max(1, duration.TotalSeconds);
            PositionSlider.Value = Math.Min(PositionSlider.Maximum, Math.Max(0, position.TotalSeconds));
            PositionText.Text = FormatTime(position);
            DurationText.Text = FormatTime(duration);
            _updatingPosition = false;
        }

        private void PositionSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (!_updatingPosition && Playback.CurrentSong != null)
                Playback.Player.PlaybackSession.Position = TimeSpan.FromSeconds(e.NewValue);
        }

        private static string FormatTime(TimeSpan value)
        {
            if (value.TotalHours >= 1) return value.ToString(@"h\:mm\:ss");
            return value.ToString(@"m\:ss");
        }

        private void UpdatePlaybackAccents()
        {
            Brush accent = (Brush)Application.Current.Resources["BehiPrimaryBrush"];
            Brush normal = (Brush)Application.Current.Resources["BehiTextBrush"];
            ShuffleIcon.Foreground = Playback.ShuffleEnabled ? accent : normal;
            RepeatIcon.Foreground = Playback.RepeatMode == RepeatMode.Off ? normal : accent;
            RepeatIcon.Glyph = Playback.RepeatMode == RepeatMode.One ? "\uE8ED" : "\uE8EE";
        }

        private void QueueList_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        {
            _draggedQueueSong = e.Items.Count == 0 ? null : e.Items[0] as Song;
            _draggedQueueOldIndex = _draggedQueueSong == null ? -1 : Playback.Queue.IndexOf(_draggedQueueSong);
        }

        private void QueueList_ItemClick(object sender, ItemClickEventArgs e)
        {
            Song song = e.ClickedItem as Song;
            if (song != null) Playback.PlayQueueSong(song);
        }

        private void QueueList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            int newIndex = _draggedQueueSong == null ? -1 : Playback.Queue.IndexOf(_draggedQueueSong);
            if (_draggedQueueOldIndex >= 0 && newIndex >= 0 && _draggedQueueOldIndex != newIndex)
                Playback.SynchronizeQueueMove(_draggedQueueOldIndex, newIndex);
            _draggedQueueSong = null;
            _draggedQueueOldIndex = -1;
            FocusCurrentQueueSong();
        }

        private void QueueItemRemove_Click(object sender, RoutedEventArgs e)
        {
            FrameworkElement element = sender as FrameworkElement;
            Song song = element == null ? null : element.DataContext as Song;
            if (song != null) Playback.Remove(song);
        }

        private void FocusCurrentQueueSong()
        {
            Song current = Playback.CurrentSong;
            if (current == null) return;
            QueueList.SelectedItem = current;
            QueueList.ScrollIntoView(current, ScrollIntoViewAlignment.Leading);
            QueueList.UpdateLayout();
            ListViewItem container = QueueList.ContainerFromItem(current) as ListViewItem;
            if (container != null) container.Focus(FocusState.Programmatic);
        }

        private void ShowToast(string message)
        {
            ActionToastText.Text = message;
            ActionToast.Visibility = Visibility.Visible;
            _toastTimer.Stop();
            _toastTimer.Start();
        }

        private void ToastTimer_Tick(object sender, object e)
        {
            _toastTimer.Stop();
            ActionToast.Visibility = Visibility.Collapsed;
        }

        private async void Playback_CurrentSongChanged(object sender, EventArgs e)
        {
            try { await ArtworkService.LoadAsync(Playback.CurrentSong); }
            catch { }
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                DataContext = null;
                DataContext = this;
                PositionTimer_Tick(null, null);
                if (QueueSplitView.IsPaneOpen) FocusCurrentQueueSong();
            });
        }

        private async void Playback_PlaybackStateChanged(object sender, EventArgs e)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                string glyph = Playback.Player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing ? "\uE769" : "\uE768";
                MiniPlayPauseIcon.Glyph = glyph;
                LargePlayPauseIcon.Glyph = glyph;
            });
        }

        private async void Playback_QueueChanged(object sender, EventArgs e)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => QueueList.ItemsSource = Playback.Queue);
        }
    }
}
