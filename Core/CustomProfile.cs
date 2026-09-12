using System;

namespace GearDown.Core
{
    public class CustomProfile
    {
        public string Name { get; set; } = string.Empty;
        public int Cpu { get; set; } = 100;
        public int GpuMode { get; set; } = 0; // 0: FixedFrequency, 1: TemperatureLock
        public int GpuFreq { get; set; } = 1800;
        public int TargetTemp { get; set; } = 75;
        public int MaxCapMhz { get; set; } = 2200;

        public string Summary => GpuMode == 1
            ? $"TEMP LOCK {TargetTemp}°C · CAP {MaxCapMhz} MHz · CPU {Cpu}%"
            : $"FIXED CAP {GpuFreq} MHz · CPU {Cpu}%";
    }
}
