using System.Collections.Generic;
using AMAIS.WKFLO.Common.Redis;

namespace AMAIS.WKFLO.MD.UnitTests.Infrastructure.Caching
{
    public class RedisSettingsTests
    {
        private static RedisSettings From(Dictionary<string, string> variables) =>
            RedisSettings.From(name => variables.GetValueOrDefault(name));

        [Fact]
        public void NothingSet_IsNotConfigured() => Assert.False(From([]).IsConfigured);

        [Fact]
        public void BlankValues_AreTreatedAsMissing() =>
            Assert.False(From(new() { ["AZURE_REDIS_HOSTNAME"] = " ", ["REDIS_CONNECTION_STRING"] = "" }).IsConfigured);

        [Fact]
        public void HostWithPort_IsUsedAsGiven()
        {
            var settings = From(new() { ["AZURE_REDIS_HOSTNAME"] = "cache.example.net:10001", ["AZURE_REDIS_CLIENT_ID"] = "client" });

            Assert.Equal("cache.example.net:10001", settings.AzureEndpoint);
            Assert.Equal("client", settings.AzureClientId);
        }

        [Fact]
        public void HostWithoutPort_GetsTheAzureManagedRedisDefaultPort() =>
            Assert.Equal("cache.example.net:10000", From(new() { ["AZURE_REDIS_HOSTNAME"] = "cache.example.net" }).AzureEndpoint);

        [Fact]
        public void IgnoresTheSharedIdentityVariables()
        {
            var settings = From(new() { ["AZURE_CLIENT_ID"] = "sql", ["MI_CLIENT_ID"] = "other" });

            Assert.Null(settings.AzureClientId);
            Assert.False(settings.IsConfigured);
        }
    }
}
