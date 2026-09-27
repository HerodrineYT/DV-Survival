namespace DVSurvival.Core
{
    // Bound the fast bootstrap retries, but retain a slow recovery path for modded
    // interiors adding their controls late without replacing loadedInterior.
    public sealed class DiscoveryRetry
    {
        public int Attempts { get; private set; }
        public float NextAttempt { get; private set; }

        public void Reset() { Attempts = 0; NextAttempt = 0f; }

        public void RecordAttempt(float now)
        {
            if (Attempts < 3) Attempts++;
            NextAttempt = now + (Attempts < 3 ? 3f : 30f);
        }
    }
}
