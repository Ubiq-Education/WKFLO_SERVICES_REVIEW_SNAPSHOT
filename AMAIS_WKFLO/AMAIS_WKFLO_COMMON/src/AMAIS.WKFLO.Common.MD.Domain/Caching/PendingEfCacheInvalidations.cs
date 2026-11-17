#nullable enable

using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.Common.MD.Domain.Caching
{
    /// <summary>
    /// Invalidation tags of writes that are not committed yet, per <see cref="DbContext"/> instance.
    /// They are announced to other replicas only once the transaction commits: announced earlier, another replica
    /// could re-cache the old row before the new one is visible. Keyed by context (not by flow) because the library
    /// reports tags from deep inside the command pipeline, and a pooled context is reused, so nothing here may outlive a transaction.
    /// </summary>
    public sealed class PendingEfCacheInvalidations
    {
        private readonly ConditionalWeakTable<DbContext, HashSet<string>> _pending = new();

        // Which context the command being processed belongs to. Set synchronously by the interceptor right before the library
        // invalidates, so it is visible to the library as a callee; weak so a finished request's context is never kept alive.
        private readonly AsyncLocal<WeakReference<DbContext>?> _current = new();

        public void SetCurrent(DbContext? context) => _current.Value = context is null ? null : new WeakReference<DbContext>(context);

        /// <summary>Starts collecting tags for the context (a SaveChanges or a transaction is running).</summary>
        public void Begin(DbContext? context)
        {
            if (context is not null)
            {
                _pending.GetValue(context, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            }
        }

        /// <summary>Adds the tags to the current context's pending set; false when nothing is pending, so the caller must publish at once.</summary>
        public bool TryAdd(IEnumerable<string> tags)
        {
            if (_current.Value is not { } reference || !reference.TryGetTarget(out var context) || !_pending.TryGetValue(context, out var set))
            {
                return false;
            }

            lock (set)
            {
                set.UnionWith(tags);
            }

            return true;
        }

        /// <summary>Returns and forgets the pending tags of the context.</summary>
        public string[] Take(DbContext? context)
        {
            if (context is null || !_pending.TryGetValue(context, out var set))
            {
                return [];
            }

            _pending.Remove(context);

            lock (set)
            {
                return [.. set];
            }
        }

        public void Discard(DbContext? context)
        {
            if (context is not null)
            {
                _pending.Remove(context);
            }
        }
    }
}
