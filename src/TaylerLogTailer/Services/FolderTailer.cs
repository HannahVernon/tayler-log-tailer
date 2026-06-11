using System.IO;
using System.Text.RegularExpressions;
using TaylerLogTailer.Models;

namespace TaylerLogTailer.Services;

/// <summary>
/// Watches a folder for log files matching one or more glob patterns and tails
/// them into a combined stream of <see cref="LogRow"/> values. Discovery is
/// driven by <see cref="FileSystemWatcher"/> events with a throttled full
/// rescan as a fallback; directory enumeration and file reads happen outside
/// the lock so a large or deep tree cannot block the poll loop. Recursive scans
/// do not follow reparse points (junctions / symlinks), and the number of
/// followed files is capped.
/// </summary>
public sealed class FolderTailer : IDisposable
{
    private const int PollIntervalMs = 250;
    private const int RescanIntervalMs = 2000;
    private const int MaxFiles = 4096;

    private readonly Dictionary<string, FileTailer> _tailers =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> _notified = new();
    private readonly object _gate = new();
    private readonly string _folder;
    private readonly bool _recursive;
    private readonly int _initialLines;
    private readonly Regex _matcher;

    private FileSystemWatcher? _watcher;
    private Timer? _timer;
    private int _polling;
    private int _rescanRequested;
    private long _lastRescanTick;
    private bool _capNotified;
    private volatile bool _paused;
    private volatile bool _disposed;

    public FolderTailer(string folder, string globPattern, bool recursive, int initialLines)
    {
        _folder = folder;
        _recursive = recursive;
        _initialLines = initialLines;
        _matcher = BuildMatcher(globPattern);
    }

    /// <summary>
    /// Raised (on a background thread) whenever new log lines are available.
    /// </summary>
    public event Action<IReadOnlyList<LogRow>>? LinesArrived;

    /// <summary>
    /// Raised (on a background thread) with a human-readable message when a
    /// non-fatal condition occurs (access denied, watcher unavailable, file
    /// limit reached).
    /// </summary>
    public event Action<string>? Notice;

    public bool Paused
    {
        get => _paused;
        set => _paused = value;
    }

    public void Start()
    {
        // Guard the initial discovery against a watcher event racing in.
        Interlocked.Exchange(ref _polling, 1);
        try
        {
            Reconcile();
        }
        finally
        {
            Interlocked.Exchange(ref _polling, 0);
        }

        _lastRescanTick = Environment.TickCount64;
        SetupWatcher();
        _timer = new Timer(_ => Poll(), null, PollIntervalMs, PollIntervalMs);
    }

