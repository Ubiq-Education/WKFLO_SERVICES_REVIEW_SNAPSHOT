using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.WorkingState;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models;
using AwesomeAssertions;
using Dapr.Client;
using Moq;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.FunctionalTests.EndPoints.Public.Agents
{
    /// <summary>
    /// US 7506: GET /api/v1/agent/{shareId}/chat/{sessionId}/state against the production-equivalent pipeline, with the
    /// Dapr state store mocked by <see cref="WidgetChatIdentityTestFixture.DaprClientMock"/>.
    /// </summary>
    public class PublicAgentChatSessionStateTests : IClassFixture<WidgetChatIdentityTestFixture>
    {
        private readonly WidgetChatIdentityTestFixture _fixture;

        public PublicAgentChatSessionStateTests(WidgetChatIdentityTestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task GetState_SessionWithRunningWork_IsWorking()
        {
            var ct = TestContext.Current.CancellationToken;
            var db = _fixture.GetDbContext();
            var tenant = new Tenant { Name = "SessionStateTenant1", Region = "US" };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(ct);
            var agent = new PublishedTenantAgent { Name = "MAISY", SystemPrompt = "p", IsActive = true, TenantId = tenant.Id };
            db.PublishedTenantAgents.Add(agent);
            await db.SaveChangesAsync(ct);
            var chatSetting = new AgentChatSetting { ShareId = Guid.NewGuid().ToString(), AgentId = agent.Id, IsShareable = true };
            db.AgentChatSettings.Add(chatSetting);
            await db.SaveChangesAsync(ct);

            var working = new SessionWorkingState();
            working.Items["turn:1"] = new SessionWorkItem { StartedAt = DateTimeOffset.UtcNow.AddSeconds(-5), ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5) };
            _fixture.DaprClientMock
                .Setup(x => x.GetStateAsync<SessionWorkingState>(CommonConsts.CommonAiPubSubConsts.StateStoreName,
                    SessionWorkingStateConsts.BuildStateKey(tenant.Name, "working-session"),
                    It.IsAny<ConsistencyMode?>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(working);

            _fixture.Client.DefaultRequestHeaders.Clear();
            _fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, tenant.Name);

            var response = await _fixture.Client.GetAsync($"/api/v1/agent/{chatSetting.ShareId}/chat/working-session/state", ct);
            var content = await response.Content.ReadAsStringAsync(ct);

            response.StatusCode.Should().Be(HttpStatusCode.OK, content);
            JObject.Parse(content)["data"]!["isWorking"]!.Value<bool>().Should().BeTrue();
        }
    }
}
