using AMAIS.WKFLO.MD.Application.Agents.Common;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json;
using System;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Common
{
    [Collection(DefaultWebOriginsEnvironmentCollection.Name)]
    public class AgentSecurityConfigurationHelperTests : IDisposable
    {
        private const string EnvironmentVariableName = "DEFAULT_WEB_ORIGINS";
        private readonly string _originalValue;

        public AgentSecurityConfigurationHelperTests()
        {
            _originalValue = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(EnvironmentVariableName, _originalValue);
        }

        [Fact]
        public void ApplyDefaultJwtDomains_NoEnvironmentVariable_ReturnsUnchanged()
        {
            Environment.SetEnvironmentVariable(EnvironmentVariableName, null);
            var json = JsonConvert.SerializeObject(new AgentSecurityConfiguration
            {
                Type = "jwt-token-callback",
                Jwt = new JwtConfiguration { Domains = ["localhost:3001"] }
            });

            var result = AgentSecurityConfigurationHelper.ApplyDefaultJwtDomains(json);

            var deserialized = JsonConvert.DeserializeObject<AgentSecurityConfiguration>(result);
            Assert.Single(deserialized.Jwt.Domains);
            Assert.Equal("localhost:3001", deserialized.Jwt.Domains[0]);
        }

        [Fact]
        public void ApplyDefaultJwtDomains_JwtTokenCallbackType_MergesAndDedupesDefaultDomains()
        {
            Environment.SetEnvironmentVariable(EnvironmentVariableName, "localhost;localhost:3001;host.docker.internal");
            var json = JsonConvert.SerializeObject(new AgentSecurityConfiguration
            {
                Type = "jwt-token-callback",
                Jwt = new JwtConfiguration { Domains = ["localhost", "app.example.com"] }
            });

            var result = AgentSecurityConfigurationHelper.ApplyDefaultJwtDomains(json);

            var deserialized = JsonConvert.DeserializeObject<AgentSecurityConfiguration>(result);
            Assert.Equal(
                ["localhost", "app.example.com", "localhost:3001", "host.docker.internal"],
                deserialized.Jwt.Domains);
        }

        [Fact]
        public void ApplyDefaultJwtDomains_JwtTokenCallbackTypeCaseInsensitive_MergesDefaultDomains()
        {
            Environment.SetEnvironmentVariable(EnvironmentVariableName, "localhost:44332");
            var json = JsonConvert.SerializeObject(new AgentSecurityConfiguration
            {
                Type = "JWT-TOKEN-CALLBACK",
                Jwt = new JwtConfiguration { Domains = [] }
            });

            var result = AgentSecurityConfigurationHelper.ApplyDefaultJwtDomains(json);

            var deserialized = JsonConvert.DeserializeObject<AgentSecurityConfiguration>(result);
            Assert.Equal(["localhost:44332"], deserialized.Jwt.Domains);
        }

        [Fact]
        public void ApplyDefaultJwtDomains_DefaultOriginsHaveScheme_StripsSchemeToBareDomain()
        {
            Environment.SetEnvironmentVariable(EnvironmentVariableName, "https://localhost;https://localhost:32771");
            var json = JsonConvert.SerializeObject(new AgentSecurityConfiguration
            {
                Type = "jwt-token-callback",
                Jwt = new JwtConfiguration { Domains = [] }
            });

            var result = AgentSecurityConfigurationHelper.ApplyDefaultJwtDomains(json);

            var deserialized = JsonConvert.DeserializeObject<AgentSecurityConfiguration>(result);
            Assert.Equal(["localhost", "localhost:32771"], deserialized.Jwt.Domains);
        }

        [Fact]
        public void ApplyDefaultJwtDomains_MixedSchemeAndBareDefaultOrigins_NormalizesOnlySchemeEntries()
        {
            Environment.SetEnvironmentVariable(EnvironmentVariableName, "https://app.example.com;localhost:3001");
            var json = JsonConvert.SerializeObject(new AgentSecurityConfiguration
            {
                Type = "jwt-token-callback",
                Jwt = new JwtConfiguration { Domains = [] }
            });

            var result = AgentSecurityConfigurationHelper.ApplyDefaultJwtDomains(json);

            var deserialized = JsonConvert.DeserializeObject<AgentSecurityConfiguration>(result);
            Assert.Equal(["app.example.com", "localhost:3001"], deserialized.Jwt.Domains);
        }

        [Fact]
        public void ApplyDefaultJwtDomains_BlankEntryInDefaultOrigins_ExcludesEmptyDomainFromResult()
        {
            Environment.SetEnvironmentVariable(EnvironmentVariableName, "localhost;   ;app.example.com");
            var json = JsonConvert.SerializeObject(new AgentSecurityConfiguration
            {
                Type = "jwt-token-callback",
                Jwt = new JwtConfiguration { Domains = [] }
            });

            var result = AgentSecurityConfigurationHelper.ApplyDefaultJwtDomains(json);

            var deserialized = JsonConvert.DeserializeObject<AgentSecurityConfiguration>(result);
            Assert.Equal(["localhost", "app.example.com"], deserialized.Jwt.Domains);
        }

        [Fact]
        public void ApplyDefaultJwtDomains_ExistingDomainsContainEmptyString_ExcludesEmptyDomainFromResult()
        {
            Environment.SetEnvironmentVariable(EnvironmentVariableName, "localhost:3001");
            var json = JsonConvert.SerializeObject(new AgentSecurityConfiguration
            {
                Type = "jwt-token-callback",
                Jwt = new JwtConfiguration { Domains = ["", "app.example.com", "  "] }
            });

            var result = AgentSecurityConfigurationHelper.ApplyDefaultJwtDomains(json);

            var deserialized = JsonConvert.DeserializeObject<AgentSecurityConfiguration>(result);
            Assert.Equal(["app.example.com", "localhost:3001"], deserialized.Jwt.Domains);
        }

        [Fact]
        public void ApplyDefaultJwtDomains_MissingJwtSection_CreatesJwtSectionWithDefaults()
        {
            Environment.SetEnvironmentVariable(EnvironmentVariableName, "localhost:3001");
            var json = JsonConvert.SerializeObject(new AgentSecurityConfiguration { Type = "jwt-token-callback" });

            var result = AgentSecurityConfigurationHelper.ApplyDefaultJwtDomains(json);

            var deserialized = JsonConvert.DeserializeObject<AgentSecurityConfiguration>(result);
            Assert.Equal(["localhost:3001"], deserialized.Jwt.Domains);
        }

        [Theory]
        [InlineData("api-key")]
        [InlineData("azure-ad")]
        [InlineData("none")]
        [InlineData("unknown-type")]
        public void ApplyDefaultJwtDomains_NonJwtType_ReturnsUnchanged(string type)
        {
            Environment.SetEnvironmentVariable(EnvironmentVariableName, "localhost:3001");
            var json = JsonConvert.SerializeObject(new AgentSecurityConfiguration { Type = type });

            var result = AgentSecurityConfigurationHelper.ApplyDefaultJwtDomains(json);

            Assert.Equal(json, result);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void ApplyDefaultJwtDomains_NullOrWhitespaceInput_ReturnsUnchanged(string input)
        {
            Environment.SetEnvironmentVariable(EnvironmentVariableName, "localhost:3001");

            var result = AgentSecurityConfigurationHelper.ApplyDefaultJwtDomains(input);

            Assert.Equal(input, result);
        }

        [Fact]
        public void ApplyDefaultJwtDomains_MalformedJson_ReturnsUnchangedWithoutThrowing()
        {
            Environment.SetEnvironmentVariable(EnvironmentVariableName, "localhost:3001");
            const string malformedJson = "{ this is not valid json";

            var result = AgentSecurityConfigurationHelper.ApplyDefaultJwtDomains(malformedJson);

            Assert.Equal(malformedJson, result);
        }

        [Fact]
        public void ApplyDefaultJwtDomains_MalformedJson_LogsWarning()
        {
            Environment.SetEnvironmentVariable(EnvironmentVariableName, "localhost:3001");
            const string malformedJson = "{ this is not valid json";
            var loggerMock = new Mock<ILogger>();

            AgentSecurityConfigurationHelper.ApplyDefaultJwtDomains(malformedJson, loggerMock.Object);

            loggerMock.Verify(
                logger => logger.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<JsonException>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        [Fact]
        public void ApplyDefaultJwtDomains_JsonDeserializesToNull_ReturnsUnchanged()
        {
            Environment.SetEnvironmentVariable(EnvironmentVariableName, "localhost:3001");
            const string nullJson = "null";

            var result = AgentSecurityConfigurationHelper.ApplyDefaultJwtDomains(nullJson);

            Assert.Equal(nullJson, result);
        }
    }
}
