using System;

namespace GearDown.Core
{
    public class ThermalGovernor
    {
        public int TargetTemp { get; set; } = 75;
        public int HardwareMinMhz { get; set; } = 210;
        public int HardwareMaxMhz { get; set; } = 3105;
        public int MinMhz { get; private set; } = 500;
        public int MaxMhzCap { get; private set; } = 2500;
        public int CurrentDynamicMhz { get; private set; } = 1500;

        private double _filteredTemp = 0;
        private int _previousTemp = 0;
        private int _settleCooldownTicks = 0;

        public ThermalGovernor()
        {
            CurrentDynamicMhz = 1500;
        }

        public void Initialize(int targetTemp, int startingMhz, int maxMhzCap)
        {
            TargetTemp = Math.Clamp(targetTemp, 40, 90);
            MinMhz = Math.Max(400, HardwareMinMhz);
            
            int upperLimit = HardwareMaxMhz > MinMhz ? HardwareMaxMhz : 3500;
            MaxMhzCap = Math.Clamp(maxMhzCap, MinMhz, upperLimit);
            
            CurrentDynamicMhz = Math.Clamp(startingMhz, MinMhz, MaxMhzCap);
            _filteredTemp = 0;
            _previousTemp = 0;
            _settleCooldownTicks = 0;
        }

        /// <summary>
        /// Processes a thermal monitor tick. Uses derivative damping (dT/dt) and thermal lag
        /// compensation to avoid the runaway downclocking seen in naive controllers.
        /// </summary>
        public bool ProcessTick(int currentTemp, out int newMhz)
        {
            newMhz = CurrentDynamicMhz;

            // If temperature readings are invalid (0°C), don't alter limits
            if (currentTemp <= 0) return false;

            // Exponential Moving Average (EMA) smoothing (50% current, 50% history)
            if (_filteredTemp <= 0) _filteredTemp = currentTemp;
            else _filteredTemp = (_filteredTemp * 0.5) + (currentTemp * 0.5);

            double effectiveTemp = _filteredTemp;
            int tempDelta = (_previousTemp > 0) ? (currentTemp - _previousTemp) : 0;
            _previousTemp = currentTemp;

            // If we recently changed clocks, give the heatsink 1 tick to respond unless temp is surging
            if (_settleCooldownTicks > 0)
            {
                _settleCooldownTicks--;
                if (tempDelta <= 0 && effectiveTemp <= TargetTemp + 2.0)
                {
                    return false;
                }
            }

            double error = effectiveTemp - TargetTemp;

            // Deadband zone: [Target - 1.5°C, Target + 0.5°C] -> Hold frequency rock steady
            if (error >= -1.5 && error <= 0.5 && tempDelta <= 0)
            {
                return false;
            }

            if (error > 0.5)
            {
                // Temperature is above target:
                // If temp is ALREADY dropping (tempDelta < 0), DO NOT downclock further!
                // The heatsink is currently dissipating heat from the previous adjustment.
                if (tempDelta < 0)
                {
                    return false;
                }

                // If temp is flat (tempDelta == 0), micro-step down gently (15 MHz)
                // If temp is actively rising (tempDelta > 0), step down proportionally (15-35 MHz)
                int stepDown = 15;
                if (error >= 4.0 || tempDelta >= 2)
                {
                    stepDown = 35;
                }
                else if (error >= 2.0 || tempDelta >= 1)
                {
                    stepDown = 25;
                }

                newMhz = Math.Max(MinMhz, CurrentDynamicMhz - stepDown);
                _settleCooldownTicks = 1; // Allow 1 tick for thermal lag response
            }
            else if (error < -1.5)
            {
                // Temperature is below target (headroom available):
                // If temp is already rapidly warming up (tempDelta >= 2), hold steady to avoid overshoot
                if (tempDelta >= 2)
                {
                    return false;
                }

                // Recover frequency smoothly in fine 15-20 MHz bins (like NVIDIA GPU Boost)
                int stepUp = (error <= -4.0) ? 25 : 15;

                newMhz = Math.Min(MaxMhzCap, CurrentDynamicMhz + stepUp);
                _settleCooldownTicks = 1;
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
