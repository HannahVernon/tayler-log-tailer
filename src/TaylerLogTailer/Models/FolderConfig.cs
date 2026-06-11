namespace TaylerLogTailer.Models;

/// <summary>
/// Persisted configuration for a single folder window. One instance per window
/// is stored so the app can reopen the same folders on the next run.
/// </summary>
public sealed class FolderConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string FolderPath { get; set; } = string.Empty;

    /// <summary>
    /// One or more glob patterns separated by ';' (for example "*.log;*.txt").
    /// </summary>
    public string GlobPattern { get; set; } = "*.log";

    /// <summary>
    /// Number of existing lines to show from each file when it is first
    /// discovered. 0 means show nothing existing and only follow new lines.
    /// </summary>
    public int InitialLines { get; set; }

    public bool Recursive { get; set; }

    public bool AutoScroll { get; set; } = true;

    /// <summary>
    /// Maximum number of rows kept in the combined view. Oldest rows are
    /// trimmed once this is exceeded to bound memory.
    /// </summary>
    public int MaxRows { get; set; } = 50_000;

    public double? Left { get; set; }

    public double? Top { get; set; }

    public double Width { get; set; } = 1000;

    public double Height { get; set; } = 640;

    public bool Maximized { get; set; }
}
