using System.Collections.ObjectModel;
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
    private readonly ObservableCollection<LogRow> _rows = new();

    private FolderTailer? _tailer;
    private bool _paused;
    private bool _forgetting;

    public FolderWindow(FolderConfig config)
    {
        _config = config;
        InitializeComponent();

        LogGrid.ItemsSource = _rows;

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
        Width = _config.Width;
        Height = _config.Height;

        if (_config.Left is double left && _config.Top is double top)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
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
        _tailer = tailer;

        UpdateStatus("Starting\u2026");
        System.Threading.Tasks.Task.Run(tailer.Start);
    }

    private void StopTailing()
    {
        if (_tailer is not null)
        {
            _tailer.LinesArrived -= OnLinesArrived;
            _tailer.Dispose();
            _tailer = null;
        }
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

        int max = _config.MaxRows > 0 ? _config.MaxRows : 50_000;
        if (_rows.Count > max)
        {
            int excess = _rows.Count - max;
            for (int i = 0; i < excess; i++)
            {
                _rows.RemoveAt(0);
            }
        }

        if (AutoScrollCheck.IsChecked == true && _rows.Count > 0)
        {
            LogGrid.ScrollIntoView(_rows[^1]);
        }

        UpdateStatus(_paused ? "Paused" : "Following");
    }

    private void UpdateStatus(string state)
    {
        StatusText.Text = $"{_config.FolderPath}    \u2022    {_rows.Count:N0} rows    \u2022    {state}";
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
            App.Current.Track(_config);
            App.Current.SaveSettings();

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

        App.Current.Track(_config);
        App.Current.SaveSettings();

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

        if (_forgetting)
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
