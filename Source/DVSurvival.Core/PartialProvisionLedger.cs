using System;
using System.Collections.Generic;

namespace DVSurvival.Core
{
    [Serializable]
    public sealed class PartialProvisionRecord
    {
        public ProvisionKind Kind;
        public int UsedUnits;
        public float CoffeeRestMultiplier = 1f;
    }

    public static class PartialProvisionLedger
    {
        public const int FullUnits = 1000;
        public static bool Supports(ProvisionKind kind) =>
            kind == ProvisionKind.Meal || kind == ProvisionKind.Water || kind == ProvisionKind.Coffee;

        // Requests contain a cumulative target, not a delta. Replays cannot grant another sip.
        public static SurvivalResultCode Consume(Dictionary<string, PartialProvisionRecord> portions,
            HashSet<string> consumed, string identity, int targetUnits, SurvivalState state,
            ProvisionKind kind, out int usedUnits, SurvivalTuning tuning = null)
        {
            usedUnits = -1;
            Guid guid;
            if (portions == null || consumed == null || state == null || !Supports(kind) ||
                identity == null || identity.Length != 36 || !Guid.TryParse(identity, out guid) ||
                guid == Guid.Empty || targetUnits < 0 || targetUnits > FullUnits)
                return SurvivalResultCode.InvalidRequest;
            identity = guid.ToString("D");
            if (consumed.Contains(identity)) { usedUnits = FullUnits; return SurvivalResultCode.Success; }
            PartialProvisionRecord item;
            var exists = portions.TryGetValue(identity, out item);
            if (exists && (item == null || item.Kind != kind || item.UsedUnits < 0 || item.UsedUnits > FullUnits))
                return SurvivalResultCode.InvalidRequest;
            usedUnits = exists ? item.UsedUnits : 0;
            if (targetUnits <= usedUnits) return SurvivalResultCode.Success;
            if (!exists && portions.Count + consumed.Count >= 16000) return SurvivalResultCode.InvalidRequest;
            state.Clamp();
            if (!Needed(state, kind)) return SurvivalResultCode.NotNeeded;
            if (!exists)
            {
                item = new PartialProvisionRecord { Kind = kind, CoffeeRestMultiplier = SurvivalSimulator.GetCoffeeRestMultiplier(state) };
                portions.Add(identity, item);
                if (kind == ProvisionKind.Coffee)
                    state.CoffeeUsesSinceSleep = Math.Min(10, state.CoffeeUsesSinceSleep + 1);
            }
            var fraction = (targetUnits - usedUnits) / (float)FullUnits;
            switch (kind)
            {
                case ProvisionKind.Meal: state.Hunger += 45f * fraction; break;
                case ProvisionKind.Water:
                    state.Hydration += 50f * fraction;
                    if (state.BodyTemperatureCelsius > 36f)
                        state.BodyTemperatureCelsius = Math.Max(36f, state.BodyTemperatureCelsius - .2f * fraction);
                    break;
                case ProvisionKind.Coffee:
                    state.Rest += 16f * item.CoffeeRestMultiplier * fraction;
                    state.Hydration += 15f * fraction;
                    var caffeineDuration = tuning == null ? 4f : tuning.CoffeeDurationHours;
                    state.CaffeineHours = Math.Min(caffeineDuration, state.CaffeineHours + caffeineDuration * fraction);
                    if (state.BodyTemperatureCelsius < 37f)
                        state.BodyTemperatureCelsius = Math.Min(37f, state.BodyTemperatureCelsius + .2f * fraction);
                    break;
            }
            item.UsedUnits = targetUnits;
            usedUnits = targetUnits;
            if (usedUnits == FullUnits) { consumed.Add(identity); portions.Remove(identity); }
            state.Revision++;
            state.Clamp();
            return SurvivalResultCode.Success;
        }
        private static bool Needed(SurvivalState s, ProvisionKind kind)
        {
            if (kind == ProvisionKind.Meal) return s.Hunger < 100f;
            if (kind == ProvisionKind.Water) return s.Hydration < 100f || s.BodyTemperatureCelsius > 36f;
            return s.Rest < 100f || s.Hydration < 100f || s.BodyTemperatureCelsius < 37f;
        }
    }
}
