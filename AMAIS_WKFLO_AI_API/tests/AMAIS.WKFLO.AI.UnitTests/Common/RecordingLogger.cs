using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace AMAIS.WKFLO.AI.UnitTests.Common
{
    /// <summary>
    /// Keeps every log entry (level and named template values) so a test can check a structured log without matching
    /// its text.
    /// </summary>
    public sealed class RecordingLogger<T> : ILogger<T>
    {
        public sealed record Entry(LogLevel Level, IReadOnlyDictionary<string, object> Values);

        private readonly ConcurrentQueue<Entry> _entries = new();

        public IReadOnlyList<Entry> Entries => _entries.ToList();

        /// <summary>The entries that carry the named template value <paramref name="property"/>.</summary>
        public IReadOnlyList<Entry> With(string property) => Entries.Where(e => e.Values.ContainsKey(property)).ToList();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            var values = state is IEnumerable<KeyValuePair<string, object>> pairs
                ? pairs.GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.First().Value)
                : new Dictionary<string, object>();

            _entries.Enqueue(new Entry(logLevel, values));
        }
    }
}
