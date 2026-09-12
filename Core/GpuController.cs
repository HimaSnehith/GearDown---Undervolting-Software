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

    public class GpuTelemetry
    {
        public int Temperature { get; set; }
        public int Utilization { get; set; }
        public string FanSpeed { get; set; } = "Auto";
        public int CurrentClockMhz { get; set; }
        public string GpuName { get; set; } = "";
        public string DriverVersion { get; set; } = "";
        public string PcieInfo { get; set; } = "";
    }

    public class GpuController
    {
        public bool IsNvidiaAvailable { get; private set; } = true;
        public GpuControlMode Mode { get; set; } = GpuControlMode.Disabled;
        public ThermalGovernor Governor { get; } = new ThermalGovernor();
        public int FixedMaxMhz { get; private set; } = 1800;

        public int GetCurrentTemp()
        {
            string output = RunNvidiaCommand("--query-gpu=temperature.gpu --format=csv,noheader,nounits");
            if (int.TryParse(output, out int temp)) return temp;
            return 0;
        }

        public GpuTelemetry GetLiveTelemetry()
        {
            var telemetry = new GpuTelemetry();
            string output = RunNvidiaCommand("--query-gpu=name,driver_version,temperature.gpu,utilization.gpu,fan.speed,clocks.current.graphics,pcie.link.gen.current,pcie.link.width.current --format=csv,noheader,nounits");
            if (!string.IsNullOrEmpty(output) && output != "ERROR")
            {
                var parts = output.Split(',');
                if (parts.Length >= 4)
                {
                    telemetry.GpuName = parts[0].Trim();
                    telemetry.DriverVersion = parts[1].Trim();
                    if (int.TryParse(parts[2].Trim(), out int temp)) telemetry.Temperature = temp;
                    if (int.TryParse(parts[3].Trim(), out int util)) telemetry.Utilization = util;

                    if (parts.Length >= 5)
                    {
                        string fan = parts[4].Trim();
                        telemetry.FanSpeed = (fan.Contains("N/A") || string.IsNullOrEmpty(fan)) ? "Auto" : $"{fan}%";
                    }
                    if (parts.Length >= 6 && int.TryParse(parts[5].Trim(), out int clock))
                    {
                        telemetry.CurrentClockMhz = clock;
                    }
                    if (parts.Length >= 8)
                    {
                        string gen = parts[6].Trim();
                        string width = parts[7].Trim();
                        if (!gen.Contains("N/A") && !width.Contains("N/A"))
                            telemetry.PcieInfo = $"PCIe {gen}.0 x{width}";
                    }
                }
            }
            return telemetry;
        }

        public string SetClockLimit(int maxMhz)
        {
            if (maxMhz < 210) maxMhz = 210;
            FixedMaxMhz = maxMhz;
            Mode = GpuControlMode.FixedFrequency;
            
            // Allow idle (210), cap at Max
            return RunNvidiaCommand($"-lgc 210,{maxMhz}");
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
                RunNvidiaCommand($"-lgc 210,{newMhz}");
                activeDynamicMhz = newMhz;
                return true;
            }

            return false;
        }

        private string RunNvidiaCommand(string arguments)
        {
            try
            {
                Process p = new Process();
                p.StartInfo.FileName = "nvidia-smi";
                p.StartInfo.Arguments = arguments;
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.CreateNoWindow = true;
                p.Start();
                string output = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit();
                return output;
            }
            catch { return "ERROR"; }
        }
    }
}