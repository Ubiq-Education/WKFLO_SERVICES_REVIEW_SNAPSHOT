using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using AMAIS.WKFLO.MD.Application.Tools.Queries.ListTenantTools;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.TenantTools.Queries.ListTenantTools
{
    public class ListTenantToolsQueryHandlerTests : IClassFixture<ListTenantToolsQueryHandlerTesting>
    {
        private readonly ListTenantToolsQueryHandlerTesting _testing;

        public ListTenantToolsQueryHandlerTests(ListTenantToolsQueryHandlerTesting testing)
        {
            _testing = testing;
        }

        [Fact]
        public async Task TestHandler_Ok()
        {
            // Act
            var result = await _testing.ListTenantToolsQueryHandler.Handle(new ListTenantToolsQuery(), TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.Ok);
            if (result is SuccessResult<List<TenantToolGetResult>> success)
            {
                Assert.NotNull(success.Data);
                Assert.Equal(2, success.Data.Count);
            }
            else
            {
                Assert.Fail("Error on TestHandler_Ok");
            }
        }
    }
}
