#nullable enable

using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AMAIS.WKFLO.Common.MD.Domain.Caching
{
    /// <summary>
    /// Ties the cross-replica invalidation to the transaction actually committing. Must be registered before the
    /// EFCoreSecondLevelCacheInterceptor: it tells <see cref="PendingEfCacheInvalidations"/> which context a command belongs to
    /// just before the library invalidates, and decides here when the collected tags are published.
    /// Stateless (the state lives in the pending set, keyed by context), so it is safe with DbContext pooling.
    /// Every hook completes synchronously on purpose: a value set inside an awaited async method would not flow to the caller.
    /// </summary>
    public sealed class EfCacheInvalidationInterceptor(
        PendingEfCacheInvalidations pending,
        BroadcastingEfCacheServiceProvider cache) : DbCommandInterceptor, ISaveChangesInterceptor, IDbTransactionInterceptor
    {
        // Command hooks: only to tell which context the library's invalidation belongs to.

        public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
        {
            pending.SetCurrent(eventData.Context);
            return result;
        }

        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            pending.SetCurrent(eventData.Context);
            return ValueTask.FromResult(result);
        }

        public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
        {
            pending.SetCurrent(eventData.Context);
            return result;
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            pending.SetCurrent(eventData.Context);
            return ValueTask.FromResult(result);
        }

        public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
        {
            pending.SetCurrent(eventData.Context);
            return result;
        }

        public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken cancellationToken = default)
        {
            pending.SetCurrent(eventData.Context);
            return ValueTask.FromResult(result);
        }

        // SaveChanges: collect from the start; publish when it ended without a surrounding transaction (autocommit or the implicit
        // transaction already committed). Inside a user transaction the commit decides.

        public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            pending.Begin(eventData.Context);
            return result;
        }

        public ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            pending.Begin(eventData.Context);
            return ValueTask.FromResult(result);
        }

        public int SavedChanges(SaveChangesCompletedEventData eventData, int result)
        {
            FlushUnlessInTransaction(eventData.Context);
            return result;
        }

        public ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            FlushUnlessInTransaction(eventData.Context);
            return ValueTask.FromResult(result);
        }

        public void SaveChangesFailed(DbContextErrorEventData eventData) => DiscardUnlessInTransaction(eventData.Context);

        public Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            DiscardUnlessInTransaction(eventData.Context);
            return Task.CompletedTask;
        }

        public void SaveChangesCanceled(DbContextEventData eventData) => DiscardUnlessInTransaction(eventData.Context);

        public Task SaveChangesCanceledAsync(DbContextEventData eventData, CancellationToken cancellationToken = default)
        {
            DiscardUnlessInTransaction(eventData.Context);
            return Task.CompletedTask;
        }

        // Transactions: user transactions and the one SaveChanges opens itself.

        public DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
        {
            pending.Begin(eventData.Context);
            return result;
        }

        public ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection, TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
        {
            pending.Begin(eventData.Context);
            return ValueTask.FromResult(result);
        }

        public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) => Flush(eventData.Context);

        public Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            Flush(eventData.Context);
            return Task.CompletedTask;
        }

        public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) => pending.Discard(eventData.Context);

        public Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            pending.Discard(eventData.Context);
            return Task.CompletedTask;
        }

        // A transaction disposed without committing is rolled back by the database without an event of its own.
        public void TransactionDisposed(DbTransaction transaction, TransactionEndEventData eventData) => pending.Discard(eventData.Context);

        private void Flush(DbContext? context)
        {
            var tags = pending.Take(context);

            if (tags.Length > 0)
            {
                cache.ApplyCommitted(tags);
            }
        }

        private void FlushUnlessInTransaction(DbContext? context)
        {
            if (context?.Database.CurrentTransaction is null)
            {
                Flush(context);
            }
        }

        // With a transaction still open, earlier writes in it may yet commit, so keep their tags for its outcome.
        private void DiscardUnlessInTransaction(DbContext? context)
        {
            if (context?.Database.CurrentTransaction is null)
            {
                pending.Discard(context);
            }
        }
    }
}
