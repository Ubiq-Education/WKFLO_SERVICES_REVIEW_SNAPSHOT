using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.Results;
using AwesomeAssertions;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.FunctionalTests.EndPoints.Public.Agents
{
    public class PublicAgentSpinUpAgentsEndPointTests : IClassFixture<TestFixture>
    {
        private readonly TestFixture _fixture;
        private const string UrlTemplate = "/api/v1/agent/{0}/spin-up/agents";

        public PublicAgentSpinUpAgentsEndPointTests(TestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task GetSpinUpAgents_ExistingShareIdWithOthersInTenant_ReturnsOkWithOtherNamesNoAuthRequired()
        {
            var db = _fixture.GetDbContext();
            var tenant = new Tenant { Name = "PublicSpinUpTenant1", Region = "US" };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var self = new PublishedTenantAgent { Name = "Self Agent", SystemPrompt = "p", IsActive = true, TenantId = tenant.Id };
            var other = new PublishedTenantAgent { Name = "Other Agent", SystemPrompt = "p", IsActive = true, TenantId = tenant.Id };
            db.PublishedTenantAgents.AddRange(self, other);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var chatSetting = new AgentChatSetting { ShareId = Guid.NewGuid().ToString(), AgentId = self.Id };
            db.AgentChatSettings.Add(chatSetting);
            db.AgentChatSettings.Add(new AgentChatSetting { AgentId = other.Id, ChatbotIconPath = "https://cdn.test/other-agent-icon.png" });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            db.AgentAllowedSpinUpAgents.Add(new AgentAllowedSpinUpAgent { AgentId = self.Id, AllowedSpinUpAgentId = other.Id });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            _fixture.Client.DefaultRequestHeaders.Clear();

            var response = await _fixture.Client.GetAsync(string.Format(UrlTemplate, chatSetting.ShareId), TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var result = JsonConvert.DeserializeObject<SuccessResult<List<AgentSpinUpTargetResult>>>(body);

            Assert.NotNull(result);
            var names = result.Data.Select(t => t.Name).ToList();
            Assert.Contains("Other Agent", names);
            Assert.DoesNotContain("Self Agent", names);

            var otherTarget = result.Data.Single(t => t.Name == "Other Agent");
            Assert.Equal("https://cdn.test/other-agent-icon.png", otherTarget.IconUrl);
        }

        [Fact]
        public async Task GetSpinUpAgents_NonExistentShareId_ReturnsNotFound()
        {
            _fixture.Client.DefaultRequestHeaders.Clear();

            var response = await _fixture.Client.GetAsync(string.Format(UrlTemplate, "nonexistent-share-id"), TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Theory]
        [InlineData("   ")]
        public async Task GetSpinUpAgents_WhitespaceShareId_ReturnsBadRequest(string invalidShareId)
        {
            _fixture.Client.DefaultRequestHeaders.Clear();

            var response = await _fixture.Client.GetAsync(string.Format(UrlTemplate, invalidShareId), TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
    }
}
