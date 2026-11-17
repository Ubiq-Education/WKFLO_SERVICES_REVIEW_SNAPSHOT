using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models;
using AwesomeAssertions;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.FunctionalTests.EndPoints.Public.Agents
{
    /// <summary>
    /// US 7507: GET /api/v1/agent/{shareId}/chat/conversations against the production-equivalent pipeline
    /// (see <see cref="WidgetChatIdentitySetup"/>), so the caller's identity resolves from the Amais JWT exactly
    /// as it does for the CMS-embedded MAISY chat.
    /// <para>
    /// Only the paths that return before the conversation query are covered here: the functional tests run on
    /// SQLite, which cannot aggregate or order DateTimeOffset values (the list's MAX/ORDER BY on last activity).
    /// The listing query itself runs on a real relational provider in ListPublicAgentConversationsQueryHandlerRelationalTests
    /// (SQLite with DateTimeOffset stored as binary: 90-day window, newest-first order, 50 cap), and its mapping
    /// rules in ListPublicAgentConversationsQueryHandlerTests.
    /// </para>
    /// </summary>
    public class PublicAgentConversationListTests : IClassFixture<WidgetChatIdentityTestFixture>
    {
        private const string UrlTemplate = "/api/v1/agent/{0}/chat/conversations";

        private readonly WidgetChatIdentityTestFixture _fixture;

        public PublicAgentConversationListTests(WidgetChatIdentityTestFixture fixture)
        {
            _fixture = fixture;
        }

        private async Task<(Tenant tenant, PublishedTenantAgent agent, AgentChatSetting chatSetting)> SeedMaisyAgentAsync(string tenantName, bool tagAsMaisy = true)
        {
            var db = _fixture.GetDbContext();
            var ct = TestContext.Current.CancellationToken;

            var tenant = new Tenant { Name = tenantName, Region = "US" };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(ct);

            var agent = new PublishedTenantAgent { Name = "MAISY", Description = "Your AI assistant", SystemPrompt = "p", IsActive = true, TenantId = tenant.Id };
            db.PublishedTenantAgents.Add(agent);
            await db.SaveChangesAsync(ct);

            var chatSetting = new AgentChatSetting { ShareId = Guid.NewGuid().ToString(), AgentId = agent.Id, IsShareable = true, ChatbotIconPath = "https://icons/maisy.png" };
            db.AgentChatSettings.Add(chatSetting);

            if (tagAsMaisy)
            {
                var tag = new Tag { Name = FeatureTagConsts.Maisy, TenantId = tenant.Id };
                db.Tags.Add(tag);
                await db.SaveChangesAsync(ct);
                db.EntityTags.Add(new EntityTag { TagId = tag.Id, EntityType = TaggableEntityTypes.Agent, EntityId = agent.Id });
            }

            await db.SaveChangesAsync(ct);

            return (tenant, agent, chatSetting);
        }

        private async Task<JArray> GetConversationsAsync(Tenant tenant, AgentChatSetting chatSetting, string bearerToken)
        {
            _fixture.Client.DefaultRequestHeaders.Clear();
            _fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, tenant.Name);
            if (bearerToken != null)
                _fixture.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

            var response = await _fixture.Client.GetAsync(string.Format(UrlTemplate, chatSetting.ShareId), TestContext.Current.CancellationToken);

            var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            response.StatusCode.Should().Be(HttpStatusCode.OK, content);
            return (JArray)JObject.Parse(content)["data"];
        }

        [Fact]
        public async Task ListConversations_AnonymousShareLink_ReturnsEmpty()
        {
            var (tenant, agent, chatSetting) = await SeedMaisyAgentAsync("ConversationListTenant2");

            var db = _fixture.GetDbContext();
            db.Conversations.Add(new Conversation { SessionId = "anonymous-session", AgentId = agent.Id, TenantId = tenant.Id, UserId = Guid.Empty.ToString(), Name = "Anonymous", LastActivityAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var items = await GetConversationsAsync(tenant, chatSetting, bearerToken: null);

            items.Should().BeEmpty();
        }

        [Fact]
        public async Task ListConversations_AgentNotTaggedMaisy_ReturnsEmpty()
        {
            var (tenant, agent, chatSetting) = await SeedMaisyAgentAsync("ConversationListTenant3", tagAsMaisy: false);
            const string visitorEmail = "conversation.list.not.maisy@example.com";

            var db = _fixture.GetDbContext();
            var visitor = new User { Id = Guid.NewGuid(), Email = visitorEmail };
            db.Users.Add(visitor);
            db.Conversations.Add(new Conversation { SessionId = "plain-agent-session", AgentId = agent.Id, TenantId = tenant.Id, UserId = visitor.Id.ToString(), Name = "Plain", LastActivityAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var items = await GetConversationsAsync(tenant, chatSetting, AmaisTestTokenBuilder.BuildToken(email: visitorEmail));

            items.Should().BeEmpty();
        }
    }
}
