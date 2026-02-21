using System.Diagnostics;

namespace GearDown.Core
{
    public class GpuController
    {
        public bool IsNvidiaAvailable { get; private set; } = true;

        public int GetCurrentTemp()
        {
            // Bulletproof temperature reading, won't drop to 0
            string output = RunNvidiaCommand("--query-gpu=temperature.gpu --format=csv,noheader");
            if (int.TryParse(output, out int temp)) return temp;
            return 0;
        }

        public string SetClockLimit(int maxMhz)
        {
            if (maxMhz < 210) maxMhz = 210;
            
            // This is the magic command: Allow idle (210), cap at Max
            return RunNvidiaCommand($"-lgc 210,{maxMhz}");
        }

        public void ResetLimits() => RunNvidiaCommand("-rgc");

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