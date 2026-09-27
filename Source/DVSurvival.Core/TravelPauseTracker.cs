using System;
using System.Collections.Generic;

namespace DVSurvival.Core
{
    /// <summary>Personal, short-lived travel leases; never change the shared world clock.</summary>
    public sealed class TravelPauseTracker
    {
        // Scene loading can stall the client's Update loop well beyond the climate report TTL.
        // An explicit end report resumes immediately; this lease only bounds a lost end report.
        public const double ReportTimeoutSeconds = 120d;
        // MP relays a world-time skip independently of the traveller's environment reports.
        public const double CalendarRelayGraceSeconds = 10d;
        private readonly Dictionary<byte, Entry> players = new Dictionary<byte, Entry>();

        public void Update(byte playerId, bool travelling, double now)
        {
            if (double.IsNaN(now) || double.IsInfinity(now)) return;
            Entry entry;
            if (!players.TryGetValue(playerId, out entry))
            {
                if (!travelling) return;
                players[playerId] = entry = new Entry();
            }
            if (travelling)
            {
                entry.ActiveUntil = now + ReportTimeoutSeconds;
                entry.LastTravelAt = now;
                entry.TouchedSinceCapture = true;
            }
            else if (entry.ActiveUntil > now)
            {
                entry.ActiveUntil = now;
                entry.LastTravelAt = now;
                entry.TouchedSinceCapture = true;
            }
        }

        public bool IsPaused(byte playerId, double now)
        {
            Entry entry;
            return players.TryGetValue(playerId, out entry) && now < entry.ActiveUntil;
        }

        // The returned snapshot belongs to this particular calendar observation. Queued sleep
        // reconciliation can run after arrival, so it must not re-read just the current flags.
        // Returns null in the usual no-travel case, avoiding a per-tick allocation.
        public HashSet<byte> Capture(double now, bool calendarJump)
        {
            HashSet<byte> paused = null;
            foreach (var pair in players)
            {
                var entry = pair.Value;
                var recentRelay = calendarJump && now - entry.LastTravelAt <= CalendarRelayGraceSeconds;
                if (now < entry.ActiveUntil || entry.TouchedSinceCapture || recentRelay)
                {
                    if (paused == null) paused = new HashSet<byte>();
                    paused.Add(pair.Key);
                }
                entry.TouchedSinceCapture = false;
            }
            return paused;
        }

        public void Remove(byte playerId) { players.Remove(playerId); }
        public void Reset() { players.Clear(); }

        private sealed class Entry
        {
            public double ActiveUntil;
            public double LastTravelAt;
            public bool TouchedSinceCapture;
        }
    }
}
