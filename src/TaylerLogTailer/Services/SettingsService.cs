using System.IO;
using System.Text.Json;
using TaylerLogTailer.Models;

namespace TaylerLogTailer.Services;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as JSON under the user's
/// %APPDATA%\TaylerLogTailer folder. Saving is serialized and atomic.
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly object _saveGate = new();
    private readonly string _filePath;

    public SettingsService()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TaylerLogTailer");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "settings.json");
    }

    public string FilePath => _filePath;

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return new AppSettings();
            }

            string json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (Exception)
        {
            // A corrupt or unreadable settings file should not stop the app
            // from starting; fall back to empty settings.
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_saveGate)
        {
            string json = JsonSerializer.Serialize(settings, JsonOptions);
            string tempPath = _filePath + ".tmp";
            File.WriteAllText(tempPath, json);

            // Atomic replace so a crash mid-write cannot leave a stray temp file
            // or a half-written settings file.
            File.Move(tempPath, _filePath, overwrite: true);
        }
    }
}
