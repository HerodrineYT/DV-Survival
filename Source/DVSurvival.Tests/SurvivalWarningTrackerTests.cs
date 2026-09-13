using DVSurvival.Core;
using Xunit;

namespace DVSurvival.Tests
{
    public sealed class SurvivalWarningTrackerTests
    {
        private static SurvivalState Healthy()
        { return new SurvivalState { Health=100, Hunger=100, Hydration=100, Rest=100, BodyTemperatureCelsius=37 }; }

        [Fact]
        public void FirstSnapshotIsSilentEvenWhenAlreadyCritical()
        {
            var tracker = new SurvivalWarningTracker();
            var s = new SurvivalState { Health=10, Hunger=5, Hydration=5, Rest=5, BodyTemperatureCelsius=34, LowRestGameHours=72 };
            Assert.Equal(SurvivalWarningEvent.None, tracker.Observe(s));
        }

        [Fact]
        public void LargeTimeJumpReportsEveryCrossedMilestoneOnlyOnce()
        {
            var tracker = new SurvivalWarningTracker(); var s = Healthy(); tracker.Observe(s);
            s.Rest=60; s.LowRestGameHours=72;
            var events=tracker.Observe(s);
            Assert.True(events.HasFlag(SurvivalWarningEvent.LowRestOneDay));
            Assert.True(events.HasFlag(SurvivalWarningEvent.ExhaustionSoon));
            Assert.True(events.HasFlag(SurvivalWarningEvent.ExhaustionStarted));
            Assert.True(events.HasFlag(SurvivalWarningEvent.HallucinationSoon));
            Assert.True(events.HasFlag(SurvivalWarningEvent.HallucinationStarted));
            Assert.Equal(SurvivalWarningEvent.None, tracker.Observe(s));
        }

        [Fact]
        public void RecoveryAfterVisibleStageAndASecondStreakCanWarnAgain()
        {
            var tracker=new SurvivalWarningTracker();var s=Healthy();tracker.Observe(s);
            s.Rest=60;s.LowRestGameHours=24;
            Assert.True(tracker.Observe(s).HasFlag(SurvivalWarningEvent.LowRestOneDay));
            s.Rest=71;s.LowRestGameHours=0;
            Assert.True(tracker.Observe(s).HasFlag(SurvivalWarningEvent.LowRestRecovered));
            s.Rest=69;s.LowRestGameHours=24;
            Assert.True(tracker.Observe(s).HasFlag(SurvivalWarningEvent.LowRestOneDay));
        }

        [Fact]
        public void SubTwentyFourHourStreakAndRecoveryAreSilent()
        {
            var tracker=new SurvivalWarningTracker();var s=Healthy();tracker.Observe(s);
            s.Rest=60;s.LowRestGameHours=.1;Assert.Equal(SurvivalWarningEvent.None,tracker.Observe(s));
            s.LowRestGameHours=8;Assert.Equal(SurvivalWarningEvent.None,tracker.Observe(s));
            s.LowRestGameHours=23.999;Assert.Equal(SurvivalWarningEvent.None,tracker.Observe(s));
            s.Rest=71;s.LowRestGameHours=0;Assert.Equal(SurvivalWarningEvent.None,tracker.Observe(s));
        }

        [Fact]
        public void ResetMakesFirstSnapshotOfNextSessionSilent()
        {
            var tracker=new SurvivalWarningTracker();var s=Healthy();tracker.Observe(s);
            s.Rest=60;s.LowRestGameHours=24;tracker.Observe(s);
            tracker.Reset();s=Healthy();
            Assert.Equal(SurvivalWarningEvent.None,tracker.Observe(s));
        }

        [Fact]
        public void NeedWarningsUseHysteresisAndRearm()
        {
            var tracker=new SurvivalWarningTracker();var s=Healthy();tracker.Observe(s);
            s.Health=39.9f;Assert.True(tracker.Observe(s).HasFlag(SurvivalWarningEvent.RunHealth));
            s.Health=42;Assert.Equal(SurvivalWarningEvent.None,tracker.Observe(s));
            s.Health=39;Assert.Equal(SurvivalWarningEvent.None,tracker.Observe(s));
            s.Health=45;tracker.Observe(s);s.Health=39;
            Assert.True(tracker.Observe(s).HasFlag(SurvivalWarningEvent.RunHealth));
        }

        [Fact]
        public void PlayersHaveIndependentLocalWarningTrackers()
        {
            var a=new SurvivalWarningTracker();var b=new SurvivalWarningTracker();var s=Healthy();a.Observe(s);b.Observe(s);
            s.Rest=60;s.LowRestGameHours=48;
            Assert.True(a.Observe(s).HasFlag(SurvivalWarningEvent.ExhaustionStarted));
            Assert.True(b.Observe(s).HasFlag(SurvivalWarningEvent.ExhaustionStarted));
        }

        [Fact]
        public void ThermalWarningsUseInclusiveHeatAndStrictColdThresholds()
        {
            var tracker = new SurvivalWarningTracker();
            var state = Healthy();
            tracker.Observe(state);
            state.BodyTemperatureCelsius = 38.5f;
            Assert.True(tracker.Observe(state).HasFlag(SurvivalWarningEvent.Hyperthermia));

            tracker.Reset();
            state = Healthy();
            tracker.Observe(state);
            state.BodyTemperatureCelsius = 35f;
            Assert.False(tracker.Observe(state).HasFlag(SurvivalWarningEvent.Hypothermia));
            state.BodyTemperatureCelsius = 34.99f;
            Assert.True(tracker.Observe(state).HasFlag(SurvivalWarningEvent.Hypothermia));
        }
    }
}
