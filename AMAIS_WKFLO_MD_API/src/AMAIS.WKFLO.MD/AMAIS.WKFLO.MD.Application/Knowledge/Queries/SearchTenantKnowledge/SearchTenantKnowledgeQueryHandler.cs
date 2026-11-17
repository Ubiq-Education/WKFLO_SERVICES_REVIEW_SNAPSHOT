using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.Common.Models.Results.Knowledge;
using AMAIS.WKFLO.Common.Models.Settings;
using AMAIS.WKFLO.MD.Application.Knowledge.Models;
using Mediator;
using Microsoft.Extensions.Options;

namespace AMAIS.WKFLO.MD.Application.Knowledge.Queries.SearchTenantKnowledge
{
    public class SearchTenantKnowledgeQueryHandler : IRequestHandler<SearchTenantKnowledgeQuery, Result>
    {
        private readonly IDatastreamProvider _datastreamProvider;
        private readonly ElasticsearchSettings _elasticsearchSettings;
        private readonly IWkfloMapper _mapper;
        private readonly TenantContext _tenantContext;

        public SearchTenantKnowledgeQueryHandler(IDatastreamProvider datastreamProvider, IOptions<ElasticsearchSettings> settings, IWkfloMapper mapper, TenantContext tenantContext)
        {
            _datastreamProvider = datastreamProvider;
            _elasticsearchSettings = settings.Value;
            _mapper = mapper;
            _tenantContext = tenantContext;
        }

        public async ValueTask<Result> Handle(SearchTenantKnowledgeQuery query, CancellationToken cancellationToken)
        {
            var results = await _datastreamProvider.SearchAllKnowledgeAsync<BaseKnowledgeResult>(query.Query, query.Tags, query.Limit, false, _tenantContext.TenantName, _elasticsearchSettings.KnowledgeIndex);
            return results == null ? new NotFoundResult() : (Result)new SuccessResult<List<TenantKnowledgeSearchResult>>(_mapper.Map<List<TenantKnowledgeSearchResult>>(results));
        }
    }
}
