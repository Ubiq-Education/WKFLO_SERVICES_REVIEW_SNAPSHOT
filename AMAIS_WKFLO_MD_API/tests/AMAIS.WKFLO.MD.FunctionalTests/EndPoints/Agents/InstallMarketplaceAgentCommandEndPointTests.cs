using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.CreateMarketplaceAgent;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Agents.Queries.GetMarketplaceAgentById;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System;
using System.Net;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.EndPoints.Agents
{
    public class InstallMarketplaceAgentCommandEndPointTests(TestFixture fixture) : IClassFixture<TestFixture>
    {
        private TestFixture Fixture => fixture;

        private const string InstallUrlTemplate = "/api/v1/marketplace/agents/{0}/install";

        private void ClearHeader()
        {
            Fixture.Client.DefaultRequestHeaders.Clear();
        }

        [Fact]
        public async Task InstallMarketplaceAgent_ResultOk()
        {
            // Arrange
            Fixture.DeleteMarketplaceAgentData();
            Fixture.DeleteTenantAgentData();
            Fixture.AddTenantContext("Test");

            var mediator = Fixture.GetMediator();
            var createCommand = GetCreateMarketplaceAgentCommand();
            var createResult = (SuccessResult<MarketplaceAgentCreateResult>)await mediator.Send(createCommand, TestContext.Current.CancellationToken);

            var installUrl = string.Format(InstallUrlTemplate, createResult.Data.Id);
            var token = TokenBuilder.BuildJWTToken();

            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test");

            var expected = new SuccessResult<TenantAgentCreateResult>(new TenantAgentCreateResult()
            {
                Name = "Test Marketplace Agent",
                Description = "Test Marketplace Agent Description",
                SystemPrompt = "Test System Prompt",
                MaxToolCallsPerTurn = 3,
                IsActive = true,
                EnableSTM = false,
                EnableLTM = false,
                EnableSpinUpAgents = false,
                LastRunAt = null
            });

            // Act
            var response = await Fixture.Client.PostAsync(installUrl, null, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var responseObject = JsonConvert.DeserializeObject<SuccessResult<TenantAgentCreateResult>>(responseBody);

            expected.Data.Id = responseObject.Data.Id;
            expected.Data.CreatedOn = responseObject.Data.CreatedOn;
            responseObject.Should().BeEquivalentTo(expected);

            var successResult = responseObject.Data;
            Assert.IsType<int>(successResult.Id);
            Assert.NotEqual(default, successResult.Id);
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
            Fixture.DeleteMarketplaceAgentData();
            Fixture.DeleteTenantAgentData();
            Fixture.AddTenantContext("Test");

            var mediator = Fixture.GetMediator();
            var createCommand = GetCreateMarketplaceAgentCommand();
            var createResult = (SuccessResult<MarketplaceAgentCreateResult>)await mediator.Send(createCommand, TestContext.Current.CancellationToken);

            var installUrl = string.Format(InstallUrlTemplate, createResult.Data.Id);
            var token = TokenBuilder.BuildJWTToken();

            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test");

            // Act - Install the same marketplace agent twice for the same tenant
            var firstResponse = await Fixture.Client.PostAsync(installUrl, null, TestContext.Current.CancellationToken);
            var firstResponseBody = await firstResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var firstResult = JsonConvert.DeserializeObject<SuccessResult<TenantAgentCreateResult>>(firstResponseBody);

            var secondResponse = await Fixture.Client.PostAsync(installUrl, null, TestContext.Current.CancellationToken);
            var secondResponseBody = await secondResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var secondResult = JsonConvert.DeserializeObject<SuccessResult<TenantAgentCreateResult>>(secondResponseBody);

            // Assert - the second install short-circuits to the SAME TenantAgent, no "(Copy 1)" duplicate
            firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            Assert.NotNull(firstResult.Data);
            Assert.NotNull(secondResult.Data);
            Assert.Equal(firstResult.Data.Id, secondResult.Data.Id);
            Assert.Equal("Test Marketplace Agent", secondResult.Data.Name);

            var db = Fixture.GetDbContext();
            var tenantAgentCount = await db.TenantAgents.CountAsync(
                ta => ta.InstalledFromMarketplaceAgentId == createResult.Data.Id && !ta.IsDeleted,
                TestContext.Current.CancellationToken);
            Assert.Equal(1, tenantAgentCount);
        }

        [Fact]
        public async Task InstallMarketplaceAgent_ShouldIncreaseLiveInstallationsCount()
        {
            // Arrange
            Fixture.DeleteMarketplaceAgentData();
            Fixture.DeleteTenantAgentData();
            Fixture.AddTenantContext("Test");

            var mediator = Fixture.GetMediator();
            var createCommand = GetCreateMarketplaceAgentCommand();
            var createResult = (SuccessResult<MarketplaceAgentCreateResult>)await mediator.Send(createCommand, TestContext.Current.CancellationToken);

            var installUrl = string.Format(InstallUrlTemplate, createResult.Data.Id);
            var token = TokenBuilder.BuildJWTToken();

            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test");

            // Get initial (live-computed) installations count
            var getQuery = new GetMarketplaceAgentByIdQuery() { Id = createResult.Data.Id };
            var initialGet = (SuccessResult<MarketplaceAgentGetResult>)await mediator.Send(getQuery, TestContext.Current.CancellationToken);
            var initialInstallations = initialGet.Data.Installations;

            // Act - install once
            var response = await Fixture.Client.PostAsync(installUrl, null, TestContext.Current.CancellationToken);

            // Assert - count increased by 1
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var afterFirstInstall = (SuccessResult<MarketplaceAgentGetResult>)await mediator.Send(getQuery, TestContext.Current.CancellationToken);
            Assert.Equal(initialInstallations + 1, afterFirstInstall.Data.Installations);

            // Act - install AGAIN for the SAME tenant (dedup short-circuit)
            var secondResponse = await Fixture.Client.PostAsync(installUrl, null, TestContext.Current.CancellationToken);

            // Assert - count does NOT increase further
            secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var afterSecondInstall = (SuccessResult<MarketplaceAgentGetResult>)await mediator.Send(getQuery, TestContext.Current.CancellationToken);
            Assert.Equal(initialInstallations + 1, afterSecondInstall.Data.Installations);
        }

        [Fact]
        public async Task InstallMarketplaceAgent_ResultInvalid_MarketplaceAgentMustExist()
        {
            // Arrange
            Fixture.DeleteMarketplaceAgentData();
            Fixture.DeleteTenantAgentData();
            Fixture.AddTenantContext("Test");

            var installUrl = string.Format(InstallUrlTemplate, 999); // Non-existent ID
            var token = TokenBuilder.BuildJWTToken();

            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test");

            var expected = new NotFoundResult();
            expected.AddError(nameof(AMAIS.WKFLO.Common.MD.Domain.Entities.MarketplaceAgent), ErrorConst.AgentNotFound);

            // Act
            var response = await Fixture.Client.PostAsync(installUrl, null, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var notFoundResult = JsonConvert.DeserializeObject<NotFoundResult>(responseBody);

            Assert.NotEmpty(notFoundResult.Errors);
            Assert.Contains(notFoundResult.Errors, m => m.Code == ErrorConst.AgentNotFound);
        }

        [Fact]
        public async Task InstallMarketplaceAgent_ResultInvalid_TenantMustExist()
        {
            // Arrange
            Fixture.DeleteMarketplaceAgentData();
            Fixture.DeleteTenantAgentData();
            Fixture.AddTenantContext("Test");

            var mediator = Fixture.GetMediator();
            var createCommand = GetCreateMarketplaceAgentCommand();
            var createResult = (SuccessResult<MarketplaceAgentCreateResult>)await mediator.Send(createCommand, TestContext.Current.CancellationToken);

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
        public async Task InstallMarketplaceAgent_WithInvalidId_ShouldReturnBadRequest()
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
        public async Task InstallMarketplaceAgent_WithDeletedMarketplaceAgent_ShouldReturnNotFound()
        {
            // Arrange
            Fixture.DeleteMarketplaceAgentData();
            Fixture.DeleteTenantAgentData();
            Fixture.AddTenantContext("Test");

            var mediator = Fixture.GetMediator();
            var createCommand = GetCreateMarketplaceAgentCommand();
            var createResult = (SuccessResult<MarketplaceAgentCreateResult>)await mediator.Send(createCommand, TestContext.Current.CancellationToken);

            // Manually mark the agent as deleted in database
            var db = Fixture.GetDbContext();
            var marketplaceAgent = await db.MarketplaceAgents.FirstOrDefaultAsync(m => m.Id == createResult.Data.Id, TestContext.Current.CancellationToken);
            marketplaceAgent.IsDeleted = true;
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

            Assert.Contains(notFoundResult.Errors, m => m.Code == ErrorConst.AgentNotFound);
        }

        [Fact]
        public async Task InstallMarketplaceAgent_WithoutAuthentication_ShouldReturnUnauthorized()
        {
            // Arrange
            Fixture.DeleteMarketplaceAgentData();
            Fixture.DeleteTenantAgentData();
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
        public async Task InstallMarketplaceAgent_WithNegativeId_ShouldReturnBadRequest(int invalidId)
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

        [Fact]
        public async Task InstallMarketplaceAgent_MultipleDifferentAgentsSameName_ShouldCreateSequentialCopies()
        {
            // Arrange - three genuinely DIFFERENT marketplace agents that happen to share a name;
            // reinstalling the SAME marketplace agent is covered by the dedup test above instead.
            Fixture.DeleteMarketplaceAgentData();
            Fixture.DeleteTenantAgentData();
            Fixture.AddTenantContext("Test");

            var mediator = Fixture.GetMediator();
            var createResult1 = (SuccessResult<MarketplaceAgentCreateResult>)await mediator.Send(GetCreateMarketplaceAgentCommand(), TestContext.Current.CancellationToken);
            var createResult2 = (SuccessResult<MarketplaceAgentCreateResult>)await mediator.Send(GetCreateMarketplaceAgentCommand(), TestContext.Current.CancellationToken);
            var createResult3 = (SuccessResult<MarketplaceAgentCreateResult>)await mediator.Send(GetCreateMarketplaceAgentCommand(), TestContext.Current.CancellationToken);

            var token = TokenBuilder.BuildJWTToken();
            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test");

            // Act - install each distinct marketplace agent (same Name) for the same tenant
            var firstResponse = await Fixture.Client.PostAsync(string.Format(InstallUrlTemplate, createResult1.Data.Id), null, TestContext.Current.CancellationToken);
            var firstResult = JsonConvert.DeserializeObject<SuccessResult<TenantAgentCreateResult>>(await firstResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

            var secondResponse = await Fixture.Client.PostAsync(string.Format(InstallUrlTemplate, createResult2.Data.Id), null, TestContext.Current.CancellationToken);
            var secondResult = JsonConvert.DeserializeObject<SuccessResult<TenantAgentCreateResult>>(await secondResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

            var thirdResponse = await Fixture.Client.PostAsync(string.Format(InstallUrlTemplate, createResult3.Data.Id), null, TestContext.Current.CancellationToken);
            var thirdResult = JsonConvert.DeserializeObject<SuccessResult<TenantAgentCreateResult>>(await thirdResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

            // Assert
            Assert.All(new[] { firstResponse, secondResponse, thirdResponse }, r => r.StatusCode.Should().Be(HttpStatusCode.OK));

            Assert.Equal("Test Marketplace Agent", firstResult.Data.Name);
            Assert.Equal("Test Marketplace Agent (Copy 1)", secondResult.Data.Name);
            Assert.Equal("Test Marketplace Agent (Copy 2)", thirdResult.Data.Name);
        }

        [Fact]
        public async Task InstallMarketplaceAgent_ShouldCreateTenantAgentWithCorrectDefaults()
        {
            // Arrange
            Fixture.DeleteMarketplaceAgentData();
            Fixture.DeleteTenantAgentData();
            Fixture.AddTenantContext("Test");

            var mediator = Fixture.GetMediator();
            var createCommand = GetCreateMarketplaceAgentCommand();
            var createResult = (SuccessResult<MarketplaceAgentCreateResult>)await mediator.Send(createCommand, TestContext.Current.CancellationToken);

            var installUrl = string.Format(InstallUrlTemplate, createResult.Data.Id);
            var token = TokenBuilder.BuildJWTToken();

            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test");

            // Act
            var response = await Fixture.Client.PostAsync(installUrl, null, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var result = JsonConvert.DeserializeObject<SuccessResult<TenantAgentCreateResult>>(responseBody);

            // Verify default values
            Assert.True(result.Data.IsActive); // Should be active by default
            Assert.False(result.Data.EnableSTM); // Should be false by default
            Assert.False(result.Data.EnableLTM); // Should be false by default
            Assert.False(result.Data.EnableSpinUpAgents); // Should be false by default
            Assert.Null(result.Data.LastRunAt); // Should be null initially
            Assert.True(result.Data.CreatedOn > DateTimeOffset.MinValue); // Should have creation date
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
