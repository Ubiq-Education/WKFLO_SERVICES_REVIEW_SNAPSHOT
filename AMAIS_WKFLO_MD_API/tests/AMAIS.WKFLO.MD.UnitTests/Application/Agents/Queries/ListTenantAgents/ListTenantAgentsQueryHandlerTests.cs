using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Agents.Queries.ListTenantAgents;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Queries.ListTenantAgents
{
    public class ListTenantAgentsQueryHandlerTests : IClassFixture<ListTenantAgentsQueryHandlerTesting>
    {
        private readonly ListTenantAgentsQueryHandlerTesting _testing;

        public ListTenantAgentsQueryHandlerTests(ListTenantAgentsQueryHandlerTesting testing)
        {
            _testing = testing;
        }

        [Fact]
        public async Task TestHandler_Ok()
        {
            // Act
            var result = await _testing.ListTenantAgentsQueryHandler.Handle(new ListTenantAgentsQuery(), TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.Ok);
            if (result is SuccessResult<List<TenantAgentGetResult>> success)
            {
                Assert.NotNull(success.Data);
                Assert.Equal(2, success.Data.Count);
            }
            else
            {
                Assert.Fail("Error on TestHandler_Ok");
            }
        }

        [Fact]
        public async Task TestHandler_MapsIconUrlFromEntity()
        {
            // Regression coverage for a reported gap: the LIST endpoint (GET /api/v1/tenant/agents,
            // backed by this handler) was suspected of dropping IconUrl even though the single-agent
            // GET endpoint returned it. Verified by reading both handlers: both map via the exact same
            // IWkfloMapper.Map<T> convention-mapping call (no hand-rolled Select() projection anywhere),
            // so IconUrl (a plain same-named scalar on TenantAgent and TenantAgentGetResult) flows
            // through identically for both. This test confirms the List path specifically.
            var result = await _testing.ListTenantAgentsQueryHandler.Handle(new ListTenantAgentsQuery(), TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<List<TenantAgentGetResult>>>(result);
            var agent = success.Data.Single(a => a.Id == _testing.TenantAgentId);
            Assert.Equal("https://static.example.com/test-tenant-agent-icon.png", agent.IconUrl);
        }

        [Fact]
        public async Task TestHandler_MapsInstalledFromMarketplaceAgentIdFromEntity()
        {
            // Same rationale as the IconUrl test above - confirms the List path (not just Get)
            // surfaces InstalledFromMarketplaceAgentId, needed for the CMS "already installed"
            // template-picker check.
            var result = await _testing.ListTenantAgentsQueryHandler.Handle(new ListTenantAgentsQuery(), TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<List<TenantAgentGetResult>>>(result);
            var agent = success.Data.Single(a => a.Id == _testing.TenantAgentId);
            Assert.Equal(555, agent.InstalledFromMarketplaceAgentId);
        }
    }
}
