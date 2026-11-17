using AMAIS.WKFLO.Common.AI.Application.Plugins.HttpCall;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Flurl.Http.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Plugins.HttpCall
{
    public class HttpCallPluginTests : IDisposable
    {
        private readonly Mock<IChatNotificationService> _notificationServiceMock;
        private readonly ChatContext _chatContext;
        private readonly HttpTest _httpTest;
        private readonly ILogger _logger = NullLogger.Instance;

        private const string SessionId = "session-abc";
        private const string ToolName = "OnPageSEO";

        public HttpCallPluginTests()
        {
            _notificationServiceMock = new Mock<IChatNotificationService>();
            _notificationServiceMock
                .Setup(x => x.SendToolExecutionUpdate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);

            _chatContext = new ChatContext { SessionId = SessionId };

            // Intercepts all Flurl HTTP calls for the duration of this test instance
            _httpTest = new HttpTest();
        }

        public void Dispose() => _httpTest.Dispose();

        // ── GET ─────────────────────────────────────────────────────────────

        [Fact]
        public async Task ExecuteHttpCallAsync_GetRequest_ReturnsResponseBody()
        {
            // Arrange
            _httpTest.RespondWith("{\"status\":\"ok\"}", 200);

            var tool = BuildTool(new
            {
                method = "GET",
                url = "https://api.example.com/data",
                headers = new { },
                parameters = new { },
                urlParams = new { },
                body = "",
                parameterConfig = new { },
                inputs = new { }
            });

            var plugin = new HttpCallPlugin(tool, _notificationServiceMock.Object, _chatContext, _logger);

            // Act
            var result = await plugin.ExecuteHttpCallAsync();

            // Assert
            Assert.Equal("status: ok", result);
            _httpTest.ShouldHaveCalled("https://api.example.com/data")
                     .WithVerb(HttpMethod.Get)
                     .Times(1);
        }

        [Fact]
        public async Task ExecuteHttpCallAsync_GetWithUrlParams_AppendsQueryString()
        {
            // Arrange
            _httpTest.RespondWith("{\"results\":[{\"title\":\"Page A\"}]}", 200);

            var tool = BuildTool(new
            {
                method = "GET",
                url = "https://api.example.com/search",
                headers = new { },
                parameters = new { },
                urlParams = new { q = "test", page = "1" },
                body = "",
                parameterConfig = new { },
                inputs = new { }
            });

            var plugin = new HttpCallPlugin(tool, _notificationServiceMock.Object, _chatContext, _logger);

            // Act
            var result = await plugin.ExecuteHttpCallAsync();

            // Assert — array item becomes a section header; child keys are short
            Assert.Equal("- results[0]\ntitle: Page A", result);
            _httpTest.ShouldHaveCalled("https://api.example.com/search*")
                     .WithQueryParam("q", "test")
                     .WithQueryParam("page", "1")
                     .Times(1);
        }

        // ── POST ────────────────────────────────────────────────────────────

        [Fact]
        public async Task ExecuteHttpCallAsync_PostWithParameters_SendsJsonBody()
        {
            // Arrange
            _httpTest.RespondWith("{\"id\":1}", 200);

            var tool = BuildTool(new
            {
                method = "POST",
                url = "https://api.dataforseo.com/v3/on_page/instant_pages",
                headers = new { Authorization = "Basic abc123" },
                parameters = new { url = "https://www.charterhouse.org.uk", max_crawl_pages = "1" },
                urlParams = new { },
                body = "",
                parameterConfig = new { },
                inputs = new { }
            });

            var plugin = new HttpCallPlugin(tool, _notificationServiceMock.Object, _chatContext, _logger);

            // Act
            var result = await plugin.ExecuteHttpCallAsync();

            // Assert
            Assert.Equal("id: 1", result);
            _httpTest.ShouldHaveCalled("https://api.dataforseo.com/v3/on_page/instant_pages")
                     .WithVerb(HttpMethod.Post)
                     .WithRequestJson(new Dictionary<string, string>
                     {
                         { "url", "https://www.charterhouse.org.uk" },
                         { "max_crawl_pages", "1" }
                     })
                     .Times(1);
        }

        [Fact]
        public async Task ExecuteHttpCallAsync_PostWithRawBody_SendsRawString()
        {
            // Arrange
            _httpTest.RespondWith("accepted", 202);

            var tool = BuildTool(new
            {
                method = "POST",
                url = "https://api.example.com/webhook",
                headers = new { },
                parameters = new { },
                urlParams = new { },
                body = "<xml><event>test</event></xml>",
                parameterConfig = new { },
                inputs = new { }
            });

            var plugin = new HttpCallPlugin(tool, _notificationServiceMock.Object, _chatContext, _logger);

            // Act
            var result = await plugin.ExecuteHttpCallAsync();

            // Assert
            Assert.Equal("accepted", result);
            _httpTest.ShouldHaveCalled("https://api.example.com/webhook")
                     .WithVerb(HttpMethod.Post)
                     .WithRequestBody("<xml><event>test</event></xml>")
                     .Times(1);
        }

        // ── Dynamic inputs ───────────────────────────────────────────────────

        [Fact]
        public async Task ExecuteHttpCallAsync_DynamicInputsOverrideStaticParameters()
        {
            // Arrange
            _httpTest.RespondWith("{\"ok\":true}", 200);

            var tool = BuildTool(new
            {
                method = "POST",
                url = "https://api.example.com/crawl",
                headers = new { },
                parameters = new { url = "https://default.com", max_crawl_pages = "1" },
                urlParams = new { },
                body = "",
                parameterConfig = new { },
                inputs = new { }
            });

            var plugin = new HttpCallPlugin(tool, _notificationServiceMock.Object, _chatContext, _logger);
            var dynamicInputs = JsonConvert.SerializeObject(new Dictionary<string, string>
            {
                { "url", "https://override.com" },
                { "depth", "3" }
            });

            // Act
            var result = await plugin.ExecuteHttpCallAsync(dynamicInputs);

            // Assert
            Assert.Equal("ok: true", result);
            _httpTest.ShouldHaveCalled("https://api.example.com/crawl")
                     .WithRequestJson(new Dictionary<string, string>
                     {
                         { "url", "https://override.com" },     // overridden
                         { "max_crawl_pages", "1" },            // retained from static config
                         { "depth", "3" }                       // added dynamically
                     })
                     .Times(1);
        }

        // ── Headers ─────────────────────────────────────────────────────────

        [Fact]
        public async Task ExecuteHttpCallAsync_SetsAuthorizationHeader()
        {
            // Arrange
            _httpTest.RespondWith("{}", 200);

            var tool = BuildTool(new
            {
                method = "GET",
                url = "https://api.example.com/secure",
                headers = new { Authorization = "Basic dXNlcjpwYXNz", ContentType = "application/json" },
                parameters = new { },
                urlParams = new { },
                body = "",
                parameterConfig = new { },
                inputs = new { }
            });

            var plugin = new HttpCallPlugin(tool, _notificationServiceMock.Object, _chatContext, _logger);

            // Act
            await plugin.ExecuteHttpCallAsync();

            // Assert
            _httpTest.ShouldHaveCalled("https://api.example.com/secure")
                     .WithHeader("Authorization", "Basic dXNlcjpwYXNz")
                     .Times(1);
        }

        // ── Error handling ───────────────────────────────────────────────────

        [Fact]
        public async Task ExecuteHttpCallAsync_HttpErrorResponse_ReturnsErrorStringAndNotifies()
        {
            // Arrange
            _httpTest.RespondWith("Unauthorized", 401);

            var tool = BuildTool(new
            {
                method = "GET",
                url = "https://api.example.com/protected",
                headers = new { },
                parameters = new { },
                urlParams = new { },
                body = "",
                parameterConfig = new { },
                inputs = new { }
            });

            var plugin = new HttpCallPlugin(tool, _notificationServiceMock.Object, _chatContext, _logger);

            // Act
            var result = await plugin.ExecuteHttpCallAsync();

            // Assert — returns error string, never throws
            Assert.Contains("401", result);
            Assert.Contains("Unauthorized", result);
            _notificationServiceMock.Verify(
                x => x.SendToolExecutionUpdate(SessionId, ToolName, "failed", It.IsAny<string>(), 401, It.IsAny<string>()),
                Times.Once);
        }

        [Fact]
        public async Task ExecuteHttpCallAsync_MissingUrl_ReturnsErrorStringWithoutCallingHttp()
        {
            // Arrange
            var tool = BuildTool(new
            {
                method = "GET",
                url = "",
                headers = new { },
                parameters = new { },
                urlParams = new { },
                body = "",
                parameterConfig = new { },
                inputs = new { }
            });

            var plugin = new HttpCallPlugin(tool, _notificationServiceMock.Object, _chatContext, _logger);

            // Act
            var result = await plugin.ExecuteHttpCallAsync();

            // Assert
            Assert.Contains("missing or invalid", result, StringComparison.OrdinalIgnoreCase);
            _httpTest.ShouldNotHaveMadeACall();
        }

        [Fact]
        public async Task ExecuteHttpCallAsync_NullConfig_ReturnsErrorStringWithoutCallingHttp()
        {
            // Arrange
            var tool = new TenantTool { Id = 1, Name = ToolName, Config = null };
            var plugin = new HttpCallPlugin(tool, _notificationServiceMock.Object, _chatContext, _logger);

            // Act
            var result = await plugin.ExecuteHttpCallAsync();

            // Assert
            Assert.Contains("missing or invalid", result, StringComparison.OrdinalIgnoreCase);
            _httpTest.ShouldNotHaveMadeACall();
        }

        // ── CleanResponse ────────────────────────────────────────────────────

        [Fact]
        public void CleanResponse_RemovesNullAndEmptyFields()
        {
            var json = """{"name":"Charterhouse","description":null,"tags":[],"meta":{}}""";

            var result = HttpCallPlugin.CleanResponse(json);

            Assert.Equal("name: Charterhouse", result);
        }

        [Fact]
        public void CleanResponse_RemovesNoiseKeys()
        {
            var json = """{"task_id":"abc123","request_id":"req_xyz","crawl_time":"2s","cost":0.01,"title":"Report"}""";

            var result = HttpCallPlugin.CleanResponse(json);

            Assert.Equal("title: Report", result);
        }

        [Fact]
        public void CleanResponse_RemovesBase64Blobs()
        {
            var base64 = new string('A', 300) + "==";
            var json = $$$"""{"screenshot":"{{{base64}}}","title":"Home"}""";

            var result = HttpCallPlugin.CleanResponse(json);

            Assert.Equal("title: Home", result);
            Assert.DoesNotContain("AAAA", result);
        }

        [Fact]
        public void CleanResponse_NestedObjectPrunedToEmpty_RemovedFromParent()
        {
            // After recursion the nested "meta" becomes {} → should be dropped too
            var json = """{"title":"Page","meta":{"request_id":"x","task_id":"y"}}""";

            var result = HttpCallPlugin.CleanResponse(json);

            Assert.Equal("title: Page", result);
        }

        [Fact]
        public void CleanResponse_NonJsonString_CollapsesWhitespaceOnly()
        {
            var text = "  hello   \n\n\t world  ";

            var result = HttpCallPlugin.CleanResponse(text);

            Assert.Equal("hello world", result);
        }

        [Fact]
        public void CleanResponse_PreservesShortStringsUnderBase64Threshold()
        {
            // A 10-char base64-looking string should NOT be stripped
            var json = """{"token":"abc123xyz=","title":"Page"}""";

            var result = HttpCallPlugin.CleanResponse(json);

            Assert.Contains("abc123xyz=", result);
        }

        // ── Truncation ───────────────────────────────────────────────────────

        [Fact]
        public async Task ExecuteHttpCallAsync_ResponseExceedsDefault_TruncatesWithNotice()
        {
            // Arrange — produce a response body larger than the 100 000-char default
            var largeBody = new string('x', 110_000);
            _httpTest.RespondWith(largeBody, 200);

            var tool = BuildTool(new
            {
                method = "GET",
                url = "https://api.example.com/large",
                headers = new { },
                parameters = new { },
                urlParams = new { },
                body = "",
                parameterConfig = new { },
                inputs = new { }
                // maxResponseChars omitted → defaults to 50 000
            });

            var plugin = new HttpCallPlugin(tool, _notificationServiceMock.Object, _chatContext, _logger);

            // Act
            var result = await plugin.ExecuteHttpCallAsync();

            // Assert — result is capped at 50 000 chars + truncation notice
            Assert.True(result.Length < largeBody.Length);
            Assert.StartsWith(new string('x', 100_000), result);
            Assert.Contains("RESPONSE TRUNCATED", result);
            Assert.Contains("110,000", result);  // original length reported
            Assert.Contains("100,000", result);  // limit reported
        }

        [Fact]
        public async Task ExecuteHttpCallAsync_ResponseWithinDefault_ReturnsFull()
        {
            // Arrange
            var body = new string('y', 99_999);
            _httpTest.RespondWith(body, 200);

            var tool = BuildTool(new
            {
                method = "GET",
                url = "https://api.example.com/small",
                headers = new { },
                parameters = new { },
                urlParams = new { },
                body = "",
                parameterConfig = new { },
                inputs = new { }
            });

            var plugin = new HttpCallPlugin(tool, _notificationServiceMock.Object, _chatContext, _logger);

            // Act
            var result = await plugin.ExecuteHttpCallAsync();

            // Assert — no truncation
            Assert.Equal(body, result);
            Assert.DoesNotContain("RESPONSE TRUNCATED", result);
        }

        [Fact]
        public async Task ExecuteHttpCallAsync_JsonResponseCleaned_NullsAndNoiseRemoved()
        {
            // Arrange — simulate a typical API response with nulls, noise keys, and empty arrays
            var body = """{"task_id":"t1","request_id":"r1","title":"Charterhouse","description":null,"tags":[]}""";
            _httpTest.RespondWith(body, 200);

            var tool = BuildTool(new
            {
                method = "GET",
                url = "https://api.example.com/page",
                headers = new { },
                parameters = new { },
                urlParams = new { },
                body = "",
                parameterConfig = new { },
                inputs = new { }
            });

            var plugin = new HttpCallPlugin(tool, _notificationServiceMock.Object, _chatContext, _logger);

            // Act
            var result = await plugin.ExecuteHttpCallAsync();

            // Assert — only meaningful field survives
            Assert.Equal("title: Charterhouse", result);
        }

        [Fact]
        public async Task ExecuteHttpCallAsync_CustomMaxResponseChars_TruncatesAtConfiguredLimit()
        {
            // Arrange — 1 000-char limit configured on the tool
            var body = new string('z', 5_000);
            _httpTest.RespondWith(body, 200);

            var tool = BuildTool(new
            {
                method = "GET",
                url = "https://api.example.com/custom-limit",
                headers = new { },
                parameters = new { },
                urlParams = new { },
                body = "",
                parameterConfig = new { },
                inputs = new { },
                maxResponseChars = 1_000
            });

            var plugin = new HttpCallPlugin(tool, _notificationServiceMock.Object, _chatContext, _logger);

            // Act
            var result = await plugin.ExecuteHttpCallAsync();

            // Assert
            Assert.StartsWith(new string('z', 1_000), result);
            Assert.Contains("RESPONSE TRUNCATED", result);
            Assert.Contains("1,000", result);    // limit reported
        }

        // ── Inputs / placeholder resolution ─────────────────────────────────

        [Fact]
        public async Task ExecuteWithArgsAsync_ReplacesBodyPlaceholdersWithLlmValues()
        {
            // Arrange
            _httpTest.RespondWith("{\"score\":92}", 200);

            var config = BuildConfig(new
            {
                method = "POST",
                url = "https://api.dataforseo.com/v3/on_page/lighthouse/live/json",
                headers = new { },
                parameters = new { },
                urlParams = new { },
                body = """[{"url":"{{url}}","for_mobile":true}]""",
                parameterConfig = new { },
                inputs = new
                {
                    url = new
                    {
                        name = "URL",
                        description = "Page to audit",
                        type = "string",
                        required = true,
                        value = "",
                        defaultValue = "https://www.charterhouse.org.uk",
                        hasDefaultValue = true
                    }
                }
            });

            var tool = new TenantTool { Id = 25, Name = ToolName, Type = "HTTP", Config = config };
            var plugin = new HttpCallPlugin(tool, _notificationServiceMock.Object, _chatContext, _logger);
            var inputs = JsonConvert.DeserializeObject<Dictionary<string, AMAIS.WKFLO.Common.Models.Config.HttpCallInputDefinition>>(
                JObject.Parse(config)["inputs"]!.ToString());
            var parsedConfig = JsonConvert.DeserializeObject<AMAIS.WKFLO.Common.Models.Config.HttpCallToolConfig>(config);
            var args = new KernelArguments { ["url"] = "https://www.eton.ac.uk" };

            // Act
            var result = await plugin.ExecuteWithArgsAsync(args, parsedConfig, inputs, TestContext.Current.CancellationToken);

            // Assert — placeholder was resolved to LLM-provided value
            _httpTest.ShouldHaveCalled("https://api.dataforseo.com/v3/on_page/lighthouse/live/json")
                     .WithVerb(HttpMethod.Post)
                     .WithRequestBody("""[{"url":"https://www.eton.ac.uk","for_mobile":true}]""")
                     .Times(1);
            Assert.Equal("score: 92", result);
        }

        [Fact]
        public async Task ExecuteWithArgsAsync_FallsBackToDefaultValueWhenLlmOmitsArg()
        {
            // Arrange
            _httpTest.RespondWith("{\"score\":88}", 200);

            var config = BuildConfig(new
            {
                method = "POST",
                url = "https://api.example.com/audit",
                headers = new { },
                parameters = new { },
                urlParams = new { },
                body = """{"url":"{{url}}"}""",
                parameterConfig = new { },
                inputs = new
                {
                    url = new
                    {
                        name = "URL",
                        description = "Page URL",
                        type = "string",
                        required = true,
                        value = "",
                        defaultValue = "https://www.charterhouse.org.uk",
                        hasDefaultValue = true
                    }
                }
            });

            var tool = new TenantTool { Id = 26, Name = ToolName, Type = "HTTP", Config = config };
            var plugin = new HttpCallPlugin(tool, _notificationServiceMock.Object, _chatContext, _logger);
            var parsedConfig = JsonConvert.DeserializeObject<AMAIS.WKFLO.Common.Models.Config.HttpCallToolConfig>(config);
            var inputs = parsedConfig.Inputs;

            // Act — no 'url' argument supplied by the LLM
            var result = await plugin.ExecuteWithArgsAsync(new KernelArguments(), parsedConfig, inputs, TestContext.Current.CancellationToken);

            // Assert — defaultValue was used
            _httpTest.ShouldHaveCalled("https://api.example.com/audit")
                     .WithRequestBody("""{"url":"https://www.charterhouse.org.uk"}""")
                     .Times(1);
            Assert.Equal("score: 88", result);
        }

        [Fact]
        public async Task ExecuteWithArgsAsync_ReturnsErrorForMissingRequiredInputWithNoDefault()
        {
            // Arrange — required input with no default, LLM provides nothing
            var config = BuildConfig(new
            {
                method = "POST",
                url = "https://api.example.com/audit",
                headers = new { },
                parameters = new { },
                urlParams = new { },
                body = """{"url":"{{url}}"}""",
                parameterConfig = new { },
                inputs = new
                {
                    url = new
                    {
                        name = "URL",
                        description = "Page URL",
                        type = "string",
                        required = true,
                        value = "",
                        defaultValue = "",
                        hasDefaultValue = false
                    }
                }
            });

            var tool = new TenantTool { Id = 27, Name = ToolName, Type = "HTTP", Config = config };
            var plugin = new HttpCallPlugin(tool, _notificationServiceMock.Object, _chatContext, _logger);
            var parsedConfig = JsonConvert.DeserializeObject<AMAIS.WKFLO.Common.Models.Config.HttpCallToolConfig>(config);
            var inputs = parsedConfig.Inputs;

            // Act
            var result = await plugin.ExecuteWithArgsAsync(new KernelArguments(), parsedConfig, inputs, TestContext.Current.CancellationToken);

            // Assert — error returned, no HTTP call made
            Assert.Contains("required input", result, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("url", result, StringComparison.OrdinalIgnoreCase);
            _httpTest.ShouldNotHaveMadeACall();
        }

        [Fact]
        public async Task ExecuteWithArgsAsync_ReplacesParameterDictPlaceholders()
        {
            // Arrange — placeholders in the parameters dict (not body)
            _httpTest.RespondWith("{\"ok\":true}", 200);

            var config = BuildConfig(new
            {
                method = "POST",
                url = "https://api.example.com/crawl",
                headers = new { },
                parameters = new { url = "{{url}}", max_crawl_pages = "{{pages}}" },
                urlParams = new { },
                body = "",
                parameterConfig = new { },
                inputs = new
                {
                    url = new
                    {
                        name = "URL",
                        description = "Target URL",
                        type = "string",
                        required = true,
                        value = "",
                        defaultValue = "https://default.com",
                        hasDefaultValue = true
                    },
                    pages = new
                    {
                        name = "Pages",
                        description = "Max pages",
                        type = "number",
                        required = false,
                        value = "5",
                        defaultValue = "1",
                        hasDefaultValue = true
                    }
                }
            });

            var tool = new TenantTool { Id = 28, Name = ToolName, Type = "HTTP", Config = config };
            var plugin = new HttpCallPlugin(tool, _notificationServiceMock.Object, _chatContext, _logger);
            var parsedConfig = JsonConvert.DeserializeObject<AMAIS.WKFLO.Common.Models.Config.HttpCallToolConfig>(config);
            var inputs = parsedConfig.Inputs;
            var args = new KernelArguments { ["url"] = "https://eton.ac.uk" };

            // Act
            var result = await plugin.ExecuteWithArgsAsync(args, parsedConfig, inputs, TestContext.Current.CancellationToken);

            // Assert — url from LLM, pages from configured value "5"
            _httpTest.ShouldHaveCalled("https://api.example.com/crawl")
                     .WithRequestJson(new Dictionary<string, string>
                     {
                         { "url", "https://eton.ac.uk" },
                         { "max_crawl_pages", "5" }
                     })
                     .Times(1);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static string BuildConfig(object config) =>
            JsonConvert.SerializeObject(config);

        private static TenantTool BuildTool(object config) =>
            new()
            {
                Id = 24,
                Name = ToolName,
                Type = "HTTP",
                Config = JsonConvert.SerializeObject(config)
            };
    }
}
