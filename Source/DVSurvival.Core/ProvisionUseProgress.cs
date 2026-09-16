using System;

namespace DVSurvival.Core
{
    public sealed class ProvisionUseProgress
    {
        private readonly float duration;
        private float elapsed;
        private bool finished;
        public ProvisionUseProgress(ProvisionKind kind) { duration = ProvisionUseTiming.Seconds(kind); }
        public float Fraction => Math.Min(1f, elapsed / duration);
        public bool Advance(float seconds)
        {
            if (finished || float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds <= 0f) return false;
            elapsed = Math.Min(duration, elapsed + seconds);
            if (elapsed < duration) return false;
            finished = true;
            return true;
        }
        public void Cancel() { finished = true; }
    }
}
