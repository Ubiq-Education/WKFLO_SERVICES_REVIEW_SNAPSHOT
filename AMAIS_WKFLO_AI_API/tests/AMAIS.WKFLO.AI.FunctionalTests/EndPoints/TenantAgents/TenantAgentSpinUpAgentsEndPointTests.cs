using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Results;
using AwesomeAssertions;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.FunctionalTests.EndPoints.TenantAgents
{
    public class TenantAgentSpinUpAgentsEndPointTests : IClassFixture<TestFixture>
    {
        private readonly TestFixture _fixture;
        private const string UrlTemplate = "/api/v1/tenant/agent/{0}/spin-up/agents";

        public TenantAgentSpinUpAgentsEndPointTests(TestFixture fixture)
        {
            _fixture = fixture;
        }

        private void SetAuthAndTenantHeaders(string tenantName)
        {
            var token = TokenBuilder.BuildJWTToken();
            _fixture.Client.DefaultRequestHeaders.Clear();
            _fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            _fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, tenantName);
        }

        [Fact]
        public async Task GetSpinUpAgents_ExistingAgentWithOthersInTenant_ReturnsOkWithOtherNames()
        {
            var db = _fixture.GetDbContext();
            var tenant = new Tenant { Name = "SpinUpTenant1", Region = "US" };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var self = new TenantAgent { Name = "Self Agent", SystemPrompt = "p", IsActive = true, TenantId = tenant.Id };
            var other = new TenantAgent { Name = "Other Agent", SystemPrompt = "p", IsActive = true, TenantId = tenant.Id };
            var iconless = new TenantAgent { Name = "Iconless Agent", SystemPrompt = "p", IsActive = true, TenantId = tenant.Id };
            db.TenantAgents.AddRange(self, other, iconless);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            db.AgentChatSettings.Add(new AgentChatSetting { AgentId = other.Id, ChatbotIconPath = "https://cdn.test/other-agent-icon.png" });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            db.AgentAllowedSpinUpAgents.AddRange(
                new AgentAllowedSpinUpAgent { AgentId = self.Id, AllowedSpinUpAgentId = other.Id },
                new AgentAllowedSpinUpAgent { AgentId = self.Id, AllowedSpinUpAgentId = iconless.Id });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            SetAuthAndTenantHeaders(tenant.Name);

            var response = await _fixture.Client.GetAsync(string.Format(UrlTemplate, self.Id), TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var result = JsonConvert.DeserializeObject<SuccessResult<List<AgentSpinUpTargetResult>>>(body);

            Assert.NotNull(result);
            var names = result.Data.Select(t => t.Name).ToList();
            Assert.Contains("Other Agent", names);
            Assert.DoesNotContain("Self Agent", names);

            var otherTarget = result.Data.Single(t => t.Name == "Other Agent");
            Assert.Equal("https://cdn.test/other-agent-icon.png", otherTarget.IconUrl);

            var iconlessTarget = result.Data.Single(t => t.Name == "Iconless Agent");
            Assert.Null(iconlessTarget.IconUrl);
        }

        [Fact]
        public async Task GetSpinUpAgents_NonExistentId_ReturnsNotFound()
        {
            var db = _fixture.GetDbContext();
            var tenant = new Tenant { Name = "SpinUpTenant2", Region = "US" };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            SetAuthAndTenantHeaders(tenant.Name);

            var response = await _fixture.Client.GetAsync(string.Format(UrlTemplate, 999999), TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task GetSpinUpAgents_NoAuthorizationHeader_ReturnsUnauthorized()
        {
            _fixture.Client.DefaultRequestHeaders.Clear();

            var response = await _fixture.Client.GetAsync(string.Format(UrlTemplate, 1), TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }
}
