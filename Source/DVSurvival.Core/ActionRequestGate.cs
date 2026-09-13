using System.Collections.Generic;

namespace DVSurvival.Core
{
    // One high-water mark per connected player. Replayed / reordered actions never mutate stock or money.
    public sealed class ActionRequestGate
    {
        private readonly Dictionary<byte, uint> lastRequests = new Dictionary<byte, uint>();

        public bool TryAccept(byte playerId, uint requestId)
        {
            if (requestId == 0) return false;
            uint last;
            if (lastRequests.TryGetValue(playerId, out last) && unchecked((int)(requestId - last)) <= 0)
                return false;
            lastRequests[playerId] = requestId;
            return true;
        }

        public void Remove(byte playerId) { lastRequests.Remove(playerId); }
        public void Clear() { lastRequests.Clear(); }
    }
}
