using System;

namespace GearDown.Core
{
    public class ThermalGovernor
    {
        public int TargetTemp { get; set; } = 75;
        public int HardwareMinMhz { get; set; } = 210;
        public int HardwareMaxMhz { get; set; } = 3105;
        public int MinMhz { get; private set; } = 210;
        public int MaxMhzCap { get; private set; } = 2500;
        public int CurrentDynamicMhz { get; private set; } = 1500;

        private double _filteredTemp = 0;
        private int _previousTemp = 0;

        public ThermalGovernor()
        {
            CurrentDynamicMhz = 1500;
        }

        public void Initialize(int targetTemp, int startingMhz, int maxMhzCap)
        {
            TargetTemp = Math.Clamp(targetTemp, 50, 90);
            MinMhz = Math.Max(210, HardwareMinMhz);
            
            // Respect hardware ceiling if discovered, otherwise allow requested cap
            int upperLimit = HardwareMaxMhz > MinMhz ? HardwareMaxMhz : 3500;
            MaxMhzCap = Math.Clamp(maxMhzCap, MinMhz, upperLimit);
            
            CurrentDynamicMhz = Math.Clamp(startingMhz, MinMhz, MaxMhzCap);
            _filteredTemp = 0;
            _previousTemp = 0;
        }

        public bool ProcessTick(int currentTemp, out int newMhz)
        {
            newMhz = CurrentDynamicMhz;

            // If temperature readings are invalid (0°C), don't alter limits
            if (currentTemp <= 0) return false;

            // Exponential Moving Average (EMA) temperature smoothing (0.7 weight on new, 0.3 on history)
            if (_filteredTemp <= 0) _filteredTemp = currentTemp;
            else _filteredTemp = (_filteredTemp * 0.3) + (currentTemp * 0.7);

            double effectiveTemp = _filteredTemp;
            int tempDelta = (_previousTemp > 0) ? (currentTemp - _previousTemp) : 0;
            _previousTemp = currentTemp;

            double error = effectiveTemp - TargetTemp;

            // Deadband zone: [Target - 2°C, Target + 1°C] -> hold clock rock steady if temp is stable
            if (error >= -2.0 && error <= 1.0 && tempDelta <= 0)
            {
                return false;
            }

            int step = 0;

            if (error > 1.0)
            {
                // Hotter than target zone -> Micro-step down smoothly (10-45 MHz)
                if (error >= 6.0) step = 45;
                else if (error >= 3.0) step = 25;
                else step = 10;

                // Damped adjustment if temp is rising fast
                if (tempDelta > 1) step += tempDelta * 8;

                newMhz = Math.Max(MinMhz, CurrentDynamicMhz - step);
            }
            else if (error < -2.0)
            {
                // Cooler than target zone -> Micro-recover clock headroom smoothly (10-30 MHz)
                if (tempDelta >= 2) return false; // Hold steady while temp is ramping up

                double deficit = -error;
                if (deficit >= 8.0) step = 30;
                else if (deficit >= 4.0) step = 20;
                else step = 10;

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
