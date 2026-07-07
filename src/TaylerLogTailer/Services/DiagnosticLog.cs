using System.IO;
using System.Text;
using TaylerLogTailer.Models;

namespace TaylerLogTailer.Services;

/// <summary>
/// Simple thread-safe append-only diagnostic log. Every non-fatal condition the
/// app encounters (watcher errors, file read/access failures) is recorded here
/// with full exception detail (type, message and HResult / Win32 code) so that
/// intermittent problems, especially on network shares, can be investigated
/// after the fact. Logging never throws: any failure to write is swallowed so a
/// logging problem cannot stop the app.
///
/// The log lives under %APPDATA%\TaylerLogTailer\logs\diagnostic.log and is
/// rolled to diagnostic.log.1 once it grows past <see cref="MaxBytes"/>.
/// </summary>
public static class DiagnosticLog
{
    private const long MaxBytes = 1024 * 1024;

    private static readonly object Gate = new();
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private static string? _directory;
    private static string? _filePath;

    /// <summary>The folder that contains the diagnostic log.</summary>
    public static string Directory
    {
        get
        {
            EnsurePaths();
            return _directory!;
        }
    }

    /// <summary>The full path to the current diagnostic log file.</summary>
    public static string FilePath
    {
        get
        {
            EnsurePaths();
            return _filePath!;
        }
    }

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message) => Write("WARN", message, null);

    public static void Error(string message, Exception? exception = null) =>
        Write("ERROR", message, exception);

    /// <summary>
    /// Writes a session header describing the app version and the settings in
    /// effect, so a log read later is self-describing.
    /// </summary>
    public static void WriteSessionHeader(AppSettings settings, string settingsPath)
    {
        var sb = new StringBuilder();
        string rule = new string('=', 70);
        sb.AppendLine(rule);
        sb.AppendLine($"{AppInfo.Product} - diagnostic log");
        sb.AppendLine($"Session start : {Now()}");
        sb.AppendLine($"Version       : {AppInfo.Version}");
        sb.AppendLine($"Operating sys : {Environment.OSVersion}");
        sb.AppendLine($".NET runtime  : {Environment.Version}");
        sb.AppendLine($"Process       : {(Environment.Is64BitProcess ? "64-bit" : "32-bit")}");
        sb.AppendLine($"Settings file : {settingsPath}");
        sb.AppendLine($"Log file      : {FilePath}");

        var windows = settings.Windows;
        sb.AppendLine($"Windows ({windows.Count}):");
        if (windows.Count == 0)
        {
            sb.AppendLine("  (none remembered)");
        }
        else
        {
            for (int i = 0; i < windows.Count; i++)
            {
                FolderConfig w = windows[i];
                sb.AppendLine(
                    $"  [{i + 1}] folder='{w.FolderPath}' pattern='{w.GlobPattern}' " +
                    $"recursive={w.Recursive} initialLines={w.InitialLines} " +
                    $"autoScroll={w.AutoScroll} maxRows={w.MaxRows}");
            }
        }

        sb.Append(rule);
        WriteRaw(sb.ToString());
    }

    private static void Write(string level, string message, Exception? exception)
    {
        var sb = new StringBuilder();
        sb.Append(Now()).Append(" [").Append(level).Append("] ").Append(message);

        if (exception is not null)
        {
            sb.Append(" | ")
              .Append(exception.GetType().FullName)
              .Append(": ")
              .Append(exception.Message)
              .Append(" (HResult 0x")
              .Append(exception.HResult.ToString("X8"))
              .Append(')');
        }

        WriteRaw(sb.ToString());
    }

    private static void WriteRaw(string text)
    {
        try
        {
            EnsurePaths();
            lock (Gate)
            {
                RollIfNeeded();
                File.AppendAllText(_filePath!, text + Environment.NewLine, Utf8);
            }
        }
        catch
        {
            // Logging must never crash the app.
        }
    }

    private static void RollIfNeeded()
    {
        try
        {
            var info = new FileInfo(_filePath!);
            if (info.Exists && info.Length > MaxBytes)
            {
                File.Move(_filePath!, _filePath! + ".1", overwrite: true);
            }
        }
        catch
        {
            // If the roll fails, keep appending to the existing file.
        }
    }

    private static string Now() => DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz");

    private static void EnsurePaths()
    {
        if (_filePath is not null)
        {
            return;
        }

        lock (Gate)
        {
            if (_filePath is not null)
            {
                return;
            }

            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TaylerLogTailer",
                "logs");
            System.IO.Directory.CreateDirectory(dir);
            _directory = dir;
            _filePath = Path.Combine(dir, "diagnostic.log");
        }
    }
}
