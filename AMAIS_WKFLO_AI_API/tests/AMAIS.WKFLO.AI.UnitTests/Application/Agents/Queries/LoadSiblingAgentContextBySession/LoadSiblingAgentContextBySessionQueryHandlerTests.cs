using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Queries.LoadSiblingAgentContextBySession;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Agents.Queries.LoadSiblingAgentContextBySession
{
    /// <summary>
    /// Covers US 7078 (agent-to-agent context awareness): the ephemeral sibling-agent context pull
    /// gated by RunnableAgent.EnableSpinUpAgents + AgentAllowedSpinUpAgent (story #6480's allowlist).
    /// </summary>
    public class LoadSiblingAgentContextBySessionQueryHandlerTests
    {
        private const string TenantName = "test-tenant";
        private const string SessionId = "sess-1";
        private const string UserGuid = "user-guid-1";
        private const int RootAgentId = 1;
        private const int ChildAgentId = 10;
        private static readonly DateTimeOffset BaseTimestamp = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        private readonly Mock<IChatStoreService> _chatStoreServiceMock = new();
        private readonly Mock<IBlobStorageProvider> _blobStorageProviderMock = new();

        private static Tenant BuildTenant() => new() { Id = 1, Name = TenantName };

        private static TenantAgent BuildChildAgent(int id, string name, bool isDeleted = false, bool isActive = true) =>
            new() { Id = id, Name = name, IsDeleted = isDeleted, IsActive = isActive, Tenant = BuildTenant() };

        private static TenantAgent BuildRootAgent(bool enableSpinUpAgents) =>
            new() { Id = RootAgentId, Name = "MAISY", EnableSpinUpAgents = enableSpinUpAgents, Tenant = BuildTenant() };

        private static ChatMessageContent BuildMessage(AuthorRole role, string text, DateTimeOffset timestamp,
            ChannelMessageType channelMessageType = ChannelMessageType.Normal, ImageContent image = null)
        {
            var items = new ChatMessageContentItemCollection();
            if (!string.IsNullOrEmpty(text))
                items.Add(new TextContent(text));
            if (image != null)
                items.Add(image);

            var metadata = new Dictionary<string, object>
            {
                { CommonMetadataConsts.Timestamp, timestamp },
                { CommonMetadataConsts.ChannelMessageType, channelMessageType }
            };

            return new ChatMessageContent(role, items, metadata: metadata);
        }

        private LoadSiblingAgentContextBySessionQueryHandler BuildHandler(
            IEnumerable<AgentAllowedSpinUpAgent> allowedSpinUpAgents,
            IEnumerable<Conversation> conversations)
        {
            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.AgentAllowedSpinUpAgents).Returns(new List<AgentAllowedSpinUpAgent>(allowedSpinUpAgents ?? []).BuildMockDbSet().Object);
            db.Setup(x => x.Conversations).Returns(new List<Conversation>(conversations ?? []).BuildMockDbSet().Object);

            return new LoadSiblingAgentContextBySessionQueryHandler(
                db.Object,
                _chatStoreServiceMock.Object,
                _blobStorageProviderMock.Object,
                new TenantContext { TenantName = TenantName },
                new UserContext { Guid = UserGuid });
        }

        // Handler that would throw if it ever touched the DB — used to prove the gate short-circuits
        // before any query is issued.
        private LoadSiblingAgentContextBySessionQueryHandler BuildHandlerWithNoDbSetup()
        {
            var db = new Mock<IMasterDataDbContext>(MockBehavior.Strict);
            return new LoadSiblingAgentContextBySessionQueryHandler(
                db.Object,
                _chatStoreServiceMock.Object,
                _blobStorageProviderMock.Object,
                new TenantContext { TenantName = TenantName },
                new UserContext { Guid = UserGuid });
        }

        private void SetupChildChat(int agentId, ChatHistory history)
        {
            _chatStoreServiceMock
                .Setup(x => x.LoadChatAsync(TenantName, agentId, SessionId, UserGuid))
                .ReturnsAsync((DateTimeOffset.UtcNow, (string)null, history, (Dictionary<string, string>)null));
        }

        [Fact]
        public async Task Handle_EnableSpinUpAgentsFalse_ReturnsEmptyAndNeverTouchesDb()
        {
            var handler = BuildHandlerWithNoDbSetup();
            var query = new LoadSiblingAgentContextBySessionQuery
            {
                Agent = BuildRootAgent(enableSpinUpAgents: false),
                SessionId = SessionId
            };

            var result = await handler.Handle(query, TestContext.Current.CancellationToken);

            Assert.Empty(result);
            _chatStoreServiceMock.Verify(x => x.LoadChatAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Handle_NoAllowedSpinUpAgents_ReturnsEmpty()
        {
            var handler = BuildHandler(allowedSpinUpAgents: [], conversations: []);
            var query = new LoadSiblingAgentContextBySessionQuery
            {
                Agent = BuildRootAgent(enableSpinUpAgents: true),
                SessionId = SessionId
            };

            var result = await handler.Handle(query, TestContext.Current.CancellationToken);

            Assert.Empty(result);
            _chatStoreServiceMock.Verify(x => x.LoadChatAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Handle_OnlyAuthorizedChildParticipated_PullsThatChildsMessagesOnly()
        {
            const int unrelatedAgentId = 99;
            var vera = BuildChildAgent(ChildAgentId, "Vera");

            var allowed = new[] { new AgentAllowedSpinUpAgent { AgentId = RootAgentId, AllowedSpinUpAgentId = ChildAgentId, AllowedSpinUpAgent = vera } };
            var conversations = new[]
            {
                new Conversation { AgentId = ChildAgentId, SessionId = SessionId, Tenant = BuildTenant() },
                new Conversation { AgentId = unrelatedAgentId, SessionId = SessionId, Tenant = BuildTenant() }
            };

            var handler = BuildHandler(allowed, conversations);

            var history = new ChatHistory { BuildMessage(AuthorRole.Assistant, "Here is your edited image", BaseTimestamp.AddMinutes(5)) };
            SetupChildChat(ChildAgentId, history);

            var query = new LoadSiblingAgentContextBySessionQuery
            {
                Agent = BuildRootAgent(enableSpinUpAgents: true),
                SessionId = SessionId
            };

            var result = await handler.Handle(query, TestContext.Current.CancellationToken);

            Assert.Single(result);
            _chatStoreServiceMock.Verify(x => x.LoadChatAsync(TenantName, ChildAgentId, SessionId, UserGuid), Times.Once);
            _chatStoreServiceMock.Verify(x => x.LoadChatAsync(TenantName, unrelatedAgentId, SessionId, UserGuid), Times.Never);
        }

        [Fact]
        public async Task Handle_ChildParticipatedButNotOnAllowlist_NeverPulled()
        {
            // Scope: the query must not treat "any other agent present in the session" as fair game —
            // only rows explicitly present in AgentAllowedSpinUpAgent for this root.
            var conversations = new[]
            {
                new Conversation { AgentId = ChildAgentId, SessionId = SessionId, Tenant = BuildTenant() }
            };

            var handler = BuildHandler(allowedSpinUpAgents: [], conversations: conversations);

            var query = new LoadSiblingAgentContextBySessionQuery
            {
                Agent = BuildRootAgent(enableSpinUpAgents: true),
                SessionId = SessionId
            };

            var result = await handler.Handle(query, TestContext.Current.CancellationToken);

            Assert.Empty(result);
            _chatStoreServiceMock.Verify(x => x.LoadChatAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Handle_DeletedAllowedChild_ExcludedFromScope()
        {
            var deletedChild = BuildChildAgent(ChildAgentId, "Vera", isDeleted: true);
            var allowed = new[] { new AgentAllowedSpinUpAgent { AgentId = RootAgentId, AllowedSpinUpAgentId = ChildAgentId, AllowedSpinUpAgent = deletedChild } };
            var conversations = new[] { new Conversation { AgentId = ChildAgentId, SessionId = SessionId, Tenant = BuildTenant() } };

            var handler = BuildHandler(allowed, conversations);

            var query = new LoadSiblingAgentContextBySessionQuery
            {
                Agent = BuildRootAgent(enableSpinUpAgents: true),
                SessionId = SessionId
            };

            var result = await handler.Handle(query, TestContext.Current.CancellationToken);

            Assert.Empty(result);
        }

        [Fact]
        public async Task Handle_InactiveAllowedChild_ExcludedFromScope()
        {
            // WI 7318 hardening: switching a child agent off must stop its history flowing into the root.
            var inactiveChild = BuildChildAgent(ChildAgentId, "Vera", isActive: false);
            var allowed = new[] { new AgentAllowedSpinUpAgent { AgentId = RootAgentId, AllowedSpinUpAgentId = ChildAgentId, AllowedSpinUpAgent = inactiveChild } };
            var conversations = new[] { new Conversation { AgentId = ChildAgentId, SessionId = SessionId, Tenant = BuildTenant() } };

            var handler = BuildHandler(allowed, conversations);
            SetupChildChat(ChildAgentId, new ChatHistory { BuildMessage(AuthorRole.Assistant, "Here is your edited image", BaseTimestamp.AddMinutes(5)) });

            var query = new LoadSiblingAgentContextBySessionQuery
            {
                Agent = BuildRootAgent(enableSpinUpAgents: true),
                SessionId = SessionId
            };

            var result = await handler.Handle(query, TestContext.Current.CancellationToken);

            Assert.Empty(result);
            _chatStoreServiceMock.Verify(x => x.LoadChatAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Handle_AllInjectableChildMessages_AreIncludedRegardlessOfAge()
        {
            // No watermark: every injectable message the child ever produced in this session comes
            // back, not just what's new since the root's own last turn.
            var vera = BuildChildAgent(ChildAgentId, "Vera");
            var allowed = new[] { new AgentAllowedSpinUpAgent { AgentId = RootAgentId, AllowedSpinUpAgentId = ChildAgentId, AllowedSpinUpAgent = vera } };
            var conversations = new[] { new Conversation { AgentId = ChildAgentId, SessionId = SessionId, Tenant = BuildTenant() } };

            var handler = BuildHandler(allowed, conversations);

            var history = new ChatHistory
            {
                BuildMessage(AuthorRole.Assistant, "much older reply", BaseTimestamp.AddDays(-1)),
                BuildMessage(AuthorRole.Assistant, "an earlier reply", BaseTimestamp.AddMinutes(-5)),
                BuildMessage(AuthorRole.Assistant, "the most recent reply", BaseTimestamp.AddMinutes(5))
            };
            SetupChildChat(ChildAgentId, history);

            var query = new LoadSiblingAgentContextBySessionQuery
            {
                Agent = BuildRootAgent(enableSpinUpAgents: true),
                SessionId = SessionId
            };

            var result = await handler.Handle(query, TestContext.Current.CancellationToken);

            Assert.Equal(3, result.Count);
            Assert.Contains(result, m => m.Content.Contains("much older reply"));
            Assert.Contains(result, m => m.Content.Contains("an earlier reply"));
            Assert.Contains(result, m => m.Content.Contains("the most recent reply"));
        }

        [Fact]
        public async Task Handle_ChildsOwnUserMessage_NeverInjected()
        {
            var vera = BuildChildAgent(ChildAgentId, "Vera");
            var allowed = new[] { new AgentAllowedSpinUpAgent { AgentId = RootAgentId, AllowedSpinUpAgentId = ChildAgentId, AllowedSpinUpAgent = vera } };
            var conversations = new[] { new Conversation { AgentId = ChildAgentId, SessionId = SessionId, Tenant = BuildTenant() } };

            var handler = BuildHandler(allowed, conversations);

            var history = new ChatHistory
            {
                BuildMessage(AuthorRole.User, "MAISY's hand-off message to Vera", BaseTimestamp.AddMinutes(1)),
                BuildMessage(AuthorRole.Assistant, "Vera's real reply", BaseTimestamp.AddMinutes(2))
            };
            SetupChildChat(ChildAgentId, history);

            var query = new LoadSiblingAgentContextBySessionQuery
            {
                Agent = BuildRootAgent(enableSpinUpAgents: true),
                SessionId = SessionId
            };

            var result = await handler.Handle(query, TestContext.Current.CancellationToken);

            var injected = Assert.Single(result);
            Assert.DoesNotContain("hand-off", injected.Content);
            Assert.Contains("Vera's real reply", injected.Content);
        }

        [Fact]
        public async Task Handle_ReasoningThinkingMessages_AreDropped()
        {
            var vera = BuildChildAgent(ChildAgentId, "Vera");
            var allowed = new[] { new AgentAllowedSpinUpAgent { AgentId = RootAgentId, AllowedSpinUpAgentId = ChildAgentId, AllowedSpinUpAgent = vera } };
            var conversations = new[] { new Conversation { AgentId = ChildAgentId, SessionId = SessionId, Tenant = BuildTenant() } };

            var handler = BuildHandler(allowed, conversations);

            var history = new ChatHistory
            {
                BuildMessage(AuthorRole.System, "internal scratch reasoning", BaseTimestamp.AddMinutes(1), ChannelMessageType.Thinking),
                BuildMessage(AuthorRole.Assistant, "Vera's real reply", BaseTimestamp.AddMinutes(2))
            };
            SetupChildChat(ChildAgentId, history);

            var query = new LoadSiblingAgentContextBySessionQuery
            {
                Agent = BuildRootAgent(enableSpinUpAgents: true),
                SessionId = SessionId
            };

            var result = await handler.Handle(query, TestContext.Current.CancellationToken);

            var injected = Assert.Single(result);
            Assert.DoesNotContain("scratch reasoning", injected.Content);
        }

        [Fact]
        public async Task Handle_InternalSystemInstructionMessage_ChannelTypeNormal_NeverInjected()
        {
            // Bug 7144: a System-role message with ChannelMessageType.Normal is an internal
            // instruction record (e.g. the pinned chatHistory[0] context message built from
            // PageData/TextFeature/GalleryFeature/Knowledge auto-invoke sources, or a
            // TopicHandoffMessage — see SemanticKernelExtensions/UpsertSystemMessageSection), not a
            // legacy generated-image record. Only System + ChannelMessageType.Metadata (the actual
            // legacy image convention, covered by the sibling test below) may be injected.
            var vera = BuildChildAgent(ChildAgentId, "Vera");
            var allowed = new[] { new AgentAllowedSpinUpAgent { AgentId = RootAgentId, AllowedSpinUpAgentId = ChildAgentId, AllowedSpinUpAgent = vera } };
            var conversations = new[] { new Conversation { AgentId = ChildAgentId, SessionId = SessionId, Tenant = BuildTenant() } };

            var handler = BuildHandler(allowed, conversations);

            var history = new ChatHistory
            {
                BuildMessage(AuthorRole.System, "internal pinned instruction context", BaseTimestamp.AddMinutes(1), ChannelMessageType.Normal),
                BuildMessage(AuthorRole.Assistant, "Vera's real reply", BaseTimestamp.AddMinutes(2))
            };
            SetupChildChat(ChildAgentId, history);

            var query = new LoadSiblingAgentContextBySessionQuery
            {
                Agent = BuildRootAgent(enableSpinUpAgents: true),
                SessionId = SessionId
            };

            var result = await handler.Handle(query, TestContext.Current.CancellationToken);

            var injected = Assert.Single(result);
            Assert.DoesNotContain("internal pinned instruction context", injected.Content);
            Assert.Contains("Vera's real reply", injected.Content);
        }

        [Fact]
        public async Task Handle_GeneratedImageRecord_LegacySystemRoleWithMetadataChannel_IsIncluded()
        {
            // Backward compatibility: images generated before the mid-conversation-system-message fix
            // may still be stored as Role == System with ChannelMessageType.Metadata.
            var vera = BuildChildAgent(ChildAgentId, "Vera");
            var allowed = new[] { new AgentAllowedSpinUpAgent { AgentId = RootAgentId, AllowedSpinUpAgentId = ChildAgentId, AllowedSpinUpAgent = vera } };
            var conversations = new[] { new Conversation { AgentId = ChildAgentId, SessionId = SessionId, Tenant = BuildTenant() } };

            var handler = BuildHandler(allowed, conversations);

            var staleBytes = Encoding.UTF8.GetBytes("stale-cached-bytes");
            var freshBytes = Encoding.UTF8.GetBytes("fresh-bytes-from-blob");
            var imageContent = new ImageContent { Uri = new Uri("https://account.blob.core.windows.net/chat-images/tenant/some-image.png") };

            var history = new ChatHistory
            {
                BuildMessage(AuthorRole.System, "FileName: 'edited.png'", BaseTimestamp.AddMinutes(1), ChannelMessageType.Metadata, imageContent)
            };
            SetupChildChat(ChildAgentId, history);

            _blobStorageProviderMock
                .Setup(x => x.DownloadAsync("chat-images", "tenant/some-image.png", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new System.IO.MemoryStream(freshBytes));

            var query = new LoadSiblingAgentContextBySessionQuery
            {
                Agent = BuildRootAgent(enableSpinUpAgents: true),
                SessionId = SessionId
            };

            var result = await handler.Handle(query, TestContext.Current.CancellationToken);

            var injected = Assert.Single(result);
            var injectedImage = injected.Items.OfType<ImageContent>().Single();
            Assert.True(injectedImage.Data.HasValue);
            Assert.Equal(freshBytes, injectedImage.Data.Value.ToArray());
            Assert.NotEqual(staleBytes, injectedImage.Data.Value.ToArray());
        }

        [Fact]
        public async Task Handle_GeneratedImageRecord_CurrentUserRoleWithMetadataChannel_IsIncluded()
        {
            // The primary use case this story exists for, current convention: Vera's generated/edited
            // images ride as Role == User with ChannelMessageType.Metadata (see
            // ImageGenerationPlugin.BuildImageMessage — Anthropic rejects an 'image' content block
            // inside an assistant turn outright, so User is the role, not Assistant or System).
            var vera = BuildChildAgent(ChildAgentId, "Vera");
            var allowed = new[] { new AgentAllowedSpinUpAgent { AgentId = RootAgentId, AllowedSpinUpAgentId = ChildAgentId, AllowedSpinUpAgent = vera } };
            var conversations = new[] { new Conversation { AgentId = ChildAgentId, SessionId = SessionId, Tenant = BuildTenant() } };

            var handler = BuildHandler(allowed, conversations);

            var freshBytes = Encoding.UTF8.GetBytes("fresh-bytes-from-blob");
            var imageContent = new ImageContent { Uri = new Uri("https://account.blob.core.windows.net/chat-images/tenant/some-image.png") };

            var history = new ChatHistory
            {
                BuildMessage(AuthorRole.User, "FileName: 'edited.png'", BaseTimestamp.AddMinutes(1), ChannelMessageType.Metadata, imageContent)
            };
            SetupChildChat(ChildAgentId, history);

            _blobStorageProviderMock
                .Setup(x => x.DownloadAsync("chat-images", "tenant/some-image.png", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new System.IO.MemoryStream(freshBytes));

            var query = new LoadSiblingAgentContextBySessionQuery
            {
                Agent = BuildRootAgent(enableSpinUpAgents: true),
                SessionId = SessionId
            };

            var result = await handler.Handle(query, TestContext.Current.CancellationToken);

            var injected = Assert.Single(result);
            Assert.Equal(freshBytes, injected.Items.OfType<ImageContent>().Single().Data.Value.ToArray());
        }

        [Fact]
        public async Task Handle_ImageBytes_MimeTypeSniffedFromContent_NotBlindlyDefaultedToPng()
        {
            // ImageContent.MimeType is null here — LoadChatAsync reconstructs it from stored blob JSON
            // with only a Uri, never a MimeType. Real bytes are JPEG; must not come back as image/png.
            var vera = BuildChildAgent(ChildAgentId, "Vera");
            var allowed = new[] { new AgentAllowedSpinUpAgent { AgentId = RootAgentId, AllowedSpinUpAgentId = ChildAgentId, AllowedSpinUpAgent = vera } };
            var conversations = new[] { new Conversation { AgentId = ChildAgentId, SessionId = SessionId, Tenant = BuildTenant() } };

            var handler = BuildHandler(allowed, conversations);

            byte[] jpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46];
            var imageContent = new ImageContent { Uri = new Uri("https://account.blob.core.windows.net/chat-images/tenant/photo.jpg") };

            var history = new ChatHistory
            {
                BuildMessage(AuthorRole.User, "FileName: 'photo.jpg'", BaseTimestamp.AddMinutes(1), ChannelMessageType.Metadata, imageContent)
            };
            SetupChildChat(ChildAgentId, history);

            _blobStorageProviderMock
                .Setup(x => x.DownloadAsync("chat-images", "tenant/photo.jpg", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new System.IO.MemoryStream(jpegBytes));

            var query = new LoadSiblingAgentContextBySessionQuery
            {
                Agent = BuildRootAgent(enableSpinUpAgents: true),
                SessionId = SessionId
            };

            var result = await handler.Handle(query, TestContext.Current.CancellationToken);

            var injected = Assert.Single(result);
            Assert.Equal("image/jpeg", injected.Items.OfType<ImageContent>().Single().MimeType);
        }

        [Fact]
        public async Task Handle_InjectedMessage_PreservesSourceMessageMetadata_ForDownstreamPluginLookup()
        {
            // GalleryFeaturePlugin/PagePlugin/PostPlugin all resolve "the image in chat history" by
            // matching FileName + FileType in a message's metadata against _chatContext.CurrentChatHistory
            // — which includes this injected message for the turn. Dropping the source message's own
            // metadata down to just IsEphemeralAgentContext + Timestamp would make an image the child
            // generated invisible to those plugins even though it's sitting right there in the context.
            var vera = BuildChildAgent(ChildAgentId, "Vera");
            var allowed = new[] { new AgentAllowedSpinUpAgent { AgentId = RootAgentId, AllowedSpinUpAgentId = ChildAgentId, AllowedSpinUpAgent = vera } };
            var conversations = new[] { new Conversation { AgentId = ChildAgentId, SessionId = SessionId, Tenant = BuildTenant() } };

            var handler = BuildHandler(allowed, conversations);

            var freshBytes = Encoding.UTF8.GetBytes("fresh-bytes-from-blob");
            var imageContent = new ImageContent { Uri = new Uri("https://account.blob.core.windows.net/chat-images/tenant/edited.png") };
            var sourceMessage = new ChatMessageContent(AuthorRole.User,
                new ChatMessageContentItemCollection { new TextContent("FileName: 'edited.png'"), imageContent },
                metadata: new Dictionary<string, object>
                {
                    { CommonMetadataConsts.Timestamp, BaseTimestamp.AddMinutes(1) },
                    { CommonMetadataConsts.ChannelMessageType, ChannelMessageType.Metadata },
                    { CommonMetadataConsts.FileName, "edited.png" },
                    { CommonMetadataConsts.FileType, "image/png" },
                    { CommonMetadataConsts.MessageId, "source-message-id" },
                    { CommonMetadataConsts.RenderAsSystem, true }
                });

            var history = new ChatHistory { sourceMessage };
            SetupChildChat(ChildAgentId, history);

            _blobStorageProviderMock
                .Setup(x => x.DownloadAsync("chat-images", "tenant/edited.png", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new System.IO.MemoryStream(freshBytes));

            var query = new LoadSiblingAgentContextBySessionQuery
            {
                Agent = BuildRootAgent(enableSpinUpAgents: true),
                SessionId = SessionId
            };

            var result = await handler.Handle(query, TestContext.Current.CancellationToken);

            var injected = Assert.Single(result);
            Assert.Equal("edited.png", injected.Metadata[CommonMetadataConsts.FileName]);
            Assert.Equal("image/png", injected.Metadata[CommonMetadataConsts.FileType]);
            Assert.Equal("source-message-id", injected.Metadata[CommonMetadataConsts.MessageId]);
            Assert.True((bool)injected.Metadata[CommonMetadataConsts.RenderAsSystem]);
            // Still set/overwritten on top of whatever the source carried.
            Assert.True((bool)injected.Metadata[CommonMetadataConsts.IsEphemeralAgentContext]);
        }

        [Fact]
        public async Task Handle_RealUserUploadedImage_UserRoleWithoutMetadataChannel_NeverInjected()
        {
            // An image the real end user uploaded directly to the child (not one the child generated)
            // has no ChannelMessageType.Metadata — must be excluded, same as the child's own inbound
            // trigger message. This story is about surfacing the child's own output, not re-surfacing
            // whatever the human already uploaded to it themselves.
            var vera = BuildChildAgent(ChildAgentId, "Vera");
            var allowed = new[] { new AgentAllowedSpinUpAgent { AgentId = RootAgentId, AllowedSpinUpAgentId = ChildAgentId, AllowedSpinUpAgent = vera } };
            var conversations = new[] { new Conversation { AgentId = ChildAgentId, SessionId = SessionId, Tenant = BuildTenant() } };

            var handler = BuildHandler(allowed, conversations);

            var imageContent = new ImageContent { Uri = new Uri("https://account.blob.core.windows.net/chat-images/tenant/user-uploaded.png") };
            var history = new ChatHistory
            {
                BuildMessage(AuthorRole.User, "FileName: 'user-uploaded.png'", BaseTimestamp.AddMinutes(1), ChannelMessageType.Normal, imageContent)
            };
            SetupChildChat(ChildAgentId, history);

            var query = new LoadSiblingAgentContextBySessionQuery
            {
                Agent = BuildRootAgent(enableSpinUpAgents: true),
                SessionId = SessionId
            };

            var result = await handler.Handle(query, TestContext.Current.CancellationToken);

            Assert.Empty(result);
        }

        [Fact]
        public async Task Handle_InjectedMessage_IsAttributedToTheSourceAgent()
        {
            var vera = BuildChildAgent(ChildAgentId, "Vera");
            var allowed = new[] { new AgentAllowedSpinUpAgent { AgentId = RootAgentId, AllowedSpinUpAgentId = ChildAgentId, AllowedSpinUpAgent = vera } };
            var conversations = new[] { new Conversation { AgentId = ChildAgentId, SessionId = SessionId, Tenant = BuildTenant() } };

            var handler = BuildHandler(allowed, conversations);

            var history = new ChatHistory { BuildMessage(AuthorRole.Assistant, "I edited the logo for you", BaseTimestamp.AddMinutes(1)) };
            SetupChildChat(ChildAgentId, history);

            var query = new LoadSiblingAgentContextBySessionQuery
            {
                Agent = BuildRootAgent(enableSpinUpAgents: true),
                SessionId = SessionId
            };

            var result = await handler.Handle(query, TestContext.Current.CancellationToken);

            var injected = Assert.Single(result);
            Assert.Contains("Vera", injected.Content);
            Assert.Equal(AuthorRole.User, injected.Role);
            Assert.True((bool)injected.Metadata[CommonMetadataConsts.IsEphemeralAgentContext]);
        }

        /// <summary>US 7502: a child's "Stopped" divider is not its output and must never reach the root agent's model.</summary>
        [Fact]
        public async Task Handle_ChildHistoryWithAStoppedMarker_KeepsItsMessagesButNeverInjectsTheMarker()
        {
            var kit = BuildChildAgent(ChildAgentId, "Kit");
            var allowed = new[] { new AgentAllowedSpinUpAgent { AgentId = RootAgentId, AllowedSpinUpAgentId = ChildAgentId, AllowedSpinUpAgent = kit } };
            var conversations = new[] { new Conversation { AgentId = ChildAgentId, SessionId = SessionId, Tenant = BuildTenant() } };
            var handler = BuildHandler(allowed, conversations);

            var history = System.Text.Json.JsonSerializer.Deserialize<ChatHistory>(System.Text.Json.JsonSerializer.Serialize(new ChatHistory
            {
                BuildMessage(AuthorRole.Assistant, "Here is the first part of the", BaseTimestamp.AddMinutes(1)),
                StoppedTurnMarker.Build(Guid.NewGuid().ToString(), BaseTimestamp.AddMinutes(1).AddMilliseconds(1), BaseTimestamp.AddMinutes(1))
            }));
            SetupChildChat(ChildAgentId, history);

            var result = await handler.Handle(new LoadSiblingAgentContextBySessionQuery
            {
                Agent = BuildRootAgent(enableSpinUpAgents: true),
                SessionId = SessionId
            }, TestContext.Current.CancellationToken);

            var injected = Assert.Single(result);
            Assert.Contains("Here is the first part of the", injected.Content);
            Assert.DoesNotContain(StoppedTurnMarker.Text, injected.Content);
        }
    }
}
