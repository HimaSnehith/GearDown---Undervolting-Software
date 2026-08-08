using System;

namespace GearDown.Core
{
    public class ThermalGovernor
    {
        public int TargetTemp { get; set; } = 75;
        public int MinMhz { get; set; } = 210;
        public int MaxMhzCap { get; set; } = 2500;
        public int CurrentDynamicMhz { get; private set; } = 1500;

        private int _previousTemp = 0;

        public ThermalGovernor()
        {
            CurrentDynamicMhz = 1500;
        }

        public void Initialize(int targetTemp, int startingMhz, int maxMhzCap)
        {
            TargetTemp = Math.Clamp(targetTemp, 50, 90);
            MaxMhzCap = Math.Max(startingMhz, maxMhzCap);
            CurrentDynamicMhz = Math.Clamp(startingMhz, MinMhz, MaxMhzCap);
            _previousTemp = 0;
        }

        public bool ProcessTick(int currentTemp, out int newMhz)
        {
            newMhz = CurrentDynamicMhz;

            // If temperature readings are invalid (0°C), don't alter limits
            if (currentTemp <= 0) return false;

            int tempDelta = (_previousTemp > 0) ? (currentTemp - _previousTemp) : 0;
            _previousTemp = currentTemp;

            int error = currentTemp - TargetTemp;

            // --- SMOOTH THERMAL EQUILIBRIUM LOGIC ---
            // Soft Deadband: within [Target - 2°C, Target + 1°C] and stable/falling temp, hold clock steady!
            // This prevents frame stutter caused by aggressive clock hunting.
            if (error >= -2 && error <= 1 && tempDelta <= 0)
            {
                return false;
            }

            int step = 0;

            if (error > 1)
            {
                // Slightly above target -> Micro-adjust down gently (15-30 MHz) to find equilibrium
                if (error >= 6) step = 60;
                else if (error >= 3) step = 30;
                else step = 15;

                // Damped adjustment if temp is rising fast
                if (tempDelta > 1) step += tempDelta * 10;

                newMhz = Math.Max(MinMhz, CurrentDynamicMhz - step);
            }
            else if (error < -2)
            {
                // Cooler than target zone -> Micro-recover clock headroom gently (15-35 MHz)
                if (tempDelta >= 2) return false; // Hold steady while temp is ramping

                int deficit = -error;
                if (deficit >= 8) step = 35;
                else if (deficit >= 4) step = 25;
                else step = 15;

                newMhz = Math.Min(MaxMhzCap, CurrentDynamicMhz + step);
            }

            if (newMhz != CurrentDynamicMhz)
            {
                CurrentDynamicMhz = newMhz;
                return true;
            }

            return false;
        }
    }
}
