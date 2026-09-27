using System;
using DVSurvival.Core;
using Xunit;

namespace DVSurvival.Tests
{
    public sealed class TravelPauseTests
    {
        [Fact]
        public void PauseIsPersonalAndAllSurvivalValuesStayUnchanged()
        {
            var pause = new TravelPauseTracker();
            pause.Update(1, true, 1);
            var captured = pause.Capture(2, false);
            var traveller = new SurvivalState
            {
                Hunger = 50, Hydration = 50, Rest = 50, Health = 40,
                BodyTemperatureCelsius = 39, LowRestGameHours = 74,
                CaffeineHours = 1, WarmthHours = 1, ExhaustionHoursRemaining = 2,
                FirstAidSecondsRemaining = 20
            };
            var before = traveller.Clone();
            var peer = new SurvivalState();
            var environment = new SurvivalEnvironment { GameHours = 1, AmbientTemperatureCelsius = 90 };
            foreach (var player in new byte[] { 1, 2 })
            {
                if (captured != null && captured.Contains(player)) continue;
                SurvivalSimulator.Advance(player == 1 ? traveller : peer, environment, new SurvivalTuning(), 60);
            }
            if (!pause.IsPaused(1, 2)) FirstAidRecovery.Advance(traveller, 20);
            foreach (var field in typeof(SurvivalState).GetFields())
                Assert.Equal(field.GetValue(before), field.GetValue(traveller));
            Assert.True(peer.Hunger < 100);
            Assert.True(peer.Hydration < 100);
            Assert.True(peer.Rest < 100);
        }

        [Fact]
        public void ArrivalConsumesTravelClockWithoutAnyCatchUp()
        {
            var pause = new TravelPauseTracker();
            var calendar = new GameCalendarClock();
            var departure = new DateTime(2026, 1, 1);
            calendar.Observe(departure);
            pause.Update(255, true, 0);
            Assert.Equal(8d, calendar.Observe(departure.AddHours(8)));
            Assert.Contains((byte)255, pause.Capture(2, true));
            pause.Update(255, false, 3);
            Assert.False(pause.IsPaused(255, 3));
            // Include the final fractional interval, then resume with only fresh elapsed time.
            Assert.Contains((byte)255, pause.Capture(3, false));
            Assert.Null(pause.Capture(4, false));
            Assert.Equal(1d, calendar.Observe(departure.AddHours(9)));
        }

        [Fact]
        public void DeferredCalendarKeepsPauseAfterArrivalAndPreservesAnotherPlayersSleep()
        {
            var pause = new TravelPauseTracker();
            var reconciler = new SleepCalendarReconciler();
            var before = new DateTime(2026, 1, 1).Ticks;
            var after = before + 8 * TimeSpan.TicksPerHour;
            pause.Update(1, true, 0);
            var captured = pause.Capture(1, true);
            Assert.True(reconciler.TryRecordJump(new SleepCalendarJump(1, before, after, 1)));
            Assert.True(reconciler.TryRecordSleep(new SleepCalendarSleep(2, 1, before, after, 2)));
            pause.Update(1, false, 3);
            pause.Capture(3, false);
            var operations = reconciler.DrainOperations(100);
            Assert.NotEmpty(operations);
            var committed = false;
            foreach (var operation in operations)
            {
                if (operation.Kind == SleepCalendarOperationKind.Advance)
                {
                    Assert.Contains((byte)1, captured);
                    Assert.DoesNotContain((byte)2, captured);
                    Assert.Equal(0d, operation.Advance.GetAwakeHours(2));
                    Assert.True(operation.Advance.GetAwakeHours(3) > 0);
                }
                if (operation.Kind == SleepCalendarOperationKind.CommitSleep)
                {
                    var sleeper = new SurvivalState { Rest = 10, Health = 50 };
                    SurvivalSimulator.Sleep(sleeper, 8, new SurvivalEnvironment(), new SurvivalTuning());
                    Assert.True(sleeper.Rest > 90);
                    Assert.True(sleeper.Health > 50);
                    committed = true;
                }
            }
            Assert.True(committed);
            Assert.Null(pause.Capture(100, false));
        }

        [Fact]
        public void DelayedRemoteTimeSkipStillKnowsWhoWasTravelling()
        {
            var pause = new TravelPauseTracker();
            pause.Update(1, true, 0);
            pause.Capture(1, false);
            pause.Update(1, false, 2);
            pause.Capture(2, false);
            Assert.Null(pause.Capture(3, false));
            Assert.Contains((byte)1, pause.Capture(4, true));
            Assert.Null(pause.Capture(13, true));
        }

        [Fact]
        public void LoadingStallIsProtectedButLostReportsCannotPauseForever()
        {
            var pause = new TravelPauseTracker();
            pause.Update(1, true, 0);
            Assert.True(pause.IsPaused(1, 60));
            pause.Capture(60, false);
            Assert.False(pause.IsPaused(1, TravelPauseTracker.ReportTimeoutSeconds));
            Assert.Null(pause.Capture(TravelPauseTracker.ReportTimeoutSeconds + 1, false));
        }

        [Fact]
        public void ShortTeleportBetweenSimulationTicksIsNotMissed()
        {
            var pause = new TravelPauseTracker();
            pause.Update(1, true, 1);
            pause.Update(1, false, 1.1);
            Assert.Contains((byte)1, pause.Capture(2, false));
            Assert.Null(pause.Capture(3, false));
        }

        [Fact]
        public void DisconnectAndWorldResetClearTravelLeases()
        {
            var pause = new TravelPauseTracker();
            pause.Update(1, true, 1);
            pause.Update(2, true, 1);
            pause.Remove(1);
            Assert.False(pause.IsPaused(1, 2));
            Assert.DoesNotContain((byte)1, pause.Capture(2, true));
            pause.Reset();
            Assert.False(pause.IsPaused(2, 2));
            Assert.Null(pause.Capture(3, true));
        }

        [Fact]
        public void NormalPlayDoesNotAllocatePauseSnapshots()
        {
            var pause = new TravelPauseTracker();
            pause.Update(1, false, 1);
            Assert.Null(pause.Capture(2, false));
            Assert.Null(pause.Capture(2, true));
        }
    }
}
