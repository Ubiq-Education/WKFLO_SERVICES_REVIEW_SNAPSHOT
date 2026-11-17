using AMAIS.WKFLO.Common.Interfaces.Entities;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using Microsoft.SemanticKernel;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities
{
    public class NullVectorStorageProvider : IVectorStorageProvider
    {
        public Task DeleteAsync(string collectionName, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteAsync(string collectionName, Expression<Func<IVectorData, bool>> filter, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task AddAsync(string collectionName, IEnumerable<string> chunks, string sessionId, int agentId, string agentName, string tenantName, string email, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IEnumerable<string>> SearchAsync(string collectionName, string query, CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<string>>(Array.Empty<string>());

        public KernelPlugin GetLongTermMemoryPlugin(string collectionName)
            => null;
    }
}
