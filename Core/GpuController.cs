using System;
using System.Diagnostics;

namespace GearDown.Core
{
    public enum GpuControlMode
    {
        Disabled = 0,        // Stock / Uncapped (Default out-of-box, no limits applied)
        FixedFrequency = 1,  // Fixed Clock Cap
        TemperatureLock = 2  // Dynamic Temperature Lock
    }

    public struct GpuTelemetry
    {
        public int Temperature;
        public int CurrentClockMhz;
        public int MaxHardwareClockMhz;
        public string DriverVersion;
        public double PowerDrawWatts;
        public int VramUsedMb;
        public int VramTotalMb;
        public string GpuName;
    }

    public class GpuController
    {
        public bool IsNvidiaAvailable { get; private set; } = true;
        public GpuControlMode Mode { get; set; } = GpuControlMode.Disabled;
        public ThermalGovernor Governor { get; } = new ThermalGovernor();
        public int FixedMaxMhz { get; private set; } = 1800;
        public int HardwareMinMhz { get; private set; } = 210;
        public int HardwareMaxMhz { get; private set; } = 3105;

        public GpuTelemetry GetTelemetry()
        {
            string output = RunNvidiaCommand("--query-gpu=temperature.gpu,clocks.current.graphics,clocks.max.graphics,driver_version,power.draw,memory.used,memory.total,name --format=csv,noheader,nounits");
            var result = new GpuTelemetry
            {
                Temperature = 0,
                CurrentClockMhz = 0,
                MaxHardwareClockMhz = HardwareMaxMhz,
                DriverVersion = "--",
                PowerDrawWatts = 0,
                VramUsedMb = 0,
                VramTotalMb = 0,
                GpuName = "NVIDIA GPU"
            };

            if (string.IsNullOrWhiteSpace(output) || output == "ERROR") return result;

            var parts = output.Split(',');
            if (parts.Length >= 8)
            {
                if (int.TryParse(parts[0].Trim(), out int temp)) result.Temperature = temp;
                if (int.TryParse(parts[1].Trim(), out int clk)) result.CurrentClockMhz = clk;
                if (int.TryParse(parts[2].Trim(), out int maxHwClk) && maxHwClk > 500)
                {
                    result.MaxHardwareClockMhz = maxHwClk;
                    HardwareMaxMhz = maxHwClk;
                    Governor.HardwareMaxMhz = maxHwClk;
                }
                result.DriverVersion = parts[3].Trim();
                if (double.TryParse(parts[4].Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double pwr)) result.PowerDrawWatts = pwr;
                if (int.TryParse(parts[5].Trim(), out int vUsed)) result.VramUsedMb = vUsed;
                if (int.TryParse(parts[6].Trim(), out int vTotal)) result.VramTotalMb = vTotal;
                result.GpuName = parts[7].Trim();
            }
            return result;
        }

        public int GetCurrentTemp()
        {
            return GetTelemetry().Temperature;
        }

        public string SetClockLimit(int maxMhz)
        {
            if (maxMhz < HardwareMinMhz) maxMhz = HardwareMinMhz;
            if (HardwareMaxMhz > HardwareMinMhz && maxMhz > HardwareMaxMhz) maxMhz = HardwareMaxMhz;
            FixedMaxMhz = maxMhz;
            Mode = GpuControlMode.FixedFrequency;
            
            // Allow idle clock (e.g. 210 MHz), cap at maxMhz
            return RunNvidiaCommand($"-lgc {HardwareMinMhz},{maxMhz}");
        }

        public void ResetLimits()
        {
            RunNvidiaCommand("-rgc");
            Mode = GpuControlMode.Disabled;
        }

        public bool ProcessThermalGovernorTick(int currentTemp, out int activeDynamicMhz)
        {
            activeDynamicMhz = Governor.CurrentDynamicMhz;
            if (Mode != GpuControlMode.TemperatureLock) return false;

            if (Governor.ProcessTick(currentTemp, out int newMhz))
            {
                RunNvidiaCommand($"-lgc {HardwareMinMhz},{newMhz}");
                activeDynamicMhz = newMhz;
                return true;
            }

            return false;
        }

        private string RunNvidiaCommand(string arguments)
        {
            try
            {
                using var p = new Process();
                p.StartInfo.FileName = "nvidia-smi";
                p.StartInfo.Arguments = arguments;
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.CreateNoWindow = true;
                p.Start();
                string output = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit(3000);
                return output;
            }
            catch { return "ERROR"; }
        }
    }
}