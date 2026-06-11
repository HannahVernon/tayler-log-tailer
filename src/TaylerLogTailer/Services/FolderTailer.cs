using System.IO;
using System.Text.RegularExpressions;
using TaylerLogTailer.Models;

namespace TaylerLogTailer.Services;

/// <summary>
/// Watches a folder for log files matching one or more glob patterns and tails
/// them into a combined stream of <see cref="LogRow"/> values. New files are
/// picked up automatically via <see cref="FileSystemWatcher"/> and a periodic
/// poll that also acts as a fallback for missed change notifications.
/// </summary>
public sealed class FolderTailer : IDisposable
{
    private const int PollIntervalMs = 250;

    private readonly Dictionary<string, FileTailer> _tailers =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly object _gate = new();
    private readonly string _folder;
    private readonly bool _recursive;
    private readonly int _initialLines;
    private readonly Regex _matcher;

    private FileSystemWatcher? _watcher;
    private Timer? _timer;
    private int _polling;
    private bool _disposed;

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

    public bool Paused { get; set; }

    public void Start()
    {
        var initial = new List<LogRow>();
        lock (_gate)
        {
            foreach (string file in EnumerateFiles())
            {
                AddTailer(file, initial);
            }
        }

        if (initial.Count > 0)
        {
            LinesArrived?.Invoke(initial);
        }

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
            _watcher.Created += (_, _) => Poll();
            _watcher.Renamed += (_, _) => Poll();
            _watcher.Deleted += (_, _) => Poll();
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception)
        {
            // If the watcher cannot be created (for example on some network
            // shares) the periodic poll still keeps the view up to date.
            _watcher = null;
        }
    }

    private void Poll()
    {
        if (_disposed || Paused)
        {
            return;
        }

        if (Interlocked.Exchange(ref _polling, 1) == 1)
        {
            return;
        }

        try
        {
            var batch = new List<LogRow>();
            lock (_gate)
            {
                var current = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string file in EnumerateFiles())
                {
                    current.Add(file);
                    if (!_tailers.ContainsKey(file))
                    {
                        AddTailer(file, batch);
                    }
                }

                foreach (var pair in _tailers)
                {
                    foreach (string line in pair.Value.ReadNew())
                    {
                        batch.Add(new LogRow { FileName = pair.Value.FileName, Text = line });
                    }
                }

                // Drop tailers whose files have disappeared.
                var removed = _tailers.Keys.Where(k => !current.Contains(k)).ToList();
                foreach (string key in removed)
                {
                    _tailers.Remove(key);
                }
            }

            if (batch.Count > 0)
            {
                LinesArrived?.Invoke(batch);
            }
        }
        catch (Exception)
        {
            // Never let a transient IO error tear down the poll loop.
        }
        finally
        {
            Interlocked.Exchange(ref _polling, 0);
        }
    }

    private void AddTailer(string file, List<LogRow> output)
    {
        var tailer = new FileTailer(file);
        foreach (string line in tailer.Initialize(_initialLines))
        {
            output.Add(new LogRow { FileName = tailer.FileName, Text = line });
        }

        _tailers[file] = tailer;
    }

    private IEnumerable<string> EnumerateFiles()
    {
        SearchOption option = _recursive
            ? SearchOption.AllDirectories
            : SearchOption.TopDirectoryOnly;

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(_folder, "*", option);
        }
        catch (Exception)
        {
            yield break;
        }

        foreach (string file in files)
        {
            if (_matcher.IsMatch(Path.GetFileName(file)))
            {
                yield return file;
            }
        }
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

        return new Regex("^(?:" + combined + ")$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
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
