using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Data;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.AssignAllowedSpinUpAgent;
using AMAIS.WKFLO.MD.Application.Agents.Commands.CreateTenantAgent;
using AMAIS.WKFLO.MD.Application.Agents.Commands.PublishTenantAgent;
using AMAIS.WKFLO.MD.Application.Agents.Commands.UnassignAllowedSpinUpAgent;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.EndPoints.Agents
{
    public class PublishTenantAgentCommandEndPointTests(TestFixture fixture) : IClassFixture<TestFixture>
    {
        private TestFixture Fixture => fixture;

        private const string PublishUrlTemplate = "/api/v1/tenant/agent/{0}/publish";

        private void ClearHeader()
        {
            Fixture.Client.DefaultRequestHeaders.Clear();
        }

        // DeleteAllAgentData() clears TenantAgents/MarketplaceAgents/AgentTools but not
        // PublishedTenantAgents, so each test also clears those directly to avoid cross-test pollution
        // within this class's shared TestFixture/in-memory database. AgentAllowedSpinUpAgent rows must
        // be cleared first — both its FKs are DeleteBehavior.Restrict against Agent, so clearing
        // TenantAgents/PublishedTenantAgents first would fail with an FK violation.
        private void DeleteAllAgentDataIncludingPublished()
        {
            var db = (MasterDataDbContext)Fixture.GetDbContext();
            db.AgentAllowedSpinUpAgents.Clear();
            db.SaveChanges();

            // TenantAgent<->PublishedTenantAgent is a circular FK: TenantAgent.PublishedTenantAgentId
            // isn't modeled as an EF relationship (a bare FK column EF won't null automatically), and
            // PublishedTenantAgent.CreatedByTenantAgentId (which IS modeled) still blocks deleting the
            // TenantAgent it references unless the PublishedTenantAgent row is gone first. Null the
            // unmodeled FK, then clear PublishedTenantAgents, BEFORE TenantAgents are cleared below.
            foreach (var tenantAgent in db.TenantAgents.Where(m => m.PublishedTenantAgentId != null))
                tenantAgent.PublishedTenantAgentId = null;
            db.PublishedTenantAgents.Clear();
            db.SaveChanges();

            Fixture.DeleteAllAgentData();
        }

        [Fact]
        public async Task PublishTenantAgent_ResultOk_EnableSpinUpAgentsFlowsUnchanged()
        {
            // Arrange
            DeleteAllAgentDataIncludingPublished();
            Fixture.AddTenantContext("Test");
            var mediator = Fixture.GetMediator();
            var createCommand = GetCreateCommandTenantAgent();
            var createResult = (SuccessResult<TenantAgentCreateResult>)await mediator.Send(createCommand, TestContext.Current.CancellationToken);

            var publishUrl = string.Format(PublishUrlTemplate, createResult.Data.Id);
            var token = TokenBuilder.BuildJWTToken();

            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test");

            var expected = new SuccessResult<TenantAgentPublishResult>(new TenantAgentPublishResult()
            {
                Name = "Test Name",
                Description = "Test Description",
                SystemPrompt = "Test System Prompt",
                AgentType = "PublishedTenantAgent",
                IsActive = true,
                EnableSTM = false,
                EnableLTM = false,
                EnableSpinUpAgents = true,
                CreatedByTenantAgentId = createResult.Data.Id,
                IsDeleted = false
            });

            // Act
            var response = await Fixture.Client.PostAsync(publishUrl, null, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var responseObject = JsonConvert.DeserializeObject<SuccessResult<TenantAgentPublishResult>>(responseBody);

            expected.Data.Id = responseObject.Data.Id;
            expected.Data.CreatedOn = responseObject.Data.CreatedOn;
            expected.Data.LastUpdatedOn = responseObject.Data.LastUpdatedOn;
            responseObject.Should().BeEquivalentTo(expected);

            var successResult = responseObject.Data;
            Assert.IsType<int>(successResult.Id);
            Assert.NotEqual(default, successResult.Id);
            Assert.True(successResult.EnableSpinUpAgents); // must flow unchanged from the source TenantAgent

            // Verify the PublishedTenantAgent persisted in the DB also carries the flag unchanged
            var db = Fixture.GetDbContext();
            var publishedAgent = await db.PublishedTenantAgents
                .FirstOrDefaultAsync(m => m.Id == successResult.Id, TestContext.Current.CancellationToken);
            Assert.NotNull(publishedAgent);
            Assert.True(publishedAgent.EnableSpinUpAgents);
        }

        [Fact]
        public async Task PublishTenantAgent_RepublishAfterDisablingFlag_UpdatesExistingPublishedAgent()
        {
            // Arrange
            DeleteAllAgentDataIncludingPublished();
            Fixture.AddTenantContext("Test");
            var mediator = Fixture.GetMediator();
            var createCommand = GetCreateCommandTenantAgent();
            var createResult = (SuccessResult<TenantAgentCreateResult>)await mediator.Send(createCommand, TestContext.Current.CancellationToken);

            var publishUrl = string.Format(PublishUrlTemplate, createResult.Data.Id);
            var token = TokenBuilder.BuildJWTToken();

            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test");

            // Act - publish once with the flag enabled
            var firstResponse = await Fixture.Client.PostAsync(publishUrl, null, TestContext.Current.CancellationToken);
            firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var firstResult = JsonConvert.DeserializeObject<SuccessResult<TenantAgentPublishResult>>(
                await firstResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.True(firstResult.Data.EnableSpinUpAgents);

            // Disable the flag on the source TenantAgent, then republish
            var db = Fixture.GetDbContext();
            var tenantAgent = await db.TenantAgents.FirstOrDefaultAsync(m => m.Id == createResult.Data.Id, TestContext.Current.CancellationToken);
            tenantAgent.EnableSpinUpAgents = false;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var secondResponse = await Fixture.Client.PostAsync(publishUrl, null, TestContext.Current.CancellationToken);

            // Assert
            secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var secondResult = JsonConvert.DeserializeObject<SuccessResult<TenantAgentPublishResult>>(
                await secondResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

            Assert.Equal(firstResult.Data.Id, secondResult.Data.Id); // same PublishedTenantAgent updated, not duplicated
            Assert.False(secondResult.Data.EnableSpinUpAgents);
        }

        [Fact]
        public async Task PublishTenantAgent_ResultInvalid_AgentMustExist()
        {
            // Arrange
            DeleteAllAgentDataIncludingPublished();
            Fixture.AddTenantContext("Test");

            var publishUrl = string.Format(PublishUrlTemplate, 999); // Non-existent id
            var token = TokenBuilder.BuildJWTToken();

            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test");

            var expected = new InvalidResult(nameof(Agent), ErrorConst.AgentMustExist);

            // Act
            var response = await Fixture.Client.PostAsync(publishUrl, null, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var invalidResult = JsonConvert.DeserializeObject<InvalidResult>(responseBody);
            invalidResult.Should().BeEquivalentTo(expected);
            Assert.NotEmpty(invalidResult.Errors);
            Assert.Contains(invalidResult.Errors, m => m.Code == ErrorConst.AgentMustExist && m.Property == nameof(Agent));
        }

        [Fact]
        public async Task PublishTenantAgent_ResultInvalid_ThereIsNoTenant()
        {
            // Arrange
            DeleteAllAgentDataIncludingPublished();

            var publishUrl = string.Format(PublishUrlTemplate, 1);
            var token = TokenBuilder.BuildJWTToken();

            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            // No headers for Tenant Context

            var expected = new InvalidResult();
            expected.AddError(nameof(TenantContext.TenantName), ErrorConst.TenantInvalid);
            expected.AddError(nameof(TenantContext.TenantName), ErrorConst.TenantMustExist);
            expected.AddError(nameof(Agent), ErrorConst.AgentMustExist);

            // Act
            var response = await Fixture.Client.PostAsync(publishUrl, null, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var invalidResult = JsonConvert.DeserializeObject<InvalidResult>(responseBody);
            invalidResult.Should().BeEquivalentTo(expected);
            Assert.NotEmpty(invalidResult.Errors);
            Assert.Contains(invalidResult.Errors, m => m.Code == ErrorConst.TenantInvalid && m.Property == nameof(TenantContext.TenantName));
            Assert.Contains(invalidResult.Errors, m => m.Code == ErrorConst.TenantMustExist && m.Property == nameof(TenantContext.TenantName));
        }

        [Fact]
        public async Task PublishTenantAgent_CopiesAllowlistOntoPublishedAgent_DroppingUnpublishedTargets()
        {
            // Arrange
            DeleteAllAgentDataIncludingPublished();
            Fixture.AddTenantContext("Test");
            var mediator = Fixture.GetMediator();

            var sourceResult = (SuccessResult<TenantAgentCreateResult>)await mediator.Send(
                new CreateTenantAgentCommand { Name = "Source Agent", Description = "d", SystemPrompt = "p", ResponseTone = "Friendly", IsActive = true },
                TestContext.Current.CancellationToken);

            var unpublishedTargetResult = (SuccessResult<TenantAgentCreateResult>)await mediator.Send(
                new CreateTenantAgentCommand { Name = "Unpublished Target", Description = "d", SystemPrompt = "p", ResponseTone = "Friendly", IsActive = true },
                TestContext.Current.CancellationToken);

            var publishedTargetResult = (SuccessResult<TenantAgentCreateResult>)await mediator.Send(
                new CreateTenantAgentCommand { Name = "Published Target", Description = "d", SystemPrompt = "p", ResponseTone = "Friendly", IsActive = true },
                TestContext.Current.CancellationToken);

            var publishedTargetPublishResult = (SuccessResult<TenantAgentPublishResult>)await mediator.Send(
                new PublishTenantAgentCommand { Id = publishedTargetResult.Data.Id }, TestContext.Current.CancellationToken);

            var assignUnpublishedResult = await mediator.Send(
                new AssignAllowedSpinUpAgentCommand { Id = sourceResult.Data.Id, AllowedSpinUpAgentId = unpublishedTargetResult.Data.Id },
                TestContext.Current.CancellationToken);
            var assignPublishedResult = await mediator.Send(
                new AssignAllowedSpinUpAgentCommand { Id = sourceResult.Data.Id, AllowedSpinUpAgentId = publishedTargetResult.Data.Id },
                TestContext.Current.CancellationToken);

            Assert.Equal(ResultType.Ok, assignUnpublishedResult.ResultType);
            Assert.Equal(ResultType.Ok, assignPublishedResult.ResultType);

            var preHttpAllowlistCount = await Fixture.GetDbContext().AgentAllowedSpinUpAgents
                .AsNoTracking()
                .Where(m => m.AgentId == sourceResult.Data.Id)
                .CountAsync(TestContext.Current.CancellationToken);
            Assert.Equal(2, preHttpAllowlistCount);

            var preHttpTargetPublishedId = await Fixture.GetDbContext().TenantAgents
                .AsNoTracking()
                .Where(m => m.Id == publishedTargetResult.Data.Id)
                .Select(m => m.PublishedTenantAgentId)
                .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
            Assert.Equal(publishedTargetPublishResult.Data.Id, preHttpTargetPublishedId);

            var publishUrl = string.Format(PublishUrlTemplate, sourceResult.Data.Id);
            var token = TokenBuilder.BuildJWTToken();

            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test");

            // Act
            var response = await Fixture.Client.PostAsync(publishUrl, null, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var responseObject = JsonConvert.DeserializeObject<SuccessResult<TenantAgentPublishResult>>(responseBody);

            var db = Fixture.GetDbContext();
            var copiedAllowlist = await db.AgentAllowedSpinUpAgents
                .AsNoTracking()
                .Where(m => m.AgentId == responseObject.Data.Id)
                .ToListAsync(TestContext.Current.CancellationToken);

            Assert.Single(copiedAllowlist);
            Assert.Equal(publishedTargetPublishResult.Data.Id, copiedAllowlist[0].AllowedSpinUpAgentId);
        }

        [Fact]
        public async Task PublishTenantAgent_Republish_FullyReplacesAllowlistNotMerges()
        {
            // Arrange
            DeleteAllAgentDataIncludingPublished();
            Fixture.AddTenantContext("Test");
            var mediator = Fixture.GetMediator();

            var sourceResult = (SuccessResult<TenantAgentCreateResult>)await mediator.Send(
                new CreateTenantAgentCommand { Name = "Source Agent 2", Description = "d", SystemPrompt = "p", ResponseTone = "Friendly", IsActive = true },
                TestContext.Current.CancellationToken);

            var targetOneResult = (SuccessResult<TenantAgentCreateResult>)await mediator.Send(
                new CreateTenantAgentCommand { Name = "Target One", Description = "d", SystemPrompt = "p", ResponseTone = "Friendly", IsActive = true },
                TestContext.Current.CancellationToken);
            await mediator.Send(new PublishTenantAgentCommand { Id = targetOneResult.Data.Id }, TestContext.Current.CancellationToken);

            var targetTwoResult = (SuccessResult<TenantAgentCreateResult>)await mediator.Send(
                new CreateTenantAgentCommand { Name = "Target Two", Description = "d", SystemPrompt = "p", ResponseTone = "Friendly", IsActive = true },
                TestContext.Current.CancellationToken);
            var targetTwoPublish = (SuccessResult<TenantAgentPublishResult>)await mediator.Send(
                new PublishTenantAgentCommand { Id = targetTwoResult.Data.Id }, TestContext.Current.CancellationToken);

            await mediator.Send(
                new AssignAllowedSpinUpAgentCommand { Id = sourceResult.Data.Id, AllowedSpinUpAgentId = targetOneResult.Data.Id },
                TestContext.Current.CancellationToken);

            var publishUrl = string.Format(PublishUrlTemplate, sourceResult.Data.Id);
            var token = TokenBuilder.BuildJWTToken();

            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test");

            var firstResponse = await Fixture.Client.PostAsync(publishUrl, null, TestContext.Current.CancellationToken);
            firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var firstResult = JsonConvert.DeserializeObject<SuccessResult<TenantAgentPublishResult>>(
                await firstResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

            // Swap the allowlist: drop target one, add target two
            await mediator.Send(
                new UnassignAllowedSpinUpAgentCommand { Id = sourceResult.Data.Id, AllowedSpinUpAgentId = targetOneResult.Data.Id },
                TestContext.Current.CancellationToken);
            await mediator.Send(
                new AssignAllowedSpinUpAgentCommand { Id = sourceResult.Data.Id, AllowedSpinUpAgentId = targetTwoResult.Data.Id },
                TestContext.Current.CancellationToken);

            // Act - republish
            var secondResponse = await Fixture.Client.PostAsync(publishUrl, null, TestContext.Current.CancellationToken);

            // Assert
            secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var secondResult = JsonConvert.DeserializeObject<SuccessResult<TenantAgentPublishResult>>(
                await secondResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

            Assert.Equal(firstResult.Data.Id, secondResult.Data.Id); // same PublishedTenantAgent updated, not duplicated

            var db = Fixture.GetDbContext();
            var allowlist = await db.AgentAllowedSpinUpAgents
                .AsNoTracking()
                .Where(m => m.AgentId == secondResult.Data.Id)
                .ToListAsync(TestContext.Current.CancellationToken);

            Assert.Single(allowlist);
            Assert.Equal(targetTwoPublish.Data.Id, allowlist[0].AllowedSpinUpAgentId);
        }

        private static CreateTenantAgentCommand GetCreateCommandTenantAgent()
        {
            return new CreateTenantAgentCommand
            {
                Name = "Test Name",
                Description = "Test Description",
                SystemPrompt = "Test System Prompt",
                ResponseTone = "Friendly",
                IsActive = true,
                EnableSpinUpAgents = true
            };
        }
    }
}