    public void Dispose()
    {
        _disposed = true;
        _timer?.Dispose();
        _timer = null;

        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }
    }

    private void SetupWatcher()
    {
        try
        {
            _watcher = new FileSystemWatcher(_folder)
            {
                IncludeSubdirectories = _recursive,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };

            _watcher.Changed += (_, _) => Poll();
            _watcher.Created += (_, _) => RequestRescanAndPoll();
            _watcher.Renamed += (_, _) => RequestRescanAndPoll();
            _watcher.Deleted += (_, _) => RequestRescanAndPoll();
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            // If the watcher cannot be created (for example on some network
            // shares) the periodic poll still keeps the view up to date.
            _watcher = null;
            RaiseNotice($"File watcher unavailable; using periodic polling only ({ex.Message}).");
        }
    }

    private void RequestRescanAndPoll()
    {
        Interlocked.Exchange(ref _rescanRequested, 1);
        Poll();
    }

    private void Poll()
    {
        if (_disposed || _paused)
        {
            return;
        }

        if (Interlocked.Exchange(ref _polling, 1) == 1)
        {
            return;
        }

        try
        {
            bool rescan = Interlocked.Exchange(ref _rescanRequested, 0) == 1
                || Environment.TickCount64 - _lastRescanTick >= RescanIntervalMs;

            if (rescan)
            {
                Reconcile();
                _lastRescanTick = Environment.TickCount64;
            }

            DrainExisting();
        }
        catch (Exception ex)
        {
            RaiseNotice($"Tailing error: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _polling, 0);
        }
    }

    /// <summary>
    /// Discovers newly matching files (and drops files that disappeared),
    /// performing enumeration and the initial read of each new file outside the
    /// lock.
    /// </summary>
    private void Reconcile()
    {
        var enumerated = new List<string>();
        bool capHit = false;
        foreach (string file in EnumerateFiles())
        {
            enumerated.Add(file);
            if (enumerated.Count >= MaxFiles)
            {
                capHit = true;
                break;
            }
        }

        var enumeratedSet = new HashSet<string>(enumerated, StringComparer.OrdinalIgnoreCase);

        List<string> toAdd;
        lock (_gate)
        {
            toAdd = enumerated.Where(f => !_tailers.ContainsKey(f)).ToList();
        }

        var batch = new List<LogRow>();
        var created = new List<KeyValuePair<string, FileTailer>>();
        foreach (string file in toAdd)
        {
            var tailer = new FileTailer(file);
            foreach (string line in tailer.Initialize(_initialLines))
            {
                batch.Add(new LogRow { FileName = tailer.FileName, Text = line });
            }

            if (tailer.LastError is not null)
            {
                NotifyError(file, tailer.LastError);
            }

            created.Add(new KeyValuePair<string, FileTailer>(file, tailer));
        }

        lock (_gate)
        {
            foreach (var pair in created)
            {
                if (_tailers.Count >= MaxFiles)
                {
                    capHit = true;
                    break;
                }

                _tailers[pair.Key] = pair.Value;
            }

            foreach (string key in _tailers.Keys.Where(k => !enumeratedSet.Contains(k)).ToList())
            {
                _tailers.Remove(key);
            }
        }

        if (batch.Count > 0)
        {
            RaiseLines(batch);
        }

        if (capHit && !_capNotified)
        {
            _capNotified = true;
            RaiseNotice($"File limit ({MaxFiles}) reached; additional files are not being followed.");
        }
    }

    private void DrainExisting()
    {
        List<FileTailer> snapshot;
        lock (_gate)
        {
            snapshot = _tailers.Values.ToList();
        }

        var batch = new List<LogRow>();
        foreach (FileTailer tailer in snapshot)
        {
            foreach (string line in tailer.ReadNew())
            {
                batch.Add(new LogRow { FileName = tailer.FileName, Text = line });
            }

            if (tailer.LastError is not null)
            {
                NotifyError(tailer.FilePath, tailer.LastError);
            }
        }

        if (batch.Count > 0)
        {
            RaiseLines(batch);
        }
    }

    private IEnumerable<string> EnumerateFiles() => EnumerateDirectory(_folder);

    private IEnumerable<string> EnumerateDirectory(string directory)
    {
        string[] files;
        try
        {
            files = Directory.GetFiles(directory);
        }
        catch (Exception)
        {
            yield break;
        }

        foreach (string file in files)
        {
            if (IsMatch(Path.GetFileName(file)))
            {
                yield return file;
            }
        }

        if (!_recursive)
        {
            yield break;
        }

        string[] subdirectories;
        try
        {
            subdirectories = Directory.GetDirectories(directory);
        }
        catch (Exception)
        {
            yield break;
        }

        foreach (string subdirectory in subdirectories)
        {
            bool isReparsePoint;
            try
            {
                isReparsePoint = (File.GetAttributes(subdirectory) & FileAttributes.ReparsePoint) != 0;
            }
            catch (Exception)
            {
                continue;
            }

            // Do not follow junctions / symlinks: they can redirect enumeration
            // to files outside the chosen folder tree.
            if (isReparsePoint)
            {
                continue;
            }

            foreach (string file in EnumerateDirectory(subdirectory))
            {
                yield return file;
            }
        }
    }

    private bool IsMatch(string name)
    {
        try
        {
            return _matcher.IsMatch(name);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private void RaiseLines(IReadOnlyList<LogRow> rows) => LinesArrived?.Invoke(rows);

    private void RaiseNotice(string message) => Notice?.Invoke(message);

    private void NotifyError(string file, string message)
    {
        string key = file + "|" + message;
        lock (_gate)
        {
            if (!_notified.Add(key))
            {
                return;
            }
        }

        RaiseNotice($"{Path.GetFileName(file)}: {message}");
    }

    private static Regex BuildMatcher(string globPattern)
    {
        var patterns = globPattern
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (patterns.Length == 0)
        {
            patterns = new[] { "*" };
        }

        string combined = string.Join(
            "|",
            patterns.Select(p => "(?:" + WildcardToRegex(p) + ")"));

        return new Regex(
            "^(?:" + combined + ")$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled,
            TimeSpan.FromMilliseconds(250));
    }

    private static string WildcardToRegex(string pattern)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in pattern)
        {
            sb.Append(c switch
            {
                '*' => ".*",
                '?' => ".",
                _ => Regex.Escape(c.ToString()),
            });
        }

        return sb.ToString();
    }
}
