using BehiMusic_UWP.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace BehiMusic_UWP.Lyrics
{
    public interface ILyricsProvider
    {
        string Name { get; }
        Task<IReadOnlyList<LyricSearchResult>> SearchAsync(Song song, CancellationToken cancellationToken);
        Task<string> DownloadAsync(LyricSearchResult result, CancellationToken cancellationToken);
    }

    public sealed class LyricsService
    {
        public LyricDocument Parse(string text, string extension)
        {
            if (string.Equals(extension, ".ttml", System.StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(text) && text.TrimStart().StartsWith("<tt")))
                return TtmlParser.Parse(text);
            return LrcParser.Parse(text);
        }

        public async Task<LyricDocument> LoadAsync(StorageFile file)
        {
            string text = await FileIO.ReadTextAsync(file);
            return Parse(text, file.FileType);
        }

        public async Task SaveAsync(StorageFile file, string text)
        {
            await FileIO.WriteTextAsync(file, text);
        }
    }
}
