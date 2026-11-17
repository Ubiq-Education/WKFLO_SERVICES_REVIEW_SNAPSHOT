using AMAIS.WKFLO.Common.Infrastructure.Providers.Models;
using AMAIS.WKFLO.Common.Models.Documents;
using System.Linq.Expressions;

namespace AMAIS.WKFLO.Common.Infrastructure.Providers
{
    /// <summary>
    /// The filters of every chat document query and delete. Built from a <see cref="ChatDocumentScope"/>, so a query
    /// cannot be made without tenant, session and user. Only equality, AND and OR over constants are used, which every
    /// vector store filter translator supports (and which can be compiled and checked in a unit test).
    /// </summary>
    public static class ChatDocumentFilters
    {
        private static readonly ParameterExpression Entry = Expression.Parameter(typeof(ChatDocumentChunkEntry), "e");

        /// <summary>Tenant, session and user, plus the document ids (any of them) when given.</summary>
        public static Expression<Func<ChatDocumentChunkEntry, bool>> ForScope(ChatDocumentScope scope, IReadOnlyCollection<Guid> documentIds = null, string fileName = null)
        {
            ArgumentNullException.ThrowIfNull(scope);

            var body = And(
                Equal(nameof(ChatDocumentChunkEntry.TenantName), scope.TenantName),
                Equal(nameof(ChatDocumentChunkEntry.SessionId), scope.SessionId),
                Equal(nameof(ChatDocumentChunkEntry.UserId), scope.UserId));

            if (documentIds != null)
                body = Expression.AndAlso(body, AnyOf(documentIds.Select(id => Equal(nameof(ChatDocumentChunkEntry.DocumentId), id.ToString("D")))));

            if (!string.IsNullOrWhiteSpace(fileName))
                body = Expression.AndAlso(body, Equal(nameof(ChatDocumentChunkEntry.FileName), fileName));

            return Expression.Lambda<Func<ChatDocumentChunkEntry, bool>>(body, Entry);
        }

        /// <summary>The scope's chunks held by any of these agents (none matches nothing).</summary>
        public static Expression<Func<ChatDocumentChunkEntry, bool>> ForAgents(ChatDocumentScope scope, IReadOnlyCollection<int> agentIds)
        {
            var scoped = ForScope(scope);
            var agents = AnyOf(agentIds.Select(id => (Expression)Expression.Equal(Expression.Property(Entry, nameof(ChatDocumentChunkEntry.AgentId)), Expression.Constant(id))));
            return Expression.Lambda<Func<ChatDocumentChunkEntry, bool>>(Expression.AndAlso(scoped.Body, agents), Entry);
        }

        /// <summary>One document of the scope, restricted to the given chunk indices.</summary>
        public static Expression<Func<ChatDocumentChunkEntry, bool>> ForChunks(ChatDocumentScope scope, Guid documentId, IReadOnlyCollection<int> chunkIndexes)
        {
            var scoped = ForScope(scope, [documentId]);
            var indexes = AnyOf(chunkIndexes.Select(i => (Expression)Expression.Equal(Expression.Property(Entry, nameof(ChatDocumentChunkEntry.ChunkIndex)), Expression.Constant(i))));
            return Expression.Lambda<Func<ChatDocumentChunkEntry, bool>>(Expression.AndAlso(scoped.Body, indexes), Entry);
        }

        private static Expression Equal(string property, string value) =>
            Expression.Equal(Expression.Property(Entry, property), Expression.Constant(value, typeof(string)));

        private static Expression And(params Expression[] parts) => parts.Aggregate(Expression.AndAlso);

        // An empty list matches nothing (never everything).
        private static Expression AnyOf(IEnumerable<Expression> parts)
        {
            var list = parts.ToList();
            return list.Count == 0 ? Expression.Constant(false) : list.Aggregate(Expression.OrElse);
        }
    }
}
