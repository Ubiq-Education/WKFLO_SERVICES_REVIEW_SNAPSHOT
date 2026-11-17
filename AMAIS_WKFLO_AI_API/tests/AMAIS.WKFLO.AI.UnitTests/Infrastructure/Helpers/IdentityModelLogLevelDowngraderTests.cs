using AMAIS.WKFLO.AI.WebAPI.Infrastructure.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Abstractions;
using Moq;
using System;

namespace AMAIS.WKFLO.AI.UnitTests.Infrastructure.Helpers
{
    public class IdentityModelLogLevelDowngraderTests
    {
        private static (Mock<IIdentityLogger> inner, Mock<ILogger> downgradedLogger, IdentityModelLogLevelDowngrader sut) BuildSut()
        {
            var inner = new Mock<IIdentityLogger>();
            var downgradedLogger = new Mock<ILogger>();

            var loggerFactory = new Mock<ILoggerFactory>();
            loggerFactory
                .Setup(f => f.CreateLogger(It.IsAny<string>()))
                .Returns(downgradedLogger.Object);

            var sut = new IdentityModelLogLevelDowngrader(inner.Object, loggerFactory.Object);

            return (inner, downgradedLogger, sut);
        }

        private static void VerifyLoggedAt(Mock<ILogger> logger, LogLevel level, Times times)
        {
            logger.Verify(l => l.Log(
                level,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                times);
        }

        [Fact]
        public void Log_WithIdx10517Message_DowngradesToDebug_AndDoesNotForwardToInnerLogger()
        {
            var (inner, downgradedLogger, sut) = BuildSut();
            var entry = new LogEntry
            {
                EventLogLevel = EventLogLevel.Error,
                Message = "IDX10517: Signature validation failed. The token's kid is missing. Keys tried: ..."
            };

            sut.Log(entry);

            VerifyLoggedAt(downgradedLogger, LogLevel.Debug, Times.Once());
            inner.Verify(i => i.Log(It.IsAny<LogEntry>()), Times.Never);
        }

        [Fact]
        public void Log_WithOtherMessage_ForwardsUnchangedToInnerLogger_AndDoesNotDowngrade()
        {
            var (inner, downgradedLogger, sut) = BuildSut();
            var entry = new LogEntry
            {
                EventLogLevel = EventLogLevel.Error,
                Message = "IDX12345: Some genuine Azure AD token validation failure."
            };

            sut.Log(entry);

            inner.Verify(i => i.Log(entry), Times.Once);
            VerifyLoggedAt(downgradedLogger, LogLevel.Debug, Times.Never());
        }

        [Fact]
        public void Log_WithNullMessage_ForwardsUnchangedToInnerLogger()
        {
            var (inner, downgradedLogger, sut) = BuildSut();
            var entry = new LogEntry
            {
                EventLogLevel = EventLogLevel.Warning,
                Message = null!
            };

            sut.Log(entry);

            inner.Verify(i => i.Log(entry), Times.Once);
            VerifyLoggedAt(downgradedLogger, LogLevel.Debug, Times.Never());
        }

        [Theory]
        [InlineData(EventLogLevel.Error, true)]
        [InlineData(EventLogLevel.Warning, false)]
        public void IsEnabled_DelegatesToInnerLogger(EventLogLevel level, bool innerResult)
        {
            var (inner, _, sut) = BuildSut();
            inner.Setup(i => i.IsEnabled(level)).Returns(innerResult);

            var result = sut.IsEnabled(level);

            Assert.Equal(innerResult, result);
            inner.Verify(i => i.IsEnabled(level), Times.Once);
        }

        [Fact]
        public void Constructor_WithNullInner_Throws()
        {
            var loggerFactory = new Mock<ILoggerFactory>();

            Assert.Throws<ArgumentNullException>(() =>
                new IdentityModelLogLevelDowngrader(null!, loggerFactory.Object));
        }

        [Fact]
        public void Constructor_WithNullLoggerFactory_Throws()
        {
            var inner = new Mock<IIdentityLogger>();

            Assert.Throws<ArgumentNullException>(() =>
                new IdentityModelLogLevelDowngrader(inner.Object, null!));
        }
    }
}
