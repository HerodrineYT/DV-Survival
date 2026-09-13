using DVSurvival.Core;
using DVSurvival.Multiplayer;
using System.IO;
using Xunit;

namespace DVSurvival.Tests
{
    public sealed class NativeBedValidationTests
    {
        private static SurvivalActionRequest Sleep()
        {
            return new SurvivalActionRequest { Action = SurvivalActionKind.SleepWithoutTimeAdvance,
                Amount = 10f, HasNativeBed = true, BedWorldX = 12345f, BedWorldY = 110f, BedWorldZ = 23456f };
        }

        [Theory]
        [InlineData(SurvivalActionKind.Sleep)]
        [InlineData(SurvivalActionKind.SleepWithoutTimeAdvance)]
        public void RemoteBedWithoutHostSceneRoundTripsAndRestoresOnlySleeper(SurvivalActionKind action)
        {
            var request = Sleep(); request.Action = action;
            using (var stream = new MemoryStream())
            {
                new SurvivalActionPacket { Request = request }.Serialize(new BinaryWriter(stream));
                stream.Position = 0;
                var packet = new SurvivalActionPacket(); packet.Deserialize(new BinaryReader(stream));
                request = packet.Request;
            }
            Assert.True(NativeBedValidation.IsNearReportedBed(request, 12345f, 108f, 23457f, 12f));
            var sleeper = new SurvivalState { Rest = 0f, LowRestGameHours = 100d };
            var other = new SurvivalState { Rest = 24f, LowRestGameHours = 30d };
            Assert.Equal(SurvivalResultCode.Success, SurvivalSimulator.Sleep(sleeper, request.Amount,
                new SurvivalEnvironment { AmbientTemperatureCelsius = 22f }, new SurvivalTuning { DamageMultiplier = 0f }));
            Assert.Equal(100f, sleeper.Rest); Assert.Equal(0d, sleeper.LowRestGameHours);
            Assert.Equal(24f, other.Rest); Assert.Equal(30d, other.LowRestGameHours);
        }

        [Fact]
        public void DifferentFloatingOriginsResolveToSameAbsoluteBed()
        {
            var request = Sleep();
            const float clientOriginX = -12000f, hostOriginX = -4000f;
            var clientBedX = request.BedWorldX + clientOriginX;
            request.BedWorldX = clientBedX - clientOriginX;
            var hostPlayerX = 12345f + hostOriginX;
            Assert.True(NativeBedValidation.IsNearReportedBed(request,
                hostPlayerX - hostOriginX, 110f, 23456f, 12f));
            Assert.False(NativeBedValidation.IsNearReportedBed(request,
                hostPlayerX, 110f, 23456f, 12f));
        }

        [Fact]
        public void MissingProofAndWrongActionNeverBypassMissingHostBed()
        {
            var request = Sleep(); request.HasNativeBed = false;
            Assert.False(NativeBedValidation.IsNearReportedBed(request, 12345, 110, 23456, 12));
            request = Sleep(); request.Action = SurvivalActionKind.ConsumePhysical;
            Assert.False(NativeBedValidation.IsNearReportedBed(request, 12345, 110, 23456, 12));
        }

        [Theory]
        [InlineData(12358f)]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        public void FarOrInvalidPlayerPositionIsRejected(float playerX)
        {
            Assert.False(NativeBedValidation.IsNearReportedBed(Sleep(), playerX, 110, 23456, 12));
        }

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.NegativeInfinity)]
        [InlineData(999999f)]
        public void BadBedAnchorIsRejected(float bedX)
        {
            var request = Sleep(); request.BedWorldX = bedX;
            Assert.False(NativeBedValidation.IsNearReportedBed(request, 12345, 110, 23456, 12));
        }
    }
}
