using BehiMusic_UWP.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace BehiMusic_UWP.Services
{
    [DataContract]
    internal sealed class MetadataCacheFile
    {
        [DataMember(Order = 1)]
        public int Version { get; set; }

        [DataMember(Order = 2)]
        public List<MetadataCacheEntry> Entries { get; set; }
    }

    [DataContract]
    internal sealed class MetadataCacheEntry
    {
        [DataMember(Order = 1)] public string Path { get; set; }
        [DataMember(Order = 2)] public ulong Size { get; set; }
        [DataMember(Order = 3)] public long ModifiedUtcTicks { get; set; }
        [DataMember(Order = 4)] public string FileName { get; set; }
        [DataMember(Order = 5)] public string Title { get; set; }
        [DataMember(Order = 6)] public string Artist { get; set; }
        [DataMember(Order = 7)] public string AlbumArtist { get; set; }
        [DataMember(Order = 8)] public string Album { get; set; }
        [DataMember(Order = 9)] public string Genre { get; set; }
        [DataMember(Order = 10)] public string Folder { get; set; }
        [DataMember(Order = 11)] public string ReleaseType { get; set; }
        [DataMember(Order = 12)] public uint TrackNumber { get; set; }
        [DataMember(Order = 13)] public uint Year { get; set; }
        [DataMember(Order = 14)] public uint Bitrate { get; set; }
        [DataMember(Order = 15)] public long DurationTicks { get; set; }
        [DataMember(Order = 16)] public long DateAddedUtcTicks { get; set; }

        public bool Matches(BasicProperties properties)
        {
            return properties != null && Size == properties.Size &&
                   ModifiedUtcTicks == properties.DateModified.UtcDateTime.Ticks;
        }

        public Song ToSong(StorageFile file)
        {
            Song song = new Song
            {
                File = file,
                Path = file.Path,
                FileName = FileName,
                Title = Title,
                Artist = Artist,
                AlbumArtist = AlbumArtist,
                Album = Album,
                Genre = Genre,
                Folder = Folder,
                ReleaseType = ReleaseType,
                TrackNumber = TrackNumber,
                Year = Year,
                Bitrate = Bitrate,
                Duration = TimeSpan.FromTicks(DurationTicks),
                DateAdded = new DateTimeOffset(DateAddedUtcTicks, TimeSpan.Zero)
            };
            MetadataCache.BuildSearchIndex(song);
            return song;
        }

        public static MetadataCacheEntry FromSong(Song song, BasicProperties properties)
        {
            return new MetadataCacheEntry
            {
                Path = song.Path,
                Size = properties.Size,
                ModifiedUtcTicks = properties.DateModified.UtcDateTime.Ticks,
                FileName = song.FileName,
                Title = song.Title,
                Artist = song.Artist,
                AlbumArtist = song.AlbumArtist,
                Album = song.Album,
                Genre = song.Genre,
                Folder = song.Folder,
                ReleaseType = song.ReleaseType,
                TrackNumber = song.TrackNumber,
                Year = song.Year,
                Bitrate = song.Bitrate,
                DurationTicks = song.Duration.Ticks,
                DateAddedUtcTicks = song.DateAdded.UtcDateTime.Ticks
            };
        }
    }

    internal sealed class MetadataCache
    {
        private const int CurrentVersion = 1;
        private const string FileName = "mp3-metadata-cache.json";
        private readonly Dictionary<string, MetadataCacheEntry> _entries;

        private MetadataCache(IEnumerable<MetadataCacheEntry> entries)
        {
            _entries = entries
                .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.Path))
                .GroupBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        }

        public static async Task<MetadataCache> LoadAsync()
        {
            try
            {
                StorageFile file = await ApplicationData.Current.LocalFolder.GetFileAsync(FileName);
                using (Stream stream = await file.OpenStreamForReadAsync())
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(MetadataCacheFile));
                    MetadataCacheFile cacheFile = serializer.ReadObject(stream) as MetadataCacheFile;
                    if (cacheFile == null || cacheFile.Version != CurrentVersion || cacheFile.Entries == null)
                        return new MetadataCache(new MetadataCacheEntry[0]);
                    return new MetadataCache(cacheFile.Entries);
                }
            }
            catch
            {
                // A missing, old, or damaged cache is equivalent to an empty cache.
                return new MetadataCache(new MetadataCacheEntry[0]);
            }
        }

        public bool TryRestore(StorageFile file, BasicProperties properties, out Song song)
        {
            song = null;
            if (file == null || string.IsNullOrWhiteSpace(file.Path)) return false;

            MetadataCacheEntry entry;
            if (!_entries.TryGetValue(file.Path, out entry) || !entry.Matches(properties)) return false;
            song = entry.ToSong(file);
            return true;
        }

        public bool TryRestoreWithoutValidation(StorageFile file, out Song song, out MetadataCacheEntry entry)
        {
            song = null;
            entry = null;
            if (file == null || string.IsNullOrWhiteSpace(file.Path)) return false;
            if (!_entries.TryGetValue(file.Path, out entry)) return false;
            song = entry.ToSong(file);
            return true;
        }

        public async Task SaveAsync(IEnumerable<MetadataCacheEntry> entries)
        {
            MetadataCacheFile cacheFile = new MetadataCacheFile
            {
                Version = CurrentVersion,
                Entries = entries.Where(entry => entry != null).ToList()
            };

            StorageFile file = await ApplicationData.Current.LocalFolder.CreateFileAsync(FileName, CreationCollisionOption.ReplaceExisting);
            using (Stream stream = await file.OpenStreamForWriteAsync())
            {
                stream.SetLength(0);
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(MetadataCacheFile));
                serializer.WriteObject(stream, cacheFile);
                await stream.FlushAsync();
            }
        }

        internal static void BuildSearchIndex(Song song)
        {
            song.NormalizedTitle = SearchService.Normalize(song.DisplayTitle);
            song.SearchIndex = string.Join("\u001e", new[]
            {
                song.DisplayTitle, song.Artist, song.AlbumArtist, song.Album,
                song.Genre, song.Folder, song.ReleaseType, song.FileName
            }.Select(SearchService.Normalize));
        }
    }
}
