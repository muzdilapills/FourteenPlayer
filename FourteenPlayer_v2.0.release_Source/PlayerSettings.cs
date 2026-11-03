using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace FourteenPlayer;

public class PlayerSettings
{
    public double Volume { get; set; } = 0.7;
    public bool IsMuted { get; set; } = false;
    public bool IsShuffle { get; set; } = false;
    public int RepeatMode { get; set; } = 1; // 0: Off, 1: Repeat List, 2: Repeat Album, 3: Repeat Track
    public int LastTrackIndex { get; set; } = 0;
    public List<string> PlaylistFiles { get; set; } = new();

    private static string SettingsFilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FourteenPlayer", "settings.json");

    private static string OldSettingsFilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LitePlayer", "settings.json");

    public static PlayerSettings Load()
    {
        try
        {
            string path = SettingsFilePath;
            // Check current path
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<PlayerSettings>(json);
                if (settings != null) return settings;
            }
            // Migrate from LitePlayer if existing
            else if (File.Exists(OldSettingsFilePath))
            {
                string json = File.ReadAllText(OldSettingsFilePath);
                var settings = JsonSerializer.Deserialize<PlayerSettings>(json);
                if (settings != null)
                {
                    settings.Save(); // Save to new path
                    return settings;
                }
            }
        }
        catch { }

        return new PlayerSettings();
    }

    public void Save()
    {
        try
        {
            string path = SettingsFilePath;
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch { }
    }
}
