using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;

namespace AtlasBiomeHighlighter
{
    public partial class AtlasBiomeHighlighter
    {
        private const string SignalPositionDebugFileName = "AtlasBiomeHighlighter.SignalPositionDebug.txt";
        private const int SignalDebugMaximumMotionSamples = 1024;
        private const int SignalDebugHeartbeatMs = 2_000;
        private const int SignalDebugAnomalyThrottleMs = 250;
        private const int SignalDebugFlushMs = 1_500;
        private const float SignalDebugPositionBucketSize = 12f;

        private readonly List<SignalPositionSample> _signalDebugFrameSamples = new(256);
        private readonly Dictionary<(int x, int y), SignalPreviousPosition> _signalDebugPreviousByCoordinate = new(1024);
        private readonly Dictionary<(int x, int y), SignalPositionBucket> _signalDebugBuckets = new(256);
        private readonly float[] _signalDebugMotionX = new float[SignalDebugMaximumMotionSamples];
        private readonly float[] _signalDebugMotionY = new float[SignalDebugMaximumMotionSamples];
        private readonly StringBuilder _signalDebugPending = new(16 * 1024);

        private long _signalDebugFrameNow;
        private long _signalDebugFrameSequence;
        private long _signalDebugLastHeartbeatMs;
        private long _signalDebugLastAnomalyMs;
        private long _signalDebugLastFlushMs;
        private long _signalDebugLastPlaceholderReportMs;
        private long _signalDebugFilteredPlaceholderCount;
        private bool _signalDebugHeaderWritten;
        private bool _signalDebugFrameEnabled;

        private readonly struct SignalPreviousPosition
        {
            public SignalPreviousPosition(Vector2 center, long seenMs)
            {
                Center = center;
                SeenMs = seenMs;
            }

            public Vector2 Center { get; }
            public long SeenMs { get; }
        }

        private readonly struct SignalPositionSample
        {
            public SignalPositionSample(
                NodeRenderInfo info,
                (int x, int y) coordinate,
                Vector2 center,
                Vector2 previousCenter,
                bool hasPrevious)
            {
                Info = info;
                Coordinate = coordinate;
                Center = center;
                PreviousCenter = previousCenter;
                HasPrevious = hasPrevious;
            }

            public NodeRenderInfo Info { get; }
            public (int x, int y) Coordinate { get; }
            public Vector2 Center { get; }
            public Vector2 PreviousCenter { get; }
            public bool HasPrevious { get; }
            public Vector2 Delta => HasPrevious ? Center - PreviousCenter : Vector2.Zero;
        }

        private struct SignalPositionBucket
        {
            public int Count;
            public (int x, int y) FirstCoordinate;
            public bool ContainsDifferentCoordinates;
        }

        private void BeginAtlasSignalPositionDebugFrame()
        {
            _signalDebugFrameEnabled = SignalPositionDebugEnabled();
            if (!_signalDebugFrameEnabled)
                return;

            _signalDebugFrameSamples.Clear();
            _signalDebugBuckets.Clear();
            _signalDebugFrameNow = Environment.TickCount64;
            unchecked { _signalDebugFrameSequence++; }
        }

        private void ObserveAtlasSignalPosition(NodeRenderInfo info, Vector2 center)
        {
            if (!_signalDebugFrameEnabled)
                return;

            (int x, int y) coordinate;
            try
            {
                coordinate = (info.Node.Coordinate.X, info.Node.Coordinate.Y);
            }
            catch
            {
                return;
            }

            bool hasPrevious =
                _signalDebugPreviousByCoordinate.TryGetValue(coordinate, out var previous) &&
                _signalDebugFrameNow - previous.SeenMs <= 1_000;

            _signalDebugFrameSamples.Add(new SignalPositionSample(
                info,
                coordinate,
                center,
                hasPrevious ? previous.Center : center,
                hasPrevious));
            _signalDebugPreviousByCoordinate[coordinate] = new SignalPreviousPosition(center, _signalDebugFrameNow);

            var bucketKey = GetSignalDebugBucketKey(center);
            if (!_signalDebugBuckets.TryGetValue(bucketKey, out var bucket))
            {
                bucket = new SignalPositionBucket
                {
                    Count = 1,
                    FirstCoordinate = coordinate
                };
            }
            else
            {
                bucket.Count++;
                if (bucket.FirstCoordinate != coordinate)
                    bucket.ContainsDifferentCoordinates = true;
            }

            _signalDebugBuckets[bucketKey] = bucket;
        }

