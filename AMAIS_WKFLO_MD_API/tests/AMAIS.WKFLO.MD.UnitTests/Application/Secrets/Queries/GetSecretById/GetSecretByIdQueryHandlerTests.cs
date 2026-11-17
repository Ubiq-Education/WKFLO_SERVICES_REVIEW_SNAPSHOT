using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Secrets.Models;
using AMAIS.WKFLO.MD.Application.Secrets.Queries.GetSecretById;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Secrets.Queries.GetSecretById
{
    public class GetSecretByIdQueryHandlerTests : IClassFixture<GetSecretByIdQueryHandlerTesting>
    {
        private readonly GetSecretByIdQueryHandlerTesting _testing;

        public GetSecretByIdQueryHandlerTests(GetSecretByIdQueryHandlerTesting testing)
        {
            _testing = testing;
        }

        [Fact]
        public async Task TestHandler_Ok()
        {
            // Act
            var result = await _testing.GetSecretByIdQueryHandler.Handle(new GetSecretByIdQuery()
            {
                Id = _testing.SecretId
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.Ok);
            if (result is SuccessResult<SecretGetResult> success)
            {
                Assert.NotNull(success.Data);
            }
            else
            {
                Assert.Fail("Error on TestHandler_Ok");
            }
        }

        [Fact]
        public async Task TestHandlerNotFound()
        {
            // Act
            var result = await _testing.GetSecretByIdQueryHandler.Handle(new GetSecretByIdQuery()
            {
                Id = GetSecretByIdQueryHandlerTesting.RandomNumber.Next()
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.NotFound);
        }
    }
}
