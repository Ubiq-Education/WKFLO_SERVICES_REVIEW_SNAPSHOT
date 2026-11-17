using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.Models.Results;
using Mediator;

namespace AMAIS.WKFLO.AI.Application.Agents.Queries.GetTenantJwtToken
{
    public class GetTenantJwtTokenQueryHandler : IRequestHandler<GetTenantJwtTokenQuery, Result>
    {
        private readonly IAmaisTokenApiService _tokenApiService;

        public GetTenantJwtTokenQueryHandler(IAmaisTokenApiService tokenApiService)
        {
            _tokenApiService = tokenApiService;
        }

        public async ValueTask<Result> Handle(GetTenantJwtTokenQuery query, CancellationToken cancellationToken)
        {
            return new SuccessResult<string>(await _tokenApiService.GetAmaisIntegrationTokenAsync());
        }
    }
}