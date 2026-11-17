using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.SyncConversation;
using System;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Commands.SyncConversation
{
    public class SyncConversationCommandHandlerTests : IClassFixture<SyncConversationCommandHandlerTesting>
    {
        private readonly SyncConversationCommandHandlerTesting _testing;

        public SyncConversationCommandHandlerTests(SyncConversationCommandHandlerTesting testing)
        {
            _testing = testing;
        }

        [Theory]
        [InlineData("new-session", "New Conversation", ResultType.Ok)]
        [InlineData(SyncConversationCommandHandlerTesting.ExistingSessionId, "Updated Name", ResultType.Ok)]
        public async Task TestHandler_Ok(string sessionId, string name, ResultType resultType)
        {
            // Act
            var result = await _testing.SyncConversationCommandHandler.Handle(new SyncConversationCommand
            {
                AgentId = SyncConversationCommandHandlerTesting.AgentId,
                TenantName = SyncConversationCommandHandlerTesting.ExistingTenantName,
                SessionId = sessionId,
                Name = name
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == resultType);
        }

        [Fact]
        public async Task TestHandler_NewConversation_PersistsSubjectId()
        {
            // Act
            await _testing.SyncConversationCommandHandler.Handle(new SyncConversationCommand
            {
                AgentId = SyncConversationCommandHandlerTesting.AgentId,
                TenantName = SyncConversationCommandHandlerTesting.ExistingTenantName,
                SessionId = "brand-new-session",
                Name = "New Conversation",
                SubjectId = "subj-new-1"
            }, TestContext.Current.CancellationToken);

            // Assert
            var created = Assert.Single(_testing.Conversations, c => c.SessionId == "brand-new-session");
            Assert.Equal("subj-new-1", created.SubjectId);
        }

        [Fact]
        public async Task TestHandler_ExistingConversation_UpdatesSubjectIdWhenProvided()
        {
            // Act
            await _testing.SyncConversationCommandHandler.Handle(new SyncConversationCommand
            {
                AgentId = SyncConversationCommandHandlerTesting.AgentId,
                TenantName = SyncConversationCommandHandlerTesting.ExistingTenantName,
                SessionId = SyncConversationCommandHandlerTesting.ExistingSessionId,
                Name = "Updated Name",
                SubjectId = "subj-updated"
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal("subj-updated", _testing.ExistingConversation.SubjectId);
        }

        [Fact]
        public async Task TestHandler_ExistingConversation_EmptySubjectIdDoesNotClearExistingValue()
        {
            // Arrange
            _testing.ExistingConversation.SubjectId = "subj-keep-me";

            // Act
            await _testing.SyncConversationCommandHandler.Handle(new SyncConversationCommand
            {
                AgentId = SyncConversationCommandHandlerTesting.AgentId,
                TenantName = SyncConversationCommandHandlerTesting.ExistingTenantName,
                SessionId = SyncConversationCommandHandlerTesting.ExistingSessionId,
                Name = "Updated Name",
                SubjectId = string.Empty
            }, TestContext.Current.CancellationToken);

            // Assert — self-healing update must not blank out a previously-known SubjectId
            Assert.Equal("subj-keep-me", _testing.ExistingConversation.SubjectId);
        }

        [Fact]
        public async Task TestHandler_NewConversation_PersistsIsTestChat()
        {
            // Act
            await _testing.SyncConversationCommandHandler.Handle(new SyncConversationCommand
            {
                AgentId = SyncConversationCommandHandlerTesting.AgentId,
                TenantName = SyncConversationCommandHandlerTesting.ExistingTenantName,
                SessionId = "brand-new-test-chat-session",
                Name = "New Test Chat",
                IsTestChat = true
            }, TestContext.Current.CancellationToken);

            // Assert
            var created = Assert.Single(_testing.Conversations, c => c.SessionId == "brand-new-test-chat-session");
            Assert.True(created.IsTestChat);
        }

        [Fact]
        public async Task TestHandler_ExistingConversation_SetsIsTestChatWhenTrue()
        {
            // Arrange
            _testing.ExistingConversation.IsTestChat = false;

            // Act
            await _testing.SyncConversationCommandHandler.Handle(new SyncConversationCommand
            {
                AgentId = SyncConversationCommandHandlerTesting.AgentId,
                TenantName = SyncConversationCommandHandlerTesting.ExistingTenantName,
                SessionId = SyncConversationCommandHandlerTesting.ExistingSessionId,
                Name = "Updated Name",
                IsTestChat = true
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(_testing.ExistingConversation.IsTestChat);
        }

        [Fact]
        public async Task TestHandler_ExistingConversation_IsTestChatNeverRevertsToFalse()
        {
            // Arrange — conversation was already flagged as a test chat on a previous sync
            _testing.ExistingConversation.IsTestChat = true;

            // Act — a later sync does not carry the test-chat signal (e.g. header absent on this call)
            await _testing.SyncConversationCommandHandler.Handle(new SyncConversationCommand
            {
                AgentId = SyncConversationCommandHandlerTesting.AgentId,
                TenantName = SyncConversationCommandHandlerTesting.ExistingTenantName,
                SessionId = SyncConversationCommandHandlerTesting.ExistingSessionId,
                Name = "Updated Name",
                IsTestChat = false
            }, TestContext.Current.CancellationToken);

            // Assert — must still be true, never reverted
            Assert.True(_testing.ExistingConversation.IsTestChat);
        }

        [Fact]
        public async Task TestHandler_TenantNotFound_ReturnsNotFound()
        {
            // Act
            var result = await _testing.SyncConversationCommandHandler.Handle(new SyncConversationCommand
            {
                AgentId = SyncConversationCommandHandlerTesting.AgentId,
                TenantName = "non-existent-tenant",
                SessionId = "any-session",
                Name = "Any Name"
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.NotFound);
        }

        private Conversation AddDeletedConversationWithOldState(string sessionId, bool isTestChat)
        {
            var conversation = new Conversation
            {
                Id = 100 + _testing.Conversations.Count,
                AgentId = SyncConversationCommandHandlerTesting.AgentId,
                TenantId = 1,
                SessionId = sessionId,
                IsDeleted = true,
                Name = "Old conversation",
                PreviewLine = "Old preview",
                TitleStatus = ConversationTitleStatus.Generated,
                ConversationDate = new DateTimeOffset(2025, 6, 1, 9, 0, 0, TimeSpan.Zero),
                UserId = "old-user",
                SubjectId = "old-subject",
                TotalThumbsUp = 7,
                TotalThumbsDown = 5,
                LastActivityAt = new DateTimeOffset(2025, 6, 1, 9, 30, 0, TimeSpan.Zero),
                IsResolved = false,
                AnalysisStatus = ConversationAnalysisStatus.Completed,
                IsTestChat = isTestChat
            };
            _testing.Conversations.Add(conversation);
            return conversation;
        }

        /// <summary>
        /// Bug 7958: a soft-deleted conversation still matches (AgentId, SessionId) when the client keeps the same session
        /// id. It must be revived in place (no duplicate row) as a brand-new conversation, with none of its old state.
        /// </summary>
        [Fact]
        public async Task TestHandler_DeletedConversation_RevivesItInPlaceAsANewConversation()
        {
            // Arrange
            const string sessionId = "deleted-session-revived";
            var deleted = AddDeletedConversationWithOldState(sessionId, isTestChat: true);
            var conversationDate = new DateTimeOffset(2026, 1, 1, 11, 0, 0, TimeSpan.Zero);

            // Act
            var result = await _testing.SyncConversationCommandHandler.Handle(new SyncConversationCommand
            {
                AgentId = SyncConversationCommandHandlerTesting.AgentId,
                TenantName = SyncConversationCommandHandlerTesting.ExistingTenantName,
                SessionId = sessionId,
                Name = "New Conversation",
                ConversationDate = conversationDate,
                UserId = "new-user",
                SubjectId = "new-subject",
                TotalThumbsUp = 1,
                TotalThumbsDown = 0,
                IsTestChat = false
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.Ok);
            var revived = Assert.Single(_testing.Conversations, c => c.SessionId == sessionId);
            Assert.Same(deleted, revived);
            Assert.False(revived.IsDeleted);
            Assert.Equal("New Conversation", revived.Name);
            Assert.Null(revived.PreviewLine);
            Assert.Equal(ConversationTitleStatus.Pending, revived.TitleStatus);
            Assert.Equal(conversationDate, revived.ConversationDate);
            Assert.Equal("new-user", revived.UserId);
            Assert.Equal("new-subject", revived.SubjectId);
            Assert.Equal(1, revived.TotalThumbsUp);
            Assert.Equal(0, revived.TotalThumbsDown);
            Assert.Null(revived.LastActivityAt);
            Assert.True(revived.IsResolved);
            Assert.Equal(ConversationAnalysisStatus.Pending, revived.AnalysisStatus);
            Assert.False(revived.IsTestChat);
        }

        [Fact]
        public async Task TestHandler_DeletedConversation_WithoutConversationDate_UsesNow()
        {
            // Arrange
            const string sessionId = "deleted-session-no-date";
            AddDeletedConversationWithOldState(sessionId, isTestChat: false);

            // Act
            await _testing.SyncConversationCommandHandler.Handle(new SyncConversationCommand
            {
                AgentId = SyncConversationCommandHandlerTesting.AgentId,
                TenantName = SyncConversationCommandHandlerTesting.ExistingTenantName,
                SessionId = sessionId,
                Name = "New Conversation",
                IsTestChat = true
            }, TestContext.Current.CancellationToken);

            // Assert
            var revived = Assert.Single(_testing.Conversations, c => c.SessionId == sessionId);
            Assert.Equal(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero), revived.ConversationDate);
            Assert.True(revived.IsTestChat);
        }

        [Fact]
        public async Task TestHandler_ExistingNotDeletedConversation_KeepsConversationDateAndResolution()
        {
            // Arrange - regression guard for Bug 7958: the revival path must only apply to deleted rows
            const string sessionId = "live-session-unchanged";
            var conversationDate = new DateTimeOffset(2025, 6, 1, 9, 0, 0, TimeSpan.Zero);
            var live = new Conversation
            {
                Id = 200,
                AgentId = SyncConversationCommandHandlerTesting.AgentId,
                TenantId = 1,
                SessionId = sessionId,
                Name = "Live conversation",
                ConversationDate = conversationDate,
                IsResolved = false,
                AnalysisStatus = ConversationAnalysisStatus.Completed,
                IsTestChat = true
            };
            _testing.Conversations.Add(live);

            // Act
            await _testing.SyncConversationCommandHandler.Handle(new SyncConversationCommand
            {
                AgentId = SyncConversationCommandHandlerTesting.AgentId,
                TenantName = SyncConversationCommandHandlerTesting.ExistingTenantName,
                SessionId = sessionId,
                Name = "Updated Name",
                ConversationDate = new DateTimeOffset(2026, 1, 1, 11, 0, 0, TimeSpan.Zero),
                IsTestChat = false
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.Single(_testing.Conversations, c => c.SessionId == sessionId);
            Assert.False(live.IsDeleted);
            Assert.Equal("Updated Name", live.Name);
            Assert.Equal(conversationDate, live.ConversationDate);
            Assert.False(live.IsResolved);
            Assert.Equal(ConversationAnalysisStatus.Completed, live.AnalysisStatus);
            Assert.True(live.IsTestChat);
        }
    }
}
