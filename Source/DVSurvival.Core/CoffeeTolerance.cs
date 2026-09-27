using System;

namespace DVSurvival.Core
{
    public static class CoffeeTolerance
    {
        public const int FullStrengthCups = 5;
        public const int MaximumTrackedCups = 25;

        public static float NextRestMultiplier(int previousCups)
        {
            var cups = Math.Max(0, Math.Min(MaximumTrackedCups, previousCups));
            // Before cup 6 there have been 5 uses: one 5-percentage-point reduction.
            var reductions = Math.Max(0, cups - FullStrengthCups + 1);
            return Math.Max(0, 20 - reductions) / 20f;
        }

        public static void RegisterCup(SurvivalState state)
        {
            state.CoffeeUsesSinceSleep = Math.Min(MaximumTrackedCups,
                Math.Max(0, Math.Min(MaximumTrackedCups, state.CoffeeUsesSinceSleep)) + 1);
        }
    }
}
