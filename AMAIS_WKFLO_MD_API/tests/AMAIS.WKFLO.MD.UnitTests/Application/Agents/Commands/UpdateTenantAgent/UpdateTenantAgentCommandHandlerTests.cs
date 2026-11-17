using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.UpdateTenantAgent;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Commands.UpdateTenantAgent
{
    public class UpdateTenantAgentCommandHandlerTests : IClassFixture<UpdateTenantAgentCommandHandlerTesting>
    {
        private readonly UpdateTenantAgentCommandHandlerTesting _testing;

        public UpdateTenantAgentCommandHandlerTests(UpdateTenantAgentCommandHandlerTesting testing)
        {
            _testing = testing;
        }

        [Theory]
        [InlineData("Name Test 1", "Description Test 1", "SystemPrompt 1", "Friendly", true, true, true, true, ResultType.Ok)]
        [InlineData("Name Test 2", "Description Test 2", "SystemPrompt 2", "Friendly", false, false, false, false, ResultType.Ok)]
        public async Task TestHandler(string name, string description, string systemPrompt, string responseTone, bool isActive,
            bool enableSTM, bool enableLTM, bool enableSpinUpAgents, ResultType resultType)
        {
            // Act
            var result = await _testing.UpdateTenantAgentCommandHandler.Handle(new UpdateTenantAgentCommand()
            {
                Id = _testing.TenantAgentId,
                Name = name,
                Description = description,
                SystemPrompt = systemPrompt,
                ResponseTone = responseTone,
                IsActive = isActive,
                EnableSTM = enableSTM,
                EnableLTM = enableLTM,
                EnableSpinUpAgents = enableSpinUpAgents

            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == resultType);
            if (result is SuccessResult<TenantAgentUpdateResult> success)
            {
                Assert.NotNull(success.Data);
                Assert.Equal(enableSTM, success.Data.EnableSTM);
                Assert.Equal(enableLTM, success.Data.EnableLTM);
                Assert.Equal(enableSpinUpAgents, success.Data.EnableSpinUpAgents);
            }
        }
    }
}
