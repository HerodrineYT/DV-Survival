using DVSurvival.Core;
using Xunit;

namespace DVSurvival.Tests
{
    public sealed class CabHeaterActionValidatorTests
    {
        private static SurvivalPlayerInfo Player()
        {
            return new SurvivalPlayerInfo
            {
                IsOnCar = true,
                OccupiedCarIsLocomotive = true,
                OccupiedCarSupportsCabHeater = true,
                OccupiedCarId = "car-guid-dm3"
            };
        }

        private static SurvivalActionRequest Request(float value = 1f)
        {
            return new SurvivalActionRequest
            {
                Action = SurvivalActionKind.SetCabHeater,
                Amount = value,
                ItemIdentity = "car-guid-dm3"
            };
        }

        [Fact]
        public void AcceptsExactSwitchForPlayersCurrentSupportedLocomotive()
        {
            Assert.True(CabHeaterActionValidator.IsValid(Player(), Request()));
        }

        [Fact]
        public void RejectsSpoofedCarOrPlayerOutsideLocomotive()
        {
            var request = Request();
            request.ItemIdentity = "another-car";
            Assert.False(CabHeaterActionValidator.IsValid(Player(), request));
            var player = Player();
            player.IsOnCar = false;
            Assert.False(CabHeaterActionValidator.IsValid(player, Request()));
        }

        [Theory]
        [InlineData(-1f)]
        [InlineData(0.5f)]
        [InlineData(2f)]
        public void RejectsNonBinaryValues(float value)
        {
            Assert.False(CabHeaterActionValidator.IsValid(Player(), Request(value)));
        }

        [Fact]
        public void RejectsUnsupportedCarAndFieldsFromOtherActions()
        {
            var player = Player();
            player.OccupiedCarSupportsCabHeater = false;
            Assert.False(CabHeaterActionValidator.IsValid(player, Request()));
            var request = Request();
            request.Provision = ProvisionKind.Coffee;
            Assert.False(CabHeaterActionValidator.IsValid(Player(), request));
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(1f)]
        public void Dm3AcceptsOnlyOffAndOn(float level)
        {
            var player = Player();
            Assert.True(CabHeaterActionValidator.IsValid(player, Request(level)));
        }

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(-0.01f)]
        [InlineData(0.5f)]
        [InlineData(1.01f)]
        public void Dm3RejectsInvalidOrIntermediateValues(float level)
        {
            var player = Player();
            Assert.False(CabHeaterActionValidator.IsValid(player, Request(level)));
        }

        [Theory]
        [InlineData(0.33333334f)]
        [InlineData(0.6666667f)]
        public void RejectsRemovedDm3IntermediateDetents(float level)
        {
            Assert.False(CabHeaterActionValidator.IsValid(Player(), Request(level)));
        }
    }
}
