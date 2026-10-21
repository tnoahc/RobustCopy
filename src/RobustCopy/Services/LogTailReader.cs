using System.IO;
using System.Text;

namespace RobustCopy.Services;

public sealed record LogTail(string Text, bool IsTruncated, long FileBytes, long MaximumBytes);

public static class LogTailReader
{
    public const long DefaultMaximumBytes = 5L * 1024 * 1024;
    private const byte LineFeed = (byte)'\n';

    public static LogTail Read(string path, long maximumBytes = DefaultMaximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maximumBytes, 0);

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var length = stream.Length;
        if (length <= maximumBytes)
        {
            var whole = ReadExactly(stream, 0, (int)length);
            return new LogTail(Decode(whole, 0), false, length, maximumBytes);
        }

        // Read one extra byte before the window so a window that already starts on a line boundary is kept intact.
        var buffer = ReadExactly(stream, length - maximumBytes - 1, (int)maximumBytes + 1);
        var start = Array.IndexOf(buffer, LineFeed);
        if (start >= 0)
        {
            start++;
        }
        else
        {
            // No line break in the window: skip UTF-8 continuation bytes so a multibyte character is never split.
            start = 1;
            while (start < buffer.Length && (buffer[start] & 0xC0) == 0x80) start++;
        }

        var header = $"[Truncated: showing the last {FormatMegabytes(maximumBytes)} of {FormatMegabytes(length)}. Open the log for the full transcript.]";
        var text = header + Environment.NewLine + Decode(buffer, start);
        return new LogTail(text, true, length, maximumBytes);
    }

    public static string FormatMegabytes(long bytes) => $"{bytes / (1024d * 1024d):0.#} MB";

    private static byte[] ReadExactly(FileStream stream, long offset, int count)
    {
        var buffer = new byte[count];
        stream.Seek(offset, SeekOrigin.Begin);
        var read = 0;
        while (read < count)
        {
            var chunk = stream.Read(buffer, read, count - read);
            if (chunk == 0) break;
            read += chunk;
        }

        return read == count ? buffer : buffer[..read];
    }

    private static string Decode(byte[] buffer, int start)
    {
        var preamble = Encoding.UTF8.Preamble;
        if (start == 0 && buffer.AsSpan().StartsWith(preamble)) start = preamble.Length;
        return Encoding.UTF8.GetString(buffer, start, buffer.Length - start);
    }
}
