using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.CreateTenantAgent;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using Moq;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Commands.CreateTenantAgent
{
    public class CreateTenantAgentCommandHandlerTests(CreateTenantAgentCommandHandlerTesting testing) : IClassFixture<CreateTenantAgentCommandHandlerTesting>
    {
        private CreateTenantAgentCommandHandlerTesting Testing => testing;

        [Theory]
        [InlineData("Name Test 1", "Description Test 1", "SystemPrompt 1", "Friendly", true, true, ResultType.Ok)]
        [InlineData("Name Test 2", "Description Test 2", "SystemPrompt 2", "Friendly", false, false, ResultType.Ok)]
        public async Task TestHandler(string name, string description, string systemPrompt, string responseTone, bool isActive, bool enableSpinUpAgents, ResultType resultType)
        {
            // Act
            var result = await Testing.CreateTenantAgentCommandHandler.Handle(new CreateTenantAgentCommand()
            {
                Name = name,
                Description = description,
                SystemPrompt = systemPrompt,
                ResponseTone = responseTone,
                IsActive = isActive,
                EnableSpinUpAgents = enableSpinUpAgents

            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(result.ResultType, resultType);
            if (result is SuccessResult<TenantAgentCreateResult> success)
            {
                Assert.NotNull(success.Data);
                Assert.Equal(enableSpinUpAgents, success.Data.EnableSpinUpAgents);
            }
        }

        // WI 7318: AdditionalInstructions is normalized on create - padding trimmed, blank stored as null.
        [Theory]
        [InlineData("Padded Agent", "  Keep answers under 100 words.  ", "Keep answers under 100 words.")]
        [InlineData("Whitespace Agent", "   \r\n\t ", null)]
        public async Task AdditionalInstructions_StoredAndReturnedNormalized(string name, string additionalInstructions, string expected)
        {
            var result = await Testing.CreateTenantAgentCommandHandler.Handle(new CreateTenantAgentCommand()
            {
                Name = name,
                Description = "Description",
                SystemPrompt = "SystemPrompt",
                AdditionalInstructions = additionalInstructions
            }, TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<TenantAgentCreateResult>>(result);
            Assert.Equal(expected, success.Data.AdditionalInstructions);
            Testing.TenantAgentsDbSetMock.Verify(x => x.AddAsync(
                It.Is<TenantAgent>(a => a.Name == name && a.AdditionalInstructions == expected),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
