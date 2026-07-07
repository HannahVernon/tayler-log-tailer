using System.IO;
using System.Text;

namespace TaylerLogTailer.Services;

/// <summary>
/// Follows a single file. Tracks a byte offset and decodes newly appended
/// content into complete lines. Reads are performed in bounded chunks and the
/// in-progress line buffer is capped, so neither a very large append nor a file
/// with no line breaks can exhaust memory. Handles partial trailing lines (held
/// until the terminating newline arrives) and file truncation / rotation
/// (offset reset).
/// </summary>
public sealed class FileTailer
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Maximum bytes read into memory in a single read operation.</summary>
    private const int ReadChunkBytes = 1024 * 1024;

    /// <summary>Reusable buffer size for streaming reads to end of file.</summary>
    private const int StreamBufferBytes = 64 * 1024;

    /// <summary>
    /// Maximum bytes buffered for a single not-yet-terminated line. A line that
    /// exceeds this is flushed as-is to bound memory for newline-free input.
    /// </summary>
    private const int MaxPendingBytes = 1024 * 1024;

    private readonly List<byte> _pending = new();
    private long _position;
    private string? _lastLoggedError;

    public FileTailer(string path)
    {
        FilePath = path;
        FileName = Path.GetFileName(path);
    }

    public string FilePath { get; }

    public string FileName { get; }

    /// <summary>
    /// The message of the most recent IO/access error, or <c>null</c> if the
    /// last operation succeeded. Cleared at the start of each operation.
    /// </summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// Positions the tailer. When <paramref name="initialLines"/> is greater
    /// than zero, returns up to that many of the most recent existing lines and
    /// leaves the read offset at the end of the file. When it is zero, no
    /// existing content is returned and following starts from the end.
    /// </summary>
    public IReadOnlyList<string> Initialize(int initialLines)
    {
        LastError = null;
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
            _position = start;

            _pending.Clear();
            DrainStream(fs, length, output, skipFirstPartialLine: start > 0);
            _position = length;

            if (output.Count > initialLines)
            {
                output.RemoveRange(0, output.Count - initialLines);
            }
        }
        catch (IOException ex)
        {
            LogReadError(ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            LogReadError(ex);
        }

        return output;
    }

    /// <summary>
    /// Reads any content appended since the last read and returns the complete
    /// lines found.
    /// </summary>
    public IReadOnlyList<string> ReadNew()
    {
        LastError = null;
        var output = new List<string>();
        try
        {
            using var fs = new FileStream(
                FilePath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            // Note: fs.Length is only used to detect truncation / rotation. It is
            // deliberately NOT used to decide whether new data exists: over SMB /
            // network shares the redirector can cache a stale (smaller) length, so
            // we instead seek to the last offset and read through to the real end
            // of file. A read at the offset goes to the server and returns the
            // genuinely appended bytes even when the cached length lags.
            long length = fs.Length;
            if (length < _position)
            {
                // File was truncated or rotated in place; start over.
                _position = 0;
                _pending.Clear();
            }

            fs.Seek(_position, SeekOrigin.Begin);
            DrainToEnd(fs, output);
            NoteReadSuccess();
        }
        catch (IOException ex)
        {
            LogReadError(ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            LogReadError(ex);
        }

        return output;
    }

    /// <summary>
    /// Reads from the current position up to <paramref name="length"/> in
    /// bounded chunks, decoding complete lines into <paramref name="output"/>.
    /// </summary>
    private void DrainStream(Stream stream, long length, List<string> output, bool skipFirstPartialLine)
    {
        byte[] buffer = new byte[(int)Math.Min(length - _position, ReadChunkBytes)];
        bool skipping = skipFirstPartialLine;

        while (_position < length)
        {
            int want = (int)Math.Min(length - _position, buffer.Length);
            int read = stream.Read(buffer, 0, want);
            if (read == 0)
            {
                break;
            }

            _position += read;

            int offset = 0;
            if (skipping)
            {
                int nl = Array.IndexOf(buffer, (byte)'\n', 0, read);
                if (nl < 0)
                {
                    continue;
                }

                offset = nl + 1;
                skipping = false;
            }

            ProcessBytes(buffer, offset, read, output);
        }
    }

    /// <summary>
    /// Reads from the current position until the actual end of the stream is
    /// reached (a read returning zero bytes), decoding complete lines. Unlike
    /// <see cref="DrainStream"/> this does not rely on a precomputed length, so a
    /// stale cached file length (common on SMB shares) cannot hide appended data.
    /// </summary>
    private void DrainToEnd(Stream stream, List<string> output)
    {
        byte[] buffer = new byte[StreamBufferBytes];
        while (true)
        {
            int read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }

            _position += read;
            ProcessBytes(buffer, 0, read, output);
        }
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
                if (_pending.Count >= MaxPendingBytes)
                {
                    // Bound memory for a line with no terminator: flush what we
                    // have and keep accumulating the remainder.
                    output.Add(DecodePending());
                    _pending.Clear();
                }
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

    /// <summary>
    /// Records a read/access failure: sets <see cref="LastError"/> for the UI and
    /// writes the full exception (type, message, HResult) to the diagnostic log.
    /// The diagnostic entry is de-duplicated so a persistently failing file logs
    /// once per distinct error rather than on every poll.
    /// </summary>
    private void LogReadError(Exception ex)
    {
        LastError = ex.Message;
        if (ex.Message != _lastLoggedError)
        {
            _lastLoggedError = ex.Message;
            DiagnosticLog.Error($"Read failed for '{FilePath}'.", ex);
        }
    }

    /// <summary>
    /// Notes a successful read; if the file had previously been failing, records
    /// that reading has resumed.
    /// </summary>
    private void NoteReadSuccess()
    {
        if (_lastLoggedError is not null)
        {
            DiagnosticLog.Info($"Reading resumed for '{FilePath}'.");
            _lastLoggedError = null;
        }
    }
}
