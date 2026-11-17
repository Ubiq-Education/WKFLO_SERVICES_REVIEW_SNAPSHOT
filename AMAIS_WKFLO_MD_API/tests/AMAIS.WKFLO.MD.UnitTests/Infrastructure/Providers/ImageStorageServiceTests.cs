using AMAIS.WKFLO.Common.Infrastructure.Providers;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using Moq;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Infrastructure.Providers
{
    /// <summary>
    /// Unit tests for the generic ImageStorageService (Common.Infrastructure), covering the
    /// pass-through to IBlobStorageProvider.UploadAsync and the AbsoluteUrl construction, which is
    /// read directly from the STATIC_WEBSITE_BASE_URL env var — no appsettings-bound config, no
    /// hardcoded per-environment URLs; Staging and Production each set their own value in their
    /// Azure App Service configuration.
    /// </summary>
    public class ImageStorageServiceTests : IDisposable
    {
        private const string EnvironmentVariableName = "STATIC_WEBSITE_BASE_URL";
        private readonly string _originalValue;

        public ImageStorageServiceTests()
        {
            _originalValue = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(EnvironmentVariableName, _originalValue);
        }

        private static ImageStorageService CreateService(Mock<IBlobStorageProvider> blobStorageProviderMock)
        {
            return new ImageStorageService(blobStorageProviderMock.Object);
        }

        [Fact]
        public async Task SaveImageAsync_PassesContainerRelativePathContentAndContentType_ToBlobStorageProvider()
        {
            // Arrange
            var blobStorageProviderMock = new Mock<IBlobStorageProvider>();
            blobStorageProviderMock
                .Setup(x => x.UploadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("https://stc8f06d4e9bd78efb4b4bd9.blob.core.windows.net/$web/test/1/abc.png");

            var service = CreateService(blobStorageProviderMock);
            using var content = new MemoryStream([1, 2, 3]);

            // Act
            await service.SaveImageAsync("$web", "test/1/abc.png", content, "image/png", TestContext.Current.CancellationToken);

            // Assert
            blobStorageProviderMock.Verify(x => x.UploadAsync(
                "$web",
                "test/1/abc.png",
                content,
                "image/png",
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SaveImageAsync_EnvironmentVariableSet_ReturnsAbsoluteUrlBuiltFromIt()
        {
            // Arrange
            Environment.SetEnvironmentVariable(EnvironmentVariableName, "https://staging.static.wkflo.ai");

            var blobStorageProviderMock = new Mock<IBlobStorageProvider>();
            blobStorageProviderMock
                .Setup(x => x.UploadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("https://stc8f06d4e9bd78efb4b4bd9.blob.core.windows.net/$web/tenant/1/abc.png");

            var service = CreateService(blobStorageProviderMock);
            using var content = new MemoryStream([1, 2, 3]);

            // Act
            var result = await service.SaveImageAsync("$web", "tenant/1/abc.png", content, "image/png", TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal("tenant/1/abc.png", result.RelativePath);
            Assert.Equal("https://staging.static.wkflo.ai/tenant/1/abc.png", result.AbsoluteUrl);
        }

        [Fact]
        public async Task SaveImageAsync_EnvironmentVariableSetWithTrailingSlash_TrimsBeforeJoining()
        {
            // Arrange
            Environment.SetEnvironmentVariable(EnvironmentVariableName, "https://static.wkflo.ai/");

            var blobStorageProviderMock = new Mock<IBlobStorageProvider>();
            blobStorageProviderMock
                .Setup(x => x.UploadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("https://stc8f06d4e9bd78efb4b4bd9.blob.core.windows.net/$web/tenant/1/abc.png");

            var service = CreateService(blobStorageProviderMock);
            using var content = new MemoryStream([1, 2, 3]);

            // Act
            var result = await service.SaveImageAsync("$web", "tenant/1/abc.png", content, "image/png", TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal("https://static.wkflo.ai/tenant/1/abc.png", result.AbsoluteUrl);
        }

        [Fact]
        public async Task SaveImageAsync_EnvironmentVariableUnset_ReturnsNullAbsoluteUrl_ButUploadStillSucceeds()
        {
            // Arrange — an unconfigured env var must not throw: the upload itself still succeeded
            // and RelativePath is still usable/persistable by the caller even without an AbsoluteUrl.
            Environment.SetEnvironmentVariable(EnvironmentVariableName, null);

            var blobStorageProviderMock = new Mock<IBlobStorageProvider>();
            blobStorageProviderMock
                .Setup(x => x.UploadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("https://stc8f06d4e9bd78efb4b4bd9.blob.core.windows.net/$web/tenant/1/abc.png");

            var service = CreateService(blobStorageProviderMock);
            using var content = new MemoryStream([1, 2, 3]);

            // Act
            var result = await service.SaveImageAsync("$web", "tenant/1/abc.png", content, "image/png", TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal("tenant/1/abc.png", result.RelativePath);
            Assert.Null(result.AbsoluteUrl);
        }

        [Fact]
        public async Task SaveImageAsync_EnvironmentVariableEmpty_ReturnsNullAbsoluteUrl()
        {
            // Arrange
            Environment.SetEnvironmentVariable(EnvironmentVariableName, string.Empty);

            var blobStorageProviderMock = new Mock<IBlobStorageProvider>();
            blobStorageProviderMock
                .Setup(x => x.UploadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("https://stc8f06d4e9bd78efb4b4bd9.blob.core.windows.net/$web/tenant/1/abc.png");

            var service = CreateService(blobStorageProviderMock);
            using var content = new MemoryStream([1, 2, 3]);

            // Act
            var result = await service.SaveImageAsync("$web", "tenant/1/abc.png", content, "image/png", TestContext.Current.CancellationToken);

            // Assert
            Assert.Null(result.AbsoluteUrl);
        }
    }
}
