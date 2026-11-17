using AMAIS.WKFLO.Common.Interfaces.Entities;
using Microsoft.SemanticKernel;
using System.Linq.Expressions;

namespace AMAIS.WKFLO.Common.Interfaces.Providers
{
    public interface IVectorStorageProvider
    {
        Task DeleteAsync(string collectionName, CancellationToken cancellationToken = default);
        Task DeleteAsync(string collectionName, Expression<Func<IVectorData, bool>> filter, CancellationToken cancellationToken = default);
        Task AddAsync(string collectionName, IEnumerable<string> chunks, string sessionId, int agentId, string agentName, string tenantName, string email, CancellationToken cancellationToken = default);
        Task<IEnumerable<string>> SearchAsync(string collectionName, string query, CancellationToken cancellationToken = default);
        KernelPlugin GetLongTermMemoryPlugin(string collectionName);
    }
}
