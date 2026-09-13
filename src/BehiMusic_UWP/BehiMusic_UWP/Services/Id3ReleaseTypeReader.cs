using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Storage;
using Windows.Storage.Streams;

namespace BehiMusic_UWP.Services
{
    internal static class Id3ReleaseTypeReader
    {
        private const int MaximumTagBytes = 512 * 1024;

        public static async Task<string> ReadAsync(StorageFile file)
        {
            try
            {
                using (IRandomAccessStream random = await file.OpenReadAsync())
                using (Stream stream = random.AsStreamForRead())
                {
                    byte[] header = new byte[10];
                    if (await ReadFullyAsync(stream, header, 0, header.Length) != header.Length) return "Unknown";
                    if (header[0] != (byte)'I' || header[1] != (byte)'D' || header[2] != (byte)'3') return "Unknown";

                    int version = header[3];
                    int tagSize = SyncSafe(header, 6);
                    if (tagSize <= 0 || tagSize > MaximumTagBytes) return "Unknown";
                    byte[] tag = new byte[tagSize];
                    int read = await ReadFullyAsync(stream, tag, 0, tag.Length);
                    return FindReleaseType(tag, read, version);
                }
            }
            catch
            {
                return "Unknown";
            }
        }

        private static string FindReleaseType(byte[] tag, int length, int version)
        {
            int offset = 0;
            while (offset + 10 <= length)
            {
                string id = Encoding.ASCII.GetString(tag, offset, 4);
                if (id.All(character => character == '\0')) break;
                int frameSize = version >= 4 ? SyncSafe(tag, offset + 4) : BigEndian(tag, offset + 4);
                if (frameSize <= 0 || offset + 10 + frameSize > length) break;

                if (id == "TXXX")
                {
                    string[] pair = DecodeUserText(tag, offset + 10, frameSize);
                    if (pair != null && IsReleaseTypeDescription(pair[0])) return NormalizeReleaseType(pair[1]);
                }

                offset += 10 + frameSize;
            }
            return "Unknown";
        }

        private static string[] DecodeUserText(byte[] data, int offset, int count)
        {
            if (count < 2) return null;
            byte encoding = data[offset];
            int contentOffset = offset + 1;
            int contentCount = count - 1;
            Encoding textEncoding;
            int terminatorWidth;

            switch (encoding)
            {
                case 1: textEncoding = Encoding.Unicode; terminatorWidth = 2; break;
                case 2: textEncoding = Encoding.BigEndianUnicode; terminatorWidth = 2; break;
                case 3: textEncoding = Encoding.UTF8; terminatorWidth = 1; break;
                default: textEncoding = Encoding.GetEncoding(28591); terminatorWidth = 1; break;
            }

            int separator = FindTerminator(data, contentOffset, contentCount, terminatorWidth);
            if (separator < 0) return null;
            string description = textEncoding.GetString(data, contentOffset, separator - contentOffset).Trim('\0', ' ');
            int valueOffset = separator + terminatorWidth;
            int valueCount = Math.Max(0, offset + count - valueOffset);
            string value = textEncoding.GetString(data, valueOffset, valueCount).Trim('\0', ' ');
            return new[] { description, value };
        }

        private static int FindTerminator(byte[] data, int offset, int count, int width)
        {
            int end = offset + count - width + 1;
            for (int i = offset; i < end; i += width)
            {
                if (data[i] == 0 && (width == 1 || data[i + 1] == 0)) return i;
            }
            return -1;
        }

        private static bool IsReleaseTypeDescription(string value)
        {
            string normalized = SearchService.Normalize(value).Replace(" ", string.Empty).Replace("_", string.Empty);
            return normalized.Contains("releasetype") || normalized.Contains("albumtype") || normalized.Contains("musicbrainzalbumtype");
        }

        private static string NormalizeReleaseType(string raw)
        {
            string value = SearchService.Normalize(raw);
            if (value.Contains("ep")) return "EP";
            if (value.Contains("single")) return "Single";
            if (value.Contains("live")) return "Live";
            if (value.Contains("compilation")) return "Compilation";
            if (value.Contains("soundtrack")) return "Soundtrack";
            if (value.Contains("remix")) return "Remix";
            if (value.Contains("demo")) return "Demo";
            if (value.Contains("audiobook")) return "Audiobook";
            if (value.Contains("album")) return "Album";
            return string.IsNullOrWhiteSpace(raw) ? "Unknown" : raw.Trim();
        }

        private static int SyncSafe(byte[] bytes, int offset)
        {
            return ((bytes[offset] & 0x7f) << 21) | ((bytes[offset + 1] & 0x7f) << 14) |
                   ((bytes[offset + 2] & 0x7f) << 7) | (bytes[offset + 3] & 0x7f);
        }

        private static int BigEndian(byte[] bytes, int offset)
        {
            return (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
        }

        private static async Task<int> ReadFullyAsync(Stream stream, byte[] buffer, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = await stream.ReadAsync(buffer, offset + total, count - total);
                if (read == 0) break;
                total += read;
            }
            return total;
        }
    }
}
