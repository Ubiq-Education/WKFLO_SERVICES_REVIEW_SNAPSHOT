using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tags.Models;
using AMAIS.WKFLO.MD.Application.Tags.Queries.ListTenantTags;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tags.Queries.ListTenantTags
{
    public class ListTenantTagsQueryHandlerTests(ListTenantTagsQueryHandlerTesting testing) : IClassFixture<ListTenantTagsQueryHandlerTesting>
    {
        private ListTenantTagsQueryHandlerTesting Testing => testing;

        [Fact]
        public async Task TestHandler_ReturnsTagsForTenant()
        {
            var result = await Testing.ListTenantTagsQueryHandler.Handle(
                new ListTenantTagsQuery(),
                TestContext.Current.CancellationToken);

            Assert.Equal(ResultType.Ok, result.ResultType);
            if (result is SuccessResult<List<TagGetResult>> success)
            {
                Assert.NotNull(success.Data);
                Assert.NotEmpty(success.Data);
            }
        }
    }
}
