using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Secrets.Commands.CreateSecret;
using AMAIS.WKFLO.MD.Application.Secrets.Models;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Secrets.Commands.CreateSecret
{
    public class CreateSecretCommandHandlerTests : IClassFixture<CreateSecretCommandHandlerTesting>
    {
        private readonly CreateSecretCommandHandlerTesting _testing;

        public CreateSecretCommandHandlerTests(CreateSecretCommandHandlerTesting testing)
        {
            _testing = testing;
        }

        [Theory]
        [InlineData("ApiKey 1", "Endpoint 1", true, ResultType.Ok)]
        [InlineData("ApiKey 2", "Endpoint 2", false, ResultType.Ok)]
        public async Task TestHandler(string apiKey, string endpoint, bool isActive, ResultType resultType)
        {
            // Act
            var result = await _testing.CreateSecretCommandHandler.Handle(new CreateSecretCommand()
            {
                ApiKey = apiKey,
                Name = endpoint,
                IsActive = isActive

            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == resultType);
            if (result is SuccessResult<SecretCreateResult> success)
            {
                Assert.NotNull(success.Data);
            }
        }
    }
}
