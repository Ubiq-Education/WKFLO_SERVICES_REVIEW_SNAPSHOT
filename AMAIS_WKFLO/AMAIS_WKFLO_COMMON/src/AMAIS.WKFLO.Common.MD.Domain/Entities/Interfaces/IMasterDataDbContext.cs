using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces
{
    public interface IMasterDataDbContext : IDisposable
    {
        DbSet<Tenant> Tenants { get; set; }

        DbSet<Agent> Agents { get; set; }
        DbSet<MarketplaceAgent> MarketplaceAgents { get; set; }
        DbSet<TenantAgent> TenantAgents { get; set; }
        DbSet<PublishedTenantAgent> PublishedTenantAgents { get; set; }

        DbSet<MarketplaceTool> MarketplaceTools { get; set; }
        DbSet<TenantTool> TenantTools { get; set; }
        DbSet<PublishedTenantTool> PublishedTenantTools { get; set; }

        DbSet<AgentTool> AgentTools { get; set; }
        DbSet<AgentChatSetting> AgentChatSettings { get; set; }
        DbSet<PredefinedPrompt> PredefinedPrompts { get; set; }

        DbSet<AIModel> AIModels { get; set; }
        DbSet<AICredit> AICredits { get; set; }
        DbSet<Secret> Secrets { get; set; }
        DbSet<ToolOAuthToken> ToolOAuthTokens { get; set; }
        DbSet<McpOAuthPendingFlow> McpOAuthPendingFlows { get; set; }

        DbSet<WebOrigin> WebOrigins { get; set; }

        DbSet<Conversation> Conversations { get; set; }
        DbSet<ConversationAnalysis> ConversationAnalyses { get; set; }

        DbSet<UserStar> UserStars { get; set; }
        DbSet<UserInstall> UserInstalls { get; set; }

        DbSet<User> Users { get; set; }

        DbSet<MessageTopicClassification> MessageTopicClassifications { get; set; }

        DbSet<ClassificationJob> ClassificationJobs { get; set; }

        DbSet<Tag> Tags { get; set; }
        DbSet<EntityTag> EntityTags { get; set; }

        DbSet<AgentAllowedSpinUpAgent> AgentAllowedSpinUpAgents { get; set; }

        DbSet<DxpIntegration> DxpIntegrations { get; set; }

        DbSet<TenantSetting> TenantSettings { get; set; }
        DbSet<SuperPowerUser> SuperPowerUsers { get; set; }
        DbSet<TenantAccess> TenantAccesses { get; set; }

        DbSet<ChatImagePurgeState> ChatImagePurgeStates { get; set; }

        DbSet<ContactFormPluginLog> ContactFormPluginLogs { get; set; }

        DbSet<AuditLogEntry> AuditLogEntries { get; set; }

        Task<int> SaveChangesAsync(CancellationToken cancellationToken);
        int SaveChanges();
        EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class;
        EntityEntry<TEntity> Attach<TEntity>(TEntity entity) where TEntity : class;
        ChangeTracker ChangeTracker { get; }

        /// <summary>
        /// Supplies the current request's tenant/actor identity for the audit trail written by
        /// <c>MasterDataDbContext.SaveChangesAsync</c>. Must be called once per request/scope (e.g. from a
        /// middleware, after tenant and user resolution) rather than injected via the constructor:
        /// <c>MasterDataDbContext</c> is registered with <c>AddDbContextPool</c>, so constructor-injected
        /// scoped services would be captured once per pooled instance and go stale across requests/tenants.
        /// </summary>
        void SetAuditActor(string tenantGuid, string actorUserGuid, string actorUserName, string actorUserEmail, string correlationId);
    }
}
