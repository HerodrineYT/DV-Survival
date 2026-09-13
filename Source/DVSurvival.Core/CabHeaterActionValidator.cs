using System;

namespace DVSurvival.Core
{
    public static class CabHeaterActionValidator
    {
        public static bool IsValid(SurvivalPlayerInfo player, SurvivalActionRequest request)
        {
            if (player == null || request == null ||
                request.Action != SurvivalActionKind.SetCabHeater) return false;
            if (!CabHeaterSetting.IsAllowed(request.Amount)) return false;
            if (request.Provision != ProvisionKind.None || request.SecondaryAmount != 0f ||
                request.CalendarBeforeTicks != 0L || request.CalendarAfterTicks != 0L ||
                request.Trauma != TraumaKind.None) return false;
            if (!player.IsOnCar || !player.OccupiedCarIsLocomotive ||
                !player.OccupiedCarSupportsCabHeater ||
                string.IsNullOrWhiteSpace(player.OccupiedCarId) ||
                player.OccupiedCarId.Length > 80 ||
                string.IsNullOrWhiteSpace(request.ItemIdentity)) return false;
            return string.Equals(player.OccupiedCarId, request.ItemIdentity,
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
