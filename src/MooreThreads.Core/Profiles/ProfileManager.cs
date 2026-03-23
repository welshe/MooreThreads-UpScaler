using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MooreThreadsUpScaler.Core.Profiles
{
    public enum ScalingMode { Auto, Fixed, Integer, Custom }

    public class AppProfile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ProcessName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;

        public string Algorithm { get; set; } = "LS1";
        public ScalingMode ScalingMode { get; set; } = ScalingMode.Auto;
        public double ScaleFactor { get; set; } = 0.5;
        public double Sharpness { get; set; } = 1.0;

        public bool FrameGenEnabled { get; set; }
        public int FrameGenMultiplier { get; set; } = 2;
        public bool ZeroLatencyMode { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
    }

    public class AppSettings
    {
        public bool MinimizeToTray { get; set; } = true;
        public bool StartMinimized { get; set; }
        public string DefaultAlgorithm { get; set; } = "LS1";
        public double DefaultSharpness { get; set; } = 1.0;
    }

    public sealed class ProfileManager
    {
        private readonly string _profilesPath;
        private readonly string _settingsPath;
        private List<AppProfile> _profiles = new();
        private AppSettings _settings = new();

        public IReadOnlyList<AppProfile> Profiles => _profiles;
        public AppSettings Settings => _settings;

        public ProfileManager()
        {
            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MooreThreadsUpScaler");

            Directory.CreateDirectory(appData);
            _profilesPath = Path.Combine(appData, "profiles.json");
            _settingsPath  = Path.Combine(appData, "settings.json");
            Load();
        }

        public AppProfile CreateProfile(string processName, string displayName)
        {
            var profile = new AppProfile { ProcessName = processName, DisplayName = displayName };
            _profiles.Add(profile);
            SaveProfiles();
            return profile;
        }

        public AppProfile? GetByProcess(string processName) =>
            _profiles.Find(p => p.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase));

        public void Update(AppProfile profile)
        {
            int idx = _profiles.FindIndex(p => p.Id == profile.Id);
            if (idx < 0) return;
            profile.ModifiedAt = DateTime.UtcNow;
            _profiles[idx]     = profile;
            SaveProfiles();
        }

        public void Delete(string id)
        {
            _profiles.RemoveAll(p => p.Id == id);
            SaveProfiles();
        }

        public void SaveSettings()
        {
            try { File.WriteAllText(_settingsPath, Serialize(_settings)); }
            catch (Exception ex) { Log(ex); }
        }

        private void Load()
        {
            try
            {
                if (File.Exists(_profilesPath))
                    _profiles = JsonSerializer.Deserialize<List<AppProfile>>(
                                    File.ReadAllText(_profilesPath)) ?? new();
            }
            catch (Exception ex) { Log(ex); _profiles = new(); }

            try
            {
                if (File.Exists(_settingsPath))
                    _settings = JsonSerializer.Deserialize<AppSettings>(
                                    File.ReadAllText(_settingsPath)) ?? new();
            }
            catch (Exception ex) { Log(ex); _settings = new(); }
        }

        private void SaveProfiles()
        {
            try { File.WriteAllText(_profilesPath, Serialize(_profiles)); }
            catch (Exception ex) { Log(ex); }
        }

        private static string Serialize(object obj) =>
            JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });

        private static void Log(Exception ex) =>
            System.Diagnostics.Debug.WriteLine($"[ProfileManager] {ex.Message}");
    }
}
