using System.IO;
using System.Text;

namespace TaylerLogTailer.Services;

/// <summary>
/// Follows a single file. Tracks a byte offset and decodes newly appended
/// content into complete lines. Handles partial trailing lines (held until the
/// terminating newline arrives) and file truncation / rotation (offset reset).
/// </summary>
public sealed class FileTailer
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly List<byte> _pending = new();
    private long _position;

    public FileTailer(string path)
    {
        FilePath = path;
        FileName = Path.GetFileName(path);
    }

    public string FilePath { get; }

    public string FileName { get; }

    /// <summary>
    /// Positions the tailer. When <paramref name="initialLines"/> is greater
    /// than zero, returns up to that many of the most recent existing lines and
    /// leaves the read offset at the end of the file. When it is zero, no
    /// existing content is returned and following starts from the end.
    /// </summary>
    public IReadOnlyList<string> Initialize(int initialLines)
    {
        var output = new List<string>();
        try
        {
            using var fs = new FileStream(
                FilePath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            long length = fs.Length;
            if (initialLines <= 0)
            {
                _position = length;
                return output;
            }

            const long capBytes = 8L * 1024 * 1024;
            long start = length > capBytes ? length - capBytes : 0;
            fs.Seek(start, SeekOrigin.Begin);

            byte[] buffer = new byte[length - start];
            int read = ReadFully(fs, buffer);
            _position = length;

            int offset = 0;
            if (start > 0)
            {
                // Started mid-file; discard the partial first line.
                int nl = Array.IndexOf(buffer, (byte)'\n', 0, read);
                offset = nl >= 0 ? nl + 1 : read;
            }

            _pending.Clear();
            ProcessBytes(buffer, offset, read, output);

            if (output.Count > initialLines)
            {
                output.RemoveRange(0, output.Count - initialLines);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return output;
    }

    /// <summary>
    /// Reads any content appended since the last read and returns the complete
    /// lines found.
    /// </summary>
    public IReadOnlyList<string> ReadNew()
    {
        var output = new List<string>();
        try
        {
            using var fs = new FileStream(
                FilePath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            long length = fs.Length;
            if (length < _position)
            {
                // File was truncated or rotated in place; start over.
                _position = 0;
                _pending.Clear();
            }

            if (length <= _position)
            {
                return output;
            }

            fs.Seek(_position, SeekOrigin.Begin);
            byte[] buffer = new byte[length - _position];
            int read = ReadFully(fs, buffer);
            _position += read;

            ProcessBytes(buffer, 0, read, output);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return output;
    }

    private void ProcessBytes(byte[] buffer, int offset, int count, List<string> output)
    {
        for (int i = offset; i < count; i++)
        {
            byte b = buffer[i];
            if (b == (byte)'\n')
            {
                output.Add(DecodePending());
                _pending.Clear();
            }
            else
            {
                _pending.Add(b);
            }
        }
    }

    private string DecodePending()
    {
        int count = _pending.Count;
        if (count > 0 && _pending[count - 1] == (byte)'\r')
        {
            count--;
        }

        return count == 0 ? string.Empty : Utf8.GetString(_pending.ToArray(), 0, count);
    }

    private static int ReadFully(Stream stream, byte[] buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = stream.Read(buffer, total, buffer.Length - total);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }
}
