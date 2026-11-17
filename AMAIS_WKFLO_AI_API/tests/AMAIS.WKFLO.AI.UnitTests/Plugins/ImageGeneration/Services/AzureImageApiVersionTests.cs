using AMAIS.WKFLO.Common.AI.Application.Plugins.ImageGeneration.Services;

namespace AMAIS.WKFLO.AI.UnitTests.Plugins.ImageGeneration.Services
{
    public class AzureImageApiVersionTests
    {
        [Theory]
        [InlineData("2025-04-01-preview", true)]
        [InlineData("2025-05-01-preview", true)]
        [InlineData("2026-01-01-preview", true)]
        [InlineData(" 2025-04-01-PREVIEW ", true)]
        [InlineData("2025-04-01", false)]
        [InlineData("2025-03-01-preview", false)]
        [InlineData("2024-10-21", false)]
        [InlineData("2024-12-01-preview", false)]
        [InlineData("v1", false)]
        [InlineData("garbage", false)]
        [InlineData("2025-04-01-preview-x", false)]
        [InlineData("", false)]
        [InlineData("   ", false)]
        [InlineData(null, false)]
        public void SupportsEditSize_GatesByDatedPreviewVersion(string version, bool expected) =>
            Assert.Equal(expected, AzureImageApiVersion.SupportsEditSize(version));

        [Fact]
        public void Default_SupportsEditSize() =>
            Assert.True(AzureImageApiVersion.SupportsEditSize(AzureImageApiVersion.Default));
    }
}
