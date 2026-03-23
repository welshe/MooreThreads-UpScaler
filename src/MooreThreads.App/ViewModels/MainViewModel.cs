using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using MooreThreadsUpScaler.Core.GPU;
using MooreThreadsUpScaler.Core.Profiles;
using MooreThreadsUpScaler.Core.Windowing;

namespace MooreThreadsUpScaler.ViewModels
{
    public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly WindowManager     _windowManager;
        private readonly ProfileManager    _profileManager;
        private readonly OptiScalerManager _optiScalerManager;

        private bool _disposed;

        // ── GPU ─────────────────────────────────────────────────────────────────
        private GpuInfo _gpu = new();
        public GpuInfo Gpu
        {
            get => _gpu;
            private set { _gpu = value; OnPropertyChanged(); OnPropertyChanged(nameof(GpuLabel)); OnPropertyChanged(nameof(GpuVramLabel)); }
        }
        public string GpuLabel     => _gpu.Name;
        public string GpuVramLabel => _gpu.VramText;

        // ── Windows ──────────────────────────────────────────────────────────────
        private ObservableCollection<WindowInfo> _windows = new();
        public ObservableCollection<WindowInfo> AvailableWindows
        {
            get => _windows;
            set { _windows = value; OnPropertyChanged(); }
        }

        private WindowInfo? _selectedWindow;
        public WindowInfo? SelectedWindow
        {
            get => _selectedWindow;
            set { _selectedWindow = value; OnPropertyChanged(); OnWindowSelected(); }
        }

        // ── Profiles ─────────────────────────────────────────────────────────────
        private ObservableCollection<ProfileEntry> _profiles = new();
        public ObservableCollection<ProfileEntry> Profiles
        {
            get => _profiles;
            set { _profiles = value; OnPropertyChanged(); }
        }

        private ProfileEntry? _selectedProfile;
        public ProfileEntry? SelectedProfile
        {
            get => _selectedProfile;
            set { _selectedProfile = value; OnPropertyChanged(); }
        }

        // ── Frame Generation ─────────────────────────────────────────────────────
        private bool _frameGenEnabled = true;
        public bool FrameGenEnabled
        {
            get => _frameGenEnabled;
            set { _frameGenEnabled = value; OnPropertyChanged(); }
        }

        private int _frameGenMultiplier = 2;
        public int FrameGenMultiplier
        {
            get => _frameGenMultiplier;
            set { _frameGenMultiplier = Math.Clamp(value, 2, 4); OnPropertyChanged(); }
        }

        // ── Upscaler ─────────────────────────────────────────────────────────────
        // These mirror the upscalers OptiScaler supports:
        //   FSR 2 — AMD FidelityFX Super Resolution 2
        //   FSR 3 — AMD FidelityFX Super Resolution 3 (with native frame gen)
        //   XeSS  — Intel Xe Super Sampling
        //   DLSS  — NVIDIA Deep Learning Super Sampling (requires DLSS-capable GPU)
        private ObservableCollection<string> _upscalers = new()
        {
            "FSR 2.2", "FSR 3.0", "XeSS", "DLSS"
        };
        public ObservableCollection<string> Upscalers
        {
            get => _upscalers;
            set { _upscalers = value; OnPropertyChanged(); }
        }

        private string _selectedUpscaler = "FSR 2.2";
        public string SelectedUpscaler
        {
            get => _selectedUpscaler;
            set { _selectedUpscaler = value; OnPropertyChanged(); }
        }

        // ── Injection state ───────────────────────────────────────────────────────
        private bool _isInjected;
        public bool IsInjected
        {
            get => _isInjected;
            private set { _isInjected = value; OnPropertyChanged(); OnPropertyChanged(nameof(ScaleButtonLabel)); }
        }
        public string ScaleButtonLabel => _isInjected ? "REMOVE" : "INJECT";

        private string _statusText = "Ready";
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        // ── Commands ─────────────────────────────────────────────────────────────
        public ICommand RefreshWindowsCommand { get; }
        public ICommand ToggleScalingCommand  { get; }
        public ICommand AddProfileCommand     { get; }
        public ICommand LoadProfileCommand    { get; }
        public ICommand DeleteProfileCommand  { get; }
        public ICommand SaveProfileCommand    => AddProfileCommand;

