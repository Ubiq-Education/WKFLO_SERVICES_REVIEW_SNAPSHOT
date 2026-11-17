using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using Mediator;

namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Queries.SearchRunnableAgentVectorDataByEmail
{
    public class SearchRunnableAgentVectorDataByEmailQueryHandler : IRequestHandler<SearchRunnableAgentVectorDataByEmailQuery, IEnumerable<string>>
    {
        private readonly IVectorStorageProvider _vectorStorageProvider;
        private readonly UserContext _userContext;

        public SearchRunnableAgentVectorDataByEmailQueryHandler(IVectorStorageProvider vectorStorageProvider, UserContext userContext)
        {
            _vectorStorageProvider = vectorStorageProvider;
            _userContext = userContext;
        }

        public async ValueTask<IEnumerable<string>> Handle(SearchRunnableAgentVectorDataByEmailQuery query, CancellationToken cancellationToken)
        {
            if (query.Agent == null) return new List<string>();
            if (string.IsNullOrEmpty(_userContext.Guid)) return new List<string>();

            if (query.Agent.EnableLTM)
            {
                return await _vectorStorageProvider.SearchAsync(_userContext.Guid, query.Message, cancellationToken);
            }
            else
            {
                return new List<string>();
            }
        }
    }
}
