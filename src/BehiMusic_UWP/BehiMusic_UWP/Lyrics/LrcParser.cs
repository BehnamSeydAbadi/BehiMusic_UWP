using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace BehiMusic_UWP.Lyrics
{
    public static class LrcParser
    {
        private static readonly Regex LineTimestamp = new Regex(@"\[(?<time>\d{1,3}:\d{2}(?:[\.:]\d{1,3})?)\]", RegexOptions.Compiled);
        private static readonly Regex WordTimestamp = new Regex(@"<(?<time>\d{1,3}:\d{2}(?:[\.:]\d{1,3})?)>", RegexOptions.Compiled);
        private static readonly Regex Metadata = new Regex(@"^\[(?<key>[a-zA-Z]+):(?<value>.*)\]$", RegexOptions.Compiled);

        public static LyricDocument Parse(string source)
        {
            LyricDocument document = new LyricDocument { Format = LyricFormat.Plain };
            if (string.IsNullOrEmpty(source)) return document;

            foreach (string rawLine in source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                Match metadata = Metadata.Match(rawLine.Trim());
                if (metadata.Success && !char.IsDigit(metadata.Groups["key"].Value[0]))
                {
                    ApplyMetadata(document, metadata.Groups["key"].Value, metadata.Groups["value"].Value);
                    continue;
                }

                MatchCollection timestamps = LineTimestamp.Matches(rawLine);
                if (timestamps.Count == 0)
                {
                    if (!string.IsNullOrWhiteSpace(rawLine))
                        document.Lines.Add(new LyricLine { Text = rawLine.Trim() });
                    continue;
                }

                int contentStart = timestamps.Cast<Match>().Max(match => match.Index + match.Length);
                string content = rawLine.Substring(contentStart).Trim();
                bool background = content.StartsWith("[bg:", StringComparison.OrdinalIgnoreCase) && content.EndsWith("]");
                if (background) content = content.Substring(4, content.Length - 5).Trim();

                foreach (Match timestamp in timestamps)
                {
                    TimeSpan time;
                    if (!TryParseTimestamp(timestamp.Groups["time"].Value, out time)) continue;
                    LyricLine line = new LyricLine
                    {
                        Start = time + document.Offset,
                        Text = WordTimestamp.Replace(content, string.Empty),
                        IsBackgroundVocal = background
                    };
                    ParseWords(content, line);
                    if (line.Words.Count > 0) document.Format = LyricFormat.EnhancedLrc;
                    else if (document.Format == LyricFormat.Plain) document.Format = LyricFormat.Lrc;
                    document.Lines.Add(line);
                }
            }

            document.Lines = document.Lines
                .OrderBy(line => line.Start ?? TimeSpan.MaxValue)
                .ToList();
            SetImplicitEndTimes(document.Lines);
            return document;
        }

        private static void ParseWords(string content, LyricLine line)
        {
            MatchCollection matches = WordTimestamp.Matches(content);
            for (int i = 0; i < matches.Count; i++)
            {
                int textStart = matches[i].Index + matches[i].Length;
                int textEnd = i + 1 < matches.Count ? matches[i + 1].Index : content.Length;
                TimeSpan start;
                if (!TryParseTimestamp(matches[i].Groups["time"].Value, out start)) continue;
                TimeSpan? end = null;
                if (i + 1 < matches.Count)
                {
                    TimeSpan parsedEnd;
                    if (TryParseTimestamp(matches[i + 1].Groups["time"].Value, out parsedEnd)) end = parsedEnd;
                }
                line.Words.Add(new LyricWord { Start = start, End = end, Text = content.Substring(textStart, textEnd - textStart) });
            }
        }

        private static void ApplyMetadata(LyricDocument document, string key, string value)
        {
            switch (key.ToLowerInvariant())
            {
                case "ti": document.Title = value.Trim(); break;
                case "ar": document.Artist = value.Trim(); break;
                case "al": document.Album = value.Trim(); break;
                case "offset":
                    double milliseconds;
                    if (double.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out milliseconds))
                        document.Offset = TimeSpan.FromMilliseconds(milliseconds);
                    break;
            }
        }

        internal static bool TryParseTimestamp(string value, out TimeSpan result)
        {
            result = TimeSpan.Zero;
            string normalized = value.Replace(',', '.');
            string[] parts = normalized.Split(':');
            if (parts.Length < 2 || parts.Length > 3) return false;
            double seconds;
            int minutes;
            int hours = 0;
            if (!double.TryParse(parts[parts.Length - 1], NumberStyles.Float, CultureInfo.InvariantCulture, out seconds)) return false;
            if (!int.TryParse(parts[parts.Length - 2], out minutes)) return false;
            if (parts.Length == 3 && !int.TryParse(parts[0], out hours)) return false;
            result = TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
            return true;
        }

        private static void SetImplicitEndTimes(IList<LyricLine> lines)
        {
            for (int i = 0; i + 1 < lines.Count; i++)
                if (!lines[i].End.HasValue && lines[i + 1].Start.HasValue) lines[i].End = lines[i + 1].Start;
        }
    }
}
