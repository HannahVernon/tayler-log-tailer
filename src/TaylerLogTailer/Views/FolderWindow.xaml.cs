using System.Windows;
using Microsoft.Win32;
using TaylerLogTailer.Models;
using TaylerLogTailer.Services;

namespace TaylerLogTailer.Views;

/// <summary>
/// A single folder window. Shows a combined, auto-tailing view of every log
/// file in one folder that matches the configured glob pattern.
/// </summary>
public partial class FolderWindow : Window
{
    private readonly FolderConfig _config;
    private readonly BoundedLogCollection _rows = new();

    private FolderTailer? _tailer;
    private bool _paused;
    private bool _forgetting;
    private int _fileCount;
    private DateTime? _lastDataAt;
    private string? _lastNotice;
    private string _state = "Idle";

    public FolderWindow(FolderConfig config)
    {
        _config = config;
        InitializeComponent();

        LogGrid.ItemsSource = _rows;

        VersionText.Text = "v" + AppInfo.Version;
        VersionText.ToolTip = $"{AppInfo.Product} {AppInfo.Version}\nDiagnostic log: {DiagnosticLog.FilePath}";

        PatternBox.Text = config.GlobPattern;
        LinesBox.Text = config.InitialLines.ToString();
        RecursiveCheck.IsChecked = config.Recursive;
        AutoScrollCheck.IsChecked = config.AutoScroll;

        ApplyWindowBounds();

        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private void ApplyWindowBounds()
    {
        double width = Clamp(_config.Width, 320, SystemParameters.VirtualScreenWidth);
        double height = Clamp(_config.Height, 200, SystemParameters.VirtualScreenHeight);
        Width = width;
        Height = height;

        if (_config.Left is double left && _config.Top is double top)
        {
            double minLeft = SystemParameters.VirtualScreenLeft;
            double minTop = SystemParameters.VirtualScreenTop;
            double maxLeft = minLeft + Math.Max(0, SystemParameters.VirtualScreenWidth - width);
            double maxTop = minTop + Math.Max(0, SystemParameters.VirtualScreenHeight - height);

            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = Clamp(left, minLeft, maxLeft);
            Top = Clamp(top, minTop, maxTop);
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        if (_config.Maximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private static double Clamp(double value, double min, double max)
    {
        if (max < min)
        {
            return min;
        }

        if (double.IsNaN(value))
        {
            return min;
        }

        return Math.Min(Math.Max(value, min), max);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_config.FolderPath))
        {
            StartTailing();
        }
        else
        {
            UpdateStatus("No folder selected. Click \u201cFolder\u2026\u201d to choose one.");
        }
    }

    private void StartTailing()
    {
        StopTailing();

        if (string.IsNullOrWhiteSpace(_config.FolderPath))
        {
            return;
        }

        Title = $"Tayler Log Tailer \u2013 {_config.FolderPath}";
        FolderText.Text = _config.FolderPath;

        var tailer = new FolderTailer(
            _config.FolderPath,
            string.IsNullOrWhiteSpace(_config.GlobPattern) ? "*" : _config.GlobPattern,
            _config.Recursive,
            _config.InitialLines)
        {
            Paused = _paused,
        };

        tailer.LinesArrived += OnLinesArrived;
        tailer.Notice += OnNotice;
        tailer.DiscoveryCompleted += OnDiscoveryCompleted;
        _tailer = tailer;

        _fileCount = 0;
        _lastDataAt = null;
        _lastNotice = null;

        UpdateStatus("Starting\u2026");
        System.Threading.Tasks.Task.Run(tailer.Start).ContinueWith(
            t => Dispatcher.BeginInvoke(() => OnNotice($"Could not start tailing: {t.Exception?.GetBaseException().Message}")),
            System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
    }

    private void StopTailing()
    {
        if (_tailer is not null)
        {
            _tailer.LinesArrived -= OnLinesArrived;
            _tailer.Notice -= OnNotice;
            _tailer.DiscoveryCompleted -= OnDiscoveryCompleted;
            _tailer.Dispose();
            _tailer = null;
        }
    }

    private void OnNotice(string message)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _lastNotice = message;
            RefreshStatus();
        });
    }

    private void OnDiscoveryCompleted(int fileCount)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _fileCount = fileCount;
            if (_state is "Starting\u2026" or "Idle")
            {
                _state = _paused ? "Paused" : "Following";
            }

