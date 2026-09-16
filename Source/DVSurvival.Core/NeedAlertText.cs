namespace DVSurvival.Core
{
    public static class NeedAlertText
    {
        public static string Format(SurvivalState state, int index, string number, bool enabled)
        {
            if (!enabled || state == null) return number;
            var low = index == 0 ? state.Health < 40f : index == 1 ? state.Hunger < 25f :
                index == 2 ? state.Hydration < 30f : state.Rest < 30f || state.LowRestGameHours >= 24d;
            return low ? "<color=#FF5555>! " + number + "</color>" : number;
        }
    }
}
