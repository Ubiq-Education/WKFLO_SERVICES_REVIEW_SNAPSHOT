using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;

namespace AMAIS.WKFLO.MD.Application.Agents.Queries.ListMarketplaceAgents
{
    public class ListMarketplaceAgentsQuery : IWkfloRequest<Result>
    {
        /// <summary>Optional exact-match filter on the name of a <c>Tag</c> assigned (via <c>EntityTag</c>,
        /// <c>EntityType == "Agent"</c>) to the Marketplace agent — e.g. "AMAIS" to list only the curated
        /// template-library catalog. Marketplace agents are a global/cross-tenant catalog, so this
        /// deliberately matches the tag by name across every tenant's tags rather than scoping to the
        /// caller's own tenant (unlike <c>ListEntitiesByTagQuery</c>, which is tenant-scoped by design).
        /// Omitted or blank returns every Marketplace agent, unfiltered.</summary>
        public string Tag { get; set; }
    }
}

