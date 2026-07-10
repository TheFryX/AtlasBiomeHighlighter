using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace AtlasBiomeHighlighter
{
    public partial class AtlasBiomeHighlighter
    {
        private const string PerformanceSpikeLogFileName = "AtlasBiomeHighlighter.PerformanceSpikes.txt";
        private readonly Dictionary<string, long> _lastProfilerLogMsByName = new(StringComparer.Ordinal);
        private readonly ConcurrentQueue<string> _performanceSpikeLogQueue = new();
        private int _performanceSpikeWriterScheduled;

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
            WritePerformanceSpike(name, elapsedMs);
        }

        private void ReportProfileElapsedTicks(string name, long elapsedTicks)
        {
            if (!Settings.DebugMode.Value || !Settings.PerformanceProfiling.Value || elapsedTicks <= 0)
                return;

            double elapsedMs = elapsedTicks * 1000.0 / Stopwatch.Frequency;
            ReportProfileElapsedMs(name, elapsedMs);
        }

        private void WritePerformanceSpike(string name, double elapsedMs)
        {
            try
            {
                var thresholdMs = Settings.PerformanceSpikeThresholdMs.Value;
                var line = string.Create(
                    CultureInfo.InvariantCulture,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {name} spike: {elapsedMs:F2} ms (threshold: {thresholdMs} ms){Environment.NewLine}");
                QueuePerformanceSpikeWrite(line);
            }
            catch
            {
            }
        }

        private void WritePerformanceSpikeDetails(string name, double elapsedMs, string details)
        {
            try
            {
                var thresholdMs = Settings.PerformanceSpikeThresholdMs.Value;
                var sb = new StringBuilder(256);
                sb.Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)).Append("] ");
                sb.Append(name).Append(" spike: ").Append(elapsedMs.ToString("F2", CultureInfo.InvariantCulture));
                sb.Append(" ms (threshold: ").Append(thresholdMs.ToString(CultureInfo.InvariantCulture)).AppendLine(" ms)");
                if (!string.IsNullOrWhiteSpace(details))
                    sb.AppendLine(details);
                QueuePerformanceSpikeWrite(sb.ToString());
            }
            catch
            {
            }
        }

        private void QueuePerformanceSpikeWrite(string text)
        {
            _performanceSpikeLogQueue.Enqueue(text);
            if (Interlocked.CompareExchange(ref _performanceSpikeWriterScheduled, 1, 0) != 0)
                return;

            ThreadPool.UnsafeQueueUserWorkItem(
                static owner => owner.DrainPerformanceSpikeWrites(),
                this,
                preferLocal: false);
        }

        private void DrainPerformanceSpikeWrites()
        {
            try
            {
                var path = Path.Combine(DirectoryFullName, PerformanceSpikeLogFileName);
                var batch = new StringBuilder(2048);

                while (true)
                {
                    while (_performanceSpikeLogQueue.TryDequeue(out var line))
                        batch.Append(line);

                    if (batch.Length != 0)
                    {
                        File.AppendAllText(path, batch.ToString(), Encoding.UTF8);
                        batch.Clear();
                    }

                    Interlocked.Exchange(ref _performanceSpikeWriterScheduled, 0);
                    if (_performanceSpikeLogQueue.IsEmpty ||
                        Interlocked.CompareExchange(ref _performanceSpikeWriterScheduled, 1, 0) != 0)
                    {
                        return;
                    }
                }
            }
            catch
            {
                Interlocked.Exchange(ref _performanceSpikeWriterScheduled, 0);
            }
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
