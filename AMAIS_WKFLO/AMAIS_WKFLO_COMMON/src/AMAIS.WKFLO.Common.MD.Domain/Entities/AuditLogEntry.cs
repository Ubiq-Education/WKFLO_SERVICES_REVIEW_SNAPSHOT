using AMAIS.WKFLO.Common.Models.Entities;

namespace AMAIS.WKFLO.Common.MD.Domain.Entities
{
    /// <summary>
    /// Immutable audit trail row written automatically by <see cref="Data.MasterDataDbContext"/> for every
    /// Create/Update/Delete against a tracked <c>IEntity</c> in the MD API. See
    /// <c>MasterDataDbContext.BuildAuditLogEntries</c> for how rows are produced, and
    /// <c>MasterDataDbContext.SetAuditActor</c> for how the current tenant/actor is supplied (never via
    /// constructor injection — <see cref="Data.MasterDataDbContext"/> is registered with
    /// <c>AddDbContextPool</c>, so constructor-injected scoped services would be captured once per pooled
    /// instance and go stale across requests).
    /// </summary>
    public class AuditLogEntry : Entity<int>
    {
        /// <summary>GUID of the tenant the audited entity belongs to (<c>TenantContext.TenantGuid</c>).</summary>
        public string TenantGuid { get; set; }

        /// <summary>Runtime CLR type name of the audited entity (e.g. "TenantAgent", "Secret", "Tag").</summary>
        public string EntityType { get; set; }

        /// <summary>String form of the audited entity's primary key (<c>IEntity.GetEntityId()</c>).</summary>
        public string EntityId { get; set; }

        /// <summary>One of <see cref="Models.CommonConsts.AuditActionConsts"/>.</summary>
        public string Action { get; set; }

        /// <summary>WKFLO <c>User.Id</c> of the actor, as a string (<c>UserContext.Guid</c>). Null for
        /// unauthenticated/system writes.</summary>
        public string ActorUserGuid { get; set; }

        /// <summary>Display name of the actor at the time of the action.</summary>
        public string ActorUserName { get; set; }

        /// <summary>Email of the actor at the time of the action.</summary>
        public string ActorUserEmail { get; set; }

        /// <summary>UTC instant the action occurred.</summary>
        public DateTimeOffset Timestamp { get; set; }

        /// <summary>For Update actions, a JSON object of changed property name -&gt; {old, new}. Null
        /// otherwise.</summary>
        public string Changes { get; set; }

        /// <summary>Optional correlation id (e.g. <c>HttpContext.TraceIdentifier</c>) to tie multiple audit
        /// rows from the same request together.</summary>
        public string CorrelationId { get; set; }
    }
}
