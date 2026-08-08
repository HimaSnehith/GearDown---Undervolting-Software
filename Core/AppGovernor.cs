using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace GearDown.Core
{
    public class AppProfile
    {
        public string ProcessName { get; set; } = string.Empty; // e.g., "cyberpunk2077"
        public string DisplayName { get; set; } = string.Empty;
        public GpuControlMode Mode { get; set; } = GpuControlMode.TemperatureLock;
        public int TargetTemp { get; set; } = 75;
        public int MaxMhz { get; set; } = 2200;
        public int CpuThrottle { get; set; } = 100;

        public string DisplaySummary => Mode == GpuControlMode.TemperatureLock
            ? $"TEMP LOCK {TargetTemp}°C (CAP {MaxMhz} MHz)"
            : $"FIXED CAP {MaxMhz} MHz";
    }

    public class AppGovernor
    {
        public List<AppProfile> Profiles { get; set; } = new List<AppProfile>();
        public bool IsEnabled { get; set; } = false;

        public AppProfile? CurrentActiveProfile { get; private set; } = null;
        public string CurrentForegroundProcessName { get; private set; } = string.Empty;

        public event Action<AppProfile?, string>? ForegroundProcessChanged;

        // --- WIN32 IMPORTS ---
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, uint flags, StringBuilder lpExeName, ref uint size);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        public string GetForegroundProcessExeName()
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return string.Empty;

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return string.Empty;

            try
            {
                IntPtr hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (hProcess != IntPtr.Zero)
                {
                    try
                    {
                        uint capacity = 1024;
                        StringBuilder sb = new StringBuilder((int)capacity);
                        if (QueryFullProcessImageName(hProcess, 0, sb, ref capacity))
                        {
                            string fullPath = sb.ToString();
                            return Path.GetFileNameWithoutExtension(fullPath).ToLowerInvariant();
                        }
                    }
                    finally
                    {
                        CloseHandle(hProcess);
                    }
                }

                // Fallback via System.Diagnostics.Process
                using var proc = Process.GetProcessById((int)pid);
                return proc.ProcessName.ToLowerInvariant();
            }
            catch
            {
                return string.Empty;
            }
        }

        public bool EvaluateForegroundProcess(out AppProfile? activeProfile, out string processName)
        {
            processName = GetForegroundProcessExeName();
            activeProfile = null;

            bool processChanged = !string.Equals(CurrentForegroundProcessName, processName, StringComparison.OrdinalIgnoreCase);
            CurrentForegroundProcessName = processName;

            if (IsEnabled && !string.IsNullOrEmpty(processName))
            {
                foreach (var profile in Profiles)
                {
                    if (string.Equals(profile.ProcessName, processName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(Path.GetFileNameWithoutExtension(profile.ProcessName), processName, StringComparison.OrdinalIgnoreCase))
                    {
                        activeProfile = profile;
                        break;
                    }
                }
            }

            bool profileChanged = (CurrentActiveProfile != activeProfile);
            if (profileChanged || processChanged)
            {
                CurrentActiveProfile = activeProfile;
                ForegroundProcessChanged?.Invoke(activeProfile, processName);
            }

            return profileChanged;
        }
    }
}
