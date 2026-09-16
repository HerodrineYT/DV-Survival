using System;

namespace DVSurvival.Core
{
    public static class FirstAidRecovery
    {
        public const float DurationSeconds = 20f;
        public const float HealthPerSecond = 2f;

        public static bool Advance(SurvivalState state, float seconds)
        {
            if (state == null || seconds <= 0f || float.IsNaN(seconds) || float.IsInfinity(seconds) ||
                state.FirstAidSecondsRemaining <= 0f) return false;
            state.Clamp();
            if (state.Health <= 0f) { state.FirstAidSecondsRemaining = 0f; return true; }
            var elapsed = Math.Min(seconds, state.FirstAidSecondsRemaining);
            state.Health = Math.Min(100f, state.Health + elapsed * HealthPerSecond);
            state.FirstAidSecondsRemaining = Math.Max(0f, state.FirstAidSecondsRemaining - elapsed);
            state.Revision++;
            return true;
        }
    }
}
