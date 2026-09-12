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
        public string GpuName;
    }

    public class GpuController
    {
        public bool IsNvidiaAvailable { get; private set; } = true;
        public GpuControlMode Mode { get; set; } = GpuControlMode.Disabled;
        public ThermalGovernor Governor { get; } = new ThermalGovernor();
        public int FixedMaxMhz { get; private set; } = 1800;

        public GpuTelemetry GetTelemetry()
        {
            string output = RunNvidiaCommand("--query-gpu=temperature.gpu,clocks.current.graphics,name --format=csv,noheader,nounits");
            var result = new GpuTelemetry { Temperature = 0, CurrentClockMhz = 0, GpuName = "NVIDIA GPU" };

            if (string.IsNullOrWhiteSpace(output) || output == "ERROR") return result;

            var parts = output.Split(',');
            if (parts.Length >= 2)
            {
                if (int.TryParse(parts[0].Trim(), out int temp)) result.Temperature = temp;
                if (int.TryParse(parts[1].Trim(), out int clk)) result.CurrentClockMhz = clk;
            }
            if (parts.Length >= 3)
            {
                result.GpuName = parts[2].Trim();
            }
            return result;
        }

        public int GetCurrentTemp()
        {
            return GetTelemetry().Temperature;
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
                using var p = new Process();
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