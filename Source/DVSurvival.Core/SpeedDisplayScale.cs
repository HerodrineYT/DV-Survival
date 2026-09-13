using System;

namespace DVSurvival.Core
{
    public static class SpeedDisplayScale
    {
        // Scale the distance from the speed-zero mark, not from the raw numeric origin.
        // Inverted/offset gauges may encode zero km/h as 1, 100, or a negative value.
        public static float Apply(float value, float zero, float factor)
        { return factor == 1f ? value : zero + (value - zero) * factor; }

        public static float NormalizedZero(float zero, float min, float max, bool absolute)
        {
            if (absolute) { zero = Math.Abs(zero); min = 0f; }
            return min == max ? 0f : Math.Max(0f, Math.Min(1f, (zero - min) / (max - min)));
        }

        public static float NeedleAngle(float displayed, float min, float max, float minAngle, float maxAngle, bool unclamped)
        {
            float level = min == max ? 0f : (displayed - min) / (max - min);
            if (!unclamped) level = Math.Max(0f, Math.Min(1f, level));
            return minAngle + (maxAngle - minAngle) * level;
        }
    }
}
