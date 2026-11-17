using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Results;
using AwesomeAssertions;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.FunctionalTests.EndPoints.Public.Agents
{
    public class PublicAgentsChatMessagesBySessionIdListEndPointTests : IClassFixture<TestFixture>
    {
        private readonly TestFixture _fixture;
        private const string UrlTemplate = "/api/v1/agent/{0}/chat/{1}/session-messages";

        public PublicAgentsChatMessagesBySessionIdListEndPointTests(TestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task GetChatMessagesBySessionId_NoParticipatingAgents_ReturnsOkWithEmptyList()
        {
            var db = _fixture.GetDbContext();
            var tenant = new Tenant { Name = "PublicSessionMsgTenant1", Region = "US" };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var agent = new PublishedTenantAgent { Name = "Self Agent", SystemPrompt = "p", IsActive = true, TenantId = tenant.Id };
            db.PublishedTenantAgents.Add(agent);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var chatSetting = new AgentChatSetting { ShareId = Guid.NewGuid().ToString(), AgentId = agent.Id, IsShareable = true };
            db.AgentChatSettings.Add(chatSetting);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            _fixture.Client.DefaultRequestHeaders.Clear();
            _fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, tenant.Name);

            var response = await _fixture.Client.GetAsync(
                string.Format(UrlTemplate, chatSetting.ShareId, "session-with-no-conversations"), TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var result = JsonConvert.DeserializeObject<SuccessResult<List<PublicAgentChatMessageResult>>>(body);

            Assert.NotNull(result);
            Assert.Empty(result.Data);
        }

        [Fact]
        public async Task GetChatMessagesBySessionId_NonExistentShareId_ReturnsUnauthorized()
        {
            _fixture.Client.DefaultRequestHeaders.Clear();

            var response = await _fixture.Client.GetAsync(
                string.Format(UrlTemplate, "nonexistent-share-id", "some-session"), TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }
}