        private void CompleteAtlasSignalPositionDebugFrame()
        {
            if (!_signalDebugFrameEnabled)
                return;

            int motionCount = 0;
            for (int i = 0; i < _signalDebugFrameSamples.Count && motionCount < SignalDebugMaximumMotionSamples; i++)
            {
                var sample = _signalDebugFrameSamples[i];
                if (!sample.HasPrevious)
                    continue;

                Vector2 delta = sample.Delta;
                if (!IsFiniteSignalDebugVector(delta) || delta.LengthSquared() > 16_000_000f)
                    continue;

                _signalDebugMotionX[motionCount] = delta.X;
                _signalDebugMotionY[motionCount] = delta.Y;
                motionCount++;
            }

            Vector2 commonMotion = Vector2.Zero;
            if (motionCount > 0)
            {
                Array.Sort(_signalDebugMotionX, 0, motionCount);
                Array.Sort(_signalDebugMotionY, 0, motionCount);
                commonMotion = new Vector2(
                    _signalDebugMotionX[motionCount / 2],
                    _signalDebugMotionY[motionCount / 2]);
            }

            int suspiciousCount = CountSuspiciousSignalPositions(commonMotion);
            bool moving = commonMotion.LengthSquared() >= 4f;
            long now = _signalDebugFrameNow;

            if (suspiciousCount > 0 && now - _signalDebugLastAnomalyMs >= SignalDebugAnomalyThrottleMs)
            {
                _signalDebugLastAnomalyMs = now;
                AppendSignalDebugAnomaly(commonMotion, suspiciousCount);
            }
            else if (moving && now - _signalDebugLastHeartbeatMs >= SignalDebugHeartbeatMs)
            {
                _signalDebugLastHeartbeatMs = now;
                AppendSignalDebugHeartbeat(commonMotion);
            }

            PruneSignalDebugPreviousPositions(now);
            FlushAtlasSignalPositionDebug(now, force: false);
        }

        private int CountSuspiciousSignalPositions(Vector2 commonMotion)
        {
            int count = 0;
            for (int i = 0; i < _signalDebugFrameSamples.Count; i++)
            {
                if (IsSuspiciousSignalPosition(_signalDebugFrameSamples[i], commonMotion, out _, out _, out _))
                    count++;
            }
            return count;
        }

        private bool IsSuspiciousSignalPosition(
            SignalPositionSample sample,
            Vector2 commonMotion,
            out float jump,
            out float residual,
            out int sharedBucketCount)
        {
            jump = sample.HasPrevious ? sample.Delta.Length() : 0f;
            residual = sample.HasPrevious ? (sample.Delta - commonMotion).Length() : 0f;

            var bucketKey = GetSignalDebugBucketKey(sample.Center);
            sharedBucketCount = 0;
            bool sharedPosition = false;
            if (_signalDebugBuckets.TryGetValue(bucketKey, out var bucket))
            {
                sharedBucketCount = bucket.Count;
                sharedPosition = bucket.Count > 1 && bucket.ContainsDifferentCoordinates;
            }

            if (!IsFiniteSignalDebugVector(sample.Center))
                return true;
            if (sharedPosition)
                return true;

            float width = Math.Max(1f, _currentRenderDisplaySize.X);
            bool nearHorizontalEdge = sample.Center.X <= width * 0.20f || sample.Center.X >= width * 0.80f;
            return sample.HasPrevious &&
                   ((jump >= 240f && residual >= 150f) ||
                    (nearHorizontalEdge && jump >= 150f && residual >= 100f));
        }

