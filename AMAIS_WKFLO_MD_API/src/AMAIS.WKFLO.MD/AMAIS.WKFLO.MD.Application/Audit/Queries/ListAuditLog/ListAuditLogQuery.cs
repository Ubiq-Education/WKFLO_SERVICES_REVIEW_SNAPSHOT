using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;

namespace AMAIS.WKFLO.MD.Application.Audit.Queries.ListAuditLog
{
    public class ListAuditLogQuery : IWkfloRequest<Result>
    {
        /// <summary>Filters to a single <c>AuditLogEntry.EntityType</c> (e.g. "TenantAgent", "Secret").</summary>
        public string EntityType { get; set; }

        /// <summary>Filters to one of <see cref="Common.Models.CommonConsts.AuditActionConsts"/>.</summary>
        public string Action { get; set; }

        /// <summary>Filters by actor — matched against <c>ActorUserGuid</c>, <c>ActorUserName</c>, or
        /// <c>ActorUserEmail</c> (case-insensitive substring match on name/email).</summary>
        public string Actor { get; set; }

        /// <summary>Inclusive lower bound on <c>Timestamp</c> (UTC).</summary>
        public DateTimeOffset? DateFrom { get; set; }

        /// <summary>Inclusive upper bound on <c>Timestamp</c> (UTC) — treated as covering the whole day named
        /// by this value (i.e. up to, but not including, midnight of the following day), not just the exact
        /// instant, since callers typically send a bare date.</summary>
        public DateTimeOffset? DateTo { get; set; }

        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 15;
    }
}
