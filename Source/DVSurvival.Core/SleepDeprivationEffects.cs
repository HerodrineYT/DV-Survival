using System;

namespace DVSurvival.Core
{
    public static class SleepDeprivationEffects
    {
        public const double ExhaustionThresholdHours = 48d;
        public const double HallucinationThresholdHours = 72d;
        public const double SpeedDivisorStepHours = 24d;
        public const double MaximumLowRestHours = 144d;
        public const float FullSleepRecoveryHours = 8f;
        public const float InitialSpeedDivisor = 1.5f;
        public const float MaximumSpeedDivisor = 3f;
        public static bool HasExhaustion(SurvivalState s)
        { return s != null && s.ExhaustionHoursRemaining > 0f; }
        public static float DisplaySpeedDivisor(SurvivalState s)
        {
            if (s == null || s.LowRestGameHours < HallucinationThresholdHours)
                return 1f;
            var completedSteps = Math.Floor(
                (s.LowRestGameHours - HallucinationThresholdHours) / SpeedDivisorStepHours);
            return Math.Min(MaximumSpeedDivisor,
                InitialSpeedDivisor + (float)completedSteps * 0.5f);
        }
        public static float DisplaySpeedMultiplier(SurvivalState s)
        { return 1f / DisplaySpeedDivisor(s); }
        public static bool SleepTriggersThirst(SurvivalState s)
        { return HasExhaustion(s) && s.ExhaustionHoursRemaining > 1f; }

        public static void RecoverAfterFullSleep(SurvivalState s, float sleepHours)
        {
            if (s != null && sleepHours >= FullSleepRecoveryHours)
            { s.LowRestGameHours = 0d; s.ExhaustionHoursRemaining = 0f; }
        }

        public static float NextStep(SurvivalState s, float remaining)
        {
            var step = remaining;
            if (HasExhaustion(s)) step = Math.Min(step, s.ExhaustionHoursRemaining);
            var boundary = s.LowRestGameHours < ExhaustionThresholdHours
                ? ExhaustionThresholdHours : HallucinationThresholdHours;
            if (s.LowRestGameHours < boundary)
                step = Math.Min(step, (float)(boundary - s.LowRestGameHours));
            return step;
        }

        public static void AdvanceClock(SurvivalState s, float hours, SurvivalEnvironment environment)
        {
            // Spend the old phase first; a phase starting at this step's end gets its full three hours.
            if (HasExhaustion(s))
            {
                ApplyCold(s, environment);
                s.ExhaustionHoursRemaining = Math.Max(0, s.ExhaustionHoursRemaining - hours);
            }
            var previous = s.LowRestGameHours;
            s.LowRestGameHours = Math.Min(MaximumLowRestHours, previous + hours);
            if (previous < ExhaustionThresholdHours && s.LowRestGameHours >= ExhaustionThresholdHours)
            {
                s.ExhaustionHoursRemaining = 3f;
                s.Hunger = Math.Min(10f, s.Hunger);
                s.Hydration = Math.Min(10f, s.Hydration);
                s.Rest = Math.Min(10f, s.Rest);
                s.CaffeineHours = 0f; s.WarmthHours = 0f;
                ApplyCold(s, environment);
            }
        }

        private static void ApplyCold(SurvivalState s, SurvivalEnvironment e)
        {
            if (e != null && e.IsWinter && e.AmbientTemperatureCelsius < 0)
                s.BodyTemperatureCelsius = Math.Min(35f, s.BodyTemperatureCelsius);
        }
    }
}