        // ── Constructor ──────────────────────────────────────────────────────────
        public MainViewModel()
        {
            _windowManager     = App.Services.GetRequiredService<WindowManager>();
            _profileManager    = App.Services.GetRequiredService<ProfileManager>();
            _optiScalerManager = App.Services.GetRequiredService<OptiScalerManager>();

            Gpu = GpuDetector.Detect();

            // Pre-select the best upscaler for the detected GPU
            _selectedUpscaler = Gpu.Vendor switch
            {
                GpuVendor.Nvidia       => "DLSS",
                GpuVendor.AMD          => "FSR 3.0",
                GpuVendor.MooreThreads => "FSR 2.2",
                _                      => "FSR 2.2"
            };

            RefreshWindowsCommand = new RelayCommand(async _ => await RefreshWindowsAsync());
            ToggleScalingCommand  = new RelayCommand(async _ => await ToggleInjectionAsync());
            AddProfileCommand     = new RelayCommand(_ => AddProfile());
            LoadProfileCommand    = new RelayCommand(_ => LoadSelectedProfile(), _ => SelectedProfile is not null);
            DeleteProfileCommand  = new RelayCommand(_ => DeleteSelectedProfile(),
                _ => SelectedProfile is not null && SelectedProfile.ProcessName != "Default");

            RefreshProfiles();
            _ = RefreshWindowsAsync();
            _ = CheckStatusAsync();
        }

        // ── Status check ─────────────────────────────────────────────────────────
        private async Task CheckStatusAsync()
        {
            StatusText = await _optiScalerManager.GetStatusAsync();
        }

        // ── Window refresh ───────────────────────────────────────────────────────
        public async Task RefreshWindowsAsync()
        {
            StatusText = "Scanning for games…";
            try
            {
                var windows = await Task.Run(() => _windowManager.GetAvailableWindows());
                AvailableWindows = new ObservableCollection<WindowInfo>(windows);
                StatusText = $"Found {windows.Count} windows";
            }
            catch (Exception ex) { StatusText = $"Error: {ex.Message}"; }
        }

        // ── Injection toggle ─────────────────────────────────────────────────────
        // How this works (matching Lossless Scaling's approach):
        //   1. User selects a running game window
        //   2. We locate the game's executable directory via its process
        //   3. We copy OptiScaler's DLL (version.dll / dxgi.dll) into that directory
        //   4. We write an OptiScaler.ini config with the user's chosen settings
        //   5. User restarts the game — OptiScaler loads automatically via DLL hijacking
        //   6. INJECT button changes to REMOVE; clicking removes the DLL + config
        //
        // This is exactly how Lossless Scaling 3.0 works under the hood — it uses
        // OptiScaler as its backend for DLSS/FSR/XeSS support and frame generation.
        public async Task ToggleInjectionAsync()
        {
            if (SelectedWindow is null)
            {
                StatusText = "Select a target game window first";
                return;
            }

            if (IsInjected)
            {
                StatusText = "Removing OptiScaler…";
                var result = await _optiScalerManager.RemoveAsync(SelectedWindow.ProcessName);
                StatusText = result;
                IsInjected = false;
            }
            else
            {
                StatusText = "Injecting OptiScaler…";
                var result = await _optiScalerManager.InjectAsync(
                    SelectedWindow.ProcessName,
                    SelectedWindow.Title,
                    FrameGenEnabled,
                    FrameGenMultiplier,
                    SelectedUpscaler);
                StatusText = result;
                IsInjected = result.StartsWith("Success", StringComparison.OrdinalIgnoreCase);
            }
        }

        // ── Profile management ───────────────────────────────────────────────────
        private void AddProfile()
        {
            string processName = SelectedWindow?.ProcessName ?? "Default";
            string displayName = SelectedWindow?.Title       ?? "Default";

            var profile = _profileManager.GetByProcess(processName)
                       ?? _profileManager.CreateProfile(processName, displayName);

            profile.FrameGenEnabled    = FrameGenEnabled;
            profile.FrameGenMultiplier = FrameGenMultiplier;
            // Store selected upscaler in the Algorithm field
            profile.Algorithm = SelectedUpscaler;

            _profileManager.Update(profile);
            RefreshProfiles();
            SelectedProfile = Profiles.FirstOrDefault(p => p.ProcessName == processName);
            StatusText = $"Profile saved: {displayName}";
        }

        private void LoadSelectedProfile()
        {
            if (SelectedProfile is null) return;
            var profile = _profileManager.GetByProcess(SelectedProfile.ProcessName);
            if (profile is null) { StatusText = "Profile not found"; return; }
            ApplyProfile(profile);
            StatusText = $"Profile loaded: {profile.DisplayName}";
        }

        private void DeleteSelectedProfile()
        {
            if (SelectedProfile is null || SelectedProfile.ProcessName == "Default") return;
            var profile = _profileManager.GetByProcess(SelectedProfile.ProcessName);
            if (profile is not null)
            {
                _profileManager.Delete(profile.Id);
                StatusText = $"Profile deleted: {SelectedProfile.DisplayName}";
                RefreshProfiles();
            }
        }

