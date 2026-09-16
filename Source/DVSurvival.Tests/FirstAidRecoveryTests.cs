using System.IO;
using DVSurvival.Core;
using Newtonsoft.Json;
using Xunit;

namespace DVSurvival.Tests
{
    public class FirstAidRecoveryTests
    {
        [Fact]
        public void FirstAidRestoresFortyHealthOverTwentyRealSeconds()
        {
            var s = new SurvivalState { Health = 20f };
            Assert.Equal(SurvivalResultCode.Success, SurvivalSimulator.Consume(s, ProvisionKind.FirstAid, new SurvivalTuning()));
            Assert.Equal(20f, s.Health);
            for (int i = 1; i <= 40; i++)
            {
                Assert.True(FirstAidRecovery.Advance(s, .5f));
                Assert.Equal(20f + i, s.Health);
            }
            Assert.Equal(0f, s.FirstAidSecondsRemaining);
            Assert.False(FirstAidRecovery.Advance(s, 10));
            Assert.Equal(60f, s.Health);
        }
        [Fact]
        public void CannotStackOrWasteAnotherKitDuringTreatment()
        {
            var s = new SurvivalState { Health = 20f, FirstAid = 2 };
            var tuning = new SurvivalTuning();
            SurvivalSimulator.Consume(s, ProvisionKind.FirstAid, tuning);
            FirstAidRecovery.Advance(s, 5f);
            Assert.Equal(SurvivalResultCode.NotNeeded, SurvivalSimulator.Consume(s, ProvisionKind.FirstAid, tuning));
            Assert.Equal(1, s.FirstAid);
            Assert.Equal(15f, s.FirstAidSecondsRemaining);
        }
        [Fact]
        public void TreatmentDoesNotUseGameCalendarAndDoesNotHealOtherPlayers()
        {
            var s = new SurvivalState { Health = 20, FirstAidSecondsRemaining = 20 };
            var other = new SurvivalState { Health = 20 };
            SurvivalSimulator.Sleep(s, 10, new SurvivalEnvironment { AmbientTemperatureCelsius = 22 },
                new SurvivalTuning { DamageMultiplier = 0 });
            Assert.Equal(20f, s.FirstAidSecondsRemaining);
            FirstAidRecovery.Advance(s, 5);
            Assert.False(FirstAidRecovery.Advance(other, 5));
            Assert.Equal(20f, other.Health);
            Assert.Equal(15f, s.FirstAidSecondsRemaining);
        }
        [Fact]
        public void HealthIsCappedAndDeathCancelsTreatment()
        {
            var s = new SurvivalState { Health = 98, FirstAidSecondsRemaining = 20 };
            FirstAidRecovery.Advance(s, 10);
            Assert.Equal(100f, s.Health);
            s.Health = 1;
            SurvivalSimulator.ApplyTrauma(s, TraumaKind.Fall, 10, 1.8f, new SurvivalTuning());
            Assert.Equal(0f, s.FirstAidSecondsRemaining);
            Assert.Equal(10f, s.Health);
        }
        [Theory]
        [InlineData(0)] [InlineData(-1)] [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)]
        public void InvalidOrPausedTimeCannotProgressTreatment(float seconds)
        {
            var s = new SurvivalState { Health = 20, FirstAidSecondsRemaining = 20 };
            Assert.False(FirstAidRecovery.Advance(s, seconds));
            Assert.Equal(20f, s.Health);
            Assert.Equal(20f, s.FirstAidSecondsRemaining);
        }
        [Fact]
        public void TreatmentSurvivesSaveAndNetworkRoundTrip()
        {
            var s = new SurvivalState { Health = 20, FirstAidSecondsRemaining = 20 };
            FirstAidRecovery.Advance(s, 7.5f);
            var saved = JsonConvert.DeserializeObject<SurvivalState>(JsonConvert.SerializeObject(s));
            using (var stream = new MemoryStream())
            {
                SurvivalStateCodec.Write(new BinaryWriter(stream), saved);
                stream.Position = 0;
                var copy = SurvivalStateCodec.Read(new BinaryReader(stream));
                Assert.Equal(12.5f, copy.FirstAidSecondsRemaining);
                Assert.Equal(12.5f, copy.Clone().FirstAidSecondsRemaining);
                FirstAidRecovery.Advance(copy, 12.5f);
                Assert.Equal(60f, copy.Health);
            }
            var old = JsonConvert.DeserializeObject<SurvivalState>("{\"Health\":20}");
            Assert.Equal(0f, old.FirstAidSecondsRemaining);
            Assert.True(old.IsValid());
        }
    }
}
