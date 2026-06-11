namespace TaylerLogTailer.Models;

/// <summary>
/// A single line of log output shown in the combined view, tagged with the
/// originating file name.
/// </summary>
public sealed class LogRow
{
    public required string FileName { get; init; }

    public required string Text { get; init; }
}
