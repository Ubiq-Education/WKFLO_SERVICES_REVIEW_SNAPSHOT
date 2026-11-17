using AMAIS.WKFLO.Common.AI.Application.Plugins.ImageGeneration.Services;

namespace AMAIS.WKFLO.AI.UnitTests.Plugins.ImageGeneration.Services
{
    public class AspectRatioMapperTests
    {
        [Theory]
        [InlineData(3024, 4032, "3:4")]
        [InlineData(4032, 3024, "4:3")]
        [InlineData(1080, 1080, "1:1")]
        [InlineData(1920, 1080, "16:9")]
        [InlineData(1080, 1920, "9:16")]
        [InlineData(2560, 1080, "21:9")]
        [InlineData(1080, 1350, "4:5")]
        [InlineData(1350, 1080, "5:4")]
        [InlineData(2000, 3000, "2:3")]
        [InlineData(3000, 2000, "3:2")]
        [InlineData(1, 10000, "9:16")]
        public void Google_PicksNearestRatio(int w, int h, string expected) =>
            Assert.Equal(expected, AspectRatioMapper.ToNearestGoogleAspectRatio(w, h));

        [Theory]
        [InlineData(3024, 4032, "1024x1536")]
        [InlineData(4032, 3024, "1536x1024")]
        [InlineData(1080, 1080, "1024x1024")]
        [InlineData(1920, 1080, "1536x1024")]
        [InlineData(1080, 1920, "1024x1536")]
        [InlineData(1100, 1000, "1024x1024")]
        public void Azure_PicksNearestSize(int w, int h, string expected) =>
            Assert.Equal(expected, AspectRatioMapper.ToNearestAzureSize(w, h));

        [Theory]
        [InlineData("16:9", "1536x1024")]
        [InlineData("9:16", "1024x1536")]
        [InlineData("1:1", "1024x1024")]
        [InlineData("4:5", "1024x1536")]
        [InlineData("wide", null)]
        [InlineData("0:1", null)]
        [InlineData("1e308:1e-308", null)]
        [InlineData("1e-308:1e308", null)]
        [InlineData("Infinity:1", null)]
        [InlineData("1:Infinity", null)]
        [InlineData("", null)]
        [InlineData(null, null)]
        public void Azure_MapsRatioStrings(string ratio, string expected) =>
            Assert.Equal(expected, AspectRatioMapper.ToNearestAzureSize(ratio));

        [Fact]
        public void InvalidDimensions_ReturnNull()
        {
            Assert.Null(AspectRatioMapper.ToNearestGoogleAspectRatio(0, 10));
            Assert.Null(AspectRatioMapper.ToNearestAzureSize(10, -1));
        }
    }
}
