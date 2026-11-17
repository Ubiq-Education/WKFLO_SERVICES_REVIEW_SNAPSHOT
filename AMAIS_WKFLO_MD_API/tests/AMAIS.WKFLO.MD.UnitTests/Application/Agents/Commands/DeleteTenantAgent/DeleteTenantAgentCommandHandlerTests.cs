using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.DeleteTenantAgent;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Commands.DeleteTenantAgent
{
    public class DeleteTenantAgentCommandHandlerTests : IClassFixture<DeleteTenantAgentCommandHandlerTesting>
    {
        private readonly DeleteTenantAgentCommandHandlerTesting _testing;

        public DeleteTenantAgentCommandHandlerTests(DeleteTenantAgentCommandHandlerTesting testing)
        {
            _testing = testing;
        }

        [Fact]
        public async Task TestHandlerOk()
        {
            // Act
            var result = await _testing.DeleteTenantAgentCommandHandler.Handle(new DeleteTenantAgentCommand()
            {
                Id = _testing.TenantAgentId
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.Ok);
            if (result is SuccessResult<TenantAgentDeleteResult> success)
            {
                Assert.NotNull(success.Data);
            }
            else
            {
                Assert.Fail("Error on TestHandlerOk");
            }
        }

        [Fact]
        public async Task TestHandlerNotFound()
        {
            // Act
            var result = await _testing.DeleteTenantAgentCommandHandler.Handle(new DeleteTenantAgentCommand()
            {
                Id = 0
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.NotFound);
            if (result is SuccessResult<TenantAgentDeleteResult> success)
            {
                Assert.NotNull(success.Data);
            }
        }

        [Fact]
        public async Task TestHandlerOk_CascadesToPublishedAgent()
        {
            // Act
            var result = await _testing.DeleteTenantAgentCommandHandler.Handle(new DeleteTenantAgentCommand()
            {
                Id = _testing.TenantAgentWithPublishedAgentId
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.Ok);
            if (result is SuccessResult<TenantAgentDeleteResult> success)
            {
                Assert.NotNull(success.Data);
            }
            else
            {
                Assert.Fail("Error on TestHandlerOk_CascadesToPublishedAgent");
            }

            // The linked PublishedTenantAgent (the entity the chat widget actually serves from) must also
            // be soft-deleted. EntityTag rows are deliberately left untouched by this handler — see
            // ListEntitiesByTagQueryHandler, which filters tag liveness at read time instead.
            Assert.True(_testing.PublishedTenantAgent.IsDeleted);
        }
    }
}
