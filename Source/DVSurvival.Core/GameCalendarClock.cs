using System;

namespace DVSurvival.Core
{
    /// <summary>
    /// Converts the game's authoritative calendar into elapsed game hours. One calendar day is
    /// always 24 game hours, regardless of how many real minutes the current difficulty assigns
    /// to that day. Backwards/load jumps rebase instead of producing negative simulation time.
    /// </summary>
    public sealed class GameCalendarClock
    {
        public const double MaximumObservedJumpHours = 168d;
        private DateTime previous;
        private bool hasPrevious;

        public bool IsInitialized { get { return hasPrevious; } }
        public long LastObservedTicks { get { return hasPrevious ? previous.Ticks : 0L; } }

        public void Reset()
        {
            previous = default(DateTime);
            hasPrevious = false;
        }

        public double Observe(DateTime current)
        {
            if (!hasPrevious)
            {
                previous = current;
                hasPrevious = true;
                return 0d;
            }

            var elapsed = current - previous;
            previous = current;
            if (elapsed <= TimeSpan.Zero) return 0d;
            return Math.Min(MaximumObservedJumpHours, elapsed.TotalHours);
        }

        /// <summary>
        /// Distinguishes an explicit time skip from the smooth calendar progress expected for
        /// the configured day length. The observed calendar remains the source of elapsed time;
        /// this classification only decides whether multiplayer sleep correlation is required.
        /// </summary>
        public static bool IsDiscontinuousAdvance(double observedHours,
            double elapsedRealSeconds, double dayLengthMinutes)
        {
            if (double.IsNaN(observedHours) || double.IsInfinity(observedHours) ||
                observedHours <= 0d)
                return false;
            if (double.IsNaN(elapsedRealSeconds) || double.IsInfinity(elapsedRealSeconds) ||
                elapsedRealSeconds < 0d)
                elapsedRealSeconds = 0d;
            if (double.IsNaN(dayLengthMinutes) || double.IsInfinity(dayLengthMinutes) ||
                dayLengthMinutes <= 0d)
                return observedHours >= 0.05d;
            var expectedHours = elapsedRealSeconds * 24d / (dayLengthMinutes * 60d);
            var tolerance = Math.Max(0.05d, expectedHours * 0.25d);
            // Calendar values are ultimately reconstructed from DateTime ticks. Keep an
            // epsilon here so an exact threshold is not lost to binary floating-point
            // rounding (notably with very short, one-real-minute game days).
            return observedHours - expectedHours >= tolerance - 1e-9d;
        }
    }
}
