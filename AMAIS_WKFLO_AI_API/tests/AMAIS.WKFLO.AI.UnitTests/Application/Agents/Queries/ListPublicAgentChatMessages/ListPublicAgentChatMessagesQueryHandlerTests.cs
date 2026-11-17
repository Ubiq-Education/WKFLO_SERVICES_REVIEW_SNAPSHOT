using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.AI.Application.Agents.Queries.ListPublicAgentChatMessages;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Agents.Queries.ListPublicAgentChatMessages
{
    public class ListPublicAgentChatMessagesQueryHandlerTests
    {
        private readonly Mock<IMasterDataDbContext> _dbMock;
        private readonly Mock<IChatStoreService> _chatStoreServiceMock;
        private readonly ListPublicAgentChatMessagesQueryHandler _handler;

        private const string TenantName = "TestTenant";
        private const string ShareId = "share-001";
        private const string SessionId = "session-001";

        public ListPublicAgentChatMessagesQueryHandlerTests()
        {
            var mapper = WkfloMapperExtensions.CreateMapper();

            _dbMock = new Mock<IMasterDataDbContext>();
            _chatStoreServiceMock = new Mock<IChatStoreService>();

            _handler = new ListPublicAgentChatMessagesQueryHandler(
                _dbMock.Object,
                mapper,
                _chatStoreServiceMock.Object,
                new TenantContext { TenantName = TenantName },
                new UserContext { Guid = Guid.Empty.ToString() });
        }

        [Fact]
        public async Task Handle_AgentNotFound_ReturnsEmptyList()
        {
            // Arrange
            _dbMock.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting>().BuildMockDbSet().Object);

            var query = new ListPublicAgentChatMessagesQuery { ShareId = "unknown-share-id", SessionId = SessionId };

            // Act
            var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

            // Assert
            var success = Assert.IsType<SuccessResult<List<PublicAgentChatMessageResult>>>(result);
            Assert.Empty(success.Data);
            _chatStoreServiceMock.Verify(x => x.LoadChatAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Handle_WithNoRatings_ReturnsMessagesWithNullRating()
        {
            // Setup mocked DateTimeProvider
            var DateTimeProviderMock = new Mock<IDateTimeProvider>();
            var fixedDate = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            DateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(fixedDate);

            // Arrange
            var tenant = new Tenant { Id = 1, Name = TenantName };
            var agent = new PublishedTenantAgent { Id = 10, Name = "Support Bot", IsDeleted = false, IsActive = true, Tenant = tenant };
            var chatSetting = new AgentChatSetting { ShareId = ShareId, IsDeleted = false, Agent = agent, AgentId = agent.Id };

            _dbMock.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting> { chatSetting }.BuildMockDbSet().Object);

            var chatHistory = new ChatHistory();
            chatHistory.Add(new ChatMessageContent(AuthorRole.Assistant, "Hello",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = Guid.NewGuid().ToString() }));

            _chatStoreServiceMock
                .Setup(x => x.LoadChatAsync(TenantName, agent.Id, SessionId, Guid.Empty.ToString()))
                .ReturnsAsync((DateTimeProviderMock.Object.DateTimeOffsetUtcNow(), "Session 1", chatHistory, (Dictionary<string, string>)null));

            var query = new ListPublicAgentChatMessagesQuery { ShareId = ShareId, SessionId = SessionId };

            // Act
            var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

            // Assert
            var success = Assert.IsType<SuccessResult<List<PublicAgentChatMessageResult>>>(result);
            Assert.Single(success.Data);
            Assert.Null(success.Data[0].Rating);
            Assert.Equal(agent.Id, success.Data[0].AgentId);
            Assert.Equal(agent.Name, success.Data[0].AgentName);
        }

        [Fact]
        public async Task Handle_WithThumbsUpRating_AppliesRatingToMatchingMessage()
        {
            // Setup mocked DateTimeProvider
            var DateTimeProviderMock = new Mock<IDateTimeProvider>();
            var fixedDate = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            DateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(fixedDate);

            // Arrange
            var tenant = new Tenant { Id = 1, Name = TenantName };
            var agent = new PublishedTenantAgent { Id = 10, Name = "Support Bot", IsDeleted = false, IsActive = true, Tenant = tenant };
            var chatSetting = new AgentChatSetting { ShareId = ShareId, IsDeleted = false, Agent = agent, AgentId = agent.Id };

            _dbMock.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting> { chatSetting }.BuildMockDbSet().Object);

            var messageId = Guid.NewGuid().ToString();
            var chatHistory = new ChatHistory();
            chatHistory.Add(new ChatMessageContent(AuthorRole.Assistant, "Hello",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = messageId }));

            var ratings = new Dictionary<string, string> { [messageId] = "thumbsUp" };

            _chatStoreServiceMock
                .Setup(x => x.LoadChatAsync(TenantName, agent.Id, SessionId, Guid.Empty.ToString()))
                .ReturnsAsync((DateTimeProviderMock.Object.DateTimeOffsetUtcNow(), "Session 1", chatHistory, ratings));

            var query = new ListPublicAgentChatMessagesQuery { ShareId = ShareId, SessionId = SessionId };

            // Act
            var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

            // Assert
            var success = Assert.IsType<SuccessResult<List<PublicAgentChatMessageResult>>>(result);
            Assert.Single(success.Data);
            Assert.Equal("thumbsUp", success.Data[0].Rating);
            Assert.Equal(agent.Id, success.Data[0].AgentId);
            Assert.Equal(agent.Name, success.Data[0].AgentName);
        }
    }
}
