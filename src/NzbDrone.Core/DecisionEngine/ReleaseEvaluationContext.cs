using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace NzbDrone.Core.DecisionEngine
{
    // A lazy, per-search read snapshot. Never used by imports, refreshes or download submission.
    public sealed class ReleaseEvaluationContext : IDisposable
    {
        private static readonly AsyncLocal<ReleaseEvaluationContext> Ambient = new AsyncLocal<ReleaseEvaluationContext>();
        private readonly ConcurrentDictionary<(string Operation, object Key), Lazy<object>> _values = new ConcurrentDictionary<(string, object), Lazy<object>>();
        private readonly ConcurrentDictionary<string, long> _ticks = new ConcurrentDictionary<string, long>();
        private readonly ReleaseEvaluationContext _previous;
        private long _hits;
        private long _misses;

        public static bool IsActive => Ambient.Value != null;

        public ReleaseEvaluationContext()
        {
            _previous = Ambient.Value;
            Ambient.Value = this;
        }

        public static T Read<T>(string operation, object key, Func<T> read)
        {
            var context = Ambient.Value;
            if (context == null)
            {
                return read();
            }

            var candidate = new Lazy<object>(
                () =>
                {
                    Interlocked.Increment(ref context._misses);
                    return Measure(operation, read);
                },
                LazyThreadSafetyMode.ExecutionAndPublication);
            var value = context._values.GetOrAdd((operation, key), candidate);
            if (!ReferenceEquals(value, candidate))
            {
                Interlocked.Increment(ref context._hits);
            }

            return (T)value.Value;
        }

        public static T Measure<T>(string operation, Func<T> action)
        {
            var context = Ambient.Value;
            if (context == null)
            {
                return action();
            }

            var start = Stopwatch.GetTimestamp();
            try
            {
                return action();
            }
            finally
            {
                var elapsed = Stopwatch.GetTimestamp() - start;
                context._ticks.AddOrUpdate(operation, elapsed, (_, ticks) => ticks + elapsed);
            }
        }

        public string Summary()
        {
            var stages = _ticks.OrderByDescending(x => x.Value).Take(12)
                .Select(x => $"{x.Key}={x.Value * 1000.0 / Stopwatch.Frequency:F1}ms");
            return $"cache hits={_hits}, misses={_misses}; cumulative worker timings (nested): {string.Join(", ", stages)}";
        }

        public void Dispose()
        {
            Ambient.Value = _previous;
        }
    }
}