            RefreshStatus();
        });
    }

    private void OnLinesArrived(IReadOnlyList<LogRow> rows)
    {
        Dispatcher.BeginInvoke(() => AppendRows(rows));
    }

    private void AppendRows(IReadOnlyList<LogRow> rows)
    {
        foreach (LogRow row in rows)
        {
            _rows.Add(row);
        }

        // Trim the oldest rows in bulk rather than one at a time. Removing rows
        // individually raised a collection-changed event per row, and at the cap
        // under a heavy log rate that flood of events stalled the UI thread and
        // stopped the display from updating. Let the buffer grow a little past
        // the cap, then drop the whole overflow in one batched notification so a
        // trim (and any scroll reposition when auto-scroll is off) happens rarely
        // instead of on every batch.
        int max = _config.MaxRows > 0 ? _config.MaxRows : 50_000;
        int slack = Math.Clamp(max / 20, 1, 2048);
        if (_rows.Count >= max + slack)
        {
            _rows.TrimHead(max);
        }

        if (AutoScrollCheck.IsChecked == true && _rows.Count > 0)
        {
            LogGrid.ScrollIntoView(_rows[^1]);
        }

        if (rows.Count > 0)
        {
            _lastDataAt = DateTime.Now;
        }

        UpdateStatus(_paused ? "Paused" : "Following");
    }

    private void UpdateStatus(string state)
    {
        _state = state;
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        if (string.IsNullOrWhiteSpace(_config.FolderPath))
        {
            StatusText.Text = _state;
            return;
        }

        const string sep = "    \u2022    ";
        var sb = new System.Text.StringBuilder();
        sb.Append(_config.FolderPath);
        sb.Append(sep).Append($"{_rows.Count:N0} rows");
        sb.Append(sep).Append($"{_fileCount:N0} files");
        sb.Append(sep).Append(_state);

        if (_lastDataAt is DateTime t)
        {
            sb.Append(sep).Append("last new ").Append(t.ToString("HH:mm:ss"));
        }

        if (!string.IsNullOrEmpty(_lastNotice))
        {
            sb.Append(sep).Append(_lastNotice);
        }

        StatusText.Text = sb.ToString();
    }

    private void UpdateConfigFromControls()
    {
        _config.GlobPattern = PatternBox.Text.Trim();
        _config.InitialLines = int.TryParse(LinesBox.Text, out int lines) && lines > 0 ? lines : 0;
        _config.Recursive = RecursiveCheck.IsChecked == true;
        _config.AutoScroll = AutoScrollCheck.IsChecked == true;
        LinesBox.Text = _config.InitialLines.ToString();
    }

    private void OnChooseFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose a folder of log files",
        };

        if (!string.IsNullOrWhiteSpace(_config.FolderPath))
        {
            dialog.InitialDirectory = _config.FolderPath;
        }

        if (dialog.ShowDialog(this) == true)
        {
            _config.FolderPath = dialog.FolderName;
            UpdateConfigFromControls();
            if (!_config.Ephemeral)
            {
                App.Current.Track(_config);
                App.Current.SaveSettings();
            }

            _rows.Clear();
            StartTailing();
        }
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        UpdateConfigFromControls();

        if (string.IsNullOrWhiteSpace(_config.FolderPath))
        {
            return;
        }

        if (!_config.Ephemeral)
        {
            App.Current.Track(_config);
            App.Current.SaveSettings();
        }

        _rows.Clear();
        StartTailing();
    }

    private void OnTogglePause(object sender, RoutedEventArgs e)
    {
        _paused = !_paused;
        if (_tailer is not null)
        {
            _tailer.Paused = _paused;
        }

        PauseButton.Content = _paused ? "Resume" : "Pause";
        UpdateStatus(_paused ? "Paused" : "Following");
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        _rows.Clear();
        UpdateStatus(_paused ? "Paused" : "Following");
    }

    private void OnViewLog(object sender, RoutedEventArgs e)
    {
        App.Current.OpenLogWindow();
    }

    private void OnNewWindow(object sender, RoutedEventArgs e)
    {
        App.Current.OpenWindow(new FolderConfig());
    }

    private void OnForget(object sender, RoutedEventArgs e)
    {
        _forgetting = true;
        App.Current.Untrack(_config);
        Close();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        StopTailing();

        if (_forgetting || _config.Ephemeral)
        {
            return;
        }

        UpdateConfigFromControls();
        CaptureWindowBounds();

        if (!string.IsNullOrWhiteSpace(_config.FolderPath))
        {
            App.Current.Track(_config);
            App.Current.SaveSettings();
        }
    }

    private void CaptureWindowBounds()
    {
        _config.Maximized = WindowState == WindowState.Maximized;

        Rect bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, ActualWidth, ActualHeight)
            : RestoreBounds;

        _config.Left = bounds.Left;
        _config.Top = bounds.Top;
        _config.Width = bounds.Width;
        _config.Height = bounds.Height;
    }
}
