using System;
using System.Collections.Generic;
using System.Management;
using Microsoft.Win32;

namespace MooreThreadsUpScaler.Core.GPU
{
    public enum GpuVendor { Unknown, Nvidia, AMD, Intel, MooreThreads }

    public class GpuInfo
    {
        public string Name { get; set; } = "Unknown GPU";
        public GpuVendor Vendor { get; set; } = GpuVendor.Unknown;
        public ulong DedicatedVramBytes { get; set; }
        public string DriverVersion { get; set; } = string.Empty;
        public bool IsDiscrete { get; set; }

        public string VramText
        {
            get
            {
                if (DedicatedVramBytes == 0) return "N/A";
                double gb = DedicatedVramBytes / (1024.0 * 1024.0 * 1024.0);
                return gb >= 1.0 ? $"{gb:F1} GB" : $"{DedicatedVramBytes / (1024 * 1024)} MB";
            }
        }

        public string VendorText => Vendor switch
        {
            GpuVendor.Nvidia       => "NVIDIA",
            GpuVendor.AMD          => "AMD",
            GpuVendor.Intel        => "Intel",
            GpuVendor.MooreThreads => "Moore Threads",
            _                      => "Unknown"
        };

        public IReadOnlyList<string> PreferredAlgorithms => Vendor switch
        {
            GpuVendor.Nvidia       => new[] { "NIS",  "LS1", "FSR" },
            GpuVendor.AMD          => new[] { "FSR",  "LS1", "NIS" },
            GpuVendor.Intel        => IsDiscrete ? new[] { "XeSS", "LS1", "FSR" } : new[] { "LS1", "Integer" },
            GpuVendor.MooreThreads => new[] { "MTSR", "LS1", "FSR" },
            _                      => new[] { "LS1",  "Integer"     }
        };
    }

