using BehiMusic_UWP.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Windows.Storage;

namespace BehiMusic_UWP.Services
{
    public sealed class SearchService
    {
        private const string HistoryKey = "SearchHistory";
        private const char Separator = '\u001f';

        public IReadOnlyList<string> History
        {
            get
            {
                object value;
                if (!ApplicationData.Current.LocalSettings.Values.TryGetValue(HistoryKey, out value))
                    return new string[0];

                return ((string)value).Split(new[] { Separator }, StringSplitOptions.RemoveEmptyEntries);
            }
        }

        public void Remember(string query)
        {
            query = (query ?? string.Empty).Trim();
            if (query.Length == 0) return;

            List<string> items = History
                .Where(item => !string.Equals(item, query, StringComparison.CurrentCultureIgnoreCase))
                .Take(9)
                .ToList();
            items.Insert(0, query);
            ApplicationData.Current.LocalSettings.Values[HistoryKey] = string.Join(Separator.ToString(), items);
        }

        public IReadOnlyList<Song> Find(IEnumerable<Song> source, string query, int limit = int.MaxValue)
        {
            string normalizedQuery = Normalize(query);
            if (normalizedQuery.Length == 0) return source.Take(limit).ToList();

            return source
                .Select(song => new { Song = song, Score = Score(song, normalizedQuery) })
                .Where(item => item.Score < int.MaxValue)
                .OrderBy(item => item.Score)
                .ThenBy(item => item.Song.DisplayTitle)
                .Take(limit)
                .Select(item => item.Song)
                .ToList();
        }

        public IReadOnlyList<object> Suggest(IEnumerable<Song> source, string query, int limit = 8)
        {
            string normalized = Normalize(query);
            if (normalized.Length == 0) return History.Cast<object>().Take(limit).ToList();
            return Find(source, query, limit).Cast<object>().ToList();
        }

        private static int Score(Song song, string query)
        {
            string title = string.IsNullOrEmpty(song.NormalizedTitle) ? Normalize(song.DisplayTitle) : song.NormalizedTitle;
            string index = string.IsNullOrEmpty(song.SearchIndex)
                ? string.Join("\u001e", new[] { song.DisplayTitle, song.Artist, song.AlbumArtist, song.Album, song.Genre, song.Folder, song.ReleaseType, song.FileName }.Select(Normalize))
                : song.SearchIndex;

            if (title == query) return 0;
            if (title.StartsWith(query)) return 10 + title.Length - query.Length;
            int titlePosition = title.IndexOf(query, StringComparison.Ordinal);
            if (titlePosition >= 0) return 20 + titlePosition;
            int position = index.IndexOf(query, StringComparison.Ordinal);
            if (position >= 0) return 30 + position;

            int best = int.MaxValue;
            if (query.Length >= 3)
            {
                int threshold = query.Length < 6 ? 1 : 2;
                foreach (string value in index.Split('\u001e'))
                {
                    foreach (string token in value.Split(new[] { ' ', '-', '_', '.', '(', ')', '[', ']' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        int compareLength = Math.Min(token.Length, query.Length + threshold);
                        string candidate = token.Substring(0, compareLength);
                        int distance = BoundedDamerauLevenshtein(candidate, query, threshold);
                        if (distance <= threshold) best = Math.Min(best, 60 + distance * 10);
                    }
                }
            }
            return best;
        }

        public static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
            StringBuilder result = new StringBuilder(decomposed.Length);
            foreach (char character in decomposed)
            {
                UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(character);
                if (category != UnicodeCategory.NonSpacingMark && category != UnicodeCategory.SpacingCombiningMark)
                {
                    // Normalize common Arabic/Persian variants for local music libraries.
                    if (character == '\u064a' || character == '\u0649') result.Append('\u06cc');
                    else if (character == '\u0643') result.Append('\u06a9');
                    else result.Append(character);
                }
            }
            return result.ToString().Normalize(NormalizationForm.FormC);
        }

        private static int BoundedDamerauLevenshtein(string left, string right, int maximum)
        {
            if (Math.Abs(left.Length - right.Length) > maximum) return maximum + 1;
            int[,] distance = new int[left.Length + 1, right.Length + 1];
            for (int i = 0; i <= left.Length; i++) distance[i, 0] = i;
            for (int j = 0; j <= right.Length; j++) distance[0, j] = j;

            for (int i = 1; i <= left.Length; i++)
            {
                int rowMinimum = int.MaxValue;
                for (int j = 1; j <= right.Length; j++)
                {
                    int cost = left[i - 1] == right[j - 1] ? 0 : 1;
                    int value = Math.Min(
                        Math.Min(distance[i - 1, j] + 1, distance[i, j - 1] + 1),
                        distance[i - 1, j - 1] + cost);
                    if (i > 1 && j > 1 && left[i - 1] == right[j - 2] && left[i - 2] == right[j - 1])
                        value = Math.Min(value, distance[i - 2, j - 2] + cost);
                    distance[i, j] = value;
                    rowMinimum = Math.Min(rowMinimum, value);
                }
                if (rowMinimum > maximum) return maximum + 1;
            }
            return distance[left.Length, right.Length];
        }
    }
}
