using System;
using System.Globalization;

namespace DVSurvival.Core
{
    public static class ProvisionDescription
    {
        public static string Format(string remainingLabel, string description, int usedUnits, IFormatProvider culture)
        {
            var remaining = (PartialProvisionLedger.FullUnits -
                Math.Max(0, Math.Min(PartialProvisionLedger.FullUnits, usedUnits))) / 10m;
            return remainingLabel + remaining.ToString("0.#", culture ?? CultureInfo.InvariantCulture) +
                "% · " + description;
        }
    }
}
