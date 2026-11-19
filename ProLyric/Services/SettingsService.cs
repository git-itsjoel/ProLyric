using System.IO;
using System.Text.Json;
using ProLyric.Models;

namespace ProLyric.Services;

/// <summary>
/// Loads and saves AppSettings as JSON in %AppData%\ProLyric\settings.json.
/// </summary>
public class SettingsService
{
    private static readonly string SettingsDir = GetSettingsDir();

    private static string GetSettingsDir()
    {
        string newDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProLyric");
        string[] oldDirs =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProLyrics"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProLyricGen")
        ];
        if (!Directory.Exists(newDir))
        {
            foreach (var oldDir in oldDirs)
            {
                if (Directory.Exists(oldDir))
                {
                    try
                    {
                        Directory.CreateDirectory(newDir);
                        foreach (var file in Directory.GetFiles(oldDir))
                        {
                            string dest = Path.Combine(newDir, Path.GetFileName(file));
                            if (!File.Exists(dest)) File.Copy(file, dest);
                        }
                        break;
                    }
                    catch { }
                }
            }
        }
        return newDir;
    }

    private static readonly string SettingsPath =
        Path.Combine(SettingsDir, "settings.json");

    private static readonly string[] KnownSongDbLocations = [];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private AppSettings _current = new();

    public AppSettings Current => _current;

    public SettingsService()
    {
        Load();
    }

    /// <summary>Loads settings from disk, creating defaults if the file doesn't exist.</summary>
    public void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                string json = File.ReadAllText(SettingsPath);
                _current = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
            else
            {
                _current = new AppSettings();
            }
        }
        catch
        {
            _current = new AppSettings();
        }

        // Auto-detect SongDB if not configured
        if (string.IsNullOrEmpty(_current.SongDbPath))
        {
            foreach (var loc in KnownSongDbLocations)
            {
                if (File.Exists(loc))
                {
                    _current.SongDbPath = loc;
                    Save();
                    break;
                }
            }
        }
    }

    /// <summary>Saves current settings to %AppData%\ProLyric\settings.json.</summary>
    public void Save()
    {
        Directory.CreateDirectory(SettingsDir);
        string json = JsonSerializer.Serialize(_current, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }

    /// <summary>Updates settings and persists immediately.</summary>
    public void Update(Action<AppSettings> configure)
    {
        configure(_current);
        Save();
    }
}