    public static class GpuDetector
    {
        public static GpuInfo Detect()
        {
            var candidates = new List<GpuInfo>();

            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name, AdapterRAM, DriverVersion, PNPDeviceID FROM Win32_VideoController");

                foreach (ManagementObject obj in searcher.Get())
                {
                    var name          = obj["Name"]?.ToString()          ?? string.Empty;
                    var driverVersion = obj["DriverVersion"]?.ToString() ?? string.Empty;
                    var pnpId         = obj["PNPDeviceID"]?.ToString()   ?? string.Empty;

                    // Win32_VideoController.AdapterRAM is uint32 — saturates at 4 294 967 295 (~4 GB).
                    // Read the true dedicated VRAM from the registry key Windows maintains per adapter.
                    ulong vram = ReadVramFromRegistry(pnpId);

                    // Fall back to AdapterRAM if registry read failed
                    if (vram == 0 && obj["AdapterRAM"] is uint adapterRam && adapterRam > 0)
                        vram = adapterRam;

                    var vendor = DetectVendor(name);
                    candidates.Add(new GpuInfo
                    {
                        Name               = name,
                        Vendor             = vendor,
                        DedicatedVramBytes = vram,
                        DriverVersion      = driverVersion,
                        IsDiscrete         = IsDiscreteGpu(vendor, name)
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GpuDetector] WMI detection failed: {ex.Message}");
                return new GpuInfo { Name = "GPU (Unknown)", Vendor = GpuVendor.Unknown };
            }

            if (candidates.Count == 0)
                return new GpuInfo { Name = "GPU (Unknown)", Vendor = GpuVendor.Unknown };

            // Priority: MooreThreads → discrete NVIDIA/AMD/Intel Arc → first found
            var mt = candidates.Find(g => g.Vendor == GpuVendor.MooreThreads);
            if (mt != null) return mt;

            var discrete = candidates.Find(g => g.IsDiscrete);
            return discrete ?? candidates[0];
        }

        /// <summary>
        /// Reads the true dedicated VRAM from the Windows display adapter registry key.
        /// HKLM\SYSTEM\ControlSet001\Control\Class\{4d36e968-...}\0000\HardwareInformation.qwMemorySize
        /// This is a QWORD (8-byte) value that Windows populates correctly regardless of AdapterRAM limits.
        /// </summary>
        private static ulong ReadVramFromRegistry(string pnpDeviceId)
        {
            if (string.IsNullOrEmpty(pnpDeviceId)) return 0;

            // Normalise PNP ID for registry matching (backslashes become #)
            string normalised = pnpDeviceId.Replace('\\', '#').ToUpperInvariant();

            try
            {
                // Display adapter class GUID
                const string displayClassKey =
                    @"SYSTEM\ControlSet001\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

                using var classKey = Registry.LocalMachine.OpenSubKey(displayClassKey);
                if (classKey == null) return 0;

                foreach (var subName in classKey.GetSubKeyNames())
                {
                    // Skip the "Properties" sub-key
                    if (subName.Equals("Properties", StringComparison.OrdinalIgnoreCase)) continue;

                    using var sub = classKey.OpenSubKey(subName);
                    if (sub == null) continue;

                    // Match the adapter via MatchingDeviceId or DriverDesc
                    var matchId = sub.GetValue("MatchingDeviceId")?.ToString()?.ToUpperInvariant() ?? string.Empty;
                    var driverDesc = sub.GetValue("DriverDesc")?.ToString() ?? string.Empty;

                    // The registry stores a shortened form; check if the PNP ID contains the match ID
                    if (!normalised.Contains(matchId) && matchId.Length > 0) continue;

                    // Try QWORD memory size first (most accurate — avoids uint32 cap)
                    var qwMemory = sub.GetValue("HardwareInformation.qwMemorySize");
                    if (qwMemory is long qw && qw > 0)
                        return (ulong)qw;

                    // Fallback: older MemorySize DWORD under HardwareInformation subkey
                    using var hwInfo = sub.OpenSubKey("HardwareInformation");
                    if (hwInfo != null)
                    {
                        var memSize = hwInfo.GetValue("MemorySize");
                        if (memSize is int ms && ms > 0)
                            return (ulong)(uint)ms;
                        if (memSize is long msL && msL > 0)
                            return (ulong)msL;
                    }
                }
            }
            catch (Exception ex)
            {
                // Registry access denied or key missing — caller will fall back to AdapterRAM
                System.Diagnostics.Debug.WriteLine($"[GpuDetector] Registry VRAM read failed: {ex.Message}");
            }

            return 0;
        }

        private static GpuVendor DetectVendor(string name)
        {
            if (string.IsNullOrEmpty(name)) return GpuVendor.Unknown;
            var n = name.ToLowerInvariant();

            if (n.Contains("moore threads") || n.Contains("musa") || n.Contains("mtgpu"))
                return GpuVendor.MooreThreads;
            if (n.Contains("nvidia") || n.Contains("geforce") || n.Contains("quadro") ||
                n.Contains("rtx")    || n.Contains("gtx")     || n.Contains("tesla"))
                return GpuVendor.Nvidia;
            if (n.Contains("amd")    || n.Contains("radeon")  || n.Contains("rx ")   ||
                n.Contains("vega")   || n.Contains("navi")    || n.Contains("rdna")  ||
                n.Contains("firepro"))
                return GpuVendor.AMD;
            if (n.Contains("intel")  || n.Contains("iris")    || n.Contains("uhd graphics") ||
                n.Contains("hd graphics") || n.Contains("arc"))
                return GpuVendor.Intel;

            return GpuVendor.Unknown;
        }

        private static bool IsDiscreteGpu(GpuVendor vendor, string name)
        {
            if (vendor is GpuVendor.Nvidia or GpuVendor.AMD or GpuVendor.MooreThreads)
                return true;
            // Intel Arc GPUs are discrete, integrated Intel graphics are not
            if (vendor == GpuVendor.Intel)
            {
                var n = name.ToLowerInvariant();
                return n.Contains("arc") || n.Contains("a-series") || n.Contains("a7") || 
                       n.Contains("a5") || n.Contains("a3") || n.Contains("battlemage");
            }
            return false;
        }
    }
}
