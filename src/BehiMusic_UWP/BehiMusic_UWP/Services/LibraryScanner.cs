using BehiMusic_UWP.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Search;

namespace BehiMusic_UWP.Services
{
    public sealed class LibraryScanner
    {
        public int LastCacheHitCount { get; private set; }

        public async Task<IReadOnlyList<Song>> ScanAsync(IProgress<int> progress, CancellationToken cancellationToken, bool validateCache = false)
        {
            List<StorageFile> files = new List<StorageFile>();
            await AddFilesAsync(KnownFolders.MusicLibrary, files, cancellationToken);

            try
            {
                IReadOnlyList<StorageFolder> roots = await KnownFolders.RemovableDevices.GetFoldersAsync();
                foreach (StorageFolder root in roots)
                    await AddFilesAsync(root, files, cancellationToken);
            }
            catch (UnauthorizedAccessException) { }

            List<StorageFile> unique = files
                .GroupBy(file => string.IsNullOrWhiteSpace(file.Path) ? file.Name : file.Path, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            MetadataCache cache = await MetadataCache.LoadAsync();
            List<MetadataCacheEntry> updatedCache = new List<MetadataCacheEntry>(unique.Count);
            List<Song> songs = new List<Song>(unique.Count);
            LastCacheHitCount = 0;
            for (int i = 0; i < unique.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                BasicProperties basicProperties = null;
                MetadataCacheEntry cachedEntry = null;
                Song song;
                if (!validateCache && cache.TryRestoreWithoutValidation(unique[i], out song, out cachedEntry))
                    LastCacheHitCount++;
                else
                {
                    basicProperties = await unique[i].GetBasicPropertiesAsync();
                    if (cache.TryRestore(unique[i], basicProperties, out song))
                        LastCacheHitCount++;
                    else
                        song = await ReadSongAsync(unique[i]);
                }

                if (song != null)
                {
                    songs.Add(song);
                    updatedCache.Add(cachedEntry ?? MetadataCacheEntry.FromSong(song, basicProperties));
                }
                if (progress != null) progress.Report(unique.Count == 0 ? 100 : (i + 1) * 100 / unique.Count);
            }

            cancellationToken.ThrowIfCancellationRequested();
            await cache.SaveAsync(updatedCache);
            return songs.OrderBy(song => song.DisplayTitle, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private static async Task AddFilesAsync(StorageFolder folder, IList<StorageFile> destination, CancellationToken cancellationToken)
        {
            try
            {
                QueryOptions options = new QueryOptions(CommonFileQuery.OrderByName, new[] { ".mp3" });
                options.FolderDepth = FolderDepth.Deep;
                options.IndexerOption = IndexerOption.UseIndexerWhenAvailable;
                StorageFileQueryResult query = folder.CreateFileQueryWithOptions(options);
                IReadOnlyList<StorageFile> files = await query.GetFilesAsync();
                foreach (StorageFile file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    destination.Add(file);
                }
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException || exception is FileNotFoundException)
            {
            }
        }

        public static async Task<Song> ReadSongAsync(StorageFile file)
        {
            try
            {
                MusicProperties properties = await file.Properties.GetMusicPropertiesAsync();
                string folder = string.Empty;
                try { folder = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(file.Path)); }
                catch (ArgumentException) { }

                Song song = new Song
                {
                    File = file,
                    Path = file.Path,
                    FileName = file.Name,
                    Title = properties.Title,
                    Artist = properties.Artist,
                    AlbumArtist = properties.AlbumArtist,
                    Album = properties.Album,
                    Genre = properties.Genre == null ? string.Empty : string.Join(", ", properties.Genre),
                    Folder = folder,
                    ReleaseType = await Id3ReleaseTypeReader.ReadAsync(file),
                    TrackNumber = properties.TrackNumber,
                    Year = properties.Year,
                    Bitrate = properties.Bitrate,
                    Duration = properties.Duration,
                    DateAdded = file.DateCreated
                };
                MetadataCache.BuildSearchIndex(song);
                return song;
            }
            catch
            {
                return null;
            }
        }
    }
}
