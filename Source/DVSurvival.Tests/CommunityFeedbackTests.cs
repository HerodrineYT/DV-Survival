using DVSurvival.Core;
using Xunit;

namespace DVSurvival.Tests
{
    public class CommunityFeedbackTests
    {
        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
        public void LowNeedIndicatorIsRedAndHasAMarkerEvenWithoutNumbers(int index)
        {
            var state = new SurvivalState { Health = 10, Hunger = 10, Hydration = 10, Rest = 10 };
            Assert.Equal("<color=#FF5555>! 10%</color>", NeedAlertText.Format(state, index, "10%", true));
            Assert.Equal("<color=#FF5555>! </color>", NeedAlertText.Format(state, index, "", true));
            Assert.Equal("10%", NeedAlertText.Format(state, index, "10%", false));
            Assert.Equal("100%", NeedAlertText.Format(new SurvivalState(), index, "100%", true));
        }
        [Theory]
        [InlineData(0f, 0f)] [InlineData(5f, 0f)] [InlineData(5.1f, .1f)]
        [InlineData(10f, 5f)] [InlineData(20f, 15f)] [InlineData(80f, 75f)]
        public void DismountCostsOneHealthPointPerKmhAboveFive(float speed, float expected)
        {
            Assert.Equal(expected, SurvivalSimulator.CalculateTraumaDamage(TraumaKind.TrainDismount, speed, 1.8f), 4);
        }

        [Theory]
        [InlineData(0f)] [InlineData(1f)] [InlineData(2f)]
        public void DismountUsesDamageMultiplier(float multiplier)
        {
            var state = new SurvivalState { Health = 100f };
            SurvivalSimulator.ApplyTrauma(state, TraumaKind.TrainDismount, 20f, 1.8f,
                new SurvivalTuning { DamageMultiplier = multiplier });
            Assert.Equal(100f - 15f * multiplier, state.Health);
        }

        [Fact]
        public void DismountEvidenceIsPersonalBoundedAndSingleUse()
        {
            var evidence = new TrainDismountEvidence();
            evidence.Observe(1, 20f, 10);
            Assert.False(evidence.TryTake(2, 20f, 11, out _));
            Assert.False(evidence.TryTake(1, float.NaN, 11, out _));
            Assert.True(evidence.TryTake(1, 200f, 11, out var speed));
            Assert.Equal(20f, speed);
            Assert.False(evidence.TryTake(1, 20f, 11, out _));
            evidence.Observe(1, 20f, 10);
            Assert.False(evidence.TryTake(1, 20f, 21, out _));
            evidence.Clear();
            Assert.False(evidence.TryTake(1, 20f, 11, out _));
        }

        [Theory]
        [InlineData(1f)] [InlineData(2f)] [InlineData(8f)]
        public void SleepHealsOneAndHalfPointsPerHourWithoutDoublePassiveRecovery(float hours)
        {
            var state = new SurvivalState { Health = 40f, Rest = 20f };
            var otherPlayer = state.Clone();
            SurvivalSimulator.Sleep(state, hours, new SurvivalEnvironment { AmbientTemperatureCelsius = 22f },
                new SurvivalTuning { HungerHoursFromFull = 48f, HydrationHoursFromFull = 36f });
            Assert.Equal(40f + hours * 1.5f, state.Health, 4);
            Assert.Equal(40f, otherPlayer.Health);
        }

        [Fact]
        public void MinorInjuriesHealWithModerateNeedsButNotWhileStarvingOrInExtremeHeat()
        {
            var state = new SurvivalState { Health = 60f, Hunger = 40f, Hydration = 40f };
            SurvivalSimulator.Sleep(state, 1f, new SurvivalEnvironment { AmbientTemperatureCelsius = 22f }, new SurvivalTuning());
            Assert.Equal(61.5f, state.Health, 4);
            state = new SurvivalState { Health = 60f, Hunger = 0f };
            SurvivalSimulator.Sleep(state, 1f, new SurvivalEnvironment { AmbientTemperatureCelsius = 22f }, new SurvivalTuning());
            Assert.True(state.Health < 60f);
            state = new SurvivalState { Health = 60f };
            SurvivalSimulator.Sleep(state, 1f, new SurvivalEnvironment { AmbientTemperatureCelsius = 56f }, new SurvivalTuning());
            Assert.True(state.Health < 60f);
        }

        [Fact]
        public void SleepHealingNeverExceedsFullHealth()
        {
            var state = new SurvivalState { Health = 99f };
            SurvivalSimulator.Sleep(state, 1f, new SurvivalEnvironment { AmbientTemperatureCelsius = 22f }, new SurvivalTuning());
            Assert.Equal(100f, state.Health);
        }

        [Theory]
        [InlineData(ProvisionKind.Meal, 5f)] [InlineData(ProvisionKind.Water, 3f)]
        [InlineData(ProvisionKind.Coffee, 3f)] [InlineData(ProvisionKind.FirstAid, 8f)]
        public void ConsumablesHaveShortButNonInstantActions(ProvisionKind kind, float seconds)
        { Assert.Equal(seconds, ProvisionUseTiming.Seconds(kind)); }

        [Fact]
        public void UseCompletesOnlyOnceAndPauseDoesNotProgress()
        {
            var use = new ProvisionUseProgress(ProvisionKind.Meal);
            Assert.False(use.Advance(0));
            Assert.False(use.Advance(float.NaN));
            Assert.False(use.Advance(-2));
            Assert.False(use.Advance(4));
            Assert.Equal(.8f, use.Fraction);
            Assert.False(use.Advance(0));
            Assert.True(use.Advance(1));
            Assert.False(use.Advance(100));
            Assert.Equal(1f, use.Fraction);
        }

        [Fact]
        public void CancelledUseCannotCompleteAndOtherPlayersAreIndependent()
        {
            var first = new ProvisionUseProgress(ProvisionKind.Water);
            var second = new ProvisionUseProgress(ProvisionKind.Water);
            first.Advance(1);
            first.Cancel();
            Assert.False(first.Advance(10));
            Assert.True(second.Advance(3));
        }
    }
}
