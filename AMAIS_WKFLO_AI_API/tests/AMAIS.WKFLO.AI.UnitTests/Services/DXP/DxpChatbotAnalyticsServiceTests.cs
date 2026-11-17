using AMAIS.DXP.INGRESS.Client.Lib.Api;
using AMAIS.DXP.INGRESS.Client.Lib.Models;
using AMAIS.WKFLO.Common.Infrastructure.Services.DXP;
using AMAIS.WKFLO.Common.Interfaces.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Moq;
using System;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Services.DXP
{
    public class DxpChatbotAnalyticsServiceTests
    {
        private readonly Mock<IInteractionApi> _interactionApiMock;
        private readonly Mock<IHttpContextAccessor> _httpContextAccessorMock;
        private readonly DxpChatbotAnalyticsService _service;

        private static readonly DxpChatbotEventContext Context = new(
            DxpTenantName: "dxp-tenant",
            Domain: "https://dxp.example.com",
            ApiKey: "api-key-123",
            TenantId: 1,
            TenantName: "wkflo-tenant",
            SessionId: "sess-1",
            SubjectId: "subj-1",
            TenantAgentId: 5,
            PublishedTenantAgentId: 9,
            ShareId: "share-abc");

        public DxpChatbotAnalyticsServiceTests()
        {
            _interactionApiMock = new Mock<IInteractionApi>();
            _interactionApiMock
                .Setup(x => x.InteractionWithIPCreateAsync(It.IsAny<CreateInteractionWithIPCommand>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new BooleanSuccessResult());

            // No HttpContext by default — mirrors the real conditions for 3 of the 4 events (a Dapr workflow
            // activity or a Worker background job). The plain InteractionCreate endpoint requires an Origin
            // header matching the tenant's Domain (meant for browser-originated calls from the JS collector),
            // which never exists for these server-to-server calls — so the service always uses
            // InteractionWithIPCreateAsync instead, regardless of HttpContext.
            _httpContextAccessorMock = new Mock<IHttpContextAccessor>();
            _httpContextAccessorMock.SetupGet(x => x.HttpContext).Returns((HttpContext)null);

            _service = new DxpChatbotAnalyticsService(
                _interactionApiMock.Object, _httpContextAccessorMock.Object, Mock.Of<ILogger<DxpChatbotAnalyticsService>>());
        }

        [Fact]
        public async Task ReportConversationStartedAsync_SendsSubjectObjectAndAgentIdentity()
        {
            var result = await _service.ReportConversationStartedAsync(Context);

            Assert.True(result);
            _interactionApiMock.Verify(x => x.InteractionWithIPCreateAsync(
                It.Is<CreateInteractionWithIPCommand>(cmd =>
                    cmd.Predicate == "ChatBot Conversation"
                    && cmd.Subject == Context.SubjectId
                    && cmd.Object == Context.SessionId
                    && cmd.ObjectMetadata.CorrelationId == null
                    && cmd.ObjectMetadata.SiteName == Context.DxpTenantName
                    && cmd.ObjectMetadata.Metadata["TenantAgentId"] == "5"
                    && cmd.ObjectMetadata.Metadata["PublishedTenantAgentId"] == "9"
                    && cmd.ObjectMetadata.Metadata["ShareId"] == "share-abc"
                    && cmd.ObjectMetadata.Metadata["EventSchemaVersion"] == "2"
                    // No conversationDate supplied — the key must be OMITTED, not sent as "". An empty string
                    // IS a present value for an ES keyword field (exists/must_not-exists can't tell "" from
                    // real), and "" on the FIRST document for a brand-new field would lock ES's dynamic mapping
                    // into text instead of date, permanently, for the whole data stream.
                    && !cmd.ObjectMetadata.Metadata.ContainsKey("ConversationDate")),
                Context.DxpTenantName, Context.Domain, Context.ApiKey), Times.Once);
        }

        [Fact]
        public async Task ReportConversationStartedAsync_WithConversationDate_SendsIsoConversationDate()
        {
            var conversationDate = new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

            await _service.ReportConversationStartedAsync(Context, conversationDate);

            _interactionApiMock.Verify(x => x.InteractionWithIPCreateAsync(
                It.Is<CreateInteractionWithIPCommand>(cmd =>
                    cmd.ObjectMetadata.Metadata["ConversationDate"] == conversationDate.ToString("o")),
                Context.DxpTenantName, Context.Domain, Context.ApiKey), Times.Once);
        }

        [Fact]
        public async Task ReportConversationResolutionAsync_SendsIsResolvedAndAnalyzedAt()
        {
            var analyzedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

            await _service.ReportConversationResolutionAsync(Context, false, analyzedAt);

            _interactionApiMock.Verify(x => x.InteractionWithIPCreateAsync(
                It.Is<CreateInteractionWithIPCommand>(cmd =>
                    cmd.Predicate == "Chatbot Conversation Resolution"
                    && cmd.ObjectMetadata.Metadata["IsResolved"] == "False"
                    && cmd.ObjectMetadata.Metadata["AnalyzedAt"] == analyzedAt.ToString("o")
                    && cmd.ObjectMetadata.Metadata["EventSchemaVersion"] == "2"
                    && !cmd.ObjectMetadata.Metadata.ContainsKey("ConversationDate")),
                Context.DxpTenantName, Context.Domain, Context.ApiKey), Times.Once);
        }

        [Fact]
        public async Task ReportConversationResolutionAsync_WithConversationDate_SendsIsoConversationDate()
        {
            var analyzedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var conversationDate = new DateTimeOffset(2025, 12, 31, 23, 0, 0, TimeSpan.Zero);

            await _service.ReportConversationResolutionAsync(Context, false, analyzedAt, conversationDate);

            _interactionApiMock.Verify(x => x.InteractionWithIPCreateAsync(
                It.Is<CreateInteractionWithIPCommand>(cmd =>
                    cmd.ObjectMetadata.Metadata["ConversationDate"] == conversationDate.ToString("o")),
                Context.DxpTenantName, Context.Domain, Context.ApiKey), Times.Once);
        }

        [Fact]
        public async Task ReportMessageFeedbackAsync_SendsMessageIdAndThumbsUp()
        {
            await _service.ReportMessageFeedbackAsync(Context, "msg-1", true);

            _interactionApiMock.Verify(x => x.InteractionWithIPCreateAsync(
                It.Is<CreateInteractionWithIPCommand>(cmd =>
                    cmd.Predicate == "Chatbot Feedback"
                    && cmd.ObjectMetadata.Metadata["MessageId"] == "msg-1"
                    && cmd.ObjectMetadata.Metadata["IsThumbsUp"] == "True"
                    && cmd.ObjectMetadata.Metadata["EventSchemaVersion"] == "2"
                    // No classification/conversation correlation supplied — the keys must be OMITTED entirely,
                    // not sent as "" (see the OMITTED-vs-empty-string comment on the ConversationStarted test).
                    && !cmd.ObjectMetadata.Metadata.ContainsKey("TopicId")
                    && !cmd.ObjectMetadata.Metadata.ContainsKey("TopicName")
                    && !cmd.ObjectMetadata.Metadata.ContainsKey("MessageDate")
                    && !cmd.ObjectMetadata.Metadata.ContainsKey("ConversationDate")
                    && !cmd.ObjectMetadata.Metadata.ContainsKey("TopicVersion")),
                Context.DxpTenantName, Context.Domain, Context.ApiKey), Times.Once);
        }

        [Fact]
        public async Task ReportMessageFeedbackAsync_WithTopicAndDates_SendsThemAll()
        {
            var topicId = Guid.NewGuid();
            var messageDate = new DateTimeOffset(2026, 1, 5, 9, 55, 0, TimeSpan.Zero);
            var conversationDate = new DateTimeOffset(2026, 1, 5, 10, 0, 0, TimeSpan.Zero);

            await _service.ReportMessageFeedbackAsync(Context, "msg-1", true, topicId, "Billing", messageDate, conversationDate, topicVersion: "v1");

            _interactionApiMock.Verify(x => x.InteractionWithIPCreateAsync(
                It.Is<CreateInteractionWithIPCommand>(cmd =>
                    cmd.ObjectMetadata.Metadata["TopicId"] == topicId.ToString()
                    && cmd.ObjectMetadata.Metadata["TopicName"] == "Billing"
                    && cmd.ObjectMetadata.Metadata["MessageDate"] == messageDate.ToString("o")
                    && cmd.ObjectMetadata.Metadata["ConversationDate"] == conversationDate.ToString("o")
                    && cmd.ObjectMetadata.Metadata["TopicVersion"] == "v1"),
                Context.DxpTenantName, Context.Domain, Context.ApiKey), Times.Once);
        }

        [Fact]
        public async Task ReportMessageFeedbackAsync_NoTopicVersion_OmitsTopicVersionKey()
        {
            // US 7142: topicVersion is optional and must follow the same "omit rather than send empty"
            // convention as every other optional field on this event.
            var topicId = Guid.NewGuid();

            await _service.ReportMessageFeedbackAsync(Context, "msg-1", true, topicId, "Billing", topicVersion: null);

            _interactionApiMock.Verify(x => x.InteractionWithIPCreateAsync(
                It.Is<CreateInteractionWithIPCommand>(cmd => !cmd.ObjectMetadata.Metadata.ContainsKey("TopicVersion")),
                Context.DxpTenantName, Context.Domain, Context.ApiKey), Times.Once);
        }

        [Fact]
        public async Task ReportMessageTopicAsync_MatchedTopic_SendsTopicIdNameAndVersion()
        {
            var topicId = Guid.NewGuid();

            await _service.ReportMessageTopicAsync(Context, "msg-1", topicId, "Billing", "v1");

            _interactionApiMock.Verify(x => x.InteractionWithIPCreateAsync(
                It.Is<CreateInteractionWithIPCommand>(cmd =>
                    cmd.Predicate == "Chatbot Message Topic"
                    && cmd.ObjectMetadata.Metadata["MessageId"] == "msg-1"
                    && cmd.ObjectMetadata.Metadata["TopicId"] == topicId.ToString()
                    && cmd.ObjectMetadata.Metadata["TopicName"] == "Billing"
                    && cmd.ObjectMetadata.Metadata["TopicVersion"] == "v1"
                    && cmd.ObjectMetadata.Metadata["EventSchemaVersion"] == "2"
                    && !cmd.ObjectMetadata.Metadata.ContainsKey("MessageDate")
                    && !cmd.ObjectMetadata.Metadata.ContainsKey("ConversationDate")),
                Context.DxpTenantName, Context.Domain, Context.ApiKey), Times.Once);
        }

        [Fact]
        public async Task ReportMessageTopicAsync_WithMessageAndConversationDates_SendsThemBoth()
        {
            var topicId = Guid.NewGuid();
            var messageDate = new DateTimeOffset(2026, 1, 5, 9, 55, 0, TimeSpan.Zero);
            var conversationDate = new DateTimeOffset(2026, 1, 5, 10, 0, 0, TimeSpan.Zero);

            await _service.ReportMessageTopicAsync(Context, "msg-1", topicId, "Billing", "v1", messageDate, conversationDate);

            _interactionApiMock.Verify(x => x.InteractionWithIPCreateAsync(
                It.Is<CreateInteractionWithIPCommand>(cmd =>
                    cmd.ObjectMetadata.Metadata["MessageDate"] == messageDate.ToString("o")
                    && cmd.ObjectMetadata.Metadata["ConversationDate"] == conversationDate.ToString("o")),
                Context.DxpTenantName, Context.Domain, Context.ApiKey), Times.Once);
        }

        [Fact]
        public async Task ReportMessageTopicAsync_NoResolutionYet_OmitsIsResolvedQuestionAndResponse()
        {
            // The original classification-time call: the answer doesn't exist yet, so these 3 optional
            // fields must be entirely absent — not sent as "" (same OMITTED-vs-empty-string convention as
            // every other optional field on this event).
            await _service.ReportMessageTopicAsync(Context, "msg-1", Guid.NewGuid(), "Billing", "v1");

            _interactionApiMock.Verify(x => x.InteractionWithIPCreateAsync(
                It.Is<CreateInteractionWithIPCommand>(cmd =>
                    !cmd.ObjectMetadata.Metadata.ContainsKey("IsResolved")
                    && !cmd.ObjectMetadata.Metadata.ContainsKey("Question")
                    && !cmd.ObjectMetadata.Metadata.ContainsKey("Response")),
                Context.DxpTenantName, Context.Domain, Context.ApiKey), Times.Once);
        }

        [Fact]
        public async Task ReportMessageTopicAsync_ResolutionReReport_SendsIsResolvedQuestionAndResponse()
        {
            var topicId = Guid.NewGuid();
            var messageDate = new DateTimeOffset(2026, 1, 5, 9, 55, 0, TimeSpan.Zero);

            await _service.ReportMessageTopicAsync(
                Context, "msg-1", topicId, "Billing", "v1", messageDate,
                isResolved: false, question: "Can I get a refund?", response: "Yes, within 30 days.");

            _interactionApiMock.Verify(x => x.InteractionWithIPCreateAsync(
                It.Is<CreateInteractionWithIPCommand>(cmd =>
                    cmd.ObjectMetadata.Metadata["IsResolved"] == "False"
                    && cmd.ObjectMetadata.Metadata["Question"] == "Can I get a refund?"
                    && cmd.ObjectMetadata.Metadata["Response"] == "Yes, within 30 days."),
                Context.DxpTenantName, Context.Domain, Context.ApiKey), Times.Once);
        }

        [Fact]
        public async Task ReportMessageTopicAsync_WithAnswerMessageId_SendsIt()
        {
            var topicId = Guid.NewGuid();
            var answerMessageId = Guid.NewGuid().ToString();

            await _service.ReportMessageTopicAsync(
                Context, "msg-1", topicId, "Billing", "v1",
                isResolved: true, question: "Can I get a refund?", response: "Yes, within 30 days.",
                answerMessageId: answerMessageId);

            _interactionApiMock.Verify(x => x.InteractionWithIPCreateAsync(
                It.Is<CreateInteractionWithIPCommand>(cmd =>
                    cmd.ObjectMetadata.Metadata["AnswerMessageId"] == answerMessageId),
                Context.DxpTenantName, Context.Domain, Context.ApiKey), Times.Once);
        }

        [Fact]
        public async Task ReportMessageTopicAsync_NoAnswerMessageId_OmitsAnswerMessageIdKey()
        {
            await _service.ReportMessageTopicAsync(Context, "msg-1", Guid.NewGuid(), "Billing", "v1");

            _interactionApiMock.Verify(x => x.InteractionWithIPCreateAsync(
                It.Is<CreateInteractionWithIPCommand>(cmd =>
                    !cmd.ObjectMetadata.Metadata.ContainsKey("AnswerMessageId")),
                Context.DxpTenantName, Context.Domain, Context.ApiKey), Times.Once);
        }

        [Fact]
        public async Task ReportMessageTopicAsync_ResolutionReReport_WithNullTopicVersion_OmitsTopicVersionKey()
        {
            // Regression guard: the resolution re-report call site (MessageResolutionWorkflowActivity /
            // ConversationAnalysisWorkflowActivity's per-message backfill) doesn't persist TopicVersion on
            // MessageTopicClassification, so it always passes topicVersion: null here. TopicVersion used to be
            // written unconditionally (defaulting to "" when null) instead of going through AddIfPresent like
            // every other optional field - found via direct Elasticsearch inspection of a real re-reported
            // document showing "TopicVersion":"" next to a real "1" on the original classification-time
            // document. Same hazard as every other OMITTED-vs-empty-string case on this event: an ES keyword
            // field can't tell "" apart from a real value on an exists/must_not-exists filter.
            var topicId = Guid.NewGuid();

            await _service.ReportMessageTopicAsync(
                Context, "msg-1", topicId, "Billing", topicVersion: null,
                isResolved: true, question: "Can I get a refund?", response: "Yes, within 30 days.");

            _interactionApiMock.Verify(x => x.InteractionWithIPCreateAsync(
                It.Is<CreateInteractionWithIPCommand>(cmd =>
                    !cmd.ObjectMetadata.Metadata.ContainsKey("TopicVersion")),
                Context.DxpTenantName, Context.Domain, Context.ApiKey), Times.Once);
        }

        [Fact]
        public async Task ReportMessageTopicAsync_IsResolvedTrue_SendsIsResolvedTrue()
        {
            await _service.ReportMessageTopicAsync(
                Context, "msg-1", Guid.NewGuid(), "Billing", "v1", isResolved: true);

            _interactionApiMock.Verify(x => x.InteractionWithIPCreateAsync(
                It.Is<CreateInteractionWithIPCommand>(cmd => cmd.ObjectMetadata.Metadata["IsResolved"] == "True"),
                Context.DxpTenantName, Context.Domain, Context.ApiKey), Times.Once);
        }

        [Fact]
        public async Task ReportMessageTopicAsync_UncategorisedResult_OmitsTopicFieldsEntirely()
        {
            // "Uncategorized" (topicId/topicName both null) must OMIT the keys, not send "" — DXP identifies
            // Uncategorized via a must_not-exists filter on TopicId.keyword, and an ES keyword field treats ""
            // as present, which would silently break that detection.
            await _service.ReportMessageTopicAsync(Context, "msg-1", null, null, "v1");

            _interactionApiMock.Verify(x => x.InteractionWithIPCreateAsync(
                It.Is<CreateInteractionWithIPCommand>(cmd =>
                    !cmd.ObjectMetadata.Metadata.ContainsKey("TopicId")
                    && !cmd.ObjectMetadata.Metadata.ContainsKey("TopicName")),
                Context.DxpTenantName, Context.Domain, Context.ApiKey), Times.Once);
        }

        [Fact]
        public async Task ReportConversationStartedAsync_MissingSessionId_DoesNotCallApi()
        {
            var context = Context with { SessionId = string.Empty };

            var result = await _service.ReportConversationStartedAsync(context);

            Assert.False(result);
            _interactionApiMock.Verify(x => x.InteractionWithIPCreateAsync(
                It.IsAny<CreateInteractionWithIPCommand>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        // --- Single choke point: IsTestChat must suppress every one of the 4 DXP event types before ever
        // calling _interactionApi, regardless of which ReportXAsync method was invoked. ---

        [Theory]
        [InlineData("ReportConversationStarted")]
        [InlineData("ReportConversationResolution")]
        [InlineData("ReportMessageFeedback")]
        [InlineData("ReportMessageTopic")]
        public async Task AllEvents_IsTestChat_DoesNotCallApiAndReturnsFalse(string eventName)
        {
            var context = Context with { IsTestChat = true };

            var result = await InvokeEventAsync(eventName, context);

            Assert.False(result);
            _interactionApiMock.Verify(x => x.InteractionWithIPCreateAsync(
                It.IsAny<CreateInteractionWithIPCommand>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task ReportConversationStartedAsync_IsTestChatFalse_StillCallsApi()
        {
            var context = Context with { IsTestChat = false };

            var result = await _service.ReportConversationStartedAsync(context);

            Assert.True(result);
            _interactionApiMock.Verify(x => x.InteractionWithIPCreateAsync(
                It.IsAny<CreateInteractionWithIPCommand>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task ReportConversationStartedAsync_MissingSubjectId_DoesNotCallApiAndReturnsFalse()
        {
            var context = Context with { SubjectId = string.Empty };

            var result = await _service.ReportConversationStartedAsync(context);

            Assert.False(result);
            _interactionApiMock.Verify(x => x.InteractionWithIPCreateAsync(
                It.IsAny<CreateInteractionWithIPCommand>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        // --- DXPINGRESS0104 regression coverage: never forward a resolved DxpIntegration with an empty
        // Domain/DxpTenantName/ApiKey to DXP Ingress — skip and log loudly instead, so the failure is
        // diagnosable (which tenant/session, which field was empty) instead of only a cryptic DXP-side error
        // code. ---

        [Fact]
        public async Task ReportConversationStartedAsync_EmptyDomain_DoesNotCallApiAndReturnsFalse()
        {
            var context = Context with { Domain = string.Empty };

            var result = await _service.ReportConversationStartedAsync(context);

            Assert.False(result);
            _interactionApiMock.Verify(x => x.InteractionWithIPCreateAsync(
                It.IsAny<CreateInteractionWithIPCommand>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task ReportConversationStartedAsync_EmptyDxpTenantName_DoesNotCallApi()
        {
            var context = Context with { DxpTenantName = string.Empty };

            var result = await _service.ReportConversationStartedAsync(context);

            Assert.False(result);
            _interactionApiMock.Verify(x => x.InteractionWithIPCreateAsync(
                It.IsAny<CreateInteractionWithIPCommand>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task ReportConversationStartedAsync_EmptyApiKey_DoesNotCallApi()
        {
            var context = Context with { ApiKey = string.Empty };

            var result = await _service.ReportConversationStartedAsync(context);

            Assert.False(result);
            _interactionApiMock.Verify(x => x.InteractionWithIPCreateAsync(
                It.IsAny<CreateInteractionWithIPCommand>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task ReportConversationStartedAsync_InteractionApiThrows_ReturnsFalseAndSwallows()
        {
            _interactionApiMock
                .Setup(x => x.InteractionWithIPCreateAsync(It.IsAny<CreateInteractionWithIPCommand>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("boom"));

            var result = await _service.ReportConversationStartedAsync(Context);

            Assert.False(result);
        }

        // --- Always InteractionWithIPCreateAsync, never the plain InteractionCreateAsync. ---
        // DXP Ingress's plain endpoint requires an Origin header matching the tenant's Domain (meant for
        // browser-originated calls from the JS collector) — our server-to-server events (Dapr workflow
        // activity, Worker background job) never have one, so that endpoint can never work for them regardless
        // of Domain/DxpIntegration correctness. InteractionWithIPCreate has no such check, so it's used for
        // every event, always — UserAgent/Language get placeholders and IPAddress gets "None" when there's no
        // real HttpContext, rather than switching endpoints.

        [Theory]
        [InlineData("ReportConversationStarted")]
        [InlineData("ReportConversationResolution")]
        [InlineData("ReportMessageFeedback")]
        [InlineData("ReportMessageTopic")]
        public async Task AllEvents_NoHttpContext_UseWithIPCommandWithPlaceholderUserAgentLanguageAndIp(string eventName)
        {
            // _httpContextAccessorMock already returns a null HttpContext (constructor default) — the exact
            // condition for events 2 (workflow activity) and 4-batch (Worker job).
            await InvokeEventAsync(eventName);

            _interactionApiMock.Verify(x => x.InteractionWithIPCreateAsync(
                It.Is<CreateInteractionWithIPCommand>(cmd =>
                    !string.IsNullOrEmpty(cmd.UserAgent)
                    && !string.IsNullOrEmpty(cmd.Language)
                    && !string.IsNullOrEmpty(cmd.IPAddress)
                    && cmd.UserAgent == "AMAIS.WKFLO.Backend"
                    && cmd.Language == "en"
                    && cmd.IPAddress == "None"),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task ReportConversationStartedAsync_HttpContextWithHeadersAndForwardedIp_UsesRealValues()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers["User-Agent"] = new StringValues("Mozilla/5.0 TestBrowser");
            httpContext.Request.Headers["Accept-Language"] = new StringValues("fr-FR");
            httpContext.Request.Headers["X-Forwarded-For"] = new StringValues("203.0.113.7, 10.0.0.1");
            _httpContextAccessorMock.SetupGet(x => x.HttpContext).Returns(httpContext);

            await _service.ReportConversationStartedAsync(Context);

            _interactionApiMock.Verify(x => x.InteractionWithIPCreateAsync(
                It.Is<CreateInteractionWithIPCommand>(cmd =>
                    cmd.IPAddress == "203.0.113.7"
                    && cmd.UserAgent == "Mozilla/5.0 TestBrowser"
                    && cmd.Language == "fr-FR"),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task ReportConversationStartedAsync_HttpContextPresentButHeadersEmpty_FallsBackToPlaceholders()
        {
            // HttpContext exists (so this could be a genuine in-request call) but carries none of the
            // headers/remote address we care about — must still fall back to placeholders rather than sending
            // empty strings or a real-looking value that isn't real.
            var httpContext = new DefaultHttpContext();
            _httpContextAccessorMock.SetupGet(x => x.HttpContext).Returns(httpContext);

            await _service.ReportConversationStartedAsync(Context);

            _interactionApiMock.Verify(x => x.InteractionWithIPCreateAsync(
                It.Is<CreateInteractionWithIPCommand>(cmd =>
                    cmd.UserAgent == "AMAIS.WKFLO.Backend"
                    && cmd.Language == "en"
                    && cmd.IPAddress == "None"),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task ReportConversationStartedAsync_HttpContextWithRemoteIpAddressOnly_UsesRealIp()
        {
            // No X-Forwarded-For header, but a real RemoteIpAddress on the connection — still a genuine IP,
            // just from a different source than the proxy header.
            var httpContext = new DefaultHttpContext();
            httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("198.51.100.42");
            _httpContextAccessorMock.SetupGet(x => x.HttpContext).Returns(httpContext);

            await _service.ReportConversationStartedAsync(Context);

            _interactionApiMock.Verify(x => x.InteractionWithIPCreateAsync(
                It.Is<CreateInteractionWithIPCommand>(cmd => cmd.IPAddress == "198.51.100.42"),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        private Task InvokeEventAsync(string eventName) => InvokeEventAsync(eventName, Context);

        private Task<bool> InvokeEventAsync(string eventName, DxpChatbotEventContext context) => eventName switch
        {
            "ReportConversationStarted" => _service.ReportConversationStartedAsync(context),
            "ReportConversationResolution" => _service.ReportConversationResolutionAsync(context, true, DateTimeOffset.UtcNow),
            "ReportMessageFeedback" => _service.ReportMessageFeedbackAsync(context, "msg-1", true),
            "ReportMessageTopic" => _service.ReportMessageTopicAsync(context, "msg-1", Guid.NewGuid(), "Billing", "v1"),
            _ => throw new ArgumentOutOfRangeException(nameof(eventName))
        };
    }
}
