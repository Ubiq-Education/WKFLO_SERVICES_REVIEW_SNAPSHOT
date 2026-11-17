using AMAIS.WKFLO.Common.MD.Domain.Security;
using Microsoft.Extensions.Configuration;
using Moq;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Security.Cryptography;

namespace AMAIS.WKFLO.MD.UnitTests.Common.MD.Domain.Security
{
    public class McpClientSecretHelperTests
    {
        private const string Mcp = "MCP";
        private const string Endpoint = "https://mcp.example.com/mcp";

        private static IMcpTokenProtector NewProtector(byte keyFill = 0)
        {
            var key = new byte[32];
            Array.Fill(key, keyFill);
            var configuration = new Mock<IConfiguration>();
            configuration.Setup(x => x[AesMcpTokenProtector.ConfigurationKey]).Returns(Convert.ToBase64String(key));
            return new AesMcpTokenProtector(configuration.Object);
        }

        private static string OAuthConfig(string clientId = "client-1", string clientSecret = null, string endpoint = Endpoint, string extra = null)
        {
            var config = new JObject
            {
                ["id"] = "server-1",
                ["name"] = "Server",
                ["transportType"] = "Http",
                ["endpoint"] = endpoint,
                ["authType"] = "OAuth2",
                ["clientId"] = clientId
            };
            if (clientSecret != null)
                config["clientSecret"] = clientSecret;
            if (extra != null)
                config.Merge(JObject.Parse(extra));
            return config.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static JObject Parse(string json) => JObject.Parse(json);

        // --- Protect / Resolve ---------------------------------------------------------------------------

        [Fact]
        public void Protect_ThenResolve_RoundTrips()
        {
            var protector = NewProtector();

            var stored = McpClientSecretHelper.Protect(protector, "s3cret");

            Assert.StartsWith(McpClientSecretHelper.EncryptedPrefix, stored);
            Assert.DoesNotContain("s3cret", stored);
            Assert.Equal("s3cret", McpClientSecretHelper.Resolve(protector, stored));
        }

        [Fact]
        public void Resolve_LegacyPlaintext_PassesThrough()
        {
            Assert.Equal("legacy-plain", McpClientSecretHelper.Resolve(NewProtector(), "legacy-plain"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void ProtectAndResolve_NullOrEmpty_ReturnedUnchanged(string value)
        {
            var protector = NewProtector();

            Assert.Equal(value, McpClientSecretHelper.Protect(protector, value));
            Assert.Equal(value, McpClientSecretHelper.Resolve(protector, value));
        }

        [Fact]
        public void Resolve_EncryptedUnderDifferentKey_Throws()
        {
            var stored = McpClientSecretHelper.Protect(NewProtector(1), "s3cret");

            Assert.ThrowsAny<CryptographicException>(() => McpClientSecretHelper.Resolve(NewProtector(2), stored));
        }

        // --- SanitizeForResponse -------------------------------------------------------------------------

        [Fact]
        public void SanitizeForResponse_OAuth2WithSecret_StripsSecretAndFlagsIt()
        {
            var stored = OAuthConfig(clientSecret: McpClientSecretHelper.Protect(NewProtector(), "s3cret"));

            var result = Parse(McpClientSecretHelper.SanitizeForResponse(Mcp, stored));

            Assert.False(result.ContainsKey("clientSecret"));
            Assert.True(result.Value<bool>("hasClientSecret"));
            Assert.Equal("client-1", result.Value<string>("clientId"));
        }

        [Fact]
        public void SanitizeForResponse_LegacyPlaintextSecret_IsAlsoStripped()
        {
            var result = McpClientSecretHelper.SanitizeForResponse(Mcp, OAuthConfig(clientSecret: "legacy-plain"));

            Assert.DoesNotContain("legacy-plain", result);
            Assert.True(Parse(result).Value<bool>("hasClientSecret"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void SanitizeForResponse_OAuth2WithoutSecret_HasClientSecretFalse(string secret)
        {
            var result = Parse(McpClientSecretHelper.SanitizeForResponse(Mcp, OAuthConfig(clientSecret: secret)));

            Assert.False(result.ContainsKey("clientSecret"));
            Assert.False(result.Value<bool>("hasClientSecret"));
        }

        [Fact]
        public void SanitizeForResponse_NonOAuth2McpWithoutSecret_ReturnedVerbatim()
        {
            const string config = "{ \"transportType\": \"Stdio\", \"command\": \"npx\" }";

            Assert.Same(config, McpClientSecretHelper.SanitizeForResponse(Mcp, config));
        }

        [Fact]
        public void SanitizeForResponse_NonOAuth2McpWithStraySecret_StillStripped()
        {
            var result = McpClientSecretHelper.SanitizeForResponse(Mcp, "{\"authType\":\"None\",\"clientSecret\":\"stray\"}");

            Assert.DoesNotContain("stray", result);
        }

        [Theory]
        [InlineData("Knowledge", "{\"url\":\"https://example.com\"}")]
        [InlineData("MCP", "not json")]
        [InlineData("MCP", null)]
        public void SanitizeForResponse_NothingToStrip_ReturnedVerbatim(string type, string config)
        {
            Assert.Same(config, McpClientSecretHelper.SanitizeForResponse(type, config));
        }

        [Fact]
        public void SanitizeForResponse_NonMcpConfigWithSecret_StripsIt()
        {
            var result = McpClientSecretHelper.SanitizeForResponse("HTTP", "{\"url\":\"https://example.com\",\"ClientSecret\":\"left-over\",\"hasClientSecret\":true}");

            Assert.DoesNotContain("left-over", result);
            Assert.False(Parse(result).ContainsKey("hasClientSecret"));
            Assert.Equal("https://example.com", Parse(result).Value<string>("url"));
        }

        [Theory]
        [InlineData("MCP")]
        [InlineData("HTTP")]
        public void SanitizeForResponse_UnparseableConfigWithSecret_SecretMasked(string type)
        {
            var result = McpClientSecretHelper.SanitizeForResponse(type, "{ \"clientSecret\": \"pl41n\", broken");

            Assert.DoesNotContain("pl41n", result);
        }

        [Fact]
        public void SanitizeForResponse_LegacyIntegerAuthType_TreatedAsOAuth2()
        {
            var result = Parse(McpClientSecretHelper.SanitizeForResponse(Mcp, "{\"authType\":1,\"clientId\":\"c\"}"));

            Assert.False(result.Value<bool>("hasClientSecret"));
        }

        // --- RedactSecrets -------------------------------------------------------------------------------

        [Fact]
        public void RedactSecrets_TopLevelProperty_IsMasked()
        {
            var result = McpClientSecretHelper.RedactSecrets("{\"clientId\":\"c\",\"clientSecret\":\"s3cret\"}");

            Assert.DoesNotContain("s3cret", result);
            Assert.Equal("***", JObject.Parse(result).Value<string>("clientSecret"));
            Assert.Equal("c", JObject.Parse(result).Value<string>("clientId"));
        }

        [Fact]
        public void RedactSecrets_SecretInsideJsonStringProperty_IsMasked()
        {
            // Shape the logging behaviour produces: a command whose Config is itself a JSON string.
            var serialized = Newtonsoft.Json.JsonConvert.SerializeObject(new { Name = "Canva", Config = OAuthConfig(clientSecret: "s3cret") });

            var result = McpClientSecretHelper.RedactSecrets(serialized);

            Assert.DoesNotContain("s3cret", result);
            var config = JObject.Parse(JObject.Parse(result).Value<string>("Config"));
            Assert.Equal("***", config.Value<string>("clientSecret"));
            Assert.Equal("client-1", config.Value<string>("clientId"));
        }

        [Fact]
        public void RedactSecrets_SecretWithEscapedQuotes_IsFullyMasked()
        {
            var serialized = Newtonsoft.Json.JsonConvert.SerializeObject(new { Config = OAuthConfig(clientSecret: "ab\"cd\ef") });

            var result = McpClientSecretHelper.RedactSecrets(serialized);

            Assert.DoesNotContain("ab", JObject.Parse(result).Value<string>("Config"));
            Assert.DoesNotContain("ef", JObject.Parse(result).Value<string>("Config"));
        }

        [Theory]
        [InlineData("not json \"clientSecret\": \"s3cret\" tail")]
        [InlineData("not json {\\\"clientSecret\\\": \\\"s3cret\\\"} tail")]
        [InlineData("{\"Config\":\"{\\\"clientSecret\\\": \\\"s3cret\\\", broken\"}")]
        public void RedactSecrets_NonJsonText_MaskedByPattern(string text)
        {
            var result = McpClientSecretHelper.RedactSecrets(text);

            Assert.DoesNotContain("s3cret", result);
            Assert.Contains("***", result);
        }

        [Fact]
        public void RedactSecrets_CaseInsensitivePropertyName_IsMasked()
        {
            Assert.DoesNotContain("s3cret", McpClientSecretHelper.RedactSecrets("{\"ClientSecret\":\"s3cret\"}"));
        }

        [Fact]
        public void RedactSecrets_NoMention_ReturnsSameInstance()
        {
            const string text = "{\"Config\":\"{\\\"url\\\":\\\"x\\\"}\"}";

            Assert.Same(text, McpClientSecretHelper.RedactSecrets(text));
        }

        [Fact]
        public void RedactSecrets_EmptySecret_LeftEmpty()
        {
            Assert.Equal("", JObject.Parse(McpClientSecretHelper.RedactSecrets("{\"clientSecret\":\"\"}")).Value<string>("clientSecret"));
        }

        // --- Review fixes: echoed stored value (B1), case variants (B2), non-OAuth2 (A2) -------------------

        [Fact]
        public void MergeOnWrite_IncomingStoredFormSecret_KeepsStoredValueWithoutReEncrypting()
        {
            // The PUT endpoint pre-fills omitted fields from the entity, so an update without `config` echoes the
            // stored "enc:v1:..." value back. It must not be treated as a new plaintext secret.
            var protector = NewProtector();
            var storedSecret = McpClientSecretHelper.Protect(protector, "s3cret");
            var stored = OAuthConfig(clientSecret: storedSecret);

            var result = McpClientSecretHelper.MergeOnWrite(protector, Mcp, stored, stored);

            Assert.Equal(storedSecret, Parse(result.Config).Value<string>("clientSecret"));
            Assert.Equal("s3cret", McpClientSecretHelper.Resolve(protector, Parse(result.Config).Value<string>("clientSecret")));
            Assert.False(result.CredentialsChanged);
        }

        [Fact]
        public void MergeOnWrite_IncomingStoredFormSecretNotMatchingStored_IsNotAccepted()
        {
            var protector = NewProtector();
            var foreign = McpClientSecretHelper.Protect(protector, "someone-elses");

            var result = McpClientSecretHelper.MergeOnWrite(protector, Mcp, OAuthConfig(clientSecret: foreign), OAuthConfig());

            Assert.True(string.IsNullOrEmpty(Parse(result.Config).Value<string>("clientSecret")));
            Assert.False(result.CredentialsChanged);
        }

        [Fact]
        public void MergeOnWrite_CaseVariantEndpointDuplicate_EffectiveValueDetectedAsChange_AndNormalised()
        {
            // Runtime deserialization is case-insensitive and the last occurrence wins, so this points the tool
            // at the second endpoint even though "endpoint" itself is unchanged.
            var protector = NewProtector();
            var previous = OAuthConfig(clientSecret: McpClientSecretHelper.Protect(protector, "s3cret"));
            var incoming = OAuthConfig(extra: "{\"Endpoint\":\"https://attacker.example.com/mcp\"}");

            var result = McpClientSecretHelper.MergeOnWrite(protector, Mcp, incoming, previous);

            var config = Parse(result.Config);
            Assert.True(string.IsNullOrEmpty(config.Value<string>("clientSecret")));
            Assert.True(result.CredentialsChanged);
            Assert.Equal("https://attacker.example.com/mcp", config.Value<string>("endpoint"));
            Assert.Single(config.Properties(), p => string.Equals(p.Name, "endpoint", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void MergeOnWrite_CaseVariantClientIdDuplicate_DetectedAsChange()
        {
            var protector = NewProtector();
            var previous = OAuthConfig(clientSecret: McpClientSecretHelper.Protect(protector, "s3cret"));
            var incoming = OAuthConfig(extra: "{\"ClientId\":\"other-client\"}");

            var result = McpClientSecretHelper.MergeOnWrite(protector, Mcp, incoming, previous);

            var config = Parse(result.Config);
            Assert.True(result.CredentialsChanged);
            Assert.True(string.IsNullOrEmpty(config.Value<string>("clientSecret")));
            Assert.Equal("other-client", config.Value<string>("clientId"));
            Assert.Single(config.Properties(), p => string.Equals(p.Name, "clientId", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void MergeOnWrite_PascalCaseClientSecret_IsEncryptedUnderCanonicalKey()
        {
            var protector = NewProtector();

            var result = McpClientSecretHelper.MergeOnWrite(protector, Mcp, OAuthConfig(extra: "{\"ClientSecret\":\"s3cret\"}"), null);

            Assert.DoesNotContain("s3cret", result.Config);
            var config = Parse(result.Config);
            Assert.Single(config.Properties(), p => string.Equals(p.Name, "clientSecret", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("s3cret", McpClientSecretHelper.Resolve(protector, config.Value<string>("clientSecret")));
        }

        [Fact]
        public void MergeOnWrite_PlaintextHiddenBehindLaterEmptyVariant_IsNotPersisted()
        {
            // Runtime sees "" (last wins); the earlier plaintext must not survive in the stored JSON either.
            var result = McpClientSecretHelper.MergeOnWrite(NewProtector(), Mcp, OAuthConfig(clientSecret: "hidden", extra: "{\"ClientSecret\":\"\"}"), null);

            Assert.DoesNotContain("hidden", result.Config);
        }

        [Fact]
        public void SanitizeForResponse_CaseVariantSecret_Stripped()
        {
            var result = McpClientSecretHelper.SanitizeForResponse(Mcp, OAuthConfig(extra: "{\"ClientSecret\":\"s3cret\"}"));

            Assert.DoesNotContain("s3cret", result);
            Assert.True(Parse(result).Value<bool>("hasClientSecret"));
        }

        [Fact]
        public void SanitizeForResponse_NonOAuth2WithPascalCaseSecret_Stripped()
        {
            var result = McpClientSecretHelper.SanitizeForResponse(Mcp, "{\"authType\":\"None\",\"ClientSecret\":\"stray\"}");

            Assert.DoesNotContain("stray", result);
        }

        [Fact]
        public void MergeOnWrite_NonOAuth2_DropsStoredAndIncomingSecret()
        {
            var protector = NewProtector();
            var previous = OAuthConfig(clientSecret: McpClientSecretHelper.Protect(protector, "s3cret"));
            var incoming = "{\"transportType\":\"Http\",\"endpoint\":\"" + Endpoint + "\",\"authType\":\"None\",\"clientId\":\"client-1\",\"clientSecret\":\"new\"}";

            var result = McpClientSecretHelper.MergeOnWrite(protector, Mcp, incoming, previous);

            Assert.False(Parse(result.Config).ContainsKey("clientSecret"));
            Assert.True(result.CredentialsChanged);
            Assert.False(result.IsClientSecretWithoutClientId);
        }

        [Fact]
        public void MergeOnWrite_NonOAuth2SecretWithoutClientId_DroppedNotRejected()
        {
            var result = McpClientSecretHelper.MergeOnWrite(NewProtector(), Mcp, "{\"authType\":\"None\",\"clientSecret\":\"x\"}", null);

            Assert.False(result.IsClientSecretWithoutClientId);
            Assert.DoesNotContain("\"x\"", result.Config);
        }

        // --- MergeOnWrite --------------------------------------------------------------------------------

        [Fact]
        public void MergeOnWrite_Create_NewSecret_IsEncrypted()
        {
            var protector = NewProtector();

            var result = McpClientSecretHelper.MergeOnWrite(protector, Mcp, OAuthConfig(clientSecret: "s3cret"), null);

            var stored = Parse(result.Config).Value<string>("clientSecret");
            Assert.StartsWith(McpClientSecretHelper.EncryptedPrefix, stored);
            Assert.Equal("s3cret", McpClientSecretHelper.Resolve(protector, stored));
            Assert.False(result.IsClientSecretWithoutClientId);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void MergeOnWrite_Update_BlankSecret_KeepsStoredSecret(string incoming)
        {
            var protector = NewProtector();
            var storedSecret = McpClientSecretHelper.Protect(protector, "s3cret");
            var previous = OAuthConfig(clientSecret: storedSecret);

            var result = McpClientSecretHelper.MergeOnWrite(protector, Mcp, OAuthConfig(clientSecret: incoming), previous);

            Assert.Equal(storedSecret, Parse(result.Config).Value<string>("clientSecret"));
            Assert.False(result.CredentialsChanged);
        }

        [Fact]
        public void MergeOnWrite_Update_LegacyPlaintextKept_IsReEncrypted()
        {
            var protector = NewProtector();

            var result = McpClientSecretHelper.MergeOnWrite(protector, Mcp, OAuthConfig(), OAuthConfig(clientSecret: "legacy-plain"));

            var stored = Parse(result.Config).Value<string>("clientSecret");
            Assert.True(McpClientSecretHelper.IsProtected(stored));
            Assert.Equal("legacy-plain", McpClientSecretHelper.Resolve(protector, stored));
            Assert.False(result.CredentialsChanged);
        }

        [Fact]
        public void MergeOnWrite_Update_NewSecret_ReplacesAndFlagsCredentialChange()
        {
            var protector = NewProtector();
            var previous = OAuthConfig(clientSecret: McpClientSecretHelper.Protect(protector, "old"));

            var result = McpClientSecretHelper.MergeOnWrite(protector, Mcp, OAuthConfig(clientSecret: "new"), previous);

            Assert.Equal("new", McpClientSecretHelper.Resolve(protector, Parse(result.Config).Value<string>("clientSecret")));
            Assert.True(result.CredentialsChanged);
        }

        [Fact]
        public void MergeOnWrite_Update_SameSecretRetyped_IsNotACredentialChange()
        {
            var protector = NewProtector();
            var previous = OAuthConfig(clientSecret: McpClientSecretHelper.Protect(protector, "same"));

            var result = McpClientSecretHelper.MergeOnWrite(protector, Mcp, OAuthConfig(clientSecret: "same"), previous);

            Assert.False(result.CredentialsChanged);
        }

        [Fact]
        public void MergeOnWrite_ClearFlag_RemovesStoredSecretAndFlag()
        {
            var protector = NewProtector();
            var previous = OAuthConfig(clientSecret: McpClientSecretHelper.Protect(protector, "s3cret"));

            var result = McpClientSecretHelper.MergeOnWrite(protector, Mcp, OAuthConfig(extra: "{\"clearClientSecret\":true}"), previous);

            var config = Parse(result.Config);
            Assert.False(config.ContainsKey("clientSecret"));
            Assert.False(config.ContainsKey("clearClientSecret"));
            Assert.True(result.CredentialsChanged);
        }

        [Fact]
        public void MergeOnWrite_ClearFlagWithoutStoredSecret_IsNotACredentialChange()
        {
            var result = McpClientSecretHelper.MergeOnWrite(NewProtector(), Mcp, OAuthConfig(extra: "{\"clearClientSecret\":true}"), OAuthConfig());

            Assert.False(Parse(result.Config).ContainsKey("clearClientSecret"));
            Assert.False(result.CredentialsChanged);
        }

        [Fact]
        public void MergeOnWrite_NewSecretAndClearFlag_NewSecretWins()
        {
            var protector = NewProtector();
            var previous = OAuthConfig(clientSecret: McpClientSecretHelper.Protect(protector, "old"));

            var result = McpClientSecretHelper.MergeOnWrite(protector, Mcp, OAuthConfig(clientSecret: "new", extra: "{\"clearClientSecret\":true}"), previous);

            var config = Parse(result.Config);
            Assert.Equal("new", McpClientSecretHelper.Resolve(protector, config.Value<string>("clientSecret")));
            Assert.False(config.ContainsKey("clearClientSecret"));
        }

        [Fact]
        public void MergeOnWrite_HasClientSecretOnInput_IsStripped()
        {
            var result = McpClientSecretHelper.MergeOnWrite(NewProtector(), Mcp, OAuthConfig(extra: "{\"hasClientSecret\":true}"), null);

            Assert.False(Parse(result.Config).ContainsKey("hasClientSecret"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("  ")]
        [InlineData(null)]
        public void MergeOnWrite_SecretWithoutClientId_IsRejected(string clientId)
        {
            var result = McpClientSecretHelper.MergeOnWrite(NewProtector(), Mcp, OAuthConfig(clientId: clientId, clientSecret: "s3cret"), null);

            Assert.True(result.IsClientSecretWithoutClientId);
        }

        [Fact]
        public void MergeOnWrite_EndpointChangedWithoutNewSecret_DropsStoredSecret()
        {
            var protector = NewProtector();
            var previous = OAuthConfig(clientSecret: McpClientSecretHelper.Protect(protector, "s3cret"));

            var result = McpClientSecretHelper.MergeOnWrite(protector, Mcp, OAuthConfig(endpoint: "https://attacker.example.com/mcp"), previous);

            Assert.False(Parse(result.Config).ContainsKey("clientSecret"));
            Assert.True(result.CredentialsChanged);
        }

        [Fact]
        public void MergeOnWrite_ClientIdChangedWithoutNewSecret_DropsStoredSecret()
        {
            var protector = NewProtector();
            var previous = OAuthConfig(clientSecret: McpClientSecretHelper.Protect(protector, "s3cret"));

            var result = McpClientSecretHelper.MergeOnWrite(protector, Mcp, OAuthConfig(clientId: "client-2"), previous);

            Assert.False(Parse(result.Config).ContainsKey("clientSecret"));
            Assert.True(result.CredentialsChanged);
        }

        [Fact]
        public void MergeOnWrite_ClientIdChangedWithNewSecret_StoresNewSecret()
        {
            var protector = NewProtector();
            var previous = OAuthConfig(clientSecret: McpClientSecretHelper.Protect(protector, "old"));

            var result = McpClientSecretHelper.MergeOnWrite(protector, Mcp, OAuthConfig(clientId: "client-2", clientSecret: "new"), previous);

            Assert.Equal("new", McpClientSecretHelper.Resolve(protector, Parse(result.Config).Value<string>("clientSecret")));
            Assert.True(result.CredentialsChanged);
        }

        [Fact]
        public void MergeOnWrite_ClientIdChangedWithNoSecretEverStored_IsStillACredentialChange()
        {
            var result = McpClientSecretHelper.MergeOnWrite(NewProtector(), Mcp, OAuthConfig(clientId: "client-2"), OAuthConfig());

            Assert.True(result.CredentialsChanged);
        }

        [Fact]
        public void MergeOnWrite_ClientIdWhitespaceOnlyDifference_IsNotAChange()
        {
            var protector = NewProtector();
            var storedSecret = McpClientSecretHelper.Protect(protector, "s3cret");

            var result = McpClientSecretHelper.MergeOnWrite(protector, Mcp, OAuthConfig(clientId: " client-1 "), OAuthConfig(clientSecret: storedSecret));

            Assert.Equal(storedSecret, Parse(result.Config).Value<string>("clientSecret"));
            Assert.False(result.CredentialsChanged);
        }

        [Fact]
        public void MergeOnWrite_UnrelatedFieldChange_KeepsSecretAndNoCredentialChange()
        {
            var protector = NewProtector();
            var storedSecret = McpClientSecretHelper.Protect(protector, "s3cret");

            var result = McpClientSecretHelper.MergeOnWrite(protector, Mcp, OAuthConfig(extra: "{\"name\":\"Renamed\",\"scopes\":[\"a\",\"b\"]}"), OAuthConfig(clientSecret: storedSecret));

            var config = Parse(result.Config);
            Assert.Equal(storedSecret, config.Value<string>("clientSecret"));
            Assert.Equal("Renamed", config.Value<string>("name"));
            Assert.False(result.CredentialsChanged);
        }

        [Fact]
        public void MergeOnWrite_UndecryptablePreviousSecret_NewSecretCountsAsChange()
        {
            var previous = OAuthConfig(clientSecret: McpClientSecretHelper.Protect(NewProtector(1), "s3cret"));

            var result = McpClientSecretHelper.MergeOnWrite(NewProtector(2), Mcp, OAuthConfig(clientSecret: "s3cret"), previous);

            Assert.True(result.CredentialsChanged);
        }

        [Fact]
        public void MergeOnWrite_NothingToChange_ReturnsInputVerbatim()
        {
            const string config = "{ \"transportType\": \"Stdio\", \"command\": \"npx\" }";

            var result = McpClientSecretHelper.MergeOnWrite(NewProtector(), Mcp, config, config);

            Assert.Same(config, result.Config);
            Assert.False(result.CredentialsChanged);
        }

        [Fact]
        public void MergeOnWrite_NonMcp_ReturnsInputVerbatim()
        {
            const string config = "{\"clientSecret\":\"x\"}";

            var result = McpClientSecretHelper.MergeOnWrite(NewProtector(), "Knowledge", config, null);

            Assert.Same(config, result.Config);
            Assert.False(result.CredentialsChanged);
            Assert.False(result.IsClientSecretWithoutClientId);
            Assert.False(result.IsInvalidConfig);
        }

        [Theory]
        [InlineData("not json \"clientSecret\": \"pl41n\"")]
        [InlineData("[1,2]")]
        [InlineData("")]
        [InlineData(null)]
        public void MergeOnWrite_McpConfigNotAJsonObject_IsInvalid(string config)
        {
            Assert.True(McpClientSecretHelper.MergeOnWrite(NewProtector(), Mcp, config, null).IsInvalidConfig);
        }

        [Fact]
        public void MergeOnWrite_EndpointChanged_PublicClientWithoutSecret_IsCredentialChange()
        {
            // Tokens issued by the old server must never be sent to the new one.
            var result = McpClientSecretHelper.MergeOnWrite(NewProtector(), Mcp, OAuthConfig(endpoint: "https://other.example.com/mcp"), OAuthConfig());

            Assert.True(result.CredentialsChanged);
        }

        [Fact]
        public void MergeOnWrite_EndpointChanged_SameSecretReEntered_IsCredentialChange()
        {
            var protector = NewProtector();
            var previous = OAuthConfig(clientSecret: McpClientSecretHelper.Protect(protector, "same"));

            var result = McpClientSecretHelper.MergeOnWrite(protector, Mcp, OAuthConfig(clientSecret: "same", endpoint: "https://other.example.com/mcp"), previous);

            Assert.True(result.CredentialsChanged);
        }

        [Fact]
        public void StripCredentials_RemovesAllCasingsOfCredentialFields()
        {
            var result = Parse(McpClientSecretHelper.StripCredentials(
                "{\"url\":\"u\",\"clientId\":\"c\",\"ClientSecret\":\"s\",\"hasClientSecret\":true,\"CLEARCLIENTSECRET\":true}"));

            Assert.Equal(["url"], result.Properties().Select(p => p.Name));
        }

        [Fact]
        public void StripCredentials_UnparseableConfig_SecretMasked()
        {
            Assert.DoesNotContain("pl41n", McpClientSecretHelper.StripCredentials("{ \"clientSecret\": \"pl41n\", broken"));
        }

        [Fact]
        public void StripCredentials_NothingToStrip_ReturnedVerbatim()
        {
            const string config = "{\"url\":\"u\"}";

            Assert.Same(config, McpClientSecretHelper.StripCredentials(config));
        }
    }
}