        private void AppendSignalDebugAnomaly(Vector2 commonMotion, int suspiciousCount)
        {
            EnsureSignalDebugHeader();
            _signalDebugPending.Append('[')
                .Append(DateTime.Now.ToString("HH:mm:ss.fff"))
                .Append("] ANOMALY frame=").Append(_signalDebugFrameSequence)
                .Append(" tick=").Append(_signalDebugFrameNow)
                .Append(" labels=").Append(_signalDebugFrameSamples.Count)
                .Append(" suspicious=").Append(suspiciousCount)
                .Append(" commonMotion=").Append(FormatSignalDebugVector(commonMotion))
                .Append(" visibleCache=").Append(_visibleNodeInfos.Count)
                .Append(" atlasNodes=").Append(_atlasNodes.Length)
                .AppendLine();

            AppendSignalDebugPanelSnapshot();

            int written = 0;
            for (int i = 0; i < _signalDebugFrameSamples.Count && written < 16; i++)
            {
                var sample = _signalDebugFrameSamples[i];
                if (!IsSuspiciousSignalPosition(sample, commonMotion, out float jump, out float residual, out int bucketCount))
                    continue;

                written++;
                AppendSignalDebugSample(sample, jump, residual, bucketCount);
            }

            _signalDebugPending.AppendLine("---");
        }

        private void AppendSignalDebugHeartbeat(Vector2 commonMotion)
        {
            EnsureSignalDebugHeader();
            _signalDebugPending.Append('[')
                .Append(DateTime.Now.ToString("HH:mm:ss.fff"))
                .Append("] MOVE frame=").Append(_signalDebugFrameSequence)
                .Append(" tick=").Append(_signalDebugFrameNow)
                .Append(" labels=").Append(_signalDebugFrameSamples.Count)
                .Append(" commonMotion=").Append(FormatSignalDebugVector(commonMotion))
                .Append(" visibleCache=").Append(_visibleNodeInfos.Count)
                .AppendLine();
        }

        private void RecordFilteredAtlasSignalPlaceholder(NodeRenderInfo info, Vector2 center, string reason)
        {
            if (!_signalDebugFrameEnabled)
                return;

            long now = Environment.TickCount64;
            _signalDebugFilteredPlaceholderCount++;
            if (now - _signalDebugLastPlaceholderReportMs < 1_000)
                return;

            _signalDebugLastPlaceholderReportMs = now;
            EnsureSignalDebugHeader();

            string name = info.MapName ?? info.UniqueName ?? info.BiomeDisplay;
            _signalDebugPending.Append('[')
                .Append(DateTime.Now.ToString("HH:mm:ss.fff"))
                .Append("] FILTERED_PLACEHOLDER total=").Append(_signalDebugFilteredPlaceholderCount)
                .Append(" reason=").Append(reason)
                .Append(" name='").Append(SanitizeSignalDebugText(name)).Append('\'')
                .Append(" center=").Append(FormatSignalDebugVector(center));

            try
            {
                var coordinate = info.Node.Coordinate;
                var element = info.Node.Element;
                _signalDebugPending.Append(" coord=").Append(coordinate.X).Append(',').Append(coordinate.Y)
                    .Append(" address=0x").Append(element.Address.ToString("X"))
                    .Append(" position=").Append(FormatSignalDebugVector(new Vector2(element.Position.X, element.Position.Y)))
                    .Append(" size=").Append(element.Width.ToString("0.##")).Append('x').Append(element.Height.ToString("0.##"))
                    .Append(" childCount=").Append(element.ChildCount)
                    .Append(" visible=").Append(element.IsVisible).Append('/').Append(element.IsVisibleLocal);
            }
            catch (Exception ex)
            {
                _signalDebugPending.Append(" snapshotError=").Append(ex.GetType().Name);
            }

            _signalDebugPending.AppendLine();
            FlushAtlasSignalPositionDebug(now, force: false);
        }

