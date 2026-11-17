using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.CreateMarketplaceAgent;
using AMAIS.WKFLO.MD.Application.Agents.Commands.InstallMarketplaceAgent;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Agents.Queries.GetMarketplaceAgentById;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.Application.Agents.Commands
{
    public class InstallMarketplaceAgentCommandHandlerTests : IClassFixture<TestFixture>
    {
        private readonly TestFixture _fixture;

        public InstallMarketplaceAgentCommandHandlerTests(TestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task InstallMarketplaceAgent_ResultOk()
        {
            // Arrange
            _fixture.DeleteMarketplaceAgentData();
            _fixture.DeleteTenantAgentData();
            _fixture.DeleteUserInstallData();
            _fixture.DeleteUserData();
            _fixture.AddTenantContext("Test");
            _fixture.AddUserContext("test@example.com", "TestUser", "test-user-id");

            var mediator = _fixture.GetMediator();
            var createCommand = GetCreateMarketplaceAgentCommand();
            var createResult = (SuccessResult<MarketplaceAgentCreateResult>)await mediator.Send(createCommand, TestContext.Current.CancellationToken);

            var installCommand = new InstallMarketplaceAgentCommand
            {
                Id = createResult.Data.Id
            };

            // Act
            var result = (Result)await mediator.Send(installCommand, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<TenantAgentCreateResult>>(result);
            var successResult = ((SuccessResult<TenantAgentCreateResult>)result).Data;
            Assert.NotNull(successResult);
            Assert.Equal("Test Marketplace Agent", successResult.Name);
            Assert.Equal("Test Marketplace Agent Description", successResult.Description);
            Assert.Equal("Test System Prompt", successResult.SystemPrompt);
            Assert.True(successResult.IsActive);
            Assert.False(successResult.EnableSTM);
            Assert.False(successResult.EnableLTM);
            Assert.Null(successResult.LastRunAt);
        }

        [Fact]
        public async Task InstallMarketplaceAgent_SameAgentInstalledTwiceBySameTenant_ReturnsExistingWithoutDuplicate()
        {
            // Arrange
            _fixture.DeleteMarketplaceAgentData();
            _fixture.DeleteTenantAgentData();
            _fixture.DeleteUserData();
            _fixture.AddTenantContext("Test");
            _fixture.AddUserContext("test@example.com", "TestUser", "test-user-id");

            var mediator = _fixture.GetMediator();
            var createCommand = GetCreateMarketplaceAgentCommand();
            var createResult = (SuccessResult<MarketplaceAgentCreateResult>)await mediator.Send(createCommand, TestContext.Current.CancellationToken);

            var installCommand = new InstallMarketplaceAgentCommand
            {
                Id = createResult.Data.Id
            };

            // Act - Install the same marketplace agent twice for the same tenant
            var firstResult = (SuccessResult<TenantAgentCreateResult>)await mediator.Send(installCommand, TestContext.Current.CancellationToken);
            var secondResult = (SuccessResult<TenantAgentCreateResult>)await mediator.Send(installCommand, TestContext.Current.CancellationToken);

            // Assert - the second install short-circuits to the SAME TenantAgent, no "(Copy 1)" duplicate
            Assert.NotNull(firstResult.Data);
            Assert.NotNull(secondResult.Data);
            Assert.Equal(firstResult.Data.Id, secondResult.Data.Id);
            Assert.Equal("Test Marketplace Agent", secondResult.Data.Name);

            var db = _fixture.GetDbContext();
            var tenantAgentCount = await db.TenantAgents.CountAsync(
                ta => ta.InstalledFromMarketplaceAgentId == createResult.Data.Id && !ta.IsDeleted,
                TestContext.Current.CancellationToken);
            Assert.Equal(1, tenantAgentCount);
        }

        [Fact]
        public async Task InstallMarketplaceAgent_DifferentAgentsWithSameName_ShouldCreateCopyWithSuffix()
        {
            // Arrange - the "(Copy N)" rename path is for a genuinely DIFFERENT marketplace agent that
            // happens to share the same name, not for reinstalling the same one (covered above).
            _fixture.DeleteMarketplaceAgentData();
            _fixture.DeleteTenantAgentData();
            _fixture.DeleteUserData();
            _fixture.AddTenantContext("Test");
            _fixture.AddUserContext("test@example.com", "TestUser", "test-user-id");

            var mediator = _fixture.GetMediator();
            var createResult1 = (SuccessResult<MarketplaceAgentCreateResult>)await mediator.Send(GetCreateMarketplaceAgentCommand(), TestContext.Current.CancellationToken);
            var createResult2 = (SuccessResult<MarketplaceAgentCreateResult>)await mediator.Send(GetCreateMarketplaceAgentCommand(), TestContext.Current.CancellationToken);

            // Act - install both distinct marketplace agents (same Name) for the same tenant
            var firstResult = (SuccessResult<TenantAgentCreateResult>)await mediator.Send(new InstallMarketplaceAgentCommand { Id = createResult1.Data.Id }, TestContext.Current.CancellationToken);
            var secondResult = (SuccessResult<TenantAgentCreateResult>)await mediator.Send(new InstallMarketplaceAgentCommand { Id = createResult2.Data.Id }, TestContext.Current.CancellationToken);

            // Assert
            Assert.NotNull(firstResult.Data);
            Assert.NotNull(secondResult.Data);
            Assert.NotEqual(firstResult.Data.Id, secondResult.Data.Id);
            Assert.Equal("Test Marketplace Agent", firstResult.Data.Name);
            Assert.Equal("Test Marketplace Agent (Copy 1)", secondResult.Data.Name);
        }

        [Fact]
        public async Task InstallMarketplaceAgent_ShouldIncreaseLiveInstallationsCount()
        {
            // Arrange
            _fixture.DeleteMarketplaceAgentData();
            _fixture.DeleteTenantAgentData();
            _fixture.DeleteUserData();
            _fixture.AddTenantContext("Test");
            _fixture.AddUserContext("test@example.com", "TestUser", "test-user-id");

            var mediator = _fixture.GetMediator();
            var createCommand = GetCreateMarketplaceAgentCommand();
            var createResult = (SuccessResult<MarketplaceAgentCreateResult>)await mediator.Send(createCommand, TestContext.Current.CancellationToken);

            var installCommand = new InstallMarketplaceAgentCommand
            {
                Id = createResult.Data.Id
            };

            // Get initial (live-computed) installations count
            var getQuery = new GetMarketplaceAgentByIdQuery { Id = createResult.Data.Id };
            var initialGet = (SuccessResult<MarketplaceAgentGetResult>)await mediator.Send(getQuery, TestContext.Current.CancellationToken);
            var initialInstallations = initialGet.Data.Installations;

            // Act - install once for "Test" tenant
            await mediator.Send(installCommand, TestContext.Current.CancellationToken);

            // Assert - count went up by 1
            var afterFirstInstall = (SuccessResult<MarketplaceAgentGetResult>)await mediator.Send(getQuery, TestContext.Current.CancellationToken);
            Assert.Equal(initialInstallations + 1, afterFirstInstall.Data.Installations);

            // Act - install AGAIN for the SAME tenant (dedup short-circuit)
            await mediator.Send(installCommand, TestContext.Current.CancellationToken);

            // Assert - count does NOT increase further
            var afterSecondInstall = (SuccessResult<MarketplaceAgentGetResult>)await mediator.Send(getQuery, TestContext.Current.CancellationToken);
            Assert.Equal(initialInstallations + 1, afterSecondInstall.Data.Installations);
        }

        [Fact]
        public async Task InstallMarketplaceAgent_ResultInvalid_MarketplaceAgentMustExist()
        {
            // Arrange
            _fixture.DeleteMarketplaceAgentData();
            _fixture.DeleteTenantAgentData();
            _fixture.AddTenantContext("Test");

            var mediator = _fixture.GetMediator();
            var installCommand = new InstallMarketplaceAgentCommand
            {
                Id = 999 // Non-existent ID
            };

            // Act
            var result = (Result)await mediator.Send(installCommand, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<NotFoundResult>(result);
            var notFoundResult = (NotFoundResult)result;
            Assert.NotEmpty(notFoundResult.Errors);
            Assert.Contains(notFoundResult.Errors, m => m.Code == ErrorConst.AgentNotFound && m.Property == nameof(MarketplaceAgent));
        }

        [Fact]
        public async Task InstallMarketplaceAgent_ResultInvalid_TenantMustExist()
        {
            // Arrange
            _fixture.DeleteMarketplaceAgentData();
            _fixture.DeleteTenantAgentData();
            _fixture.AddTenantContext("NonExistentTenant");

            var mediator = _fixture.GetMediator();
            var createCommand = GetCreateMarketplaceAgentCommand();

            // First create with valid tenant context
            _fixture.AddTenantContext("Test");
            var createResult = (SuccessResult<MarketplaceAgentCreateResult>)await mediator.Send(createCommand, TestContext.Current.CancellationToken);

            // Then try to install with invalid tenant context
            _fixture.AddTenantContext("NonExistentTenant");
            var installCommand = new InstallMarketplaceAgentCommand
            {
                Id = createResult.Data.Id
            };

            // Act
            var result = (Result)await mediator.Send(installCommand, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<InvalidResult>(result);
            var invalidResult = (InvalidResult)result;
            Assert.NotEmpty(invalidResult.Errors);
            Assert.Contains(invalidResult.Errors, m => m.Code == ErrorConst.TenantMustExist);
        }

        private static CreateMarketplaceAgentCommand GetCreateMarketplaceAgentCommand()
        {
            return new CreateMarketplaceAgentCommand
            {
                Name = "Test Marketplace Agent",
                Description = "Test Marketplace Agent Description",
                SystemPrompt = "Test System Prompt"
            };
        }
    }
}