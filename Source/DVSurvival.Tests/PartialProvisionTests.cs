using System;
using System.Collections.Generic;
using DVSurvival.Core;
using Newtonsoft.Json;
using Xunit;

namespace DVSurvival.Tests
{
    public class PartialProvisionTests
    {
        private readonly string id = Guid.NewGuid().ToString("D");
        private readonly Dictionary<string, PartialProvisionRecord> partial = new Dictionary<string, PartialProvisionRecord>();
        private readonly HashSet<string> consumed = new HashSet<string>();

        [Theory]
        [InlineData(ProvisionKind.Meal, 45f)]
        [InlineData(ProvisionKind.Water, 50f)]
        [InlineData(ProvisionKind.Coffee, 16f)]
        public void HalfPortionGivesHalfBenefitAndResumesWithoutRepeating(ProvisionKind kind, float total)
        {
            var s = new SurvivalState { Hunger = 0, Hydration = 0, Rest = 0 };
            Assert.Equal(SurvivalResultCode.Success, PartialProvisionLedger.Consume(partial, consumed, id, 500, s, kind, out var used));
            Assert.Equal(500, used);
            Func<float> value = () => kind == ProvisionKind.Meal ? s.Hunger : kind == ProvisionKind.Water ? s.Hydration : s.Rest;
            Assert.Equal(total / 2, value(), 3);
            PartialProvisionLedger.Consume(partial, consumed, id, 500, s, kind, out _);
            PartialProvisionLedger.Consume(partial, consumed, id, 300, s, kind, out _);
            Assert.Equal(total / 2, value(), 3);
            PartialProvisionLedger.Consume(partial, consumed, id, 1000, s, kind, out used);
            Assert.Equal(total, value(), 3);
            Assert.Equal(1000, used);
            Assert.Contains(id, consumed);
            Assert.Empty(partial);
            PartialProvisionLedger.Consume(partial, consumed, id, 1000, s, kind, out _);
            Assert.Equal(total, value(), 3);
        }

        [Fact]
        public void ManySmallSipsEqualOneCupAndToleranceCountsOnce()
        {
            var s = new SurvivalState { Rest = 0, Hydration = 0, BodyTemperatureCelsius = 36, CoffeeUsesSinceSleep = 2 };
            for (var units = 10; units <= 1000; units += 10)
                PartialProvisionLedger.Consume(partial, consumed, id, units, s, ProvisionKind.Coffee, out _);
            Assert.Equal(12.8f, s.Rest, 3);
            Assert.Equal(15f, s.Hydration, 3);
            Assert.Equal(36.2f, s.BodyTemperatureCelsius, 3);
            Assert.Equal(4f, s.CaffeineHours, 3);
            Assert.Equal(3, s.CoffeeUsesSinceSleep);
        }

        [Fact]
        public void ProgressSurvivesSaveLoadAndDoesNotAffectOtherPlayers()
        {
            var s = new SurvivalState { Hunger = 0 };
            var other = new SurvivalState { Hunger = 0 };
            PartialProvisionLedger.Consume(partial, consumed, id, 250, s, ProvisionKind.Meal, out _);
            var loaded = JsonConvert.DeserializeObject<Dictionary<string, PartialProvisionRecord>>(JsonConvert.SerializeObject(partial));
            PartialProvisionLedger.Consume(loaded, consumed, id, 250, other, ProvisionKind.Meal, out var used);
            Assert.Equal(250, used);
            Assert.Equal(0f, other.Hunger);
            PartialProvisionLedger.Consume(loaded, consumed, id, 500, other, ProvisionKind.Meal, out _);
            Assert.Equal(11.25f, other.Hunger);
            Assert.Equal(11.25f, s.Hunger);
        }

        [Fact]
        public void FullNeedsDoNotWasteRemainder()
        {
            var s = new SurvivalState();
            Assert.Equal(SurvivalResultCode.NotNeeded, PartialProvisionLedger.Consume(partial, consumed, id, 250, s, ProvisionKind.Meal, out _));
            Assert.Empty(partial);
            s.Hunger = 50;
            PartialProvisionLedger.Consume(partial, consumed, id, 250, s, ProvisionKind.Meal, out _);
            s.Hunger = 100;
            Assert.Equal(SurvivalResultCode.NotNeeded, PartialProvisionLedger.Consume(partial, consumed, id, 500, s, ProvisionKind.Meal, out var used));
            Assert.Equal(250, used);
        }

        [Theory]
        [InlineData(-1)] [InlineData(1001)]
        public void InvalidAmountsCannotChangeState(int units)
        {
            var s = new SurvivalState { Hunger = 0 };
            Assert.Equal(SurvivalResultCode.InvalidRequest, PartialProvisionLedger.Consume(partial, consumed, id, units, s, ProvisionKind.Meal, out _));
            Assert.Equal(0, s.Hunger);
        }

        [Theory]
        [InlineData(ProvisionKind.FirstAid)] [InlineData(ProvisionKind.HeatPack)]
        public void MedicalItemsDoNotSupportPartialUse(ProvisionKind kind)
        {
            Assert.False(PartialProvisionLedger.Supports(kind));
            Assert.Equal(SurvivalResultCode.InvalidRequest, PartialProvisionLedger.Consume(partial, consumed, id, 500, new SurvivalState(), kind, out _));
        }

        [Fact]
        public void IdentityCannotChangeProvisionKind()
        {
            var s = new SurvivalState { Hunger = 0, Hydration = 0 };
            PartialProvisionLedger.Consume(partial, consumed, id, 250, s, ProvisionKind.Meal, out _);
            Assert.Equal(SurvivalResultCode.InvalidRequest, PartialProvisionLedger.Consume(partial, consumed, id, 500, s, ProvisionKind.Water, out _));
            Assert.Equal(0, s.Hydration);
        }

        [Theory]
        [InlineData(ProvisionKind.Water, 36.01f, 36f)]
        [InlineData(ProvisionKind.Coffee, 36.99f, 37f)]
        public void DrinksRespectTemperatureLimits(ProvisionKind kind, float before, float expected)
        {
            var s = new SurvivalState { BodyTemperatureCelsius = before, Hydration = 0 };
            PartialProvisionLedger.Consume(partial, consumed, id, 500, s, kind, out _);
            Assert.Equal(expected, s.BodyTemperatureCelsius);
        }

        [Fact]
        public void SleepPreviewIsImmediateButDoesNotGrantAuthorityBenefits()
        {
            var state = new SurvivalState { Health = 40, Rest = 0, LowRestGameHours = 80 };
            var preview = new SleepHudPreview();
            preview.Begin(7, state, 10, new SurvivalEnvironment { AmbientTemperatureCelsius = 22 }, new SurvivalTuning(), 1);
            Assert.Equal(100f, preview.Get(state, 1).Rest);
            Assert.Equal(0f, state.Rest);
            preview.Resolve(8);
            Assert.Equal(100f, preview.Get(state, 2).Rest);
            preview.Resolve(7);
            Assert.Same(state, preview.Get(state, 2));
            preview.Begin(9, state, 10, new SurvivalEnvironment(), new SurvivalTuning(), 5);
            Assert.Same(state, preview.Get(state, 21));
        }
    }
}
