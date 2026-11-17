using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.AI.Application.Agents.Queries.ListTenantAgentChatMessages;
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

namespace AMAIS.WKFLO.AI.UnitTests.Application.Agents.Queries.ListTenantAgentChatMessages
{
    public class ListTenantAgentChatMessagesQueryHandlerTests
    {
        private readonly Mock<IMasterDataDbContext> _dbMock;
        private readonly Mock<IChatStoreService> _chatStoreServiceMock;
        private readonly ListTenantAgentChatMessagesQueryHandler _handler;

        private const string TenantName = "TestTenant";
        private const int AgentId = 10;
        private const string AgentName = "Support Bot";
        private const string SessionId = "session-001";

        public ListTenantAgentChatMessagesQueryHandlerTests()
        {
            var mapper = WkfloMapperExtensions.CreateMapper();

            _dbMock = new Mock<IMasterDataDbContext>();
            _chatStoreServiceMock = new Mock<IChatStoreService>();

            var agent = new TenantAgent { Id = AgentId, Name = AgentName };
            _dbMock.Setup(x => x.Agents).Returns(new List<Agent> { agent }.BuildMockDbSet().Object);

            _handler = new ListTenantAgentChatMessagesQueryHandler(
                _dbMock.Object,
                mapper,
                _chatStoreServiceMock.Object,
                new TenantContext { TenantName = TenantName },
                new UserContext { Guid = Guid.Empty.ToString() });
        }

        [Fact]
        public async Task Handle_WithNoRatings_ReturnsMessagesWithNullRating()
        {
            // Setup mocked DateTimeProvider
            var DateTimeProviderMock = new Mock<IDateTimeProvider>();
            var fixedDate = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            DateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(fixedDate);

            // Arrange
            var chatHistory = new ChatHistory();
            chatHistory.Add(new ChatMessageContent(AuthorRole.Assistant, "Hello",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = Guid.NewGuid().ToString() }));

            _chatStoreServiceMock
                .Setup(x => x.LoadChatAsync(TenantName, AgentId, SessionId, Guid.Empty.ToString()))
                .ReturnsAsync((DateTimeProviderMock.Object.DateTimeOffsetUtcNow(), "Session 1", chatHistory, (Dictionary<string, string>)null));

            var query = new ListTenantAgentChatMessagesQuery { Id = AgentId, SessionId = SessionId };

            // Act
            var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

            // Assert
            var success = Assert.IsType<SuccessResult<List<TenantAgentChatMessageResult>>>(result);
            Assert.Single(success.Data);
            Assert.Null(success.Data[0].Rating);
            Assert.Equal(AgentId, success.Data[0].AgentId);
            Assert.Equal(AgentName, success.Data[0].AgentName);
        }

        [Fact]
        public async Task Handle_WithThumbsUpRating_AppliesRatingToMatchingMessage()
        {
            // Setup mocked DateTimeProvider
            var DateTimeProviderMock = new Mock<IDateTimeProvider>();
            var fixedDate = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            DateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(fixedDate);

            // Arrange
            var messageId = Guid.NewGuid().ToString();
            var chatHistory = new ChatHistory();
            chatHistory.Add(new ChatMessageContent(AuthorRole.Assistant, "Hello",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = messageId }));

            var ratings = new Dictionary<string, string> { [messageId] = "thumbsUp" };

            _chatStoreServiceMock
                .Setup(x => x.LoadChatAsync(TenantName, AgentId, SessionId, Guid.Empty.ToString()))
                .ReturnsAsync((DateTimeProviderMock.Object.DateTimeOffsetUtcNow(), "Session 1", chatHistory, ratings));

            var query = new ListTenantAgentChatMessagesQuery { Id = AgentId, SessionId = SessionId };

            // Act
            var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

            // Assert
            var success = Assert.IsType<SuccessResult<List<TenantAgentChatMessageResult>>>(result);
            Assert.Single(success.Data);
            Assert.Equal("thumbsUp", success.Data[0].Rating);
            Assert.Equal(AgentId, success.Data[0].AgentId);
            Assert.Equal(AgentName, success.Data[0].AgentName);
        }

        [Fact]
        public async Task Handle_WithThumbsDownRating_AppliesRatingToMatchingMessage()
        {
            // Setup mocked DateTimeProvider
            var DateTimeProviderMock = new Mock<IDateTimeProvider>();
            var fixedDate = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            DateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(fixedDate);

            // Arrange
            var messageId = Guid.NewGuid().ToString();
            var chatHistory = new ChatHistory();
            chatHistory.Add(new ChatMessageContent(AuthorRole.Assistant, "Hello",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = messageId }));

            var ratings = new Dictionary<string, string> { [messageId] = "thumbsDown" };

            _chatStoreServiceMock
                .Setup(x => x.LoadChatAsync(TenantName, AgentId, SessionId, Guid.Empty.ToString()))
                .ReturnsAsync((DateTimeProviderMock.Object.DateTimeOffsetUtcNow(), "Session 1", chatHistory, ratings));

            var query = new ListTenantAgentChatMessagesQuery { Id = AgentId, SessionId = SessionId };

            // Act
            var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

            // Assert
            var success = Assert.IsType<SuccessResult<List<TenantAgentChatMessageResult>>>(result);
            Assert.Single(success.Data);
            Assert.Equal("thumbsDown", success.Data[0].Rating);
            Assert.Equal(AgentId, success.Data[0].AgentId);
            Assert.Equal(AgentName, success.Data[0].AgentName);
        }
    }
}
