using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.AIModels.Commands.UpdateAIModel;
using AMAIS.WKFLO.MD.Application.AIModels.Models;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.AIModels.Commands.UpdateAIModel
{
    public class UpdateAIModelCommandHandlerTests : IClassFixture<UpdateAIModelCommandHandlerTesting>
    {
        private readonly UpdateAIModelCommandHandlerTesting _testing;

        public UpdateAIModelCommandHandlerTests(UpdateAIModelCommandHandlerTesting testing)
        {
            _testing = testing;
        }

        [Theory]
        [InlineData("Name Test 1", "Model Test 1", "Provider 1", "Model Test 1", true, null, ResultType.Ok)]
        [InlineData("Name Test 2", "Model Test 2", "Provider 2", "Model Type 2", false, null, ResultType.Ok)]
        [InlineData("Name Test 3", "Model Test 3", "azure", "Model Type 3", true, "managedIdentity", ResultType.Ok)]
        [InlineData("Name Test 4", "Model Test 4", "azure", "Model Type 4", true, "apiKey", ResultType.Ok)]
        public async Task TestHandler(string name, string model, string provider, string type, bool isActive, string authMode, ResultType resultType)
        {
            // Act
            var result = await _testing.UpdateAIModelCommandHandler.Handle(new UpdateAIModelCommand()
            {
                Id = _testing.AIModelId,
                Name = name,
                Model = model,
                Provider = provider,
                Type = type,
                IsActive = isActive,
                AuthMode = authMode

            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == resultType);
            if (result is SuccessResult<AIModelUpdateResult> success)
            {
                Assert.NotNull(success.Data);
                // Mapping round-trip: AuthMode must flow from command -> entity -> result unchanged
                Assert.Equal(authMode, success.Data.AuthMode);
            }
        }
    }
}
