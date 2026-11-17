using AMAIS.WKFLO.Common.Infrastructure.Providers;
using AMAIS.WKFLO.Common.Infrastructure.Providers.Models;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Models.Documents;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace AMAIS.WKFLO.AI.UnitTests.Providers
{
    /// <summary>
    /// Bug 8189 / Task 8219: the filters every chat document query and delete carries. Compiled and applied to sample
    /// records, so what the database would return is checked without a database.
    /// </summary>
    public class ChatDocumentFiltersTests
    {
        private static readonly Guid DocumentId = Guid.NewGuid();
        private static readonly ChatDocumentScope Scope = new("acme", "session-1", "user-1");

        private static ChatDocumentChunkEntry Entry(string tenant = "acme", string session = "session-1", string user = "user-1", Guid? document = null,
            int chunk = 0, string fileName = "report.pdf", DateTime? createdAt = null) => new()
            {
                Id = $"{document ?? DocumentId}_{chunk}",
                TenantName = tenant,
                SessionId = session,
                UserId = user,
                DocumentId = (document ?? DocumentId).ToString("D"),
                ChunkIndex = chunk,
                FileName = fileName,
                CreatedAt = createdAt ?? DateTime.UtcNow
            };

        private static List<ChatDocumentChunkEntry> Matching(System.Linq.Expressions.Expression<Func<ChatDocumentChunkEntry, bool>> filter, params ChatDocumentChunkEntry[] entries) =>
            entries.Where(filter.Compile()).ToList();

        [Fact]
        public void ForScope_MatchesOnlyTheOwnTenantSessionAndUser()
        {
            var own = Entry();

            var found = Matching(ChatDocumentFilters.ForScope(Scope, [DocumentId]),
                own,
                Entry(tenant: "other-tenant"),
                Entry(session: "session-2"),
                Entry(user: "user-2"),
                Entry(user: Guid.Empty.ToString()));

            Assert.Same(own, Assert.Single(found));
        }

        [Fact]
        public void ForScope_ADocumentIdOfAnotherTenantOrSession_ReturnsNothing()
        {
            // The same document id in another tenant, another session or for another user: never returned.
            var found = Matching(ChatDocumentFilters.ForScope(Scope, [DocumentId]),
                Entry(tenant: "other-tenant", document: DocumentId),
                Entry(session: "other-session", document: DocumentId),
                Entry(user: "other-user", document: DocumentId));

            Assert.Empty(found);
        }

        [Fact]
        public void ForScope_WithDocumentIds_MatchesOnlyThoseDocuments()
        {
            var other = Guid.NewGuid();
            var third = Guid.NewGuid();

            var found = Matching(ChatDocumentFilters.ForScope(Scope, [DocumentId, other]),
                Entry(document: DocumentId), Entry(document: other), Entry(document: third));

            Assert.Equal(2, found.Count);
            Assert.DoesNotContain(found, e => e.DocumentId == third.ToString("D"));
        }

        [Fact]
        public void ForScope_WithAnEmptyDocumentList_MatchesNothing()
        {
            Assert.Empty(Matching(ChatDocumentFilters.ForScope(Scope, []), Entry()));
        }

        [Fact]
        public void ForScope_WithoutDocumentIds_MatchesTheWholeChat_ButNothingOfAnotherChat()
        {
            var found = Matching(ChatDocumentFilters.ForScope(Scope),
                Entry(document: Guid.NewGuid()), Entry(document: Guid.NewGuid()), Entry(session: "session-2"));

            Assert.Equal(2, found.Count);
        }

        [Fact]
        public void ForScope_WithAFileName_AlsoFiltersByFile()
        {
            var found = Matching(ChatDocumentFilters.ForScope(Scope, [DocumentId], "a.pdf"),
                Entry(fileName: "a.pdf"), Entry(fileName: "b.pdf"));

            Assert.Equal("a.pdf", Assert.Single(found).FileName);
        }

        [Fact]
        public void ForChunks_KeepsTheScopeAndTheDocument()
        {
            var found = Matching(ChatDocumentFilters.ForChunks(Scope, DocumentId, [1, 3]),
                Entry(chunk: 0), Entry(chunk: 1), Entry(chunk: 3), Entry(chunk: 3, tenant: "other-tenant"), Entry(chunk: 3, document: Guid.NewGuid()));

            Assert.Equal([1, 3], found.Select(e => e.ChunkIndex).ToArray());
            Assert.All(found, e => Assert.Equal("acme", e.TenantName));
        }

        [Fact]
        public void ForAgents_MatchesOnlyTheGivenAgentsChunksOfTheScope()
        {
            ChatDocumentChunkEntry Held(int agent, string session = "session-1") => new()
            {
                Id = Guid.NewGuid().ToString(), TenantName = "acme", SessionId = session, UserId = "user-1", DocumentId = DocumentId.ToString("D"), AgentId = agent
            };

            var found = Matching(ChatDocumentFilters.ForAgents(Scope, [1, 3]), Held(1), Held(2), Held(3), Held(1, "session-2"));

            Assert.Equal([1, 3], found.Select(e => e.AgentId).ToArray());
            Assert.Empty(Matching(ChatDocumentFilters.ForAgents(Scope, []), Held(1)));
        }

        [Fact]
        public void TheStoreHasNoReadOrDeleteThatIsNotScoped()
        {
            // Every method that reads or deletes takes the scope.
            var unscoped = typeof(IChatDocumentStore).GetMethods()
                .Where(m => !m.GetParameters().Any(p => p.ParameterType == typeof(ChatDocumentScope)))
                .Select(m => m.Name)
                .ToList();

            Assert.Empty(unscoped);
        }
    }
}
