namespace TaylerLogTailer.Models;

/// <summary>
/// Root settings object persisted to disk. Holds the set of folder windows
/// that should be reopened on the next run.
/// </summary>
public sealed class AppSettings
{
    public List<FolderConfig> Windows { get; set; } = new();
}
