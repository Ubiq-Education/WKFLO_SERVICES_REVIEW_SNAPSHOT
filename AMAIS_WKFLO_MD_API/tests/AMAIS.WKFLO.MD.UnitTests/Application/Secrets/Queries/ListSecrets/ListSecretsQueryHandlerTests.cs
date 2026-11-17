using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Secrets.Models;
using AMAIS.WKFLO.MD.Application.Secrets.Queries.ListSecrets;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Secrets.Queries.ListSecrets
{
    public class ListSecretsQueryHandlerTests : IClassFixture<ListSecretsQueryHandlerTesting>
    {
        private readonly ListSecretsQueryHandlerTesting _testing;

        public ListSecretsQueryHandlerTests(ListSecretsQueryHandlerTesting testing)
        {
            _testing = testing;
        }

        [Fact]
        public async Task TestHandler_Ok()
        {
            // Act
            var result = await _testing.ListSecretsQueryHandler.Handle(new ListSecretsQuery(), TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.Ok);
            if (result is SuccessResult<List<SecretGetResult>> success)
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
