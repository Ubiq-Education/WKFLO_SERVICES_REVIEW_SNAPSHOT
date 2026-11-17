using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tools.Commands.DeleteTenantTool;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.TenantTools.Commands.DeleteTenantTool
{
    public class DeleteTenantToolCommandHandlerTests : IClassFixture<DeleteTenantToolCommandHandlerTesting>
    {
        private readonly DeleteTenantToolCommandHandlerTesting _testing;

        public DeleteTenantToolCommandHandlerTests(DeleteTenantToolCommandHandlerTesting testing)
        {
            _testing = testing;
        }

        [Fact]
        public async Task TestHandlerOk()
        {
            // Act
            var result = await _testing.DeleteTenantToolCommandHandler.Handle(new DeleteTenantToolCommand()
            {
                Id = _testing.TenantToolId
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.Ok);
            if (result is SuccessResult<TenantToolDeleteResult> success)
            {
                Assert.NotNull(success.Data);
            }
            else
            {
                Assert.Fail("Error on TestHandlerOk");
            }
        }

        [Fact]
        public async Task TestHandlerOk_CascadesToPublishedTool()
        {
            // Act
            var result = await _testing.DeleteTenantToolCommandHandler.Handle(new DeleteTenantToolCommand()
            {
                Id = _testing.TenantToolWithPublishedToolId
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.Ok);
            if (result is SuccessResult<TenantToolDeleteResult> success)
            {
                Assert.NotNull(success.Data);
            }
            else
            {
                Assert.Fail("Error on TestHandlerOk_CascadesToPublishedTool");
            }

            // The linked PublishedTenantTool (the entity the chat widget actually serves from) must also
            // be soft-deleted. EntityTag rows are deliberately left untouched by this handler — see
            // ListEntitiesByTagQueryHandler, which filters tag liveness at read time instead.
            Assert.True(_testing.PublishedTenantTool.IsDeleted);
        }

        [Fact]
        public async Task TestHandlerOk_NoLinkedPublishedTool_DoesNotThrow()
        {
            // Act
            var result = await _testing.DeleteTenantToolCommandHandler.Handle(new DeleteTenantToolCommand()
            {
                Id = _testing.TenantToolId
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.Ok);
            if (result is SuccessResult<TenantToolDeleteResult> success)
            {
                Assert.NotNull(success.Data);
            }
            else
            {
                Assert.Fail("Error on TestHandlerOk_NoLinkedPublishedTool_DoesNotThrow");
            }

            // A dedicated, never-deleted PublishedTenantTool (linked to a different TenantTool that no test
            // in this class ever deletes) must remain untouched. Deliberately NOT the same PublishedTenantTool
            // object TestHandlerOk_CascadesToPublishedTool mutates — IClassFixture shares one *Testing
            // instance across every [Fact] in this class with no ordering guarantee, so asserting IsDeleted
            // == false here against that other test's target object would make this test's outcome depend on
            // execution order (fails whenever the cascade test happens to run first).
            Assert.False(_testing.UnrelatedPublishedTenantTool.IsDeleted);
        }

        [Fact]
        public async Task TestHandlerNotFound()
        {
            // Act
            var result = await _testing.DeleteTenantToolCommandHandler.Handle(new DeleteTenantToolCommand()
            {
                Id = 0
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.NotFound);
            if (result is SuccessResult<TenantToolDeleteResult> success)
            {
                Assert.NotNull(success.Data);
            }
        }
    }
}