        private void AppendSignalDebugPanelSnapshot()
        {
            try
            {
                if (_atlasPanel == null)
                {
                    _signalDebugPending.AppendLine("  panel=null");
                    return;
                }

                _signalDebugPending.Append("  panel address=0x").Append(_atlasPanel.Address.ToString("X"))
                    .Append(" visible=").Append(_atlasPanel.IsVisible)
                    .Append('/').Append(_atlasPanel.IsVisibleLocal)
                    .Append(" active=").Append(_atlasPanel.IsActive)
                    .Append(" scale=").Append(_atlasPanel.Scale.ToString("0.###"))
                    .Append(" center=").Append(FormatSignalDebugVector(new Vector2(_atlasPanel.Center.X, _atlasPanel.Center.Y)))
                    .Append(" position=").Append(FormatSignalDebugVector(new Vector2(_atlasPanel.Position.X, _atlasPanel.Position.Y)))
                    .Append(" size=").Append(_atlasPanel.Width.ToString("0.##")).Append('x').Append(_atlasPanel.Height.ToString("0.##"))
                    .Append(" childCount=").Append(_atlasPanel.ChildCount)
                    .Append(" display=").Append(FormatSignalDebugVector(_currentRenderDisplaySize))
                    .AppendLine();
            }
            catch (Exception ex)
            {
                _signalDebugPending.Append("  panelSnapshotError=").Append(ex.GetType().Name).AppendLine();
            }
        }

        private void AppendSignalDebugSample(SignalPositionSample sample, float jump, float residual, int bucketCount)
        {
            string mapName = sample.Info.MapName ?? sample.Info.UniqueName ?? sample.Info.BiomeDisplay;
            _signalDebugPending.Append("  node coord=").Append(sample.Coordinate.x).Append(',').Append(sample.Coordinate.y)
                .Append(" name='").Append(SanitizeSignalDebugText(mapName)).Append('\'')
                .Append(" center=").Append(FormatSignalDebugVector(sample.Center))
                .Append(" previous=").Append(FormatSignalDebugVector(sample.PreviousCenter))
                .Append(" delta=").Append(FormatSignalDebugVector(sample.Delta))
                .Append(" jump=").Append(jump.ToString("0.##"))
                .Append(" residual=").Append(residual.ToString("0.##"))
                .Append(" bucketCount=").Append(bucketCount)
                .Append(" status=L:").Append(sample.Info.Locked)
                .Append(" U:").Append(sample.Info.Unlocked)
                .Append(" V:").Append(sample.Info.Visited)
                .Append(" A:").Append(sample.Info.Attempted)
                .Append(" C:").Append(sample.Info.Completed)
                .Append(" flags=").Append(sample.Info.SpecialFlags)
                .AppendLine();

            try
            {
                var element = sample.Info.Node.Element;
                if (element == null)
                {
                    _signalDebugPending.AppendLine("    element=null");
                    return;
                }

                _signalDebugPending.Append("    element address=0x").Append(element.Address.ToString("X"))
                    .Append(" visible=").Append(element.IsVisible)
                    .Append('/').Append(element.IsVisibleLocal)
                    .Append(" active=").Append(element.IsActive)
                    .Append(" indexInParent=").Append(element.IndexInParent)
                    .Append(" childCount=").Append(element.ChildCount)
                    .Append(" liveCenter=").Append(FormatSignalDebugVector(new Vector2(element.Center.X, element.Center.Y)))
                    .Append(" position=").Append(FormatSignalDebugVector(new Vector2(element.Position.X, element.Position.Y)))
                    .Append(" size=").Append(element.Width.ToString("0.##")).Append('x').Append(element.Height.ToString("0.##"))
                    .AppendLine();
            }
            catch (Exception ex)
            {
                _signalDebugPending.Append("    elementSnapshotError=").Append(ex.GetType().Name)
                    .Append(": ").Append(SanitizeSignalDebugText(ex.Message)).AppendLine();
            }
        }

