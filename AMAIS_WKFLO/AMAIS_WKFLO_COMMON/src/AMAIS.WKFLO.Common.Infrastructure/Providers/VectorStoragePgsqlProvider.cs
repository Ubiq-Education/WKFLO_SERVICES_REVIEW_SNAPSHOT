using AMAIS.WKFLO.Common.Infrastructure.Extensions;
using AMAIS.WKFLO.Common.Infrastructure.Providers.Models;
using AMAIS.WKFLO.Common.Interfaces.Entities;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using CommunityToolkit.VectorData.PgVector;
using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Data;
using Npgsql;
using System.Linq.Expressions;
using System.Reflection;

namespace AMAIS.WKFLO.Common.Infrastructure.Providers
{
    public class VectorStoragePgsqlProvider<T> : IVectorStorageProvider where T : class, IVectorData
    {
        private readonly INpgsqlProvider _npgsqlProvider;
        private readonly PostgresVectorStore _postgresVectorStore;
        private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
        private readonly IDateTimeProvider _dateTimeProvider;

        public VectorStoragePgsqlProvider(INpgsqlProvider npgsqlProvider, IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator, IDateTimeProvider dateTimeProvider)
        {
            _npgsqlProvider = npgsqlProvider;
            _postgresVectorStore = new PostgresVectorStore((NpgsqlDataSource)_npgsqlProvider.GetDataSource(), true);
            _embeddingGenerator = embeddingGenerator;
            _dateTimeProvider = dateTimeProvider;
        }

        public async Task DeleteAsync(string collectionName, CancellationToken cancellationToken = default)
        {
            var recordCollection = _postgresVectorStore.GetCollection<string, T>(collectionName);
            await recordCollection.EnsureCollectionDeletedAsync();
        }

        public async Task DeleteAsync(string collectionName, Expression<Func<IVectorData, bool>> filter, CancellationToken cancellationToken = default)
        {
            // SK PgVector requires the concrete type that carries [VectorStoreKey]/[VectorStoreData] attributes.
            // Rewrite the expression so member accesses use VectorData properties instead of IVectorData ones.
            var recordCollection = _postgresVectorStore.GetCollection<string, VectorData>(collectionName);
            await recordCollection.EnsureCollectionExistsAsync();

            var concreteFilter = RewriteFilter(filter);

            var records = new List<VectorData>();
            await foreach (var record in recordCollection.GetAsync(concreteFilter, 10_000, cancellationToken: cancellationToken))
            {
                records.Add(record);
            }

            await recordCollection.DeleteAsync(records.Select(i => i.Id), cancellationToken);
        }

        public async Task AddAsync(string collectionName, IEnumerable<string> chunks, string sessionId, int agentId, string agentName, string tenantName, string email, CancellationToken cancellationToken = default)
        {
            // Default user guid when the user is anonymous
            if (collectionName == "00000000-0000-0000-0000-000000000000")
            {
                // We wont add any data for anonymous chats
                return;
            }

            var items = chunks.Select(chunk => new VectorData
            {
                Id = Guid.NewGuid().ToString(),
                Title = $"Session for agent {agentName} and user {email}",
                Content = chunk,
                Published = _dateTimeProvider.DateTimeOffsetUtcNow().UtcDateTime,
                Authors = new List<string> { email },
                Categories = new List<string> { "chat", sessionId, $"agent-{agentId}", email },
                Tenant = tenantName
            });

            var recordCollection = _postgresVectorStore.GetCollection<string, VectorData>(collectionName);
            await recordCollection.EnsureCollectionExistsAsync();
            foreach (var batch in items.Batch(20))
            {
                var embeddings = await _embeddingGenerator.GenerateAndZipAsync(batch.Select(r => r.Content).ToList());
                foreach (var zipped in batch.Zip(embeddings, (record, embedding) => (record, embedding)))
                {
                    zipped.record.Embedding = zipped.embedding.Embedding;
                }
                await recordCollection.UpsertAsync(batch, cancellationToken);
            }
        }

