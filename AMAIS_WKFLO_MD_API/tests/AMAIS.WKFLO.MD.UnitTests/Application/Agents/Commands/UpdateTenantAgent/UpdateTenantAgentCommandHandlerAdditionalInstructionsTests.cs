using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.UpdateTenantAgent;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using MockQueryable.Moq;
using Moq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Commands.UpdateTenantAgent
{
    /// <summary>
    /// WI 7318: <see cref="UpdateTenantAgentCommand.AdditionalInstructions"/> has null = unchanged,
    /// "" / whitespace = clear, anything else = replace semantics, even though the rest of the command is
    /// applied with a full Mapster copy. Each test builds its own agent so state never leaks between cases.
    /// </summary>
    public class UpdateTenantAgentCommandHandlerAdditionalInstructionsTests
    {
        private const int AgentId = 1;
        private const string ExistingInstructions = "Always use a warm, welcoming tone.";

        private static (UpdateTenantAgentCommandHandler Handler, TenantAgent Agent) Build(string existingInstructions)
        {
            var agent = new TenantAgent
            {
                Id = AgentId,
                Name = "Agent",
                Description = "Description",
                SystemPrompt = "Platform prompt",
                AgentTools = [],
                IsActive = true,
                AdditionalInstructions = existingInstructions
            };

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.TenantAgents).Returns(new List<TenantAgent> { agent }.BuildMockDbSet().Object);
            db.Setup(x => x.SaveChangesAsync(It.IsAny<System.Threading.CancellationToken>())).ReturnsAsync(1);

            return (new UpdateTenantAgentCommandHandler(db.Object, WkfloMapperExtensions.CreateMapper()), agent);
        }

        private static UpdateTenantAgentCommand Command(string additionalInstructions) => new()
        {
            Id = AgentId,
            Name = "Agent",
            Description = "Description",
            SystemPrompt = "Platform prompt",
            IsActive = true,
            AdditionalInstructions = additionalInstructions
        };

        [Fact]
        public async Task Handle_NewValue_PersistsAndReturnsIt()
        {
            var (handler, agent) = Build(existingInstructions: null);

            var result = await handler.Handle(Command("  Keep answers under 100 words.  "), TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<TenantAgentUpdateResult>>(result);
            Assert.Equal("Keep answers under 100 words.", agent.AdditionalInstructions);
            Assert.Equal("Keep answers under 100 words.", success.Data.AdditionalInstructions);
        }

        [Fact]
        public async Task Handle_Null_LeavesExistingValueUnchanged()
        {
            var (handler, agent) = Build(ExistingInstructions);

            var result = await handler.Handle(Command(null), TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<TenantAgentUpdateResult>>(result);
            Assert.Equal(ExistingInstructions, agent.AdditionalInstructions);
            Assert.Equal(ExistingInstructions, success.Data.AdditionalInstructions);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\r\n\t")]
        public async Task Handle_EmptyOrWhitespace_ClearsToNull(string value)
        {
            var (handler, agent) = Build(ExistingInstructions);

            var result = await handler.Handle(Command(value), TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<TenantAgentUpdateResult>>(result);
            Assert.Null(agent.AdditionalInstructions);
            Assert.Null(success.Data.AdditionalInstructions);
        }

        [Fact]
        public async Task Handle_DifferentValue_ReplacesExisting()
        {
            var (handler, agent) = Build(ExistingInstructions);

            await handler.Handle(Command("Avoid discussing fees."), TestContext.Current.CancellationToken);

            Assert.Equal("Avoid discussing fees.", agent.AdditionalInstructions);
        }

        [Fact]
        public async Task Handle_OtherFieldsStillMappedWhenAdditionalInstructionsIsNull()
        {
            var (handler, agent) = Build(ExistingInstructions);
            var command = Command(null);
            command.Name = "Renamed";

            await handler.Handle(command, TestContext.Current.CancellationToken);

            Assert.Equal("Renamed", agent.Name);
            Assert.Equal(ExistingInstructions, agent.AdditionalInstructions);
        }
    }
}
