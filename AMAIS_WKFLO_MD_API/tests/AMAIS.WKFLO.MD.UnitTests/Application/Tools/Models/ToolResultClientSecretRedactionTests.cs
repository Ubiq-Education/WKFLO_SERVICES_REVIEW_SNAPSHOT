using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tools.Models
{
    /// <summary>
    /// US 7849: every result DTO that carries a tool's Config must strip the stored OAuth client secret and
    /// expose <c>hasClientSecret</c> instead — exercised through the real mapper, exactly as the handlers use it.
    /// </summary>
    public class ToolResultClientSecretRedactionTests
    {
        private const string StoredSecret = "enc:v1:c2VjcmV0LWNpcGhlcnRleHQ=";

        private static readonly IWkfloMapper Mapper = WkfloMapperExtensions.CreateMapper();

        private static string OAuthConfig(string clientSecret) => new JObject
        {
            ["transportType"] = "Http",
            ["endpoint"] = "https://api.canva.com/connect/v1/mcp",
            ["authType"] = "OAuth2",
            ["clientId"] = "canva-client",
            ["clientSecret"] = clientSecret
        }.ToString(Newtonsoft.Json.Formatting.None);

        private static T Tool<T>(string config) where T : Tool, new() =>
            new() { Id = 1, Name = "Canva", Description = "d", Type = CommonToolTypeConsts.MCP, Config = config };

        // (source entity, mapping to the DTO's Config) for each of the 10 Config-carrying result DTOs.
        public static IEnumerable<object[]> Mappings()
        {
            yield return Case<TenantTool, TenantToolGetResult>(r => r.Config);
            yield return Case<TenantTool, TenantToolCreateResult>(r => r.Config);
            yield return Case<TenantTool, TenantToolUpdateResult>(r => r.Config);
            yield return Case<PublishedTenantTool, TenantToolPublishResult>(r => r.Config);
            yield return Case<MarketplaceTool, TenantToolShareResult>(r => r.Config);
            yield return Case<MarketplaceTool, MarketplaceToolGetResult>(r => r.Config);
            yield return Case<MarketplaceTool, MarketplaceToolCreateResult>(r => r.Config);
            yield return Case<MarketplaceTool, MarketplaceToolUpdateResult>(r => r.Config);
            yield return Case<TenantTool, TenantAgentToolGetResult>(r => r.Config);
            yield return Case<MarketplaceTool, MarketplaceAgentToolGetResult>(r => r.Config);
        }

        private static object[] Case<TSource, TResult>(Func<TResult, string> config) where TSource : Tool, new() =>
            [typeof(TResult).Name, (Func<string, string>)(stored => config(Mapper.Map<TSource, TResult>(Tool<TSource>(stored))))];

        [Theory]
        [MemberData(nameof(Mappings))]
        public void Map_McpOAuthConfigWithSecret_StripsSecretAndSetsHasClientSecret(string resultType, Func<string, string> mapConfig)
        {
            var source = Tool<TenantTool>(OAuthConfig(StoredSecret)).Config;

            var config = JObject.Parse(mapConfig(source));

            Assert.False(config.ContainsKey("clientSecret"), resultType);
            Assert.True(config.Value<bool>("hasClientSecret"), resultType);
            Assert.Equal("canva-client", config.Value<string>("clientId"));
        }

        [Theory]
        [MemberData(nameof(Mappings))]
        public void Map_McpOAuthConfigWithoutSecret_HasClientSecretFalse(string resultType, Func<string, string> mapConfig)
        {
            var config = JObject.Parse(mapConfig(OAuthConfig("")));

            Assert.False(config.ContainsKey("clientSecret"), resultType);
            Assert.False(config.Value<bool>("hasClientSecret"), resultType);
        }

        [Fact]
        public void Map_NonMcpTool_ConfigUnchanged()
        {
            var tool = new TenantTool { Id = 1, Name = "Http", Type = "HTTP", Config = "{\"url\":\"https://example.com\"}" };

            var result = Mapper.Map<TenantTool, TenantToolGetResult>(tool);

            Assert.Equal(tool.Config, result.Config);
        }

        [Fact]
        public void Map_NonMcpToolWithLeftOverClientSecret_NeverEchoesIt()
        {
            var tool = new TenantTool { Id = 1, Name = "Http", Type = "HTTP", Config = "{\"url\":\"https://example.com\",\"clientSecret\":\"left-over\"}" };

            var result = Mapper.Map<TenantTool, TenantToolGetResult>(tool);

            Assert.DoesNotContain("left-over", result.Config);
        }
    }
}
