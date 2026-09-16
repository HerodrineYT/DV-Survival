using System;
using System.Collections.Generic;

namespace DVSurvival.Core
{
    // Short-lived authority evidence; never trusts the client's requested damage.
    public sealed class TrainDismountEvidence
    {
        private struct Ride { public double Time; public float Speed; }
        private readonly Dictionary<byte, Ride> rides = new Dictionary<byte, Ride>();
        public void Observe(byte player, float speed, double now)
        {
            if (float.IsNaN(speed) || float.IsInfinity(speed) || speed < 0f || speed > 300f) return;
            rides[player] = new Ride { Time = now, Speed = speed };
        }
        public bool TryTake(byte player, float reportedSpeed, double now, out float speed)
        {
            speed = 0f;
            Ride ride;
            if (!rides.TryGetValue(player, out ride) || now < ride.Time || now - ride.Time > 10 ||
                float.IsNaN(reportedSpeed) || float.IsInfinity(reportedSpeed) ||
                reportedSpeed <= 5 || reportedSpeed > 300 || ride.Speed <= 5) return false;
            rides.Remove(player);
            speed = Math.Min(reportedSpeed, ride.Speed);
            return true;
        }
        public void Remove(byte player) { rides.Remove(player); }
        public void Clear() { rides.Clear(); }
    }
}
