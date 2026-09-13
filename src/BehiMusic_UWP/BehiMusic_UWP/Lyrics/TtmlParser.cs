using System;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace BehiMusic_UWP.Lyrics
{
    public static class TtmlParser
    {
        public static LyricDocument Parse(string source)
        {
            LyricDocument result = new LyricDocument { Format = LyricFormat.Ttml };
            if (string.IsNullOrWhiteSpace(source) || source.IndexOf("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) >= 0)
                return result;

            XDocument xml = XDocument.Parse(source, LoadOptions.PreserveWhitespace);
            foreach (XElement paragraph in xml.Descendants().Where(element => element.Name.LocalName == "p"))
            {
                TimeSpan? begin = ParseTime(Attribute(paragraph, "begin"));
                TimeSpan? end = ParseTime(Attribute(paragraph, "end"));
                LyricLine line = new LyricLine
                {
                    Start = begin,
                    End = end,
                    Text = string.Concat(paragraph.Nodes().Where(node => !(node is XElement)).Select(node => node.ToString())).Trim()
                };

                foreach (XElement span in paragraph.Descendants().Where(element => element.Name.LocalName == "span"))
                {
                    string role = Attribute(span, "role");
                    string text = span.Value.Trim();
                    if (role != null && role.IndexOf("translation", StringComparison.OrdinalIgnoreCase) >= 0) line.Translation = text;
                    else if (role != null && (role.IndexOf("roman", StringComparison.OrdinalIgnoreCase) >= 0 || role.IndexOf("transliteration", StringComparison.OrdinalIgnoreCase) >= 0)) line.Romanization = text;
                    else
                    {
                        TimeSpan? wordStart = ParseTime(Attribute(span, "begin"));
                        if (wordStart.HasValue)
                            line.Words.Add(new LyricWord { Start = wordStart.Value, End = ParseTime(Attribute(span, "end")), Text = text });
                    }
                }

                if (string.IsNullOrWhiteSpace(line.Text))
                    line.Text = string.Concat(line.Words.Select(word => word.Text));
                result.Lines.Add(line);
            }
            result.Lines = result.Lines.OrderBy(line => line.Start ?? TimeSpan.MaxValue).ToList();
            return result;
        }

        private static string Attribute(XElement element, string localName)
        {
            XAttribute attribute = element.Attributes().FirstOrDefault(item => item.Name.LocalName == localName);
            return attribute == null ? null : attribute.Value;
        }

        private static TimeSpan? ParseTime(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            TimeSpan clock;
            if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out clock)) return clock;
            double amount;
            if (value.EndsWith("ms") && double.TryParse(value.Substring(0, value.Length - 2), NumberStyles.Float, CultureInfo.InvariantCulture, out amount))
                return TimeSpan.FromMilliseconds(amount);
            if (value.EndsWith("s") && double.TryParse(value.Substring(0, value.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out amount))
                return TimeSpan.FromSeconds(amount);
            if (value.EndsWith("m") && double.TryParse(value.Substring(0, value.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out amount))
                return TimeSpan.FromMinutes(amount);
            return null;
        }
    }
}
