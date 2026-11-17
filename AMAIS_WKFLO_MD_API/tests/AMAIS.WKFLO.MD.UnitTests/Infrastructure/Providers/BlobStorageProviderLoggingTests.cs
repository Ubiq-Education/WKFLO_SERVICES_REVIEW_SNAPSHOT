using AMAIS.WKFLO.Common.Infrastructure.Providers;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Models.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Infrastructure.Providers
{
    /// <summary>
    /// Blob names can end with a user's original file name, so <see cref="BlobStorageProvider"/> logs
    /// <see cref="BlobStorageProvider.BlobNameForLog"/> (folders + fingerprint) instead of the name itself.
    /// </summary>
    public class BlobStorageProviderLoggingTests
    {
        private const string BlobName = "6f1c0a52-0000-0000-0000-000000000001/chat-session-123/7d2e-My Passport Scan.png";

        [Fact]
        public void BlobNameForLog_WithFolders_KeepsFoldersAndReplacesFileNameWithFingerprint()
        {
            var result = BlobStorageProvider.BlobNameForLog(BlobName);

            Assert.Matches(new Regex(@"^6f1c0a52-0000-0000-0000-000000000001/chat-session-123/… \[#[0-9a-f]{8}\]$"), result);
            Assert.DoesNotContain("Passport", result);
        }

        [Fact]
        public void BlobNameForLog_WithoutFolders_IsOnlyTheFingerprint()
        {
            var result = BlobStorageProvider.BlobNameForLog("holiday-photo.png");

            Assert.Matches(new Regex(@"^\[#[0-9a-f]{8}\]$"), result);
            Assert.DoesNotContain("holiday", result);
        }

        [Fact]
        public void BlobNameForLog_FingerprintIsStableAndDistinguishesNames()
        {
            Assert.Equal(BlobStorageProvider.BlobNameForLog(BlobName), BlobStorageProvider.BlobNameForLog(BlobName));
            Assert.NotEqual(BlobStorageProvider.BlobNameForLog("a/one.png"), BlobStorageProvider.BlobNameForLog("a/two.png"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void BlobNameForLog_NoName_IsPlaceholder(string blobName)
        {
            Assert.Equal("(none)", BlobStorageProvider.BlobNameForLog(blobName));
        }

        [Fact]
        public async Task DeleteAsync_Failure_LogsBlobRefWithoutFileName_AndRethrows()
        {
            var loggerMock = new Mock<ILogger<BlobStorageProvider>>();
            var provider = new BlobStorageProvider(
                Options.Create(new BlobStorageSettings { BaseUrl = "https://unit-test-account.blob.core.windows.net" }),
                Mock.Of<IDateTimeProvider>(),
                loggerMock.Object);

            // A cancelled token makes the first storage call fail before any network or credential access.
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.DeleteAsync("chat-images", BlobName, cts.Token));

            loggerMock.Verify(x => x.Log(LogLevel.Error, It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString().Contains(BlobStorageProvider.BlobNameForLog(BlobName))
                    && !v.ToString().Contains("Passport")),
                It.IsAny<OperationCanceledException>(), It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);
        }
    }
}
