using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.Tools.Commands.CreateMarketplaceTool;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using AMAIS.WKFLO.MD.Application.Tools.Queries.GetMarketplaceToolById;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.EndPoints.Tools
{
    public class InstallMarketplaceToolCommandEndPointTests(TestFixture fixture) : IClassFixture<TestFixture>
    {
        private TestFixture Fixture => fixture;

        private const string InstallUrlTemplate = "/api/v1/marketplace/tools/{0}/install";

        private void ClearHeader()
        {
            Fixture.Client.DefaultRequestHeaders.Clear();
        }

        [Fact]
        public async Task InstallMarketplaceTool_ResultOk()
        {
            // Arrange
            Fixture.DeleteMarketplaceToolData();
            Fixture.DeleteTenantToolData();
            Fixture.AddTenantContext("Test");

            var mediator = Fixture.GetMediator();
            var createCommand = GetCreateMarketplaceToolCommand();
            var createResult = (SuccessResult<MarketplaceToolCreateResult>)await mediator.Send(createCommand, TestContext.Current.CancellationToken);

            var installUrl = string.Format(InstallUrlTemplate, createResult.Data.Id);
            var token = TokenBuilder.BuildJWTToken();

            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test");

            var expected = new SuccessResult<TenantToolCreateResult>(new TenantToolCreateResult()
            {
                Name = "Test Marketplace Tool",
                Description = "Test Marketplace Tool Description",
                Type = "API",
                Config = "{\"endpoint\": \"https://api.example.com\"}",
                IsActive = true
            });

            // Act
            var response = await Fixture.Client.PostAsync(installUrl, null, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var responseObject = JsonConvert.DeserializeObject<SuccessResult<TenantToolCreateResult>>(responseBody);

            expected.Data.Id = responseObject.Data.Id;
            expected.Data.CreatedOn = responseObject.Data.CreatedOn;
            responseObject.Should().BeEquivalentTo(expected);

            var successResult = responseObject.Data;
            Assert.IsType<int>(successResult.Id);
            Assert.NotEqual(default, successResult.Id);
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
            Fixture.DeleteMarketplaceToolData();
            Fixture.DeleteTenantToolData();
            Fixture.AddTenantContext("Test");

            var mediator = Fixture.GetMediator();
            var createCommand = GetCreateMarketplaceToolCommand();
            var createResult = (SuccessResult<MarketplaceToolCreateResult>)await mediator.Send(createCommand, TestContext.Current.CancellationToken);

            var installUrl = string.Format(InstallUrlTemplate, createResult.Data.Id);
            var token = TokenBuilder.BuildJWTToken();

            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test");

            // Act - Install the same marketplace tool twice for the same tenant
            var firstResponse = await Fixture.Client.PostAsync(installUrl, null, TestContext.Current.CancellationToken);
            var firstResponseBody = await firstResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var firstResult = JsonConvert.DeserializeObject<SuccessResult<TenantToolCreateResult>>(firstResponseBody);

            var secondResponse = await Fixture.Client.PostAsync(installUrl, null, TestContext.Current.CancellationToken);
            var secondResponseBody = await secondResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var secondResult = JsonConvert.DeserializeObject<SuccessResult<TenantToolCreateResult>>(secondResponseBody);

            // Assert - the second install short-circuits to the SAME TenantTool, no "(Copy 1)" duplicate
            firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            Assert.NotNull(firstResult.Data);
            Assert.NotNull(secondResult.Data);
            Assert.Equal(firstResult.Data.Id, secondResult.Data.Id);
            Assert.Equal("Test Marketplace Tool", secondResult.Data.Name);

            var db = Fixture.GetDbContext();
            var tenantToolCount = await db.TenantTools.CountAsync(
                tt => tt.InstalledFromMarketplaceToolId == createResult.Data.Id && !tt.IsDeleted,
                TestContext.Current.CancellationToken);
            Assert.Equal(1, tenantToolCount);
        }

        [Fact]
        public async Task InstallMarketplaceTool_ShouldIncreaseLiveInstallationsCount()
        {
            // Arrange
            Fixture.DeleteMarketplaceToolData();
            Fixture.DeleteTenantToolData();
            Fixture.AddTenantContext("Test");

            var mediator = Fixture.GetMediator();
            var createCommand = GetCreateMarketplaceToolCommand();
            var createResult = (SuccessResult<MarketplaceToolCreateResult>)await mediator.Send(createCommand, TestContext.Current.CancellationToken);

            var installUrl = string.Format(InstallUrlTemplate, createResult.Data.Id);
            var token = TokenBuilder.BuildJWTToken();

            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test");

            // Get initial (live-computed) installations count
            var getQuery = new GetMarketplaceToolByIdQuery() { Id = createResult.Data.Id };
            var initialGet = (SuccessResult<MarketplaceToolGetResult>)await mediator.Send(getQuery, TestContext.Current.CancellationToken);
            var initialInstallations = initialGet.Data.Installations;

            // Act - install once
            var response = await Fixture.Client.PostAsync(installUrl, null, TestContext.Current.CancellationToken);

            // Assert - count increased by 1
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var afterFirstInstall = (SuccessResult<MarketplaceToolGetResult>)await mediator.Send(getQuery, TestContext.Current.CancellationToken);
            Assert.Equal(initialInstallations + 1, afterFirstInstall.Data.Installations);

            // Act - install AGAIN for the SAME tenant (dedup short-circuit)
            var secondResponse = await Fixture.Client.PostAsync(installUrl, null, TestContext.Current.CancellationToken);

            // Assert - count does NOT increase further
            secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var afterSecondInstall = (SuccessResult<MarketplaceToolGetResult>)await mediator.Send(getQuery, TestContext.Current.CancellationToken);
            Assert.Equal(initialInstallations + 1, afterSecondInstall.Data.Installations);
        }

        [Fact]
        public async Task InstallMarketplaceTool_ResultInvalid_MarketplaceToolMustExist()
        {
            // Arrange
            Fixture.DeleteMarketplaceToolData();
            Fixture.DeleteTenantToolData();
            Fixture.AddTenantContext("Test");

            var installUrl = string.Format(InstallUrlTemplate, 999); // Non-existent ID
            var token = TokenBuilder.BuildJWTToken();

            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test");

            var expected = new NotFoundResult();
            expected.AddError(nameof(AMAIS.WKFLO.Common.MD.Domain.Entities.MarketplaceTool), ErrorConst.ToolNotFound);

            // Act
            var response = await Fixture.Client.PostAsync(installUrl, null, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var notFoundResult = JsonConvert.DeserializeObject<NotFoundResult>(responseBody);

            Assert.NotEmpty(notFoundResult.Errors);
            Assert.Contains(notFoundResult.Errors, m => m.Code == ErrorConst.ToolNotFound);
        }

        [Fact]
        public async Task InstallMarketplaceTool_ResultInvalid_TenantMustExist()
        {
            // Arrange
            Fixture.DeleteMarketplaceToolData();
            Fixture.DeleteTenantToolData();
            Fixture.AddTenantContext("Test");

            var mediator = Fixture.GetMediator();
            var createCommand = GetCreateMarketplaceToolCommand();
            var createResult = (SuccessResult<MarketplaceToolCreateResult>)await mediator.Send(createCommand, TestContext.Current.CancellationToken);

            var installUrl = string.Format(InstallUrlTemplate, createResult.Data.Id);
            var token = TokenBuilder.BuildJWTToken();

            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            // No tenant header - should fail

            var expected = new InvalidResult();
            expected.AddError(nameof(TenantContext.TenantName), ErrorConst.TenantInvalid);
            expected.AddError(nameof(TenantContext.TenantName), ErrorConst.TenantMustExist);

            // Act
            var response = await Fixture.Client.PostAsync(installUrl, null, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var invalidResult = JsonConvert.DeserializeObject<InvalidResult>(responseBody);

            Assert.NotEmpty(invalidResult.Errors);
            Assert.Contains(invalidResult.Errors, m => m.Code == ErrorConst.TenantInvalid && m.Property == nameof(TenantContext.TenantName));
        }

        [Fact]
        public async Task InstallMarketplaceTool_WithInvalidId_ShouldReturnBadRequest()
        {
            // Arrange
            var installUrl = string.Format(InstallUrlTemplate, 0); // Invalid ID
            var token = TokenBuilder.BuildJWTToken();

            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test");

            // Act
            var response = await Fixture.Client.PostAsync(installUrl, null, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var invalidResult = JsonConvert.DeserializeObject<InvalidResult>(responseBody);

            Assert.NotEmpty(invalidResult.Errors);
            Assert.Contains(invalidResult.Errors, m => m.Code == ErrorConst.MustBePositive);
        }

        [Fact]
        public async Task InstallMarketplaceTool_WithDeletedMarketplaceTool_ShouldReturnNotFound()
        {
            // Arrange
            Fixture.DeleteMarketplaceToolData();
            Fixture.DeleteTenantToolData();
            Fixture.AddTenantContext("Test");

            var mediator = Fixture.GetMediator();
            var createCommand = GetCreateMarketplaceToolCommand();
            var createResult = (SuccessResult<MarketplaceToolCreateResult>)await mediator.Send(createCommand, TestContext.Current.CancellationToken);

            // Manually mark the tool as deleted in database
            var db = Fixture.GetDbContext();
            var marketplaceTool = await db.MarketplaceTools.FirstOrDefaultAsync(m => m.Id == createResult.Data.Id, TestContext.Current.CancellationToken);
            marketplaceTool.IsDeleted = true;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var installUrl = string.Format(InstallUrlTemplate, createResult.Data.Id);
            var token = TokenBuilder.BuildJWTToken();

            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test");

            // Act
            var response = await Fixture.Client.PostAsync(installUrl, null, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var notFoundResult = JsonConvert.DeserializeObject<NotFoundResult>(responseBody);

            Assert.Contains(notFoundResult.Errors, m => m.Code == ErrorConst.ToolNotFound);
        }

        [Fact]
        public async Task InstallMarketplaceTool_WithoutAuthentication_ShouldReturnUnauthorized()
        {
            // Arrange
            Fixture.DeleteMarketplaceToolData();
            Fixture.DeleteTenantToolData();
            Fixture.AddTenantContext("Test");

            var installUrl = string.Format(InstallUrlTemplate, 1);

            ClearHeader();
            // No authorization header

            // Act
            var response = await Fixture.Client.PostAsync(installUrl, null, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(-100)]
        [InlineData(int.MinValue)]
        public async Task InstallMarketplaceTool_WithNegativeId_ShouldReturnBadRequest(int invalidId)
        {
            // Arrange
            var installUrl = string.Format(InstallUrlTemplate, invalidId);
            var token = TokenBuilder.BuildJWTToken();

            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test");

            // Act
            var response = await Fixture.Client.PostAsync(installUrl, null, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var invalidResult = JsonConvert.DeserializeObject<InvalidResult>(responseBody);

            Assert.NotEmpty(invalidResult.Errors);
            Assert.Contains(invalidResult.Errors, m => m.Code == ErrorConst.MustBePositive);
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
