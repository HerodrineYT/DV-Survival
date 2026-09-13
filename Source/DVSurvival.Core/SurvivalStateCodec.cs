using System;
using System.IO;

namespace DVSurvival.Core
{
    public static class SurvivalStateCodec
    {
        public static void Write(BinaryWriter writer, SurvivalState state)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            if (state == null) throw new ArgumentNullException(nameof(state));
            state.Clamp();
            writer.Write(state.Version);
            writer.Write(state.Hunger);
            writer.Write(state.Hydration);
            writer.Write(state.Rest);
            writer.Write(state.Health);
            writer.Write(state.BodyTemperatureCelsius);
            writer.Write(state.CaffeineHours);
            writer.Write(state.WarmthHours);
            writer.Write(state.Meals);
            writer.Write(state.Water);
            writer.Write(state.Coffee);
            writer.Write(state.FirstAid);
            writer.Write(state.HeatPacks);
            writer.Write(state.Revision);
            writer.Write(state.CollapseCount);
            writer.Write(state.SimulatedGameHours);
            writer.Write(state.LowRestGameHours);
            writer.Write(state.ExhaustionHoursRemaining);
            writer.Write(state.CoffeeUsesSinceSleep);
        }

        public static SurvivalState Read(BinaryReader reader)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            var state = new SurvivalState
            {
                Version = reader.ReadInt32(),
                Hunger = reader.ReadSingle(),
                Hydration = reader.ReadSingle(),
                Rest = reader.ReadSingle(),
                Health = reader.ReadSingle(),
                BodyTemperatureCelsius = reader.ReadSingle(),
                CaffeineHours = reader.ReadSingle(),
                WarmthHours = reader.ReadSingle(),
                Meals = reader.ReadInt32(),
                Water = reader.ReadInt32(),
                Coffee = reader.ReadInt32(),
                FirstAid = reader.ReadInt32(),
                HeatPacks = reader.ReadInt32(),
                Revision = reader.ReadUInt32(),
                CollapseCount = reader.ReadUInt32(),
                SimulatedGameHours = reader.ReadDouble(),
                LowRestGameHours = reader.ReadDouble(),
                ExhaustionHoursRemaining = reader.ReadSingle(),
                CoffeeUsesSinceSleep = reader.ReadInt32()
            };
            if (!state.IsValid()) throw new InvalidDataException("Invalid survival state payload.");
            return state;
        }
    }
}
