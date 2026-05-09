using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace rancher_minimap
{
    internal static class TimeTracker
    {
#if RMM_DIAGNOSTICS
        private sealed class Bucket
        {
            public int Count;
            public int SlowCount;
            public double TotalMs;
            public double MaxMs;

            public void Add(double elapsedMs, double slowThresholdMs)
            {
                Count++;
                TotalMs += elapsedMs;
                if (elapsedMs > MaxMs)
                    MaxMs = elapsedMs;
                if (elapsedMs >= slowThresholdMs)
                    SlowCount++;
            }

            public void Clear()
            {
                Count = 0;
                SlowCount = 0;
                TotalMs = 0.0;
                MaxMs = 0.0;
            }
        }

        private sealed class Scope : IDisposable
        {
            private readonly string _name;
            private readonly Stopwatch _watch;
            private bool _disposed;

            public Scope(string name)
            {
                _name = name;
                _watch = Stopwatch.StartNew();
            }

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                _watch.Stop();
                Record(_name, _watch.Elapsed.TotalMilliseconds);
            }
        }

        private static readonly Dictionary<string, Bucket> Buckets = new Dictionary<string, Bucket>(StringComparer.Ordinal);
        private static bool _diagnosticsEnabled;
        private static bool _performanceLoggingEnabled;
        private static float _nextSummaryAt;
        private static double _slowThresholdMs = 2.0;

        public static void Configure(bool diagnosticsEnabled, bool performanceLoggingEnabled, float slowThresholdMs = 2.0f)
        {
            _diagnosticsEnabled = diagnosticsEnabled;
            _performanceLoggingEnabled = performanceLoggingEnabled;
            _slowThresholdMs = Math.Max(0.05, slowThresholdMs);
        }

        public static IDisposable Measure(string name)
        {
            if (!_diagnosticsEnabled || !_performanceLoggingEnabled || string.IsNullOrEmpty(name))
                return null;

            return new Scope(name);
        }

        public static void TickSummary()
        {
            if (!_diagnosticsEnabled || !_performanceLoggingEnabled)
                return;

            var now = Time.realtimeSinceStartup;
            if (now < _nextSummaryAt)
                return;

            _nextSummaryAt = now + 5.0f;
            var rows = new List<string[]>();
            foreach (var pair in Buckets)
            {
                var bucket = pair.Value;
                if (bucket == null || bucket.Count == 0)
                    continue;

                var averageMs = bucket.TotalMs / bucket.Count;
                rows.Add(new[]
                {
                    pair.Key,
                    bucket.Count.ToString(),
                    averageMs.ToString("F3"),
                    bucket.MaxMs.ToString("F3"),
                    bucket.SlowCount.ToString()
                });
                bucket.Clear();
            }

            if (rows.Count == 0)
                return;

            var builder = new System.Text.StringBuilder();
            builder.Append("perf: summary slow>=").Append(_slowThresholdMs.ToString("F2")).Append("ms");
            DiagnosticTable.Append(builder, new[] { "scope", "count", "avgMs", "maxMs", "slow" }, rows, maxCellLength: 40);
            Log.Info(builder.ToString());
        }

        private static void Record(string name, double elapsedMs)
        {
            if (!_diagnosticsEnabled || !_performanceLoggingEnabled || string.IsNullOrEmpty(name))
                return;

            if (!Buckets.TryGetValue(name, out var bucket))
            {
                bucket = new Bucket();
                Buckets[name] = bucket;
            }

            bucket.Add(elapsedMs, _slowThresholdMs);
        }
#else
        public static void Configure(bool diagnosticsEnabled, bool performanceLoggingEnabled, float slowThresholdMs = 2.0f) { }
        public static IDisposable Measure(string name) => null;
        public static void TickSummary() { }
#endif
    }
}
