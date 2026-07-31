using System;
using System.IO;
using System.Text.Json;
using openTransferWPF.Models;

namespace openTransferWPF.Services
{
    public class SettingsService
    {
        private static readonly Lazy<SettingsService> _instance = new(() => new SettingsService());
        public static SettingsService Instance => _instance.Value;

        private readonly string _settingsFilePath;
        public AppSettings Settings { get; private set; }

        public event EventHandler<AppSettings>? SettingsChanged;

        private SettingsService()
        {
            string appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "openTransferWPF");
            if (!Directory.Exists(appDataDir))
            {
                Directory.CreateDirectory(appDataDir);
            }

            _settingsFilePath = Path.Combine(appDataDir, "appsettings.json");
            Settings = LoadSettings();
        }

        private AppSettings LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    string json = File.ReadAllText(_settingsFilePath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null)
                        return settings;
                }
            }
            catch { }

            return new AppSettings();
        }

        public void SaveSettings(AppSettings newSettings)
        {
            try
            {
                Settings = newSettings;
                string json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsFilePath, json);
                SettingsChanged?.Invoke(this, Settings);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
            }
        }
    }
}
