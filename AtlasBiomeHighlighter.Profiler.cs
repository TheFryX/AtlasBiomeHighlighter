using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace AtlasBiomeHighlighter
{
    public partial class AtlasBiomeHighlighter
    {
        private readonly Dictionary<string, long> _lastProfilerLogMsByName = new(StringComparer.Ordinal);

        private IDisposable ProfileScope(string name)
        {
            if (!Settings.DebugMode.Value || !Settings.PerformanceProfiling.Value)
                return NoopProfileScope.Instance;

            return new ProfileScopeImpl(this, name, Stopwatch.GetTimestamp());
        }

        private void ReportProfileSample(string name, long startTimestamp)
        {
            if (!Settings.DebugMode.Value || !Settings.PerformanceProfiling.Value)
                return;

            double elapsedMs = (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;
            if (elapsedMs < Settings.PerformanceSpikeThresholdMs.Value)
                return;

            ReportProfileElapsedMs(name, elapsedMs);
        }


        private void ReportProfileElapsedMs(string name, double elapsedMs)
        {
            if (!Settings.DebugMode.Value || !Settings.PerformanceProfiling.Value)
                return;

            if (elapsedMs < Settings.PerformanceSpikeThresholdMs.Value)
                return;

            long now = Environment.TickCount64;
            if (_lastProfilerLogMsByName.TryGetValue(name, out var last) && now - last < 250)
                return;

            _lastProfilerLogMsByName[name] = now;
            LogMessage($"[AtlasBiomeHighlighter] {name} spike: {elapsedMs:F2} ms", 3);
        }

        private void ReportProfileElapsedTicks(string name, long elapsedTicks)
        {
            if (!Settings.DebugMode.Value || !Settings.PerformanceProfiling.Value || elapsedTicks <= 0)
                return;

            double elapsedMs = elapsedTicks * 1000.0 / Stopwatch.Frequency;
            ReportProfileElapsedMs(name, elapsedMs);
        }

        private sealed class ProfileScopeImpl : IDisposable
        {
            private readonly AtlasBiomeHighlighter _owner;
            private readonly string _name;
            private readonly long _startTimestamp;
            private bool _disposed;

            public ProfileScopeImpl(AtlasBiomeHighlighter owner, string name, long startTimestamp)
            {
                _owner = owner;
                _name = name;
                _startTimestamp = startTimestamp;
            }

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                _owner.ReportProfileSample(_name, _startTimestamp);
            }
        }

        private sealed class NoopProfileScope : IDisposable
        {
            public static readonly NoopProfileScope Instance = new();
            private NoopProfileScope() { }
            public void Dispose() { }
        }
    }
}
