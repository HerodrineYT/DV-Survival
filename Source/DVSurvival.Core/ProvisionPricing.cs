using System;

namespace DVSurvival.Core
{
    public static class ProvisionPricing
    {
        // Matches GlobalShopController: non-career sessions have free shop supplies.
        // No active session yet must not overwrite configured career prices with zero.
        public static int Resolve(int configuredPrice, string gameMode)
        {
            if (configuredPrice < 0) return configuredPrice;
            return !string.IsNullOrEmpty(gameMode) && !string.Equals(gameMode, "Career", StringComparison.Ordinal)
                ? 0 : configuredPrice;
        }
    }
}
