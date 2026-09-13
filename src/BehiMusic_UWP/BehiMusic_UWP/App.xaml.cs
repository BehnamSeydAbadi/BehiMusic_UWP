using BehiMusic_UWP.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace BehiMusic_UWP
{
    sealed partial class App : Application
    {
        public App()
        {
            InitializeComponent();
            Suspending += OnSuspending;
        }

        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            Frame rootFrame = EnsureRootFrame();
            if (!e.PrelaunchActivated)
            {
                if (rootFrame.Content == null) rootFrame.Navigate(typeof(MainPage), e.Arguments);
                Window.Current.Activate();
            }
        }

        protected override async void OnFileActivated(FileActivatedEventArgs args)
        {
            Frame rootFrame = EnsureRootFrame();
            if (rootFrame.Content == null) rootFrame.Navigate(typeof(MainPage));
            Window.Current.Activate();

            IEnumerable<StorageFile> mp3Files = args.Files.OfType<StorageFile>()
                .Where(file => string.Equals(file.FileType, ".mp3", StringComparison.OrdinalIgnoreCase));
            await PlaybackService.Current.PlayFilesAsync(mp3Files);
        }

        private Frame EnsureRootFrame()
        {
            Frame rootFrame = Window.Current.Content as Frame;
            if (rootFrame != null) return rootFrame;

            rootFrame = new Frame();
            rootFrame.NavigationFailed += OnNavigationFailed;
            Window.Current.Content = rootFrame;
            return rootFrame;
        }

        private void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }

        private async void OnSuspending(object sender, SuspendingEventArgs e)
        {
            SuspendingDeferral deferral = e.SuspendingOperation.GetDeferral();
            await PlaybackService.Current.SaveStateAsync();
            deferral.Complete();
        }
    }
}
