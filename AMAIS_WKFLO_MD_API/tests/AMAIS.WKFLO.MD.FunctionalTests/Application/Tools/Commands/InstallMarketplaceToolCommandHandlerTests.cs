using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.Tools.Commands.CreateMarketplaceTool;
using AMAIS.WKFLO.MD.Application.Tools.Commands.InstallMarketplaceTool;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using AMAIS.WKFLO.MD.Application.Tools.Queries.GetMarketplaceToolById;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.Application.Tools.Commands
{
    public class InstallMarketplaceToolCommandHandlerTests : IClassFixture<TestFixture>
    {
        private readonly TestFixture _fixture;

        public InstallMarketplaceToolCommandHandlerTests(TestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task InstallMarketplaceTool_ResultOk()
        {
            // Arrange
            _fixture.DeleteMarketplaceToolData();
            _fixture.DeleteTenantToolData();
            _fixture.DeleteUserInstallData();
            _fixture.DeleteUserData();
            _fixture.AddTenantContext("Test");
            _fixture.AddUserContext("test@example.com", "TestUser", "test-user-id");

            var mediator = _fixture.GetMediator();
            var createCommand = GetCreateMarketplaceToolCommand();
            var createResult = (SuccessResult<MarketplaceToolCreateResult>)await mediator.Send(createCommand, TestContext.Current.CancellationToken);

            var installCommand = new InstallMarketplaceToolCommand
            {
                Id = createResult.Data.Id
            };

            // Act
            var result = (Result)await mediator.Send(installCommand, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<TenantToolCreateResult>>(result);
            var successResult = ((SuccessResult<TenantToolCreateResult>)result).Data;
            Assert.NotNull(successResult);
            Assert.Equal("Test Marketplace Tool", successResult.Name);
            Assert.Equal("Test Marketplace Tool Description", successResult.Description);
            Assert.Equal("API", successResult.Type);
            Assert.Equal("{\"endpoint\": \"https://api.example.com\"}", successResult.Config);
            Assert.True(successResult.IsActive);
        }

        [Fact]
        public async Task InstallMarketplaceTool_SameToolInstalledTwiceBySameTenant_ReturnsExistingWithoutDuplicate()
        {
            // Arrange
            _fixture.DeleteMarketplaceToolData();
            _fixture.DeleteTenantToolData();
            _fixture.DeleteUserInstallData();
            _fixture.DeleteUserData();
            _fixture.AddTenantContext("Test");
            _fixture.AddUserContext("test@example.com", "TestUser", "test-user-id");

            var mediator = _fixture.GetMediator();
            var createCommand = GetCreateMarketplaceToolCommand();
            var createResult = (SuccessResult<MarketplaceToolCreateResult>)await mediator.Send(createCommand, TestContext.Current.CancellationToken);

            var installCommand = new InstallMarketplaceToolCommand
            {
                Id = createResult.Data.Id
            };

            // Act - Install the same marketplace tool twice for the same tenant
            var firstResult = (SuccessResult<TenantToolCreateResult>)await mediator.Send(installCommand, TestContext.Current.CancellationToken);
            var secondResult = (SuccessResult<TenantToolCreateResult>)await mediator.Send(installCommand, TestContext.Current.CancellationToken);

            // Assert - the second install short-circuits to the SAME TenantTool, no "(Copy 1)" duplicate
            Assert.NotNull(firstResult.Data);
            Assert.NotNull(secondResult.Data);
            Assert.Equal(firstResult.Data.Id, secondResult.Data.Id);
            Assert.Equal("Test Marketplace Tool", secondResult.Data.Name);

            var db = _fixture.GetDbContext();
            var tenantToolCount = await db.TenantTools.CountAsync(
                tt => tt.InstalledFromMarketplaceToolId == createResult.Data.Id && !tt.IsDeleted,
                TestContext.Current.CancellationToken);
            Assert.Equal(1, tenantToolCount);
        }

        [Fact]
        public async Task InstallMarketplaceTool_DifferentToolsWithSameName_ShouldIncrementCopySuffix()
        {
            // Arrange - the "(Copy N)" rename path is for genuinely DIFFERENT marketplace tools that
            // happen to share the same name, not for reinstalling the same one (covered above).
            _fixture.DeleteMarketplaceToolData();
            _fixture.DeleteTenantToolData();
            _fixture.DeleteUserInstallData();
            _fixture.DeleteUserData();
            _fixture.AddTenantContext("Test");
            _fixture.AddUserContext("test@example.com", "TestUser", "test-user-id");

            var mediator = _fixture.GetMediator();
            var createResult1 = (SuccessResult<MarketplaceToolCreateResult>)await mediator.Send(GetCreateMarketplaceToolCommand(), TestContext.Current.CancellationToken);
            var createResult2 = (SuccessResult<MarketplaceToolCreateResult>)await mediator.Send(GetCreateMarketplaceToolCommand(), TestContext.Current.CancellationToken);
            var createResult3 = (SuccessResult<MarketplaceToolCreateResult>)await mediator.Send(GetCreateMarketplaceToolCommand(), TestContext.Current.CancellationToken);

            // Act - install three distinct marketplace tools (same Name) for the same tenant
            var firstResult = (SuccessResult<TenantToolCreateResult>)await mediator.Send(new InstallMarketplaceToolCommand { Id = createResult1.Data.Id }, TestContext.Current.CancellationToken);
            var secondResult = (SuccessResult<TenantToolCreateResult>)await mediator.Send(new InstallMarketplaceToolCommand { Id = createResult2.Data.Id }, TestContext.Current.CancellationToken);
            var thirdResult = (SuccessResult<TenantToolCreateResult>)await mediator.Send(new InstallMarketplaceToolCommand { Id = createResult3.Data.Id }, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal("Test Marketplace Tool", firstResult.Data.Name);
            Assert.Equal("Test Marketplace Tool (Copy 1)", secondResult.Data.Name);
            Assert.Equal("Test Marketplace Tool (Copy 2)", thirdResult.Data.Name);
        }

        [Fact]
        public async Task InstallMarketplaceTool_ShouldIncreaseLiveInstallationsCount()
        {
            // Arrange
            _fixture.DeleteMarketplaceToolData();
            _fixture.DeleteTenantToolData();
            _fixture.DeleteUserInstallData();
            _fixture.DeleteUserData();
            _fixture.AddTenantContext("Test");
            _fixture.AddUserContext("test@example.com", "TestUser", "test-user-id");

            var mediator = _fixture.GetMediator();
            var createCommand = GetCreateMarketplaceToolCommand();
            var createResult = (SuccessResult<MarketplaceToolCreateResult>)await mediator.Send(createCommand, TestContext.Current.CancellationToken);

            var installCommand = new InstallMarketplaceToolCommand
            {
                Id = createResult.Data.Id
            };

            var getQuery = new GetMarketplaceToolByIdQuery { Id = createResult.Data.Id };
            var initialGet = (SuccessResult<MarketplaceToolGetResult>)await mediator.Send(getQuery, TestContext.Current.CancellationToken);
            var initialInstallations = initialGet.Data.Installations;

            // Act - install once for "Test" tenant
            await mediator.Send(installCommand, TestContext.Current.CancellationToken);

            // Assert - count went up by 1
            var afterFirstInstall = (SuccessResult<MarketplaceToolGetResult>)await mediator.Send(getQuery, TestContext.Current.CancellationToken);
            Assert.Equal(initialInstallations + 1, afterFirstInstall.Data.Installations);

            // Act - install AGAIN for the SAME tenant (dedup short-circuit)
            await mediator.Send(installCommand, TestContext.Current.CancellationToken);

            // Assert - count does NOT increase further
            var afterSecondInstall = (SuccessResult<MarketplaceToolGetResult>)await mediator.Send(getQuery, TestContext.Current.CancellationToken);
            Assert.Equal(initialInstallations + 1, afterSecondInstall.Data.Installations);
        }

        [Fact]
        public async Task InstallMarketplaceTool_ShouldSetTenantToolPropertiesCorrectly()
        {
            // Arrange
            _fixture.DeleteMarketplaceToolData();
            _fixture.DeleteTenantToolData();
            _fixture.DeleteUserInstallData();
            _fixture.DeleteUserData();
            _fixture.AddTenantContext("Test");
            _fixture.AddUserContext("test@example.com", "TestUser", "test-user-id");

            var mediator = _fixture.GetMediator();
            var createCommand = GetCreateMarketplaceToolCommand();
            var createResult = (SuccessResult<MarketplaceToolCreateResult>)await mediator.Send(createCommand, TestContext.Current.CancellationToken);

            var installCommand = new InstallMarketplaceToolCommand
            {
                Id = createResult.Data.Id
            };

            // Act
            var result = (SuccessResult<TenantToolCreateResult>)await mediator.Send(installCommand, TestContext.Current.CancellationToken);

            // Assert - Verify TenantTool properties
            var db = _fixture.GetDbContext();
            var tenantTool = await db.TenantTools.FirstOrDefaultAsync(m => m.Id == result.Data.Id, TestContext.Current.CancellationToken);
            Assert.NotNull(tenantTool);
            Assert.True(tenantTool.IsActive); // Should be active by default
            Assert.Equal("Test Marketplace Tool", tenantTool.Name);
            Assert.Equal("Test Marketplace Tool Description", tenantTool.Description);
            Assert.Equal("API", tenantTool.Type);
            Assert.Equal("{\"endpoint\": \"https://api.example.com\"}", tenantTool.Config);
            Assert.False(tenantTool.IsDeleted);
        }

        [Fact]
        public async Task InstallMarketplaceTool_ResultInvalid_MarketplaceToolMustExist()
        {
            // Arrange
            _fixture.DeleteMarketplaceToolData();
            _fixture.DeleteTenantToolData();
            _fixture.AddTenantContext("Test");

            var mediator = _fixture.GetMediator();
            var installCommand = new InstallMarketplaceToolCommand
            {
                Id = 999 // Non-existent ID
            };

            // Act
            var result = (Result)await mediator.Send(installCommand, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<NotFoundResult>(result);
            var notFoundResult = (NotFoundResult)result;
            Assert.NotEmpty(notFoundResult.Errors);
            Assert.Contains(notFoundResult.Errors, m => m.Code == ErrorConst.ToolNotFound && m.Property == nameof(MarketplaceTool));
        }

        [Fact]
        public async Task InstallMarketplaceTool_ResultInvalid_TenantMustExist()
        {
            // Arrange
            _fixture.DeleteMarketplaceToolData();
            _fixture.DeleteTenantToolData();
            _fixture.AddTenantContext("NonExistentTenant");

            var mediator = _fixture.GetMediator();
            var createCommand = GetCreateMarketplaceToolCommand();

            // First create with valid tenant context
            _fixture.AddTenantContext("Test");
            var createResult = (SuccessResult<MarketplaceToolCreateResult>)await mediator.Send(createCommand, TestContext.Current.CancellationToken);

            // Then try to install with invalid tenant context
            _fixture.AddTenantContext("NonExistentTenant");
            var installCommand = new InstallMarketplaceToolCommand
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

        [Fact]
        public async Task InstallMarketplaceTool_WithDeletedMarketplaceTool_ShouldReturnNotFoundResult()
        {
            // Arrange
            _fixture.DeleteMarketplaceToolData();
            _fixture.DeleteTenantToolData();
            _fixture.DeleteUserInstallData();
            _fixture.DeleteUserData();
            _fixture.AddTenantContext("Test");
            _fixture.AddUserContext("test@example.com", "TestUser", "test-user-id");

            var mediator = _fixture.GetMediator();
            var createCommand = GetCreateMarketplaceToolCommand();
            var createResult = (SuccessResult<MarketplaceToolCreateResult>)await mediator.Send(createCommand, TestContext.Current.CancellationToken);

            // Manually mark the tool as deleted in database
            var db = _fixture.GetDbContext();
            var marketplaceTool = await db.MarketplaceTools.FirstOrDefaultAsync(m => m.Id == createResult.Data.Id, TestContext.Current.CancellationToken);
            marketplaceTool.IsDeleted = true;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var installCommand = new InstallMarketplaceToolCommand
            {
                Id = createResult.Data.Id
            };

            // Act
            var result = (Result)await mediator.Send(installCommand, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<NotFoundResult>(result);
            var notFoundResult = (NotFoundResult)result;
            Assert.Contains(notFoundResult.Errors, m => m.Code == ErrorConst.ToolNotFound);
        }

        private static CreateMarketplaceToolCommand GetCreateMarketplaceToolCommand()
        {
            return new CreateMarketplaceToolCommand
            {
                Name = "Test Marketplace Tool",
                Description = "Test Marketplace Tool Description",
                Type = "API",
                Config = "{\"endpoint\": \"https://api.example.com\"}"
            };
        }
    }
}