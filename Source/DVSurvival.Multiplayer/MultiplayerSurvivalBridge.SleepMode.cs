using System;

namespace DVSurvival.Multiplayer
{
    public sealed partial class MultiplayerSurvivalBridge
    {
        private Func<bool> nativeTimeAdvanceEnabled;

        public bool IsNativeSleepTimeSuppressed
        {
            get
            {
                if (!IsSessionActive || nativeTimeAdvanceEnabled == null) return false;
                // Read the setting, regardless of role/player count. The completed native sleep
                // must also have produced no actual calendar jump before the runtime uses it.
                try { return !nativeTimeAdvanceEnabled(); }
                catch { return false; }
            }
        }
    }
}
