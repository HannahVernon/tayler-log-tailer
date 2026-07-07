using System.Windows;
using TaylerLogTailer.Models;
using TaylerLogTailer.Services;
using TaylerLogTailer.Views;

namespace TaylerLogTailer;

/// <summary>
/// Application entry point. Restores the set of folder windows that were
/// remembered from the previous run and owns the shared settings store.
/// </summary>
public partial class App : Application
{
    private readonly SettingsService _settingsService = new();

    public AppSettings Settings { get; private set; } = new();

    public static new App Current => (App)Application.Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Settings = _settingsService.Load();
        DiagnosticLog.WriteSessionHeader(Settings, _settingsService.FilePath);

        var configs = Settings.Windows
            .Where(c => !string.IsNullOrWhiteSpace(c.FolderPath))
            .ToList();

        if (configs.Count == 0)
        {
            OpenWindow(new FolderConfig());
        }
        else
        {
            foreach (FolderConfig config in configs)
            {
                OpenWindow(config);
            }
        }
    }

    /// <summary>
    /// Opens a folder window. The window is remembered once a folder has been
    /// chosen for it.
    /// </summary>
    public void OpenWindow(FolderConfig config)
    {
        var window = new FolderWindow(config);
        window.Show();
    }

    /// <summary>
    /// Opens a transient window that live-tails the diagnostic log folder. The
    /// window is not remembered across runs.
    /// </summary>
    public void OpenLogWindow()
    {
        var config = new FolderConfig
        {
            FolderPath = DiagnosticLog.Directory,
            GlobPattern = "*.log",
            InitialLines = 2000,
            Recursive = false,
            AutoScroll = true,
            Ephemeral = true,
        };

        OpenWindow(config);
    }

    public void SaveSettings()
    {
        _settingsService.Save(Settings);
    }

    /// <summary>
    /// Ensures the supplied configuration is part of the persisted set so the
    /// window is reopened on the next run.
    /// </summary>
    public void Track(FolderConfig config)
    {
        if (!Settings.Windows.Any(c => c.Id == config.Id))
        {
            Settings.Windows.Add(config);
        }
    }

    /// <summary>
    /// Removes the configuration from the persisted set so the window is not
    /// reopened on the next run.
    /// </summary>
    public void Untrack(FolderConfig config)
    {
        Settings.Windows.RemoveAll(c => c.Id == config.Id);
        SaveSettings();
    }
}
