using System;
using System.Diagnostics;

namespace GearDown.Core
{
    public enum GpuControlMode
    {
        FixedFrequency = 0,  // Fixed Clock Cap
        TemperatureLock = 1, // Dynamic Temperature Lock
        Disabled = 2         // Stock / Uncapped (Default out-of-box, no limits applied)
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

        /// <summary>Last temperature polled during monitor tick — avoids a redundant nvidia-smi call.</summary>
        public int LastKnownTemp { get; private set; } = 0;

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
                // New NVIDIA drivers (560+) return temperature as a float (e.g. "42.56").
                // Parse as double and round — handles both old integer and new float formats.
                if (double.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double tempD))
                    result.Temperature = (int)Math.Round(tempD);

                if (double.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double clkD))
                    result.CurrentClockMhz = (int)Math.Round(clkD);

                if (double.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double maxHwClkD) && maxHwClkD > 500)
                {
                    result.MaxHardwareClockMhz = (int)Math.Round(maxHwClkD);
                    HardwareMaxMhz = result.MaxHardwareClockMhz;
                    Governor.HardwareMaxMhz = result.MaxHardwareClockMhz;
                }

                result.DriverVersion = parts[3].Trim();
                if (double.TryParse(parts[4].Trim(), System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double pwr))
                    result.PowerDrawWatts = pwr;

                if (double.TryParse(parts[5].Trim(), System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double vUsed))
                    result.VramUsedMb = (int)Math.Round(vUsed);

                if (double.TryParse(parts[6].Trim(), System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double vTotal))
                    result.VramTotalMb = (int)Math.Round(vTotal);

                result.GpuName = parts[7].Trim();
            }

            LastKnownTemp = result.Temperature;
            return result;
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
                p.StartInfo.RedirectStandardError = true;
                p.StartInfo.CreateNoWindow = true;
                p.Start();
                string output = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit(3000);
                if (!p.HasExited) p.Kill();
                return output;
            }
            catch { return "ERROR"; }
        }
    }
}
