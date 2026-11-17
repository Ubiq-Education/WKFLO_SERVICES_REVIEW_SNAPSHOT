using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Results;
using AwesomeAssertions;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.FunctionalTests.EndPoints.TenantAgents
{
    public class TenantAgentsChatMessagesBySessionIdListEndPointTests : IClassFixture<TestFixture>
    {
        private readonly TestFixture _fixture;
        private const string UrlTemplate = "/api/v1/tenant/agent/{0}/chat/{1}/session-messages";

        public TenantAgentsChatMessagesBySessionIdListEndPointTests(TestFixture fixture)
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
        public async Task GetChatMessagesBySessionId_NoParticipatingAgents_ReturnsOkWithEmptyList()
        {
            var db = _fixture.GetDbContext();
            var tenant = new Tenant { Name = "SessionMsgTenant1", Region = "US" };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var agent = new TenantAgent { Name = "Lone Agent", SystemPrompt = "p", IsActive = true, TenantId = tenant.Id };
            db.TenantAgents.Add(agent);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            SetAuthAndTenantHeaders(tenant.Name);

            var response = await _fixture.Client.GetAsync(
                string.Format(UrlTemplate, agent.Id, "session-with-no-conversations"), TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var result = JsonConvert.DeserializeObject<SuccessResult<List<TenantAgentChatMessageResult>>>(body);

            Assert.NotNull(result);
            Assert.Empty(result.Data);
        }

        [Fact]
        public async Task GetChatMessagesBySessionId_NonExistentAgentId_ReturnsBadRequest()
        {
            var db = _fixture.GetDbContext();
            var tenant = new Tenant { Name = "SessionMsgTenant2", Region = "US" };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            SetAuthAndTenantHeaders(tenant.Name);

            var response = await _fixture.Client.GetAsync(
                string.Format(UrlTemplate, 999999, "some-session"), TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task GetChatMessagesBySessionId_NoTenantHeader_ReturnsBadRequest()
        {
            var token = TokenBuilder.BuildJWTToken();
            _fixture.Client.DefaultRequestHeaders.Clear();
            _fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            // No tenant header

            var response = await _fixture.Client.GetAsync(
                string.Format(UrlTemplate, 1, "some-session"), TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task GetChatMessagesBySessionId_NoAuthorizationHeader_ReturnsUnauthorized()
        {
            _fixture.Client.DefaultRequestHeaders.Clear();

            var response = await _fixture.Client.GetAsync(
                string.Format(UrlTemplate, 1, "some-session"), TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }
}
