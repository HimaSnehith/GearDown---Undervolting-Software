using System;
using System.Diagnostics;

namespace GearDown.Core
{
    public enum GpuControlMode
    {
        FixedFrequency,
        TemperatureLock
    }

    public class GpuController
    {
        public bool IsNvidiaAvailable { get; private set; } = true;
        public GpuControlMode Mode { get; set; } = GpuControlMode.FixedFrequency;
        public ThermalGovernor Governor { get; } = new ThermalGovernor();
        public int FixedMaxMhz { get; private set; } = 1800;

        public int GetCurrentTemp()
        {
            // Bulletproof temperature reading
            string output = RunNvidiaCommand("--query-gpu=temperature.gpu --format=csv,noheader");
            if (int.TryParse(output, out int temp)) return temp;
            return 0;
        }

        public string SetClockLimit(int maxMhz)
        {
            if (maxMhz < 210) maxMhz = 210;
            FixedMaxMhz = maxMhz;
            
            // This is the magic command: Allow idle (210), cap at Max
            return RunNvidiaCommand($"-lgc 210,{maxMhz}");
        }

        public void ResetLimits() => RunNvidiaCommand("-rgc");

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