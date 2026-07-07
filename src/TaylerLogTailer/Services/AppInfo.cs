using System.Reflection;

namespace TaylerLogTailer.Services;

/// <summary>
/// Static information about the running application (version, product name).
/// The version is supplied by MinVer through the assembly's informational
/// version attribute; any build metadata after '+' is trimmed for display.
/// </summary>
public static class AppInfo
{
    /// <summary>The display version, for example "1.3.12" or "1.3.13-alpha.0.4".</summary>
    public static string Version { get; } = ResolveVersion();

    /// <summary>The product name.</summary>
    public static string Product { get; } =
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyProductAttribute>()?.Product
        ?? "Tayler Log Tailer";

    private static string ResolveVersion()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();

        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrEmpty(informational))
        {
            int plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString() ?? "unknown";
    }
}
