using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.Models.Results;
using Mediator;

namespace AMAIS.WKFLO.AI.Application.Agents.Queries.CheckPublicAgentContactExists
{
    public class CheckPublicAgentContactExistsQueryHandler : IRequestHandler<CheckPublicAgentContactExistsQuery, Result>
    {
        private readonly TenantContext _tenantContext;
        private readonly IContactFormPluginLogService _contactFormLogService;

        public CheckPublicAgentContactExistsQueryHandler(
            TenantContext tenantContext,
            IContactFormPluginLogService contactFormLogService)
        {
            _tenantContext = tenantContext;
            _contactFormLogService = contactFormLogService;
        }

        public async ValueTask<Result> Handle(CheckPublicAgentContactExistsQuery query, CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(query.SubjectId, out var subjectGuid))
                return new SuccessResult<bool>(false);

            var exists = await _contactFormLogService.ExistsAsync(subjectGuid, _tenantContext.TenantName, cancellationToken);
            return new SuccessResult<bool>(exists);
        }
    }
}
