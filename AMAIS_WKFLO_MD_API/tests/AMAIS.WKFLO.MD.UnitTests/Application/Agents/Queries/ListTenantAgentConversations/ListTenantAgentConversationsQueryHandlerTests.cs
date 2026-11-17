using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Agents.Queries.ListTenantAgentConversations;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Queries.ListTenantAgentConversations
{
    public class ListTenantAgentConversationsQueryHandlerTests : IClassFixture<ListTenantAgentConversationsQueryHandlerTesting>
    {
        private readonly ListTenantAgentConversationsQueryHandlerTesting _testing;

        public ListTenantAgentConversationsQueryHandlerTests(ListTenantAgentConversationsQueryHandlerTesting testing)
        {
            _testing = testing;
        }

        [Theory]
        [InlineData(1, 3, 3, 2, ResultType.Ok)]
        [InlineData(2, 3, 2, 2, ResultType.Ok)]
        [InlineData(1, 15, 5, 1, ResultType.Ok)]
        public async Task TestHandler(int page, int pageSize, int expectedItemCount, int expectedTotalPages, ResultType resultType)
        {
            // Act
            var result = await _testing.ListTenantAgentConversationsQueryHandler.Handle(new ListTenantAgentConversationsQuery
            {
                Id = 1,
                Page = page,
                PageSize = pageSize
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == resultType);
            if (result is SuccessResult<TenantAgentConversationsPageResult> success)
            {
                Assert.NotNull(success.Data);
                Assert.Equal(5, success.Data.TotalCount);
                Assert.Equal(expectedItemCount, success.Data.Items.Count);
                Assert.Equal(expectedTotalPages, success.Data.TotalPages);
                Assert.Equal(page, success.Data.Page);
                Assert.Equal(pageSize, success.Data.PageSize);
            }
            else
            {
                Assert.Fail($"Expected SuccessResult but got {result.ResultType}");
            }
        }

        [Fact]
        public async Task TestHandler_HideTestChatsDefaultTrue_ExcludesTestConversations()
        {
            // Act — HideTestChats not set explicitly, defaults to true
            var result = await _testing.ListTenantAgentConversationsQueryHandler.Handle(new ListTenantAgentConversationsQuery
            {
                Id = 1,
                Page = 1,
                PageSize = 15
            }, TestContext.Current.CancellationToken);

            // Assert
            var success = Assert.IsType<SuccessResult<TenantAgentConversationsPageResult>>(result);
            Assert.Equal(5, success.Data.TotalCount);
            Assert.DoesNotContain(success.Data.Items, i => i.SessionId == "s6-test");
        }

        [Fact]
        public async Task TestHandler_HideTestChatsFalse_IncludesTestConversations()
        {
            // Act
            var result = await _testing.ListTenantAgentConversationsQueryHandler.Handle(new ListTenantAgentConversationsQuery
            {
                Id = 1,
                Page = 1,
                PageSize = 15,
                HideTestChats = false
            }, TestContext.Current.CancellationToken);

            // Assert
            var success = Assert.IsType<SuccessResult<TenantAgentConversationsPageResult>>(result);
            Assert.Equal(6, success.Data.TotalCount);
            var testChat = Assert.Single(success.Data.Items, i => i.SessionId == "s6-test");
            Assert.True(testChat.IsTestChat);
        }
    }
}
