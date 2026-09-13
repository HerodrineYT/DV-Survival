using System;

namespace DVSurvival.Core
{
    [Flags]
    public enum SurvivalWarningEvent
    {
        None = 0,
        LowRestOneDay = 1 << 1,
        ExhaustionSoon = 1 << 2,
        ExhaustionStarted = 1 << 3,
        HallucinationSoon = 1 << 4,
        HallucinationStarted = 1 << 5,
        LowRestRecovered = 1 << 6,
        RunHealth = 1 << 7,
        RunHunger = 1 << 8,
        RunHydration = 1 << 9,
        RunRest = 1 << 10,
        CriticalHealth = 1 << 11,
        Starving = 1 << 12,
        Dehydrated = 1 << 13,
        ExtremeFatigue = 1 << 14,
        Hypothermia = 1 << 15,
        Hyperthermia = 1 << 16
    }

    // Local presentation state only. It is deliberately absent from saves and network packets.
    public sealed class SurvivalWarningTracker
    {
        private bool initialized;
        private double previousLowRestHours;
        private SurvivalWarningEvent active;

        public void Reset()
        {
            initialized = false;
            previousLowRestHours = 0d;
            active = SurvivalWarningEvent.None;
        }

        public SurvivalWarningEvent Observe(SurvivalState state)
        {
            if (state == null) return SurvivalWarningEvent.None;
            if (!initialized)
            {
                initialized = true;
                previousLowRestHours = state.LowRestGameHours;
                active = CurrentConditions(state);
                return SurvivalWarningEvent.None;
            }

            var result = SurvivalWarningEvent.None;
            UpdateCondition(state.Health < 40f, state.Health >= 45f, SurvivalWarningEvent.RunHealth, ref result);
            UpdateCondition(state.Hunger < 25f, state.Hunger >= 30f, SurvivalWarningEvent.RunHunger, ref result);
            UpdateCondition(state.Hydration < 30f, state.Hydration >= 35f, SurvivalWarningEvent.RunHydration, ref result);
            UpdateCondition(state.Rest < 30f, state.Rest >= 35f, SurvivalWarningEvent.RunRest, ref result);
            UpdateCondition(state.Health <= 20f, state.Health > 30f, SurvivalWarningEvent.CriticalHealth, ref result);
            UpdateCondition(state.Hunger < 15f, state.Hunger >= 20f, SurvivalWarningEvent.Starving, ref result);
            UpdateCondition(state.Hydration < 20f, state.Hydration >= 25f, SurvivalWarningEvent.Dehydrated, ref result);
            UpdateCondition(state.Rest < 15f, state.Rest >= 20f, SurvivalWarningEvent.ExtremeFatigue, ref result);
            UpdateCondition(state.BodyTemperatureCelsius < 35f, state.BodyTemperatureCelsius >= 36f,
                SurvivalWarningEvent.Hypothermia, ref result);
            UpdateCondition(state.BodyTemperatureCelsius >= 38.5f, state.BodyTemperatureCelsius <= 38f,
                SurvivalWarningEvent.Hyperthermia, ref result);

            var hours = state.LowRestGameHours;
            if (Crossed(24d, hours)) result |= SurvivalWarningEvent.LowRestOneDay;
            if (Crossed(44d, hours)) result |= SurvivalWarningEvent.ExhaustionSoon;
            if (Crossed(48d, hours)) result |= SurvivalWarningEvent.ExhaustionStarted;
            if (Crossed(68d, hours)) result |= SurvivalWarningEvent.HallucinationSoon;
            if (Crossed(72d, hours)) result |= SurvivalWarningEvent.HallucinationStarted;
            // Short low-rest periods stay completely silent. Recovery is worth announcing only
            // after the player has already crossed the first visible (24-hour) warning stage.
            if (previousLowRestHours >= 24d && hours <= 0d) result |= SurvivalWarningEvent.LowRestRecovered;
            previousLowRestHours = hours;
            return result;
        }

        private bool Crossed(double threshold, double current)
        { return previousLowRestHours < threshold && current >= threshold; }

        private void UpdateCondition(bool entered, bool cleared, SurvivalWarningEvent flag,
            ref SurvivalWarningEvent result)
        {
            if ((active & flag) != 0)
            {
                if (cleared) active &= ~flag;
            }
            else if (entered)
            {
                active |= flag;
                result |= flag;
            }
        }

        private static SurvivalWarningEvent CurrentConditions(SurvivalState s)
        {
            var result = SurvivalWarningEvent.None;
            if (s.Health < 40f) result |= SurvivalWarningEvent.RunHealth;
            if (s.Hunger < 25f) result |= SurvivalWarningEvent.RunHunger;
            if (s.Hydration < 30f) result |= SurvivalWarningEvent.RunHydration;
            if (s.Rest < 30f) result |= SurvivalWarningEvent.RunRest;
            if (s.Health <= 20f) result |= SurvivalWarningEvent.CriticalHealth;
            if (s.Hunger < 15f) result |= SurvivalWarningEvent.Starving;
            if (s.Hydration < 20f) result |= SurvivalWarningEvent.Dehydrated;
            if (s.Rest < 15f) result |= SurvivalWarningEvent.ExtremeFatigue;
            if (s.BodyTemperatureCelsius < 35f) result |= SurvivalWarningEvent.Hypothermia;
            if (s.BodyTemperatureCelsius >= 38.5f) result |= SurvivalWarningEvent.Hyperthermia;
            return result;
        }
    }
}
