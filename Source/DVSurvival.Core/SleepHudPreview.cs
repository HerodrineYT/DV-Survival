namespace DVSurvival.Core
{
    // Presentation only: inventory, movement and authority keep the confirmed state.
    public sealed class SleepHudPreview
    {
        private SurvivalState preview;
        private uint requestId;
        private double expiresAt;
        public void Begin(uint request, SurvivalState confirmed, float hours, SurvivalEnvironment environment,
            SurvivalTuning tuning, double now)
        {
            Clear();
            if (confirmed == null) return;
            var next = confirmed.Clone();
            if (SurvivalSimulator.Sleep(next, hours, environment, tuning) != SurvivalResultCode.Success) return;
            preview = next; requestId = request; expiresAt = now + 15d;
        }
        public SurvivalState Get(SurvivalState confirmed, double now)
        { if (now >= expiresAt) Clear(); return preview ?? confirmed; }
        public void Resolve(uint request) { if (request != 0 && requestId == request) Clear(); }
        public void Clear() { preview = null; requestId = 0; expiresAt = 0; }
    }
}
