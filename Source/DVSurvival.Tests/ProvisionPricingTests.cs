using DVSurvival.Core;
using Xunit;

namespace DVSurvival.Tests
{
    public sealed class ProvisionPricingTests
    {
        [Theory]
        [InlineData(90)]
        [InlineData(40)]
        [InlineData(75)]
        [InlineData(350)]
        [InlineData(120)]
        [InlineData(777)]
        public void SandboxIsFreeButCareerRetainsConfiguredPrice(int price)
        {
            Assert.Equal(0, ProvisionPricing.Resolve(price, "Sandbox"));
            Assert.Equal(price, ProvisionPricing.Resolve(price, "Career"));
            // Switching sessions must not destroy the user's configured price.
            Assert.Equal(0, ProvisionPricing.Resolve(price, "Sandbox"));
            Assert.Equal(price, ProvisionPricing.Resolve(price, "Career"));
        }
        [Fact]
        public void UnknownSessionAndInvalidProvisionStaySafe()
        {
            Assert.Equal(90, ProvisionPricing.Resolve(90, null));
            Assert.Equal(90, ProvisionPricing.Resolve(90, ""));
            Assert.Equal(-1, ProvisionPricing.Resolve(-1, "Sandbox"));
            Assert.Equal(0, ProvisionPricing.Resolve(0, "Career"));
        }
    }
}
