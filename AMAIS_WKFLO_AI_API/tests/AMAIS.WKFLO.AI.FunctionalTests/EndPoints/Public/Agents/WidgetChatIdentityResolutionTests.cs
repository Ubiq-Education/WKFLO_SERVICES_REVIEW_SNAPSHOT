using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models;
using AwesomeAssertions;
using System;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.FunctionalTests.EndPoints.Public.Agents
{
    /// <summary>
    /// Runtime verification for Bug 7163 / Task 7164: "widget chat requests discard the
    /// authenticated visitor's identity before UserContext resolves it." Static tracing of
    /// Program.cs alone disagreed with observed production behavior (chats were seen stored under
    /// real user GUIDs), because it missed <c>MultiSchemeAuthenticationMiddleware</c> — these tests
    /// settle it against the actual pipeline instead of more code reading. See
    /// <see cref="WidgetChatIdentitySetup"/> for why a dedicated fixture was needed.
    /// </summary>
    public class WidgetChatIdentityResolutionTests : IClassFixture<WidgetChatIdentityTestFixture>
    {
        private readonly WidgetChatIdentityTestFixture _fixture;
        private const string UrlTemplate = "/api/v1/agent/{0}/chat/{1}/session-messages";

        public WidgetChatIdentityResolutionTests(WidgetChatIdentityTestFixture fixture)
        {
            _fixture = fixture;
        }

        private async Task<(Tenant tenant, AgentChatSetting chatSetting)> SeedShareableAgentAsync(string tenantName)
        {
            var db = _fixture.GetDbContext();

            var tenant = new Tenant { Name = tenantName, Region = "US" };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var agent = new PublishedTenantAgent { Name = "Widget Agent", SystemPrompt = "p", IsActive = true, TenantId = tenant.Id };
            db.PublishedTenantAgents.Add(agent);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var chatSetting = new AgentChatSetting { ShareId = Guid.NewGuid().ToString(), AgentId = agent.Id, IsShareable = true };
            db.AgentChatSettings.Add(chatSetting);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            return (tenant, chatSetting);
        }

        [Fact]
        public async Task WidgetChatRequest_WithValidAmaisJwt_ResolvesRealVisitorGuid_NotGuidEmpty()
        {
            var (tenant, chatSetting) = await SeedShareableAgentAsync("WidgetIdentityTenant1");
            const string visitorEmail = "widget.visitor@example.com";
            var visitorToken = AmaisTestTokenBuilder.BuildToken(email: visitorEmail);

            _fixture.Client.DefaultRequestHeaders.Clear();
            _fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, tenant.Name);
            _fixture.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", visitorToken);

            var response = await _fixture.Client.GetAsync(
                string.Format(UrlTemplate, chatSetting.ShareId, "session-with-no-conversations"),
                TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var authenticated = response.Headers.GetValues(UserContextObservationFilter.AuthenticatedHeader).FirstOrDefault();
            var observedGuid = response.Headers.GetValues(UserContextObservationFilter.GuidHeader).FirstOrDefault();
            var observedEmail = response.Headers.GetValues(UserContextObservationFilter.EmailHeader).FirstOrDefault();

            authenticated.Should().Be("true",
                "WebChatAuthorizationAttribute should have validated the Amais JWT before the controller action ran");
            observedEmail.Should().Be(visitorEmail);
            observedGuid.Should().NotBeNullOrEmpty();
            observedGuid.Should().NotBe(Guid.Empty.ToString(),
                "Bug 7163 claims this is always Guid.Empty for widget chat visitors regardless of a valid Amais JWT " +
                "— this assertion is the actual repro check");

            var db = _fixture.GetDbContext();
            var createdUser = db.Users.FirstOrDefault(u => u.Email == visitorEmail);
            createdUser.Should().NotBeNull("AutoUserCreationMiddleware should have created/found the visitor's User row");
            observedGuid.Should().Be(createdUser!.Id.ToString());
        }

        [Fact]
        public async Task WidgetChatRequest_AnonymousShareLink_NoJwt_StaysGuidEmpty()
        {
            var (tenant, chatSetting) = await SeedShareableAgentAsync("WidgetIdentityTenant2");

            _fixture.Client.DefaultRequestHeaders.Clear();
            _fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, tenant.Name);

            var response = await _fixture.Client.GetAsync(
                string.Format(UrlTemplate, chatSetting.ShareId, "session-with-no-conversations"),
                TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var authenticated = response.Headers.GetValues(UserContextObservationFilter.AuthenticatedHeader).FirstOrDefault();
            var observedGuid = response.Headers.GetValues(UserContextObservationFilter.GuidHeader).FirstOrDefault();

            authenticated.Should().Be("false");
            observedGuid.Should().Be(Guid.Empty.ToString(),
                "a genuinely anonymous share-link visitor has no identity to resolve — this is expected, not a bug");
        }
    }
}
