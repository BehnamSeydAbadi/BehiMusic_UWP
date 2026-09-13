using System;
using System.Collections.Generic;

namespace BehiMusic_UWP.Lyrics
{
    public enum LyricFormat
    {
        Plain,
        Lrc,
        EnhancedLrc,
        Ttml
    }

    public sealed class LyricWord
    {
        public TimeSpan Start { get; set; }
        public TimeSpan? End { get; set; }
        public string Text { get; set; }
    }

    public sealed class LyricLine
    {
        public TimeSpan? Start { get; set; }
        public TimeSpan? End { get; set; }
        public string Text { get; set; }
        public string Translation { get; set; }
        public string Romanization { get; set; }
        public bool IsBackgroundVocal { get; set; }
        public IList<LyricWord> Words { get; set; } = new List<LyricWord>();
    }

    public sealed class LyricDocument
    {
        public LyricFormat Format { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public string Album { get; set; }
        public TimeSpan Offset { get; set; }
        public IList<LyricLine> Lines { get; set; } = new List<LyricLine>();
    }

    public sealed class LyricSearchResult
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public string Album { get; set; }
        public TimeSpan? Duration { get; set; }
        public LyricFormat Format { get; set; }
    }
}
