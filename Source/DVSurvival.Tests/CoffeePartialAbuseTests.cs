using System;
using System.Collections.Generic;
using DVSurvival.Core;
using Newtonsoft.Json;
using Xunit;

namespace DVSurvival.Tests
{
    public class CoffeePartialAbuseTests
    {
        [Theory]
        [InlineData(1)] [InlineData(500)] [InlineData(950)] [InlineData(990)]
        public void EveryStartedCupCountsEvenWhenItIsNeverFinished(int usedPerCup)
        {
            var portions = new Dictionary<string, PartialProvisionRecord>();
            var consumed = new HashSet<string>();
            var state = new SurvivalState();
            for (int cup = 0; cup < 11; cup++)
            {
                state.Rest = 0;
                var id = Guid.NewGuid().ToString("D");
                var result = PartialProvisionLedger.Consume(portions, consumed, id, usedPerCup,
                    state, ProvisionKind.Coffee, out var used);
                Assert.Equal(SurvivalResultCode.Success, result);
                Assert.Equal(usedPerCup, used);
                Assert.Equal(Math.Min(cup + 1, 10), state.CoffeeUsesSinceSleep);
                Assert.Equal(16f * Math.Max(0, 10 - cup) / 10f * usedPerCup / 1000f, state.Rest, 4);
                Assert.Equal(usedPerCup, portions[id].UsedUnits);
            }
            // None of the cups was empty: finishing is not the tolerance trigger.
            Assert.Equal(11, portions.Count);
            Assert.Empty(consumed);
            Assert.Equal(0f, SurvivalSimulator.GetCoffeeRestMultiplier(state));
        }

        [Fact]
        public void RepeatedRequestsAndLaterSipsDoNotCountTheSameCupAgain()
        {
            var portions = new Dictionary<string, PartialProvisionRecord>();
            var consumed = new HashSet<string>();
            var state = new SurvivalState { Rest = 0 };
            var id = Guid.NewGuid().ToString("D");
            PartialProvisionLedger.Consume(portions, consumed, id, 1, state, ProvisionKind.Coffee, out _);
            Assert.Equal(1, state.CoffeeUsesSinceSleep);
            var afterFirstSip = state.Rest;
            PartialProvisionLedger.Consume(portions, consumed, id, 1, state, ProvisionKind.Coffee, out _);
            Assert.Equal(afterFirstSip, state.Rest);
            PartialProvisionLedger.Consume(portions, consumed, id, 950, state, ProvisionKind.Coffee, out _);
            Assert.Equal(1, state.CoffeeUsesSinceSleep);
            Assert.Equal(15.2f, state.Rest, 4);
            PartialProvisionLedger.Consume(portions, consumed, id, 1000, state, ProvisionKind.Coffee, out _);
            Assert.Equal(1, state.CoffeeUsesSinceSleep);
            Assert.Equal(16f, state.Rest, 4);
        }

        [Fact]
        public void SavingUnfinishedCupsDoesNotResetTheDrinkersTolerance()
        {
            var portions = new Dictionary<string, PartialProvisionRecord>();
            var consumed = new HashSet<string>();
            var state = new SurvivalState { Rest = 0 };
            for (int cup = 0; cup < 3; cup++)
                PartialProvisionLedger.Consume(portions, consumed, Guid.NewGuid().ToString("D"),
                    950, state, ProvisionKind.Coffee, out _);
            var restoredState = JsonConvert.DeserializeObject<SurvivalState>(JsonConvert.SerializeObject(state));
            var restoredPortions = JsonConvert.DeserializeObject<Dictionary<string, PartialProvisionRecord>>(
                JsonConvert.SerializeObject(portions));
            Assert.Equal(3, restoredState.CoffeeUsesSinceSleep);
            restoredState.Rest = 0;
            PartialProvisionLedger.Consume(restoredPortions, consumed, Guid.NewGuid().ToString("D"),
                950, restoredState, ProvisionKind.Coffee, out _);
            Assert.Equal(4, restoredState.CoffeeUsesSinceSleep);
            Assert.Equal(16f * .7f * .95f, restoredState.Rest, 4);
            Assert.Empty(consumed);
        }
    }
}
