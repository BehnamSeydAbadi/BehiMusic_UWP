using BehiMusic_UWP.Models;
using System;
using System.Threading.Tasks;
using Windows.Data.Xml.Dom;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;
using Windows.UI.Notifications;

namespace BehiMusic_UWP.Services
{
    internal static class LiveTileService
    {
        private static int _imageSlot;

        public static async Task UpdateAsync(Song song)
        {
            if (song == null) return;

            string imageUri = null;
            if (song.File != null)
            {
                try
                {
                    using (StorageItemThumbnail thumbnail = await song.File.GetThumbnailAsync(
                        ThumbnailMode.MusicView, 600, ThumbnailOptions.UseCurrentScale))
                    {
                        if (thumbnail != null && thumbnail.Size > 0)
                        {
                            _imageSlot = (_imageSlot + 1) % 2;
                            string imageName = "live-tile-cover-" + _imageSlot + ".jpg";
                            StorageFile imageFile = await ApplicationData.Current.LocalFolder.CreateFileAsync(
                                imageName, CreationCollisionOption.ReplaceExisting);
                            using (IRandomAccessStream output = await imageFile.OpenAsync(FileAccessMode.ReadWrite))
                            {
                                thumbnail.Seek(0);
                                await RandomAccessStream.CopyAsync(thumbnail, output);
                                await output.FlushAsync();
                            }
                            imageUri = "ms-appdata:///local/" + imageName;
                        }
                    }
                }
                catch
                {
                    imageUri = null;
                }
            }

            string title = EscapeXml(song.DisplayTitle);
            string artist = EscapeXml(song.DisplayArtist);
            string image = imageUri == null ? string.Empty :
                "<image src=\"" + imageUri + "\" placement=\"background\" hint-overlay=\"25\"/>";

            string payload =
                "<tile><visual branding=\"name\">" +
                "<binding template=\"TileMedium\">" + image +
                "<text hint-wrap=\"true\" hint-style=\"caption\">" + title + "</text></binding>" +
                "<binding template=\"TileWide\">" + image +
                "<text hint-style=\"subtitle\">" + title + "</text>" +
                "<text hint-style=\"captionSubtle\">" + artist + "</text></binding>" +
                "<binding template=\"TileLarge\">" + image +
                "<text hint-style=\"subtitle\">" + title + "</text>" +
                "<text hint-style=\"captionSubtle\">" + artist + "</text></binding>" +
                "</visual></tile>";

            XmlDocument document = new XmlDocument();
            document.LoadXml(payload);
            TileUpdateManager.CreateTileUpdaterForApplication().Update(new TileNotification(document));
        }

        private static string EscapeXml(string value)
        {
            return (value ?? string.Empty).Replace("&", "&amp;").Replace("<", "&lt;")
                .Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&apos;");
        }
    }
}
