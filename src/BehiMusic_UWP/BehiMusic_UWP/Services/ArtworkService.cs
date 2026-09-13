using BehiMusic_UWP.Models;
using System;
using System.Threading.Tasks;
using Windows.Storage.FileProperties;
using Windows.UI.Xaml.Media.Imaging;

namespace BehiMusic_UWP.Services
{
    public static class ArtworkService
    {
        public static async Task LoadAsync(Song song)
        {
            if (song == null || song.File == null || song.ArtworkLoadAttempted) return;
            song.ArtworkLoadAttempted = true;
            using (StorageItemThumbnail thumbnail = await song.File.GetThumbnailAsync(ThumbnailMode.MusicView, 320, ThumbnailOptions.UseCurrentScale))
            {
                if (thumbnail == null || thumbnail.Size == 0) return;
                BitmapImage image = new BitmapImage();
                await image.SetSourceAsync(thumbnail);
                song.Artwork = image;
            }
        }

        public static async Task LoadAsync(LibraryGroup group)
        {
            if (group == null || group.ArtworkLoadAttempted) return;
            group.ArtworkLoadAttempted = true;
            await LoadAsync(group.RepresentativeSong);
            if (group.RepresentativeSong != null) group.Artwork = group.RepresentativeSong.Artwork;
        }
    }
}
