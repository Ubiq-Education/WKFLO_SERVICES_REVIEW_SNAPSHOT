using AMAIS.WKFLO.Common.MD.Domain.Data;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;

namespace AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities
{
    public static class DeleteDataHelper
    {
        public static void DeleteTenantAgentData(this TestFixture _fixture)
        {
            var db = (MasterDataDbContext)_fixture.ServiceProvider.GetRequiredService<IMasterDataDbContext>();
            db.TenantAgents.Clear();
            db.SaveChanges();
        }

        public static void DeleteMarketplaceAgentData(this TestFixture _fixture)
        {
            var db = (MasterDataDbContext)_fixture.ServiceProvider.GetRequiredService<IMasterDataDbContext>();
            // Every test in this suite calls DeleteMarketplaceAgentData() BEFORE DeleteTenantAgentData()
            // (parent before child). That was harmless before Agent.InstalledFromMarketplaceAgentId
            // existed, but that self-referencing FK means a TenantAgent/PublishedTenantAgent row
            // installed by an EARLIER test in the same fixture-shared SQLite DB can still be pointing
            // at a MarketplaceAgent this call is about to remove. Since that dependent row isn't loaded
            // in THIS SaveChanges call, EF has no way to null its FK automatically, so the DELETE would
            // violate the constraint. Null out any dangling reference first, in the SAME SaveChanges
            // call as the removal, so EF Core's dependency ordering issues the UPDATE before the DELETE
            // regardless of which order the two Delete*Data() calls happen to run in at each call site.
            // IgnoreQueryFilters() so a soft-deleted dependent (still holding the FK physically) counts too.
            foreach (var agent in db.Set<Agent>().IgnoreQueryFilters().Where(a => a.InstalledFromMarketplaceAgentId != null))
            {
                agent.InstalledFromMarketplaceAgentId = null;
            }
            db.MarketplaceAgents.Clear();
            db.SaveChanges();
        }

        public static void DeleteTenantToolData(this TestFixture _fixture)
        {
            var db = (MasterDataDbContext)_fixture.ServiceProvider.GetRequiredService<IMasterDataDbContext>();
            db.TenantTools.Clear();
            db.SaveChanges();
        }

        public static void DeleteMarketplaceToolData(this TestFixture _fixture)
        {
            var db = (MasterDataDbContext)_fixture.ServiceProvider.GetRequiredService<IMasterDataDbContext>();
            // Symmetric fix to DeleteMarketplaceAgentData() above, for Tool.InstalledFromMarketplaceToolId.
            foreach (var tool in db.Set<Tool>().IgnoreQueryFilters().Where(t => t.InstalledFromMarketplaceToolId != null))
            {
                tool.InstalledFromMarketplaceToolId = null;
            }
            db.MarketplaceTools.Clear();
            db.SaveChanges();
        }

        public static void DeleteAgentToolData(this TestFixture _fixture)
        {
            var db = (MasterDataDbContext)_fixture.ServiceProvider.GetRequiredService<IMasterDataDbContext>();
            db.AgentTools.Clear();
            db.SaveChanges();
        }

        public static void DeleteAgentChatSettingData(this TestFixture _fixture)
        {
            var db = (MasterDataDbContext)_fixture.ServiceProvider.GetRequiredService<IMasterDataDbContext>();
            db.PredefinedPrompts.Clear();
            db.AgentChatSettings.Clear();
            db.PublishedTenantAgents.Clear();
            db.TenantAgents.Clear();
            db.SaveChanges();
        }

        public static void DeleteAllAgentData(this TestFixture _fixture)
        {
            var db = (MasterDataDbContext)_fixture.ServiceProvider.GetRequiredService<IMasterDataDbContext>();
            db.AgentTools.Clear();
            db.TenantAgents.Clear();
            db.MarketplaceAgents.Clear();
            db.SaveChanges();
        }

        public static void DeleteSecretData(this TestFixture _fixture)
        {
            var db = (MasterDataDbContext)_fixture.ServiceProvider.GetRequiredService<IMasterDataDbContext>();
            db.Secrets.Clear();
            db.SaveChanges();
        }

        public static void DeleteAIModelData(this TestFixture _fixture)
        {
            var db = (MasterDataDbContext)_fixture.ServiceProvider.GetRequiredService<IMasterDataDbContext>();
            db.AIModels.Clear();
            db.SaveChanges();
        }

        public static void DeleteUserStarData(this TestFixture _fixture)
        {
            var db = (MasterDataDbContext)_fixture.ServiceProvider.GetRequiredService<IMasterDataDbContext>();
            db.UserStars.Clear();
            db.SaveChanges();
        }

        public static void DeleteUserInstallData(this TestFixture _fixture)
        {
            var db = (MasterDataDbContext)_fixture.ServiceProvider.GetRequiredService<IMasterDataDbContext>();
            db.UserInstalls.Clear();
            db.SaveChanges();
        }

        public static void DeleteUserData(this TestFixture _fixture)
        {
            var db = (MasterDataDbContext)_fixture.ServiceProvider.GetRequiredService<IMasterDataDbContext>();
            db.Users.Clear();
            db.SaveChanges();
        }

        public static void DeleteTenantData(this TestFixture _fixture)
        {
            var db = (MasterDataDbContext)_fixture.ServiceProvider.GetRequiredService<IMasterDataDbContext>();
            db.Tenants.Clear();
            db.SaveChanges();
        }

        public static void DeleteConversationData(this TestFixture _fixture)
        {
            var db = (MasterDataDbContext)_fixture.ServiceProvider.GetRequiredService<IMasterDataDbContext>();
            db.Conversations.Clear();
            db.SaveChanges();
        }
    }
}
