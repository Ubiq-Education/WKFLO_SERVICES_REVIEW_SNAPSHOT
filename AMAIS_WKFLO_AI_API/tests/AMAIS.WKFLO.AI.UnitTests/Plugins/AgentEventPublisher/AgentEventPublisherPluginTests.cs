using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Events.PublishAgentEvent;
using AMAIS.WKFLO.Common.AI.Application.Plugins.AgentEventPublisher;
using AMAIS.WKFLO.Common.AI.Application.Services.Documents;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using MockQueryable.Moq;
using Moq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.UnitTests.Plugins.AgentEventPublisher
{
    public class AgentEventPublisherPluginTests
    {
        private const string TenantName = "test-tenant";
        private const string SessionId = "session-xyz";
        private const int CallingAgentId = 1;

        private readonly Mock<IWkfloMediator> _mediatorMock;
        private readonly Mock<IChatNotificationService> _notificationServiceMock;
        private readonly Mock<IBlobStorageProvider> _blobStorageProviderMock;
        private readonly ChatContext _chatContext;
        private readonly TenantContext _tenantContext;
        private readonly ILogger _logger = NullLogger.Instance;

        public AgentEventPublisherPluginTests()
        {
            _mediatorMock = new Mock<IWkfloMediator>();
            _notificationServiceMock = new Mock<IChatNotificationService>();
            _notificationServiceMock
                .Setup(x => x.SendToolExecutionUpdate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);
            _notificationServiceMock
                .Setup(x => x.SendSubagentActivityUpdate(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);
            _blobStorageProviderMock = new Mock<IBlobStorageProvider>();

            _chatContext = new ChatContext { SessionId = SessionId };
            _tenantContext = new TenantContext { TenantName = TenantName };
        }

        private AgentEventPublisherPlugin BuildPlugin(IEnumerable<Agent> agents, IEnumerable<AgentAllowedSpinUpAgent> allowedSpinUpAgents = null, IEnumerable<AgentChatSetting> agentChatSettings = null, bool? rootIsWidget = null, bool handOffApprovalsAvailable = false,
            IChatStopCheckpoint chatStopCheckpoint = null)
        {
            var dbSet = new List<Agent>(agents).BuildMockDbSet();
            var allowedDbSet = new List<AgentAllowedSpinUpAgent>(allowedSpinUpAgents ?? []).BuildMockDbSet();
            var chatSettingsDbSet = new List<AgentChatSetting>(agentChatSettings ?? []).BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.Agents).Returns(dbSet.Object);
            db.Setup(x => x.AgentAllowedSpinUpAgents).Returns(allowedDbSet.Object);
            db.Setup(x => x.AgentChatSettings).Returns(chatSettingsDbSet.Object);

            return new AgentEventPublisherPlugin(_mediatorMock.Object, _notificationServiceMock.Object, _chatContext, _tenantContext, db.Object, _logger, CallingAgentId, _blobStorageProviderMock.Object, rootIsWidget, handOffApprovalsAvailable, chatStopCheckpoint);
        }

        private static ChatMessageContent BuildImageMessage(string fileName, string mimeType, byte[] data = null, Uri uri = null)
        {
            var imageContent = data != null
                ? new ImageContent(data, mimeType) { Uri = uri }
                : new ImageContent { Uri = uri };

            var items = new ChatMessageContentItemCollection
            {
                new TextContent($"FileName: '{fileName}'\nFileType: '{mimeType}'"),
                imageContent
            };

            var metadata = new Dictionary<string, object>
            {
                { CommonMetadataConsts.FileName, fileName },
                { CommonMetadataConsts.FileType, mimeType }
            };

            return new ChatMessageContent(AuthorRole.User, items, metadata: metadata);
        }

        private static ChatMessageContent BuildDocumentMessage(string fileName, string mimeType, string extractedText)
        {
            var items = new ChatMessageContentItemCollection
            {
                new TextContent($"Document content:\n{extractedText}", metadata: new Dictionary<string, object>
                {
                    { CommonMetadataConsts.FileName, fileName },
                    { CommonMetadataConsts.FileType, mimeType }
                })
            };

            return new ChatMessageContent(AuthorRole.User, items, metadata: new Dictionary<string, object>());
        }

        private static Tenant BuildTenant() => new() { Id = 1, Name = TenantName };

        private static AgentAllowedSpinUpAgent Allow(int allowedSpinUpAgentId) =>
            new() { AgentId = CallingAgentId, AllowedSpinUpAgentId = allowedSpinUpAgentId };

        [Fact]
        public async Task PublishAgentEventAsync_SingleResolvableTarget_PublishesOneDomainEventAndReturnsSuccessSummary()
        {
            var tenant = BuildTenant();
            var plugin = BuildPlugin(new Agent[]
            {
                new TenantAgent { Id = 5, Name = "Refunds Bot", Tenant = tenant, IsActive = true }
            },
            [Allow(5)]);

            _mediatorMock
                .Setup(x => x.Publish(It.IsAny<PublishAgentEventDomainEvent>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var targetsJson = JsonConvert.SerializeObject(new[] { new { agentName = "Refunds Bot" } });

            var result = await plugin.PublishAgentEventAsync(targetsJson, "please help", "refund-requested", cancellationToken: TestContext.Current.CancellationToken);

            _mediatorMock.Verify(x => x.Publish(
                It.Is<PublishAgentEventDomainEvent>(e =>
                    e.AgentId == 5 && e.AgentRole == "Refunds Bot" && e.Text == "please help" &&
                    e.SessionId == SessionId && e.EventType == "refund-requested"),
                It.IsAny<CancellationToken>()),
                Times.Once);

            Assert.Contains("Refunds Bot", result);
            Assert.DoesNotContain("Failed", result);
        }

        [Theory]
        [InlineData(true, CallingAgentId)]
        [InlineData(false, null)]
        public async Task PublishAgentEventAsync_CarriesTheApprovalRootOnlyFromAnInteractiveRun(bool handOffApprovalsAvailable, int? expectedRoot)
        {
            // Bug 7722: a hand-off from an interactive chat lets the target agent ask the same person for approval.
            var plugin = BuildPlugin(new Agent[] { new TenantAgent { Id = 5, Name = "Refunds Bot", Tenant = BuildTenant(), IsActive = true } },
                [Allow(5)], handOffApprovalsAvailable: handOffApprovalsAvailable);
            _mediatorMock.Setup(x => x.Publish(It.IsAny<PublishAgentEventDomainEvent>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            await plugin.PublishAgentEventAsync(JsonConvert.SerializeObject(new[] { new { agentName = "Refunds Bot" } }), "please help", "refund-requested",
                cancellationToken: TestContext.Current.CancellationToken);

            _mediatorMock.Verify(x => x.Publish(It.Is<PublishAgentEventDomainEvent>(e => e.ApprovalRootAgentId == expectedRoot), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task PublishAgentEventAsync_SingleResolvableTarget_SendsStartedSubagentActivityUpdate()
        {
            var tenant = BuildTenant();
            var plugin = BuildPlugin(new Agent[]
            {
                new TenantAgent { Id = 5, Name = "Refunds Bot", Tenant = tenant, IsActive = true }
            },
            [Allow(5)],
            new[] { new AgentChatSetting { Id = 1, AgentId = 5, IsWidget = false, ChatbotIconPath = "https://cdn.test/refunds-bot-icon.png" } });

            _mediatorMock
                .Setup(x => x.Publish(It.IsAny<PublishAgentEventDomainEvent>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var targetsJson = JsonConvert.SerializeObject(new[] { new { agentName = "Refunds Bot" } });

            await plugin.PublishAgentEventAsync(targetsJson, "please help", "refund-requested", cancellationToken: TestContext.Current.CancellationToken);

            _notificationServiceMock.Verify(x => x.SendSubagentActivityUpdate(
                SessionId, 5, "Refunds Bot", "started", "https://cdn.test/refunds-bot-icon.png", "refund-requested", It.IsAny<string>(), It.IsAny<string>()),
                Times.Once);
        }

        [Fact]
        public async Task PublishAgentEventAsync_CallingAgentHasNonWidgetChatSetting_ResolvesRootIsWidgetFalseOnDomainEvent()
        {
            // The calling agent (CallingAgentId) is the root of this spin-up hand-off — its own
            // widget/non-widget context is resolved once here, while still running inside the calling
            // agent's own turn, and must ride on the published domain event so the target agent
            // resolves its icon consistently rather than independently.
            var tenant = BuildTenant();
            var plugin = BuildPlugin(
                new Agent[] { new TenantAgent { Id = 5, Name = "Refunds Bot", Tenant = tenant, IsActive = true } },
                [Allow(5)],
                new[] { new AgentChatSetting { Id = 1, AgentId = CallingAgentId, IsWidget = false, ChatbotIconPath = "https://cdn.test/caller-non-widget-icon.png" } });

            PublishAgentEventDomainEvent captured = null;
            _mediatorMock
                .Setup(x => x.Publish(It.IsAny<PublishAgentEventDomainEvent>(), It.IsAny<CancellationToken>()))
                .Callback<PublishAgentEventDomainEvent, CancellationToken>((e, _) => captured = e)
                .Returns(Task.CompletedTask);

            var targetsJson = JsonConvert.SerializeObject(new[] { new { agentName = "Refunds Bot" } });

            await plugin.PublishAgentEventAsync(targetsJson, "please help", "refund-requested", cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotNull(captured);
            Assert.False(captured.RootIsWidget);
        }

        [Fact]
        public async Task PublishAgentEventAsync_RootIsWidgetInjected_UsesInjectedValueWithoutRecomputing()
        {
            // The calling agent was itself invoked via a ShareId-rooted flow (e.g. PublicAISyncService),
            // which already resolved the definitive widget/non-widget context for this turn — that value
            // must win outright, even though the calling agent's own AgentChatSetting rows (if queried)
            // would resolve to the opposite value via the AgentId heuristic.
            var tenant = BuildTenant();
            var plugin = BuildPlugin(
                new Agent[] { new TenantAgent { Id = 5, Name = "Refunds Bot", Tenant = tenant, IsActive = true } },
                [Allow(5)],
                new[] { new AgentChatSetting { Id = 1, AgentId = CallingAgentId, IsWidget = false, ChatbotIconPath = "https://cdn.test/caller-non-widget-icon.png" } },
                rootIsWidget: true);

            PublishAgentEventDomainEvent captured = null;
            _mediatorMock
                .Setup(x => x.Publish(It.IsAny<PublishAgentEventDomainEvent>(), It.IsAny<CancellationToken>()))
                .Callback<PublishAgentEventDomainEvent, CancellationToken>((e, _) => captured = e)
                .Returns(Task.CompletedTask);

            var targetsJson = JsonConvert.SerializeObject(new[] { new { agentName = "Refunds Bot" } });

            await plugin.PublishAgentEventAsync(targetsJson, "please help", "refund-requested", cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotNull(captured);
            Assert.True(captured.RootIsWidget);
        }

        [Fact]
        public async Task PublishAgentEventAsync_CallingAgentHasNoChatSetting_ResolvesRootIsWidgetNullOnDomainEvent()
        {
            var tenant = BuildTenant();
            var plugin = BuildPlugin(
                new Agent[] { new TenantAgent { Id = 5, Name = "Refunds Bot", Tenant = tenant, IsActive = true } },
                [Allow(5)]);

            PublishAgentEventDomainEvent captured = null;
            _mediatorMock
                .Setup(x => x.Publish(It.IsAny<PublishAgentEventDomainEvent>(), It.IsAny<CancellationToken>()))
                .Callback<PublishAgentEventDomainEvent, CancellationToken>((e, _) => captured = e)
                .Returns(Task.CompletedTask);

            var targetsJson = JsonConvert.SerializeObject(new[] { new { agentName = "Refunds Bot" } });

            await plugin.PublishAgentEventAsync(targetsJson, "please help", "refund-requested", cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotNull(captured);
            Assert.Null(captured.RootIsWidget);
        }

        [Fact]
        public async Task PublishAgentEventAsync_MultipleResolvableTargets_PublishesOneDomainEventPerTarget()
        {
            var tenant = BuildTenant();
            var plugin = BuildPlugin(new Agent[]
            {
                new TenantAgent { Id = 5, Name = "Refunds Bot", Tenant = tenant, IsActive = true },
                new PublishedTenantAgent { Id = 6, Name = "Billing Bot", Tenant = tenant, IsActive = true }
            },
            [Allow(5), Allow(6)]);

            _mediatorMock
                .Setup(x => x.Publish(It.IsAny<PublishAgentEventDomainEvent>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var targetsJson = JsonConvert.SerializeObject(new[]
            {
                new { agentName = "Refunds Bot" },
                new { agentName = "Billing Bot" }
            });

            var result = await plugin.PublishAgentEventAsync(targetsJson, "please help", "escalation", cancellationToken: TestContext.Current.CancellationToken);

            _mediatorMock.Verify(x => x.Publish(It.IsAny<PublishAgentEventDomainEvent>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
            _mediatorMock.Verify(x => x.Publish(It.Is<PublishAgentEventDomainEvent>(e => e.AgentId == 5), It.IsAny<CancellationToken>()), Times.Once);
            _mediatorMock.Verify(x => x.Publish(It.Is<PublishAgentEventDomainEvent>(e => e.AgentId == 6), It.IsAny<CancellationToken>()), Times.Once);

            Assert.Contains("Refunds Bot", result);
            Assert.Contains("Billing Bot", result);
        }

        [Fact]
        public async Task PublishAgentEventAsync_NameNotFound_ReturnsMixedSummaryWithNotFoundReason()
        {
            var tenant = BuildTenant();
            var plugin = BuildPlugin(new Agent[]
            {
                new TenantAgent { Id = 5, Name = "Refunds Bot", Tenant = tenant, IsActive = true }
            },
            [Allow(5)]);

            _mediatorMock
                .Setup(x => x.Publish(It.IsAny<PublishAgentEventDomainEvent>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var targetsJson = JsonConvert.SerializeObject(new[]
            {
                new { agentName = "Refunds Bot" },
                new { agentName = "Nonexistent Bot" }
            });

            var result = await plugin.PublishAgentEventAsync(targetsJson, "please help", "escalation", cancellationToken: TestContext.Current.CancellationToken);

            Assert.Contains("Refunds Bot", result);
            Assert.Contains("Nonexistent Bot (not found)", result);
            _mediatorMock.Verify(x => x.Publish(It.IsAny<PublishAgentEventDomainEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task PublishAgentEventAsync_AmbiguousName_ReturnsFailureWithAmbiguousReason()
        {
            var tenant = BuildTenant();
            var plugin = BuildPlugin(new Agent[]
            {
                new TenantAgent { Id = 5, Name = "Bot", Tenant = tenant, IsActive = true },
                new PublishedTenantAgent { Id = 6, Name = "Bot", Tenant = tenant, IsActive = true }
            },
            [Allow(5), Allow(6)]);

            var targetsJson = JsonConvert.SerializeObject(new[] { new { agentName = "Bot" } });

            var result = await plugin.PublishAgentEventAsync(targetsJson, "please help", "escalation", cancellationToken: TestContext.Current.CancellationToken);

            Assert.Contains("Bot (ambiguous", result);
            _mediatorMock.Verify(x => x.Publish(It.IsAny<PublishAgentEventDomainEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task PublishAgentEventAsync_NameMatchesOnlyInDifferentTenant_ReturnsNotFound()
        {
            var otherTenant = new Tenant { Id = 2, Name = "other-tenant" };
            var plugin = BuildPlugin(new Agent[]
            {
                new TenantAgent { Id = 5, Name = "Refunds Bot", Tenant = otherTenant, IsActive = true }
            },
            [Allow(5)]);

            var targetsJson = JsonConvert.SerializeObject(new[] { new { agentName = "Refunds Bot" } });

            var result = await plugin.PublishAgentEventAsync(targetsJson, "please help", "escalation", cancellationToken: TestContext.Current.CancellationToken);

            Assert.Contains("Refunds Bot (not found)", result);
            _mediatorMock.Verify(x => x.Publish(It.IsAny<PublishAgentEventDomainEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task PublishAgentEventAsync_NameMatchesOnlyMarketplaceAgent_ReturnsNotFound()
        {
            // MarketplaceAgent is not a RunnableAgent — even with a same-tenant name match it must not resolve.
            var tenant = BuildTenant();
            var plugin = BuildPlugin(new Agent[]
            {
                new MarketplaceAgent { Id = 5, Name = "Refunds Bot", Tenant = tenant }
            },
            [Allow(5)]);

            var targetsJson = JsonConvert.SerializeObject(new[] { new { agentName = "Refunds Bot" } });

            var result = await plugin.PublishAgentEventAsync(targetsJson, "please help", "escalation", cancellationToken: TestContext.Current.CancellationToken);

            Assert.Contains("Refunds Bot (not found)", result);
            _mediatorMock.Verify(x => x.Publish(It.IsAny<PublishAgentEventDomainEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task PublishAgentEventAsync_TargetResolvesButNotOnAllowlist_ReturnsNotFound()
        {
            // Story #6480 follow-up: an active, same-tenant target that resolves by name must still be
            // rejected if the calling agent has no AgentAllowedSpinUpAgent row for it.
            var tenant = BuildTenant();
            var plugin = BuildPlugin(new Agent[]
            {
                new TenantAgent { Id = 5, Name = "Refunds Bot", Tenant = tenant, IsActive = true }
            });

            var targetsJson = JsonConvert.SerializeObject(new[] { new { agentName = "Refunds Bot" } });

            var result = await plugin.PublishAgentEventAsync(targetsJson, "please help", "escalation", cancellationToken: TestContext.Current.CancellationToken);

            Assert.Contains("Refunds Bot (not found)", result);
            _mediatorMock.Verify(x => x.Publish(It.IsAny<PublishAgentEventDomainEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task PublishAgentEventAsync_OneTargetThrowsOnPublish_ReturnsMixedSummaryAndNotifiesFailure()
        {
            var tenant = BuildTenant();
            var plugin = BuildPlugin(new Agent[]
            {
                new TenantAgent { Id = 5, Name = "Refunds Bot", Tenant = tenant, IsActive = true },
                new PublishedTenantAgent { Id = 6, Name = "Billing Bot", Tenant = tenant, IsActive = true }
            },
            [Allow(5), Allow(6)]);

            _mediatorMock
                .Setup(x => x.Publish(It.Is<PublishAgentEventDomainEvent>(e => e.AgentId == 5), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            _mediatorMock
                .Setup(x => x.Publish(It.Is<PublishAgentEventDomainEvent>(e => e.AgentId == 6), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("broker unavailable"));

            var targetsJson = JsonConvert.SerializeObject(new[]
            {
                new { agentName = "Refunds Bot" },
                new { agentName = "Billing Bot" }
            });

            var result = await plugin.PublishAgentEventAsync(targetsJson, "please help", "escalation", cancellationToken: TestContext.Current.CancellationToken);

            Assert.Contains("Refunds Bot", result);
            Assert.Contains("Failed to notify: Billing Bot", result);
            _notificationServiceMock.Verify(x => x.SendToolExecutionUpdate(
                SessionId, "AgentEventPublisher", "failed", It.IsAny<string>(), 500, It.IsAny<string>()),
                Times.Once);
        }

        [Fact]
        public async Task PublishAgentEventAsync_InvalidJson_ReturnsFailureMessageWithoutPublishing()
        {
            var plugin = BuildPlugin([]);

            var result = await plugin.PublishAgentEventAsync("{not-valid-json", "please help", "escalation", cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal("Failed to publish the agent event: the target list was not valid JSON.", result);
            _mediatorMock.Verify(x => x.Publish(It.IsAny<PublishAgentEventDomainEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task PublishAgentEventAsync_EmptyArray_ReturnsFailureMessageWithoutPublishing()
        {
            var plugin = BuildPlugin([]);

            var result = await plugin.PublishAgentEventAsync("[]", "please help", "escalation", cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal("Failed to publish the agent event: no target agents were provided.", result);
            _mediatorMock.Verify(x => x.Publish(It.IsAny<PublishAgentEventDomainEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task PublishAgentEventAsync_ImageFileWithInlineData_ForwardsFileAttachment()
        {
            var tenant = BuildTenant();
            var plugin = BuildPlugin(new Agent[]
            {
                new TenantAgent { Id = 5, Name = "Refunds Bot", Tenant = tenant, IsActive = true }
            },
            [Allow(5)]);

            var imageBytes = new byte[] { 1, 2, 3, 4 };
            _chatContext.CurrentChatHistory = new ChatHistory { BuildImageMessage("receipt.png", "image/png", data: imageBytes) };

            PublishAgentEventDomainEvent captured = null;
            _mediatorMock
                .Setup(x => x.Publish(It.IsAny<PublishAgentEventDomainEvent>(), It.IsAny<CancellationToken>()))
                .Callback<PublishAgentEventDomainEvent, CancellationToken>((e, _) => captured = e)
                .Returns(Task.CompletedTask);

            var targetsJson = JsonConvert.SerializeObject(new[] { new { agentName = "Refunds Bot" } });
            var fileNamesJson = JsonConvert.SerializeObject(new[] { new { fileName = "receipt.png", fileMimeType = "image/png" } });

            var result = await plugin.PublishAgentEventAsync(targetsJson, "please help", "refund-requested", fileNamesJson, TestContext.Current.CancellationToken);

            Assert.NotNull(captured);
            var attachment = Assert.Single(captured.Files);
            Assert.Equal("receipt.png", attachment.FileName);
            Assert.Equal($"data:image/png;base64,{Convert.ToBase64String(imageBytes)}", attachment.FileData);
            Assert.Equal(imageBytes.Length, attachment.FileSize);
            Assert.DoesNotContain("Files not found", result);
            _blobStorageProviderMock.Verify(x => x.DownloadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        // US 7974 / Task 7979: legacy session-rooted names and new tenant-prefixed names both resolve to their blob.
        [Theory]
        [InlineData("session-xyz/receipt.png")]
        [InlineData("3f2504e0-4f89-11d3-9a0c-0305e82c3301/session-xyz/0f8fad5b-d9cb-469f-a165-70867728950e-receipt.png")]
        [InlineData("3f2504e0-4f89-11d3-9a0c-0305e82c3301/edited/0f8fad5b-d9cb-469f-a165-70867728950e.png")]
        public async Task PublishAgentEventAsync_ImageFileWithoutInlineData_DownloadsFromBlobStorage(string blobName)
        {
            var tenant = BuildTenant();
            var plugin = BuildPlugin(new Agent[]
            {
                new TenantAgent { Id = 5, Name = "Refunds Bot", Tenant = tenant, IsActive = true }
            },
            [Allow(5)]);

            var blobUri = new Uri($"https://storage.example.com/chat-images/{blobName}");
            _chatContext.CurrentChatHistory = new ChatHistory { BuildImageMessage("receipt.png", "image/png", uri: blobUri) };

            var downloadedBytes = new byte[] { 9, 8, 7 };
            _blobStorageProviderMock
                .Setup(x => x.DownloadAsync("chat-images", blobName, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MemoryStream(downloadedBytes));

            PublishAgentEventDomainEvent captured = null;
            _mediatorMock
                .Setup(x => x.Publish(It.IsAny<PublishAgentEventDomainEvent>(), It.IsAny<CancellationToken>()))
                .Callback<PublishAgentEventDomainEvent, CancellationToken>((e, _) => captured = e)
                .Returns(Task.CompletedTask);

            var targetsJson = JsonConvert.SerializeObject(new[] { new { agentName = "Refunds Bot" } });
            var fileNamesJson = JsonConvert.SerializeObject(new[] { new { fileName = "receipt.png", fileMimeType = "image/png" } });

            await plugin.PublishAgentEventAsync(targetsJson, "please help", "refund-requested", fileNamesJson, TestContext.Current.CancellationToken);

            var attachment = Assert.Single(captured.Files);
            Assert.Equal($"data:image/png;base64,{Convert.ToBase64String(downloadedBytes)}", attachment.FileData);
            Assert.Equal(blobUri.ToString(), attachment.BlobUrl);
        }

        [Fact]
        public async Task PublishAgentEventAsync_DocumentFile_ForwardsExtractedTextAsPlainTextAttachment()
        {
            var tenant = BuildTenant();
            var plugin = BuildPlugin(new Agent[]
            {
                new TenantAgent { Id = 5, Name = "Refunds Bot", Tenant = tenant, IsActive = true }
            },
            [Allow(5)]);

            const string extractedText = "Order #123 was refunded on 2026-07-01.";
            _chatContext.CurrentChatHistory = new ChatHistory { BuildDocumentMessage("order.pdf", "application/pdf", extractedText) };

            PublishAgentEventDomainEvent captured = null;
            _mediatorMock
                .Setup(x => x.Publish(It.IsAny<PublishAgentEventDomainEvent>(), It.IsAny<CancellationToken>()))
                .Callback<PublishAgentEventDomainEvent, CancellationToken>((e, _) => captured = e)
                .Returns(Task.CompletedTask);

            var targetsJson = JsonConvert.SerializeObject(new[] { new { agentName = "Refunds Bot" } });
            var fileNamesJson = JsonConvert.SerializeObject(new[] { new { fileName = "order.pdf", fileMimeType = "application/pdf" } });

            await plugin.PublishAgentEventAsync(targetsJson, "please help", "refund-requested", fileNamesJson, TestContext.Current.CancellationToken);

            var attachment = Assert.Single(captured.Files);
            Assert.Equal("order.pdf", attachment.FileName);
            Assert.Equal($"data:text/plain;base64,{Convert.ToBase64String(Encoding.UTF8.GetBytes(extractedText))}", attachment.FileData);
        }

        [Fact]
        public async Task PublishAgentEventAsync_IndexedDocument_ForwardsAReferenceToTheSourceDocumentInsteadOfItsText()
        {
            var tenant = BuildTenant();
            var plugin = BuildPlugin(new Agent[]
            {
                new TenantAgent { Id = 5, Name = "Refunds Bot", Tenant = tenant, IsActive = true }
            },
            [Allow(5)]);

            var info = new AttachedDocumentInfo
            {
                DocumentId = Guid.NewGuid(), FileName = "report.pdf", FileType = "application/pdf", FileSize = 11_000_000, CharCount = 2_400_000,
                ChunkCount = 1_600, IndexedChunkCount = 1_600, IndexStatus = ChatDocumentIndexStatus.Ready, Summary = "A long annual report."
            };
            _chatContext.CurrentChatHistory = new ChatHistory
            {
                new ChatMessageContent(AuthorRole.User, [new TextContent(AttachedDocumentMessage.BuildText(info), metadata: AttachedDocumentMessage.BuildItemMetadata(info))])
            };

            PublishAgentEventDomainEvent captured = null;
            _mediatorMock
                .Setup(x => x.Publish(It.IsAny<PublishAgentEventDomainEvent>(), It.IsAny<CancellationToken>()))
                .Callback<PublishAgentEventDomainEvent, CancellationToken>((e, _) => captured = e)
                .Returns(Task.CompletedTask);

            var targetsJson = JsonConvert.SerializeObject(new[] { new { agentName = "Refunds Bot" } });
            var fileNamesJson = JsonConvert.SerializeObject(new[] { new { fileName = "report.pdf", fileMimeType = "application/pdf" } });

            await plugin.PublishAgentEventAsync(targetsJson, "please help", "refund-requested", fileNamesJson, TestContext.Current.CancellationToken);

            var attachment = Assert.Single(captured.Files);
            Assert.Equal(info.DocumentId, attachment.SourceDocumentId);
            Assert.Null(attachment.FileData);
            Assert.Equal("A long annual report.", attachment.SourceDocument.Summary);
            Assert.Equal(1_600, attachment.SourceDocument.ChunkCount);
        }

        [Fact]
        public async Task PublishAgentEventAsync_FileNameMatchesButMimeTypeDoesNot_ReportsFileNotFoundWithoutFailingPublish()
        {
            var tenant = BuildTenant();
            var plugin = BuildPlugin(new Agent[]
            {
                new TenantAgent { Id = 5, Name = "Refunds Bot", Tenant = tenant, IsActive = true }
            },
            [Allow(5)]);

            _chatContext.CurrentChatHistory = new ChatHistory { BuildImageMessage("receipt.png", "image/png", data: [1, 2, 3]) };

            _mediatorMock
                .Setup(x => x.Publish(It.IsAny<PublishAgentEventDomainEvent>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var targetsJson = JsonConvert.SerializeObject(new[] { new { agentName = "Refunds Bot" } });
            var fileNamesJson = JsonConvert.SerializeObject(new[] { new { fileName = "receipt.png", fileMimeType = "image/jpeg" } });

            var result = await plugin.PublishAgentEventAsync(targetsJson, "please help", "refund-requested", fileNamesJson, TestContext.Current.CancellationToken);

            Assert.Contains("Refunds Bot", result);
            Assert.Contains("Files not found: receipt.png", result);
            _mediatorMock.Verify(x => x.Publish(
                It.Is<PublishAgentEventDomainEvent>(e => e.Files == null || e.Files.Count == 0),
                It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task PublishAgentEventAsync_FileNotInHistory_ReportsFileNotFoundWithoutFailingPublish()
        {
            var tenant = BuildTenant();
            var plugin = BuildPlugin(new Agent[]
            {
                new TenantAgent { Id = 5, Name = "Refunds Bot", Tenant = tenant, IsActive = true }
            },
            [Allow(5)]);

            _chatContext.CurrentChatHistory = new ChatHistory();

            _mediatorMock
                .Setup(x => x.Publish(It.IsAny<PublishAgentEventDomainEvent>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var targetsJson = JsonConvert.SerializeObject(new[] { new { agentName = "Refunds Bot" } });
            var fileNamesJson = JsonConvert.SerializeObject(new[] { new { fileName = "missing.png", fileMimeType = "image/png" } });

            var result = await plugin.PublishAgentEventAsync(targetsJson, "please help", "refund-requested", fileNamesJson, TestContext.Current.CancellationToken);

            Assert.Contains("Files not found: missing.png", result);
            _notificationServiceMock.Verify(x => x.SendToolExecutionUpdate(
                SessionId, "AgentEventPublisher", "failed", It.Is<string>(s => s.Contains("missing.png")), 500, It.IsAny<string>()),
                Times.Once);
        }

        [Fact]
        public async Task PublishAgentEventAsync_NoFileNamesJson_PublishesWithNullFilesAndNoFailureNoise()
        {
            var tenant = BuildTenant();
            var plugin = BuildPlugin(new Agent[]
            {
                new TenantAgent { Id = 5, Name = "Refunds Bot", Tenant = tenant, IsActive = true }
            },
            [Allow(5)]);

            PublishAgentEventDomainEvent captured = null;
            _mediatorMock
                .Setup(x => x.Publish(It.IsAny<PublishAgentEventDomainEvent>(), It.IsAny<CancellationToken>()))
                .Callback<PublishAgentEventDomainEvent, CancellationToken>((e, _) => captured = e)
                .Returns(Task.CompletedTask);

            var targetsJson = JsonConvert.SerializeObject(new[] { new { agentName = "Refunds Bot" } });

            var result = await plugin.PublishAgentEventAsync(targetsJson, "please help", "refund-requested", cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotNull(captured);
            Assert.Empty(captured.Files);
            Assert.DoesNotContain("Files not found", result);
        }

        private static (List<Agent> agents, List<AgentAllowedSpinUpAgent> allowed) KitAllowedForCaller()
        {
            var kit = new TenantAgent { Id = 7, Name = "Kit", IsActive = true, Tenant = new Tenant { Name = TenantName } };
            return ([kit], [new AgentAllowedSpinUpAgent { AgentId = CallingAgentId, AllowedSpinUpAgentId = 7 }]);
        }

        [Fact]
        public async Task PublishAgentEventAsync_SessionStoppedBeforePublishing_PublishesNothing()
        {
            // US 7501: the one read of the stop state finds the Stop; the turn is cancelled and nothing is handed off.
            var (agents, allowed) = KitAllowedForCaller();
            var checkpoint = new Mock<IChatStopCheckpoint>();
            checkpoint
                .Setup(x => x.ThrowIfStoppedAsync(TenantName, SessionId, ChatStopCheckpoints.HandOffPublish, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException());
            var plugin = BuildPlugin(agents, allowed, chatStopCheckpoint: checkpoint.Object);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                plugin.PublishAgentEventAsync("[{\"agentName\":\"Kit\"}]", "help", "escalation", cancellationToken: TestContext.Current.CancellationToken));

            _mediatorMock.Verify(x => x.Publish(It.IsAny<PublishAgentEventDomainEvent>(), It.IsAny<CancellationToken>()), Times.Never);
            checkpoint.Verify(x => x.ThrowIfStoppedAsync(TenantName, SessionId, ChatStopCheckpoints.HandOffPublish, It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>Review fix (thread 97618): the hand-off carries this turn's start and is published with this turn's Stop token.</summary>
        [Fact]
        public async Task PublishAgentEventAsync_PublishesWithTheTurnsStartAndStopToken()
        {
            var (agents, allowed) = KitAllowedForCaller();
            var turnStartedAt = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
            _chatContext.TurnStartedAt = turnStartedAt;
            using var turn = new CancellationTokenSource();
            _mediatorMock.Setup(x => x.Publish(It.IsAny<PublishAgentEventDomainEvent>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            await BuildPlugin(agents, allowed).PublishAgentEventAsync("[{\"agentName\":\"Kit\"}]", "help", "escalation", cancellationToken: turn.Token);

            _mediatorMock.Verify(x => x.Publish(It.Is<PublishAgentEventDomainEvent>(e => e.RequestedByTurnStartedAt == turnStartedAt), turn.Token), Times.Once);
        }

        [Fact]
        public async Task PublishAgentEventAsync_NoRegisteredTurn_PublishesWithoutATurnStart()
        {
            var (agents, allowed) = KitAllowedForCaller();
            _mediatorMock.Setup(x => x.Publish(It.IsAny<PublishAgentEventDomainEvent>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            await BuildPlugin(agents, allowed).PublishAgentEventAsync("[{\"agentName\":\"Kit\"}]", "help", "escalation", cancellationToken: TestContext.Current.CancellationToken);

            _mediatorMock.Verify(x => x.Publish(It.Is<PublishAgentEventDomainEvent>(e => e.RequestedByTurnStartedAt == null), It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>A Stop while the hand-off is being published ends the turn: rethrown, not reported as a failed hand-off.</summary>
        [Fact]
        public async Task PublishAgentEventAsync_StoppedDuringThePublish_PropagatesTheCancellation()
        {
            var (agents, allowed) = KitAllowedForCaller();
            using var turn = new CancellationTokenSource();
            _mediatorMock.Setup(x => x.Publish(It.IsAny<PublishAgentEventDomainEvent>(), It.IsAny<CancellationToken>()))
                .Returns<PublishAgentEventDomainEvent, CancellationToken>((_, ct) =>
                {
                    turn.Cancel();
                    return Task.FromCanceled(ct);
                });

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                BuildPlugin(agents, allowed).PublishAgentEventAsync("[{\"agentName\":\"Kit\"}]", "help", "escalation", cancellationToken: turn.Token));

            _notificationServiceMock.Verify(x => x.SendToolExecutionUpdate(It.IsAny<string>(), It.IsAny<string>(), "failed", It.IsAny<string>(),
                It.IsAny<int?>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task PublishAgentEventAsync_SessionNotStopped_ReadsTheStopStateOnceAndPublishes()
        {
            var (agents, allowed) = KitAllowedForCaller();
            var checkpoint = new Mock<IChatStopCheckpoint>();
            var plugin = BuildPlugin(agents, allowed, chatStopCheckpoint: checkpoint.Object);
            _mediatorMock.Setup(x => x.Publish(It.IsAny<PublishAgentEventDomainEvent>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            await plugin.PublishAgentEventAsync("[{\"agentName\":\"Kit\"}, {\"agentName\":\"Kit\"}]", "help", "escalation",
                cancellationToken: TestContext.Current.CancellationToken);

            checkpoint.Verify(x => x.ThrowIfStoppedAsync(TenantName, SessionId, ChatStopCheckpoints.HandOffPublish, It.IsAny<CancellationToken>()), Times.Once);
            _mediatorMock.Verify(x => x.Publish(It.IsAny<PublishAgentEventDomainEvent>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Fact]
        public async Task PublishAgentEventAsync_TurnAlreadyCancelled_PublishesNothing()
        {
            // US 7501: a Stop broadcast that already cancelled this turn stops the hand-offs without another read.
            var (agents, allowed) = KitAllowedForCaller();
            var plugin = BuildPlugin(agents, allowed);
            using var stopped = new CancellationTokenSource();
            stopped.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                plugin.PublishAgentEventAsync("[{\"agentName\":\"Kit\"}]", "help", "escalation", cancellationToken: stopped.Token));

            _mediatorMock.Verify(x => x.Publish(It.IsAny<PublishAgentEventDomainEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