        public void RenameSelectedProfile(string newName)
        {
            if (SelectedProfile is null || string.IsNullOrWhiteSpace(newName)) return;
            var profile = _profileManager.GetByProcess(SelectedProfile.ProcessName);
            if (profile is null) return;
            profile.DisplayName = newName.Trim();
            _profileManager.Update(profile);
            RefreshProfiles();
            StatusText = $"Profile renamed to: {newName.Trim()}";
        }

        // Raised when the view should show a rename dialog
        public event EventHandler<ProfileEntry>? RenameRequested;
        public void RequestRenameProfile() => RenameRequested?.Invoke(this, SelectedProfile!);

        private void RefreshProfiles()
        {
            var entries = new ObservableCollection<ProfileEntry>();

            // Always ensure a Default profile exists
            var def = _profileManager.GetByProcess("Default")
                   ?? _profileManager.CreateProfile("Default", "Default");
            entries.Add(new ProfileEntry { ProcessName = def.ProcessName, DisplayName = def.DisplayName });

            foreach (var p in _profileManager.Profiles)
            {
                if (p.ProcessName == "Default") continue;
                entries.Add(new ProfileEntry { ProcessName = p.ProcessName, DisplayName = p.DisplayName });
            }

            Profiles = entries;
            SelectedProfile = SelectedProfile is not null
                ? Profiles.FirstOrDefault(p => p.ProcessName == SelectedProfile.ProcessName) ?? Profiles.FirstOrDefault()
                : Profiles.FirstOrDefault();
        }

        // ── Helpers ──────────────────────────────────────────────────────────────
        private void OnWindowSelected()
        {
            if (SelectedWindow is null) return;

            // Auto-apply saved profile for this game if one exists
            var profile = _profileManager.GetByProcess(SelectedWindow.ProcessName);
            if (profile is not null)
            {
                ApplyProfile(profile);
                SelectedProfile = Profiles.FirstOrDefault(p => p.ProcessName == SelectedWindow.ProcessName);
                StatusText = $"Profile loaded: {profile.DisplayName}";
            }

            // Reset injection state — a different game was selected
            IsInjected = false;
        }

        private void ApplyProfile(AppProfile p)
        {
            FrameGenEnabled    = p.FrameGenEnabled;
            FrameGenMultiplier = p.FrameGenMultiplier;
            if (!string.IsNullOrEmpty(p.Algorithm) && Upscalers.Contains(p.Algorithm))
                SelectedUpscaler = p.Algorithm;
        }