        private void EnsureSignalDebugHeader()
        {
            if (_signalDebugHeaderWritten)
                return;

            _signalDebugHeaderWritten = true;
            _signalDebugPending.AppendLine("AtlasBiomeHighlighter Atlas Signal position diagnostics")
                .Append("Started: ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).AppendLine()
                .AppendLine("ANOMALY = non-finite center, multiple atlas coordinates sharing one screen point, or a node movement that strongly disagrees with the frame median.")
                .AppendLine("MOVE = throttled heartbeat used to correlate this file with ExileCore's 'Element with index: 0 not found' timestamps.")
                .AppendLine("No render positions are modified by this diagnostic build.")
                .AppendLine("===");
        }

        private void FlushAtlasSignalPositionDebug(long now, bool force)
        {
            if (_signalDebugPending.Length == 0)
                return;
            if (!force && _signalDebugPending.Length < 16 * 1024 && now - _signalDebugLastFlushMs < SignalDebugFlushMs)
                return;

            try
            {
                string path = Path.Combine(DirectoryFullName, SignalPositionDebugFileName);
                File.AppendAllText(path, _signalDebugPending.ToString(), Encoding.UTF8);
                _signalDebugPending.Clear();
                _signalDebugLastFlushMs = now;
            }
            catch
            {
                // Diagnostics must never affect the plugin's render path.
            }
        }

        private void PruneSignalDebugPreviousPositions(long now)
        {
            if (_signalDebugPreviousByCoordinate.Count < 2_048 || (_signalDebugFrameSequence & 255) != 0)
                return;

            var stale = new List<(int x, int y)>(64);
            foreach (var pair in _signalDebugPreviousByCoordinate)
            {
                if (now - pair.Value.SeenMs > 10_000)
                    stale.Add(pair.Key);
            }

            for (int i = 0; i < stale.Count; i++)
                _signalDebugPreviousByCoordinate.Remove(stale[i]);
        }

        private bool SignalPositionDebugEnabled()
        {
            try
            {
                return Settings?.DebugMode?.Value == true &&
                       Settings?.DebugSignalPositions?.Value == true;
            }
            catch { return false; }
        }

        private void ClearAtlasSignalPositionDebugLog()
        {
            _signalDebugPending.Clear();
            _signalDebugFrameSamples.Clear();
            _signalDebugPreviousByCoordinate.Clear();
            _signalDebugBuckets.Clear();
            _signalDebugHeaderWritten = false;
            _signalDebugLastFlushMs = 0;
            _signalDebugLastHeartbeatMs = 0;
            _signalDebugLastAnomalyMs = 0;
            _signalDebugLastPlaceholderReportMs = 0;
            _signalDebugFilteredPlaceholderCount = 0;
            _signalDebugFrameEnabled = false;

            try
            {
                string path = Path.Combine(DirectoryFullName, SignalPositionDebugFileName);
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }

        private static (int x, int y) GetSignalDebugBucketKey(Vector2 center)
        {
            if (!IsFiniteSignalDebugVector(center))
                return (int.MinValue, int.MinValue);
            return (
                (int)MathF.Round(center.X / SignalDebugPositionBucketSize),
                (int)MathF.Round(center.Y / SignalDebugPositionBucketSize));
        }

        private static bool IsFiniteSignalDebugVector(Vector2 value)
        {
            return float.IsFinite(value.X) && float.IsFinite(value.Y);
        }

        private static string FormatSignalDebugVector(Vector2 value)
        {
            return $"<{value.X:0.##},{value.Y:0.##}>";
        }

        private static string SanitizeSignalDebugText(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            return value.Replace('\r', ' ').Replace('\n', ' ');
        }
    }
}
