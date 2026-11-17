using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.Common.Models.Results.Knowledge;
using AMAIS.WKFLO.Common.Models.Settings;
using AMAIS.WKFLO.MD.Application.Knowledge.Models;
using Mediator;
using Microsoft.Extensions.Options;

namespace AMAIS.WKFLO.MD.Application.Knowledge.Queries.ListTenantKnowledge
{
    public class ListTenantKnowledgeQueryHandler : IRequestHandler<ListTenantKnowledgeQuery, Result>
    {
        private readonly IDatastreamProvider _datastreamProvider;
        private readonly ElasticsearchSettings _elasticsearchSettings;
        private readonly IWkfloMapper _mapper;
        private readonly TenantContext _tenantContext;

        public ListTenantKnowledgeQueryHandler(IDatastreamProvider datastreamProvider, IOptions<ElasticsearchSettings> settings, IWkfloMapper mapper, TenantContext tenantContext)
        {
            _datastreamProvider = datastreamProvider;
            _elasticsearchSettings = settings.Value;
            _mapper = mapper;
            _tenantContext = tenantContext;
        }

        public async ValueTask<Result> Handle(ListTenantKnowledgeQuery query, CancellationToken cancellationToken)
        {
            var results = await _datastreamProvider.ListAllKnowledgeAsync<BaseKnowledgeResult>(_tenantContext.TenantName, _elasticsearchSettings.KnowledgeIndex);
            return results == null ? new NotFoundResult() : (Result)new SuccessResult<List<TenantKnowledgeGetResult>>(_mapper.Map<List<TenantKnowledgeGetResult>>(results));
        }
    }
}
