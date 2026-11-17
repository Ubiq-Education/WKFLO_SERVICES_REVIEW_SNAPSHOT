using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Commands;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results.Knowledge;
using AMAIS.WKFLO.Common.Models.Settings;
using Microsoft.Extensions.Options;

namespace AMAIS.WKFLO.Common.MD.Application.Common.Validators
{
    public class DataStreamMasterDataValidator<T> : BaseMasterDataValidator<T>
    {
        protected readonly IDatastreamProvider _datastreamProvider;
        private readonly ElasticsearchSettings _elasticsearchSettings;

        public DataStreamMasterDataValidator(IDatastreamProvider datastreamProvider, IOptions<ElasticsearchSettings> settings, IMasterDataDbContext db, TenantContext tenantContext)
            : base(db, tenantContext)
        {
            _datastreamProvider = datastreamProvider;
            _elasticsearchSettings = settings.Value;
        }

        protected async Task<bool> BeExistingTenantKnowledge(IRequestId<string> request, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(request?.Id))
                return false;

            return await _datastreamProvider.ExistDocumentAsync<BaseKnowledgeResult>(request.Id, _tenantContext.TenantName, _elasticsearchSettings.KnowledgeIndex);
        }
    }
}
