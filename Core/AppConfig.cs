using System;
using System.Collections.Generic;

namespace GearDown.Core
{
    public class AppConfig
    {
        public int Cpu { get; set; } = 100;
        public int GpuMode { get; set; } = 0;
        public int Gpu { get; set; } = 1800;
        public int TargetTemp { get; set; } = 75;
        public int MaxCapMhz { get; set; } = 2200;
        public List<CustomProfile> Profiles { get; set; } = new List<CustomProfile>();
    }
}
