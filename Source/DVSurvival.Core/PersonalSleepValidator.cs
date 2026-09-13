using System;

namespace DVSurvival.Core
{
    public static class PersonalSleepValidator
    {
        public static bool IsNoAdvanceMode(bool multiplayerActive,
            bool multiplayerTimeDisabled, bool gameTimeOverridden)
        {
            // Player count and host/client role deliberately do not affect this decision.
            return multiplayerActive && (multiplayerTimeDisabled || gameTimeOverridden);
        }

        public static bool IsValid(bool multiplayerActive, bool timeAdvanceSuppressed,
            bool nearBed, SurvivalActionRequest request)
        {
            return multiplayerActive && timeAdvanceSuppressed && nearBed && request != null &&
                request.Action == SurvivalActionKind.SleepWithoutTimeAdvance &&
                !float.IsNaN(request.Amount) && !float.IsInfinity(request.Amount) &&
                request.Amount >= 0.1f && request.Amount <= 24f &&
                request.CalendarBeforeTicks == 0L && request.CalendarAfterTicks == 0L &&
                request.Provision == ProvisionKind.None && request.Trauma == TraumaKind.None &&
                request.SecondaryAmount == 0f && string.IsNullOrEmpty(request.ItemIdentity);
        }
    }
}
