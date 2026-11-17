using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Knowledge.Models;
using AMAIS.WKFLO.MD.Application.Knowledge.Queries.ListTenantKnowledge;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Knowledge.Queries.ListTenantKnowledge
{
    public class ListTenantKnowledgeQueryHandlerTests : IClassFixture<ListTenantKnowledgeQueryHandlerTesting>
    {
        private readonly ListTenantKnowledgeQueryHandlerTesting _testing;

        public ListTenantKnowledgeQueryHandlerTests(ListTenantKnowledgeQueryHandlerTesting testing)
        {
            _testing = testing;
        }

        [Fact]
        public async Task Handle_ReturnsDescriptionAndTimestampForEachDocument()
        {
            // Act
            var result = await _testing.Handler.Handle(new ListTenantKnowledgeQuery(), TestContext.Current.CancellationToken);

            // Assert
            var success = Assert.IsType<SuccessResult<List<TenantKnowledgeGetResult>>>(result);
            Assert.Equal(2, success.Data.Count);

            var withDescription = success.Data.Single(d => d.Id == "doc-1");
            Assert.Equal("Full admissions process and requirements", withDescription.Description);
            Assert.Equal(ListTenantKnowledgeQueryHandlerTesting.FixedTimestamp, withDescription.Timestamp);
            Assert.Equal("admissions-faq.pdf", withDescription.FileName);
            Assert.Equal("https://storage.example.com/tenant-knowledge/admissions-faq.pdf", withDescription.BlobUrl);

            var withoutDescription = success.Data.Single(d => d.Id == "doc-2");
            Assert.Null(withoutDescription.Description);
            Assert.Equal(ListTenantKnowledgeQueryHandlerTesting.FixedTimestamp, withoutDescription.Timestamp);
        }
    }
}
