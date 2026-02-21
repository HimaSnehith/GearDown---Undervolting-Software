using System.Diagnostics;

namespace GearDown.Core
{
    public class CpuController
    {
        public void SetThrottleLevel(int maxPercentage)
        {
            if (maxPercentage < 50) maxPercentage = 50;
            if (maxPercentage > 100) maxPercentage = 100;

            RunPowerCfg($"-setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX {maxPercentage}");
            RunPowerCfg($"-setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX {maxPercentage}");
            RunPowerCfg("-setactive SCHEME_CURRENT");
        }

        public void ResetLimits() => SetThrottleLevel(100);

        private void RunPowerCfg(string arguments)
        {
            try
            {
                Process p = new Process();
                p.StartInfo.FileName = "powercfg";
                p.StartInfo.Arguments = arguments;
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.CreateNoWindow = true;
                p.Start();
                p.WaitForExit();
            }
            catch { }
        }
    }
}