        public async Task<IEnumerable<string>> SearchAsync(string collectionName, string query, CancellationToken cancellationToken = default)
        {
            // Default user guid when the user is anonymous
            if (collectionName == "00000000-0000-0000-0000-000000000000")
            {
                // We wont retrieve any data for anonymous chats
                return null;
            }

            var recordCollection = _postgresVectorStore.GetCollection<string, T>(collectionName);
            await recordCollection.EnsureCollectionExistsAsync();
            var searchVector = (await _embeddingGenerator.GenerateAsync(query)).Vector;
            var vectorSearchResults = recordCollection.SearchAsync(searchVector, top: 10, cancellationToken: cancellationToken);
            var results = new List<string>();
            await foreach (var vectorSearchResult in vectorSearchResults.WithCancellation(cancellationToken))
            {
                results.Add(vectorSearchResult.Record.Content);
            }
            return results;
        }

        public KernelPlugin GetLongTermMemoryPlugin(string collectionName)
        {
            var recordCollection = _postgresVectorStore.GetCollection<string, VectorData>(collectionName);
#pragma warning disable SKEXP0001
            var textSearch = new VectorStoreTextSearch<VectorData>(recordCollection, _embeddingGenerator);
#pragma warning restore SKEXP0001

            var options = new KernelFunctionFromMethodOptions()
            {
                FunctionName = "LongTermMemorySearch",
                Description = "Search for user information from the past session chats.",
                Parameters =
                [
                    new KernelParameterMetadata("query") { Description = "What to search for", IsRequired = true },
                    new KernelParameterMetadata("count") { Description = "Number of results", IsRequired = true, DefaultValue = 3, ParameterType = typeof(int) },
                    new KernelParameterMetadata("skip") { Description = "Number of results to skip", IsRequired = false, DefaultValue = 0 },
                ],
                ReturnParameter = new() { ParameterType = typeof(KernelSearchResults<string>) },
            };

            return KernelPluginFactory.CreateFromFunctions("LongTermMemory", "Function for retriving user information", [
                textSearch.CreateGetSearchResults(options)
            ]);
        }

        /// <summary>
        /// Rewrites an <see cref="Expression{Func{IVectorData, bool}}"/> so that the parameter type
        /// becomes the concrete <see cref="VectorData"/> class and all member accesses resolve to
        /// <see cref="VectorData"/> properties (which carry the SK vector-store attributes).
        /// </summary>
        private static Expression<Func<VectorData, bool>> RewriteFilter(Expression<Func<IVectorData, bool>> filter)
        {
            var oldParam = filter.Parameters[0];
            var newParam = Expression.Parameter(typeof(VectorData), oldParam.Name);
            var body = new InterfaceToConcreteVisitor(oldParam, newParam).Visit(filter.Body);
            return Expression.Lambda<Func<VectorData, bool>>(body, newParam);
        }

        private sealed class InterfaceToConcreteVisitor : ExpressionVisitor
        {
            private readonly ParameterExpression _oldParam;
            private readonly ParameterExpression _newParam;

            public InterfaceToConcreteVisitor(ParameterExpression oldParam, ParameterExpression newParam)
            {
                _oldParam = oldParam;
                _newParam = newParam;
            }

            protected override Expression VisitParameter(ParameterExpression node)
                => node == _oldParam ? _newParam : base.VisitParameter(node);

            protected override Expression VisitMember(MemberExpression node)
            {
                // When accessing a property through the IVectorData parameter, redirect the member
                // access to the corresponding property on VectorData so SK can find the attributes.
                if (node.Expression is ParameterExpression p && p == _oldParam)
                {
                    var concreteProperty = typeof(VectorData).GetProperty(node.Member.Name, BindingFlags.Public | BindingFlags.Instance);
                    if (concreteProperty != null)
                        return Expression.Property(_newParam, concreteProperty);
                }
                return base.VisitMember(node);
            }
        }
    }
}