        // ── INotifyPropertyChanged ───────────────────────────────────────────────
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // ── IDisposable ──────────────────────────────────────────────────────────
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }

    // ── Profile sidebar entry ────────────────────────────────────────────────────
    public class ProfileEntry
    {
        public string ProcessName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public override string ToString() => DisplayName;
    }

    // ── RelayCommand ─────────────────────────────────────────────────────────────
    public sealed class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;

        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute    = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add    => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? p) => _canExecute?.Invoke(p) ?? true;
        public void Execute(object? p)    => _execute(p);
    }

    // ── OptiScaler Manager ───────────────────────────────────────────────────────
    //
    // OptiScaler is an open-source project that enables FSR 2/3, XeSS, and DLSS
    // in games that don't natively support them, by acting as a drop-in DLL.
    // This is the same mechanism Lossless Scaling 3.0 uses for its upscaling.
    //
    // OptiScaler GitHub: https://github.com/cdozdil/OptiScaler
    // License: MIT
    //
    public class OptiScalerManager
    {
        private readonly string _storageDir;

        // OptiScaler can be dropped as several DLL names depending on what the game loads.
        // version.dll works for the vast majority of DX11/DX12 games.
        private const string DllName = "version.dll";

        // Latest stable release download (direct asset link from GitHub releases)
        private const string DownloadUrl =
            "https://github.com/cdozdil/OptiScaler/releases/latest/download/OptiScaler.zip";

        public OptiScalerManager()
        {
            _storageDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MooreThreadsUpScaler", "OptiScaler");
            Directory.CreateDirectory(_storageDir);
        }

        public Task<string> GetStatusAsync()
        {
            var dll = Path.Combine(_storageDir, DllName);
            return Task.FromResult(File.Exists(dll)
                ? "OptiScaler ready"
                : "OptiScaler not downloaded — will download on first inject");
        }

        /// <summary>
        /// Copies OptiScaler into the game's directory and writes a config file.
        /// The game must be restarted for the injection to take effect.
        /// </summary>
        public async Task<string> InjectAsync(
            string processName, string gameTitle,
            bool frameGen, int multiplier, string upscaler)
        {
            try
            {
                // 1. Find the game's folder by inspecting its running process
                var gamePath = GetProcessDirectory(processName);
                if (gamePath is null)
                    return $"Cannot find {processName} — make sure the game is running.";

                // 2. Ensure OptiScaler DLL is available locally
                var localDll = Path.Combine(_storageDir, DllName);
                if (!File.Exists(localDll))
                {
                    var downloaded = await DownloadOptiScalerAsync();
                    if (!downloaded)
                        return "Failed to download OptiScaler. Check your internet connection.\n" +
                               "You can also manually download from:\n" +
                               "https://github.com/cdozdil/OptiScaler/releases";
                }

                // 3. Copy DLL into game directory
                var destDll = Path.Combine(gamePath, DllName);
                File.Copy(localDll, destDll, overwrite: true);

                // 4. Write OptiScaler.ini with the chosen settings
                var ini = BuildIni(upscaler, frameGen, multiplier);
                File.WriteAllText(Path.Combine(gamePath, "OptiScaler.ini"), ini);

                return $"Success! Injected into {gameTitle}.\nRestart the game to apply settings.";
            }
            catch (UnauthorizedAccessException)
            {
                return "Access denied — try running Moore Threads UpScaler as Administrator.";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        /// <summary>
        /// Removes OptiScaler files from the game's directory.
        /// </summary>
        public Task<string> RemoveAsync(string processName)
        {
            try
            {
                var gamePath = GetProcessDirectory(processName);
                if (gamePath is null)
                    return Task.FromResult("Game not found. If already closed, remove version.dll manually from the game folder.");

                int removed = 0;
                foreach (var file in new[] { DllName, "OptiScaler.ini", "OptiScaler.log" })
                {
                    var path = Path.Combine(gamePath, file);
                    if (File.Exists(path)) { File.Delete(path); removed++; }
                }

                return Task.FromResult(removed > 0
                    ? "OptiScaler removed. Restart the game to apply."
                    : "No OptiScaler files found in game directory.");
            }
            catch (UnauthorizedAccessException)
            {
                return Task.FromResult("Access denied — try running as Administrator.");
            }
            catch (Exception ex)
            {
                return Task.FromResult($"Error: {ex.Message}");
            }
        }

        private static string? GetProcessDirectory(string processName)
        {
            try
            {
                var procs = Process.GetProcessesByName(processName);
                if (procs.Length == 0) return null;
                var module = procs[0].MainModule;
                return module is not null ? Path.GetDirectoryName(module.FileName) : null;
            }
            catch (Exception ex) 
            { 
                System.Diagnostics.Debug.WriteLine($"[OptiScalerManager] GetProcessDirectory failed: {ex.Message}");
                return null; 
            }
        }

        private async Task<bool> DownloadOptiScalerAsync()
        {
            try
            {
                // Download the zip, extract version.dll
                var zipPath = Path.Combine(_storageDir, "OptiScaler.zip");

                using var client = new System.Net.Http.HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "MooreThreadsUpScaler/1.0");
                var data = await client.GetByteArrayAsync(DownloadUrl);
                File.WriteAllBytes(zipPath, data);

                // Extract just version.dll from the zip
                using var zip = System.IO.Compression.ZipFile.OpenRead(zipPath);
                foreach (var entry in zip.Entries)
                {
                    if (entry.Name.Equals(DllName, StringComparison.OrdinalIgnoreCase))
                    {
                        entry.ExtractToFile(Path.Combine(_storageDir, DllName), overwrite: true);
                        break;
                    }
                }

                File.Delete(zipPath);
                return File.Exists(Path.Combine(_storageDir, DllName));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OptiScalerManager] DownloadOptiScalerAsync failed: {ex.Message}");
                return false;
            }
        }

        private static string BuildIni(string upscaler, bool frameGen, int multiplier)
        {
            // Map friendly names to OptiScaler's internal identifiers
            string upscalerKey = upscaler switch
            {
                "DLSS"    => "DLSS",
                "XeSS"    => "XeSS",
                "FSR 3.0" => "FSR3",
                _         => "FSR22"   // FSR 2.2 default
            };

            return $"""
[OptiScaler]
Upscaler={upscalerKey}
FrameGeneration={(frameGen ? "true" : "false")}
FrameGenerationMultiplier={multiplier}

[Overlay]
Enabled=true
ToggleKey=Insert

[Logging]
Enabled=false
""";
        }
    }
}
