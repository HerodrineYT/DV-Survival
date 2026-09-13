using System;

namespace DVSurvival.Core
{
    // Stateful, frame-rate independent model. Times are real seconds, sampled at 2 Hz by the game adapter.
    public sealed class CabinClimate
    {
        public float AirTemperature { get; private set; }
        public float EngineWarmth { get; private set; }
        public float SmoothedLoad { get; private set; }
        private bool initialized;

        public float Advance(float seconds, float outside, bool running, float load, bool open, bool fan,
            float? fireboxAirTarget = null, float heatingRateMultiplier = 1f, bool heaterEnabled = true, float heaterPower = 1f)
        {
            if (!initialized) { AirTemperature = outside; initialized = true; }
            seconds = Math.Max(0f, Math.Min(5f, seconds));
            load = Math.Max(0f, Math.Min(1f, load));
            heatingRateMultiplier = Math.Max(0f, Math.Min(4f, heatingRateMultiplier));
            heaterPower = CabHeaterSetting.Normalize(heaterPower);
            SmoothedLoad = Approach(SmoothedLoad, load, seconds, 8f);
            EngineWarmth = Approach(EngineWarmth, running ? 1f : 0f, seconds, running ? 65f : 100f);
            // Keep natural exchange and artificial heat separate. This makes the invariant
            // explicit: a disabled diesel heater can only approach outdoors and can never
            // receive residual heat from EngineWarmth or SmoothedLoad.
            var artificialBoost = fireboxAirTarget.HasValue
                ? Math.Max(0f, fireboxAirTarget.Value - outside)
                : running && heaterEnabled
                    ? (Math.Max(outside, 22f + 8f * SmoothedLoad) - outside) * EngineWarmth * heaterPower
                    : 0f;
            // Openings exchange cabin air with outdoors even while a heat source is active.
            if (open) artificialBoost = 0f;
            else if (fan) artificialBoost = Math.Max(0f, artificialBoost - 2.2f);
            var target = outside + artificialBoost;
            // The DM1U's heater can warm the still cabin by natural convection at 25% speed;
            // its blower restores normal heat transfer. Openings and cooling always retain their
            // full response, so a slow heater cannot make an opened cab hold heat unnaturally.
            var transferSeconds = !open && heaterEnabled && artificialBoost > 0f && target > AirTemperature
                ? seconds * heatingRateMultiplier
                : seconds;
            var previousAir = AirTemperature;
            var nextAir = Approach(previousAir, target, transferSeconds, open ? 12f : 30f);
            if (!heaterEnabled && !fireboxAirTarget.HasValue)
            {
                var lower = Math.Min(previousAir, outside);
                var upper = Math.Max(previousAir, outside);
                nextAir = Math.Max(lower, Math.Min(upper, nextAir));
            }
            AirTemperature = nextAir;
            return AirTemperature;
        }

        private static float Approach(float value, float target, float seconds, float timeConstant)
        {
            return value + (target - value) * (1f - (float)Math.Exp(-seconds / timeConstant));
        }
    }
}
