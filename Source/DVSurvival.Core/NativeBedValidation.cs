using System;

namespace DVSurvival.Core
{
    public static class NativeBedValidation
    {
        public static bool IsNearReportedBed(SurvivalActionRequest request,
            float playerX, float playerY, float playerZ, float maximumDistance)
        {
            if (request == null || !request.HasNativeBed ||
                (request.Action != SurvivalActionKind.Sleep &&
                 request.Action != SurvivalActionKind.SleepWithoutTimeAdvance) ||
                !Finite(playerX) || !Finite(playerY) || !Finite(playerZ) ||
                !Finite(request.BedWorldX) || !Finite(request.BedWorldY) || !Finite(request.BedWorldZ) ||
                !Finite(maximumDistance) || maximumDistance <= 0f) return false;
            var x = (double)request.BedWorldX - playerX;
            var y = (double)request.BedWorldY - playerY;
            var z = (double)request.BedWorldZ - playerZ;
            var limit = Math.Min(30f, maximumDistance);
            return x * x + y * y + z * z <= limit * limit;
        }

        private static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
