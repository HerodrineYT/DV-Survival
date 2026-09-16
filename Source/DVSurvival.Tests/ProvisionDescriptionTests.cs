using System.Globalization;
using DVSurvival.Core;
using Xunit;

namespace DVSurvival.Tests
{
    public class ProvisionDescriptionTests
    {
        [Theory]
        [InlineData(0, "100")] [InlineData(365, "63.5")]
        [InlineData(999, "0.1")] [InlineData(1000, "0")]
        [InlineData(-10, "100")] [InlineData(1200, "0")]
        public void DescriptionShowsClampedExactRemainder(int used, string expected)
        {
            Assert.Equal("Remaining: " + expected + "% · Details",
                ProvisionDescription.Format("Remaining: ", "Details", used, CultureInfo.InvariantCulture));
        }
        [Fact]
        public void RussianDescriptionUsesDecimalComma()
        {
            Assert.Equal("Осталось: 63,5% · Описание",
                ProvisionDescription.Format("Осталось: ", "Описание", 365, CultureInfo.GetCultureInfo("ru-RU")));
        }
        [Fact]
        public void DifferentItemsNeverShareTheirRemainingAmount()
        {
            var first = ProvisionDescription.Format("Remaining: ", "Coffee", 950, CultureInfo.InvariantCulture);
            var second = ProvisionDescription.Format("Remaining: ", "Coffee", 0, CultureInfo.InvariantCulture);
            Assert.StartsWith("Remaining: 5%", first);
            Assert.StartsWith("Remaining: 100%", second);
        }
    }
}
