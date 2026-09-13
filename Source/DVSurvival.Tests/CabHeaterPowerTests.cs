using DVSurvival.Core;
using Xunit;

namespace DVSurvival.Tests
{
    public sealed class CabHeaterPowerTests
    {
        [Fact]
        public void Dm3HigherDetentsWarmFasterAndReachHigherTemperature()
        {
            var weak = new CabinClimate();
            var medium = new CabinClimate();
            var full = new CabinClimate();
            for (var i = 0; i < 120; i++)
            {
                weak.Advance(0.5f, -10f, true, 0f, false, false,
                    heatingRateMultiplier: 1f / 3f, heaterPower: 1f / 3f);
                medium.Advance(0.5f, -10f, true, 0f, false, false,
                    heatingRateMultiplier: 2f / 3f, heaterPower: 2f / 3f);
                full.Advance(0.5f, -10f, true, 0f, false, false);
            }
            Assert.True(weak.AirTemperature > -10f);
            Assert.True(medium.AirTemperature > weak.AirTemperature);
            Assert.True(full.AirTemperature > medium.AirTemperature);
            for (var i = 0; i < 2400; i++)
            {
                weak.Advance(0.5f, -10f, true, 0f, false, false,
                    heatingRateMultiplier: 1f / 3f, heaterPower: 1f / 3f);
                full.Advance(0.5f, -10f, true, 0f, false, false);
            }
            Assert.InRange(weak.AirTemperature, 0.5f, 0.8f);
            Assert.InRange(full.AirTemperature, 21.9f, 22.1f);
        }

        [Fact]
        public void TurningOffOrOpeningWindowStopsHeatAtEveryDetent()
        {
            foreach (var power in new[] { 1f / 3f, 2f / 3f, 1f })
            {
                var cabin = new CabinClimate();
                for (var i = 0; i < 600; i++)
                    cabin.Advance(0.5f, -10f, true, 1f, false, false, heaterPower: power);
                var before = cabin.AirTemperature;
                cabin.Advance(0.5f, -10f, true, 1f, true, false, heaterPower: power);
                Assert.True(cabin.AirTemperature < before);
                before = cabin.AirTemperature;
                cabin.Advance(0.5f, -10f, false, 0f, false, false,
                    heatingRateMultiplier: 0f, heaterEnabled: false, heaterPower: 0f);
                Assert.True(cabin.AirTemperature < before);
            }
        }
    }
}
