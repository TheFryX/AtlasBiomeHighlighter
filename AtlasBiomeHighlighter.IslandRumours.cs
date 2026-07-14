using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using ExileCore2.PoEMemory;
using ExileCore2.PoEMemory.Elements.AtlasElements;
using ImGuiNET;

namespace AtlasBiomeHighlighter
{
    public partial class AtlasBiomeHighlighter
    {
        private readonly Dictionary<long, IslandRumourSnapshot> _islandRumourSnapshotsByKey = new(128);
        private readonly List<IslandRumourSnapshot> _islandRumourSnapshots = new(128);
        private readonly HashSet<string> _observedIslandRumours = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<(string Text, int FontSize), Vector2> _islandRumourTextSizeCache = new(512);
        private readonly Dictionary<(string Text, int FontSize, int MaxWidth), string> _islandRumourFittedTextCache = new(512);
        private readonly float[] _islandRumourNaturalColumnWidths = new float[4];
        private readonly float[] _islandRumourColumnLefts = new float[4];

        // AtlasPanel.Buttons is a List<AtlasButtonNode> in the supplied ExileCore2 build.
        // AtlasButtonNode.Rumors is Dictionary<string, int>, so discovery can use the exact
        // cached game-facing API instead of walking tooltip UI trees.
        private readonly HashSet<long> _islandRumourSeenButtonKeys = new();
        private readonly HashSet<long> _islandRumourRegionSeenScratch = new();
        private readonly HashSet<long> _islandRumourCompatibilityProbedKeys = new();
        private readonly List<string> _islandRumourReadScratch = new(16);
        private readonly HashSet<string> _islandRumourReadSeenScratch = new(StringComparer.Ordinal);
        private readonly List<string> _islandRumourTooltipCandidateScratch = new(3);
        private readonly List<AtlasButtonNode> _islandRumourLiveTooltipCandidates = new(4);
        private readonly Dictionary<long, int> _islandRumourLiveBestScoreByKey = new(16);
        private readonly List<IslandRumourRenderCandidate> _islandRumourRenderCandidates = new(8);

        // Incremental schedulers. They preserve the existing Rumours/tooltip parsing and source
        // priority exactly, but distribute full-atlas walks over multiple game ticks.
        private readonly List<AtlasButtonNode> _islandRumourCacheScanButtons = new(512);
        private int _islandRumourCacheScanIndex;
        private int _islandRumourCacheScanButtonCount;
        private string _islandRumourCacheScanFirstSource = string.Empty;
        private bool _islandRumourCacheScanInProgress;

        private readonly List<AtlasButtonNode> _islandRumourTooltipDiscoveryButtons = new(512);
        private int _islandRumourTooltipDiscoveryIndex;
        private bool _islandRumourTooltipDiscoveryInProgress;
        private readonly List<AtlasButtonNode> _islandRumourTooltipDiscoveryVisibleCandidates = new(8);
        private readonly Dictionary<long, IslandRumourTooltipDiscoveryCandidate> _islandRumourTooltipDiscoveryBestByKey = new(32);
        private readonly Dictionary<long, IslandRumourTooltipDiscoveryCandidate> _islandRumourPollBestByKey = new(16);

        // Discovery keeps the original all-buttons winner rules, but publication is decoupled
        // from discovery completion. Applying every hidden-tooltip result in one Tick was the
        // remaining source of 50-100 ms frame-time spikes.
        private readonly List<long> _islandRumourTooltipPublishKeys = new(32);
        private readonly Dictionary<long, IslandRumourTooltipDiscoveryCandidate> _islandRumourTooltipPublishByKey = new(32);
        private int _islandRumourTooltipPublishIndex;

        private readonly Dictionary<(int x, int y), IslandRumourRegionStats> _islandRumourRegionStatsByCoordinate = new(32);
        private int _islandRumourRegionStatsCursor;

        private long _lastIslandRumourLiveTooltipRefreshMs;
        private long _lastIslandRumourTooltipDiscoveryMs;
        private bool _islandRumourForceRefresh = true;
        private bool _islandRumourLiveScanWasEnabled;

        // Camera motion throttles only tooltip discovery. Rendering remains visually stable.
        private AtlasNodeDescription? _islandRumourCameraAnchorNode;
        private Vector2 _islandRumourLastCameraAnchorCenter;
        private bool _islandRumourHasCameraAnchorCenter;
        private long _islandRumourLastCameraMovementMs;

        private static readonly string[] IslandRumourTableHeaders = { "Rumours", "Mods", "Map type", "Tier" };
        private static readonly float[] IslandRumourMinimumColumnWidths = { 92f, 112f, 104f, 46f };
        private static readonly float[] IslandRumourMaximumColumnWidths = { 178f, 205f, 176f, 58f };
        private static readonly int[] IslandRumourColumnShrinkOrder = { 1, 2, 0, 3 };
        private long _lastIslandRumourRefreshMs;
        private int _islandRumourLastButtonCount;
        private int _islandRumourLastNodeCount;
        private int _islandRumourLastRumourCount;
        private string _islandRumourLastSource = string.Empty;
        private string _islandRumourLastError = string.Empty;
        private const int IslandRumourCameraSettleMs = 180;
        private const int IslandRumourLiveTooltipRefreshMs = 100;
        private const int IslandRumourLiveTooltipRefreshWhileMovingMs = 350;
        private const int IslandRumourTooltipDiscoveryMs = 200;
        private const int IslandRumourTooltipDiscoveryWhileMovingMs = 750;
        private const int IslandRumourTooltipAuthorityGraceMs = 500;
        private const int IslandRumourVisibleTooltipScore = 8;
        private const int IslandRumourRegionStatsFastRetryMs = 120;
        private const int IslandRumourRegionStatsRetryMs = 1500;
        private const float IslandRumourCameraMotionThresholdSquared = 0.64f;
        private const int IslandRumourTextCacheMaxEntries = 1024;
        private const int IslandRumourCacheMaxButtonsPerTick = 16;
        private const int IslandRumourCacheMaxButtonsPerMovingTick = 1;
        private const int IslandRumourTooltipDiscoveryMaxButtonsPerTick = 12;
        private const int IslandRumourTooltipDiscoveryMaxButtonsPerMovingTick = 1;
        private const int IslandRumourTooltipPublishMaxItemsPerTick = 1;
        private const double IslandRumourCacheTimeBudgetMs = 0.85;
        private const double IslandRumourTooltipDiscoveryTimeBudgetMs = 0.65;
        private const double IslandRumourTooltipPublishTimeBudgetMs = 0.35;
        private const double IslandRumourMovingTotalWorkBudgetMs = 1.15;
        private const float IslandRumourPanelPaddingX = 10f;
        private const float IslandRumourPanelPaddingY = 7f;
        private const float IslandRumourColumnGap = 10f;
        private const float IslandRumourCellPaddingX = 4f;
        private const float IslandRumourViewportMargin = 8f;
        private const float IslandRumourTopAccentHeight = 3f;

        private sealed class IslandRumourSnapshot
        {
            public long Key;
            public AtlasButtonNode? Button;
            public string[] Rumours = Array.Empty<string>();
            public string[] Tokens = Array.Empty<string>();
            public IslandRumourTableRow[] TableRows = Array.Empty<IslandRumourTableRow>();
            public string Signature = string.Empty;
            public (int x, int y)? Coordinate;
            public (int x, int y)? RegionCoordinate;
            public int RegionMapCount;
            public int RegionGrandExpeditionCount;
            public string RegionStatsSource = string.Empty;
            public string RegionStatsText = string.Empty;
            public IslandRumourTableLayout? TableLayout;
            public bool RegionStatsResolved;
            public long LastVisibleTooltipReadMs;
            public long NextRegionStatsAttemptMs;
            public long LastSeenMs;
        }

        private readonly struct IslandRumourRegionStats
        {
            public IslandRumourRegionStats(int mapCount, int grandExpeditionCount, string source)
            {
                MapCount = mapCount;
                GrandExpeditionCount = grandExpeditionCount;
                Source = source ?? string.Empty;
            }

            public int MapCount { get; }
            public int GrandExpeditionCount { get; }
            public string Source { get; }
        }

        private readonly struct IslandRumourTableRow
        {
            public IslandRumourTableRow(string rumour, string mods, string mapType, string tier)
            {
                Rumour = rumour;
                Mods = mods;
                MapType = mapType;
                Tier = tier;
            }

            public string Rumour { get; }
            public string Mods { get; }
            public string MapType { get; }
            public string Tier { get; }
        }

        private struct IslandRumourRenderCandidate
        {
            public IslandRumourRenderCandidate(IslandRumourSnapshot snapshot, Vector2 center)
            {
                Snapshot = snapshot;
                Center = center;
            }

            public IslandRumourSnapshot Snapshot;
            public Vector2 Center;
        }

        private readonly struct IslandRumourTooltipDiscoveryCandidate
        {
            public IslandRumourTooltipDiscoveryCandidate(
                AtlasButtonNode button,
                IReadOnlyList<string> rumours,
                int visibilityScore)
            {
                Button = button;
                VisibilityScore = visibilityScore;
                Count = Math.Min(3, rumours.Count);
                First = Count > 0 ? rumours[0] : string.Empty;
                Second = Count > 1 ? rumours[1] : string.Empty;
                Third = Count > 2 ? rumours[2] : string.Empty;
                Signature = BuildIslandRumourSignature(rumours);
            }

            public AtlasButtonNode Button { get; }
            public int VisibilityScore { get; }
            public int Count { get; }
            public string First { get; }
            public string Second { get; }
            public string Third { get; }
            public string Signature { get; }

            public void CopyRumoursTo(List<string> destination)
            {
                destination.Clear();
                if (Count > 0) destination.Add(First);
                if (Count > 1) destination.Add(Second);
                if (Count > 2) destination.Add(Third);
            }
        }

        private readonly struct IslandRumourTableRowLayout
        {
            public IslandRumourTableRowLayout(
                IslandRumourTableRow row,
                string rumour,
                string mods,
                string mapType,
                string tier,
                Vector2 tierSize)
            {
                Row = row;
                Rumour = rumour;
                Mods = mods;
                MapType = mapType;
                Tier = tier;
                TierSize = tierSize;
            }

            public IslandRumourTableRow Row { get; }
            public string Rumour { get; }
            public string Mods { get; }
            public string MapType { get; }
            public string Tier { get; }
            public Vector2 TierSize { get; }
        }

        private sealed class IslandRumourTableLayout
        {
            public string Signature = string.Empty;
            public string RegionStatsText = string.Empty;
            public int FontSize;
            public int HeaderFontSize;
            public int FooterFontSize;
            public int MaxPanelWidth;
            public int ConfiguredRowHeight;
            public int MaxLabels;
            public int RowCount;
            public bool ShowRegionStats;
            public float ValueLineHeight;
            public float HeaderLineHeight;
            public float FooterLineHeight;
            public float HeaderHeight;
            public float RowHeight;
            public float FooterHeight;
            public float PanelWidth;
            public float PanelHeight;
            public float[] ColumnWidths = new float[4];
            public string[] Headers = new string[4];
            public IslandRumourTableRowLayout[] Rows = Array.Empty<IslandRumourTableRowLayout>();
            public string FittedStats = string.Empty;
            public Vector2 StatsSize;
        }

        private void ClearIslandRumourCache()
        {
            _islandRumourSnapshotsByKey.Clear();
            _islandRumourSnapshots.Clear();
            _islandRumourSeenButtonKeys.Clear();
            _islandRumourCompatibilityProbedKeys.Clear();
            _islandRumourReadScratch.Clear();
            _islandRumourTooltipCandidateScratch.Clear();
            _islandRumourLiveTooltipCandidates.Clear();
            _islandRumourRenderCandidates.Clear();
            _islandRumourReadSeenScratch.Clear();
            _islandRumourLiveBestScoreByKey.Clear();
            _islandRumourCacheScanButtons.Clear();
            _islandRumourCacheScanIndex = 0;
            _islandRumourCacheScanButtonCount = 0;
            _islandRumourCacheScanFirstSource = string.Empty;
            _islandRumourCacheScanInProgress = false;
            _islandRumourTooltipDiscoveryButtons.Clear();
            _islandRumourTooltipDiscoveryIndex = 0;
            _islandRumourTooltipDiscoveryInProgress = false;
            _islandRumourTooltipDiscoveryVisibleCandidates.Clear();
            _islandRumourTooltipDiscoveryBestByKey.Clear();
            _islandRumourPollBestByKey.Clear();
            _islandRumourTooltipPublishKeys.Clear();
            _islandRumourTooltipPublishByKey.Clear();
            _islandRumourTooltipPublishIndex = 0;
            _islandRumourRegionStatsByCoordinate.Clear();
            _islandRumourRegionStatsCursor = 0;
            _lastIslandRumourLiveTooltipRefreshMs = 0;
            _lastIslandRumourTooltipDiscoveryMs = 0;
            _islandRumourLastButtonCount = 0;
            _islandRumourLastNodeCount = 0;
            _islandRumourLastRumourCount = 0;
            _lastIslandRumourRefreshMs = 0;
            _islandRumourLastSource = string.Empty;
            _islandRumourLastError = string.Empty;
            _islandRumourForceRefresh = true;
            _islandRumourTextSizeCache.Clear();
            _islandRumourFittedTextCache.Clear();
            _islandRumourRegionSeenScratch.Clear();
            _islandRumourCameraAnchorNode = null;
            _islandRumourHasCameraAnchorCenter = false;
            _islandRumourLastCameraMovementMs = 0;
        }

        private void RequestIslandRumourRefresh()
        {
            _islandRumourForceRefresh = true;
            _lastIslandRumourRefreshMs = 0;
        }

        private void UpdateIslandRumourCache()
        {
            if (!Settings.IslandRumoursEnabled.Value)
            {
                if (_islandRumourSnapshots.Count != 0 || _islandRumourCameraAnchorNode != null)
                    ClearIslandRumourCache();
                return;
            }

            long now = Environment.TickCount64;
            UpdateIslandRumourCameraMotion(now);
            bool cameraMoving = IsIslandRumourCameraMoving(now);
            bool liveScanEnabled = Settings.IslandRumourLiveTooltipScanEnabled.Value;

            if (_islandRumourLiveScanWasEnabled != liveScanEnabled)
                HandleIslandRumourLiveScanModeChanged(liveScanEnabled);

            long updateStart = Stopwatch.GetTimestamp();
            if (liveScanEnabled)
            {
                try
                {
                    RefreshIslandRumourLiveTooltipCache(now);
                }
                catch (Exception ex)
                {
                    _islandRumourLastError =
                        "Live tooltip refresh " + ex.GetType().Name + ": " + ex.Message;
                }
            }

            bool movingBudgetAvailable =
                !cameraMoving ||
                ElapsedIslandRumourMilliseconds(updateStart) < IslandRumourMovingTotalWorkBudgetMs;

            int refreshMs = Math.Clamp(
                Settings.IslandRumourRefreshMs.Value,
                Settings.IslandRumourRefreshMs.Min,
                Settings.IslandRumourRefreshMs.Max);

            if (!_islandRumourCacheScanInProgress &&
                (_islandRumourForceRefresh || now - _lastIslandRumourRefreshMs >= refreshMs))
            {
                BeginIslandRumourCacheRefresh(now);
            }

            if (_islandRumourCacheScanInProgress && movingBudgetAvailable)
            {
                try
                {
                    RefreshIslandRumourCacheSlice(now, cameraMoving);
                }
                catch (Exception ex)
                {
                    _islandRumourLastError = ex.GetType().Name + ": " + ex.Message;
                    CancelIslandRumourCacheRefresh();
                }
            }

            // Maps / GE use the exact existing resolver, but only one unresolved region is
            // processed per tick and results are shared by RegionCoordinate. This removes the
            // former burst where every card resolved its complete region in a single frame.
            long regionStatsStart = Stopwatch.GetTimestamp();
            ProcessIslandRumourRegionStats(now, cameraMoving);
            ReportProfileElapsedTicks(
                "Island Rumours region stats",
                Stopwatch.GetTimestamp() - regionStatsStart);
        }

        private void HandleIslandRumourLiveScanModeChanged(bool enabled)
        {
            _islandRumourLiveScanWasEnabled = enabled;
            ResetIslandRumourLiveTooltipScanState();

            // When returning to fast mode, release the short tooltip-authority window so the
            // next AtlasButtonNode.Rumors pass can immediately restore the complete list.
            if (!enabled)
            {
                for (int i = 0; i < _islandRumourSnapshots.Count; i++)
                    _islandRumourSnapshots[i].LastVisibleTooltipReadMs = 0;
            }

            RequestIslandRumourRefresh();
        }

        private void ResetIslandRumourLiveTooltipScanState()
        {
            _islandRumourLiveTooltipCandidates.Clear();
            _islandRumourLiveBestScoreByKey.Clear();
            _islandRumourPollBestByKey.Clear();
            _islandRumourTooltipPublishKeys.Clear();
            _islandRumourTooltipPublishByKey.Clear();
            _islandRumourTooltipPublishIndex = 0;
            _lastIslandRumourLiveTooltipRefreshMs = 0;
            _lastIslandRumourTooltipDiscoveryMs = 0;
            CancelIslandRumourTooltipDiscovery();
        }

        private bool IsIslandRumourCameraMoving(long now)
        {
            return _islandRumourHasCameraAnchorCenter &&
                   now - _islandRumourLastCameraMovementMs < IslandRumourCameraSettleMs;
        }

        private void BeginIslandRumourCacheRefresh(long now)
        {
            var atlasPanel = _atlasPanel;
            var buttons = atlasPanel?.Buttons;
            if (buttons == null || buttons.Count == 0)
            {
                _islandRumourLastError = "AtlasPanel.Buttons is empty; keeping the previous cache.";
                _lastIslandRumourRefreshMs = now;
                _islandRumourForceRefresh = false;
                return;
            }

            EnsureIslandRumourAtlasLookupReady();
            _islandRumourSeenButtonKeys.Clear();
            _islandRumourLastError = string.Empty;
            _islandRumourCacheScanButtons.Clear();
            _islandRumourCacheScanButtons.AddRange(buttons);
            _islandRumourCacheScanIndex = 0;
            _islandRumourCacheScanButtonCount = 0;
            _islandRumourCacheScanFirstSource = string.Empty;
            _islandRumourCacheScanInProgress = true;
            _islandRumourForceRefresh = false;
        }

        private void CancelIslandRumourCacheRefresh()
        {
            _islandRumourCacheScanButtons.Clear();
            _islandRumourCacheScanIndex = 0;
            _islandRumourCacheScanButtonCount = 0;
            _islandRumourCacheScanFirstSource = string.Empty;
            _islandRumourCacheScanInProgress = false;
        }

        private void RefreshIslandRumourCacheSlice(long now, bool cameraMoving)
        {
            var buttons = _islandRumourCacheScanButtons;
            if (buttons.Count == 0)
            {
                CancelIslandRumourCacheRefresh();
                return;
            }

            int maximumButtons = cameraMoving
                ? IslandRumourCacheMaxButtonsPerMovingTick
                : IslandRumourCacheMaxButtonsPerTick;
            long sliceStart = Stopwatch.GetTimestamp();
            int processed = 0;

            while (_islandRumourCacheScanIndex < buttons.Count && processed < maximumButtons)
            {
                AtlasButtonNode? button;
                try
                {
                    button = buttons[_islandRumourCacheScanIndex++];
                }
                catch
                {
                    // A single retired wrapper is skipped; the next scheduled pass receives a
                    // fresh AtlasPanel.Buttons snapshot.
                    processed++;
                    continue;
                }

                processed++;
                if (button != null)
                    RefreshIslandRumourCacheButton(button, now);

                if (processed > 0 &&
                    ElapsedIslandRumourMilliseconds(sliceStart) >= IslandRumourCacheTimeBudgetMs)
                {
                    break;
                }
            }

            if (_islandRumourCacheScanIndex >= buttons.Count)
                CompleteIslandRumourCacheRefresh(now);
        }

        private void RefreshIslandRumourCacheButton(AtlasButtonNode button, long now)
        {
            _islandRumourCacheScanButtonCount++;

            try
            {
                long buttonKey = GetIslandRumourButtonKey(button);
                _islandRumourSeenButtonKeys.Add(buttonKey);

                _islandRumourSnapshotsByKey.TryGetValue(buttonKey, out var snapshot);
                bool allowCompatibilityFallback =
                    Settings.IslandRumourLiveTooltipScanEnabled.Value &&
                    !_islandRumourCompatibilityProbedKeys.Contains(buttonKey);
                ReadActiveIslandRumours(
                    button,
                    _islandRumourReadScratch,
                    allowCompatibilityFallback,
                    out var source);
                if (allowCompatibilityFallback)
                    _islandRumourCompatibilityProbedKeys.Add(buttonKey);

                if (_islandRumourReadScratch.Count == 0)
                {
                    // Rumors can be briefly empty while ExileCore rebuilds the atlas UI. Keep an
                    // already known snapshot attached to the live button instead of flickering it.
                    if (snapshot != null)
                        AttachIslandRumourButton(snapshot, button, isTooltipAuthoritative: false);
                    return;
                }

                UpsertIslandRumourSnapshot(
                    button,
                    buttonKey,
                    _islandRumourReadScratch,
                    now,
                    isTooltipAuthoritative: false);

                if (_islandRumourCacheScanFirstSource.Length == 0)
                    _islandRumourCacheScanFirstSource = source;
            }
            catch (Exception ex)
            {
                if (_islandRumourLastError.Length == 0)
                {
                    _islandRumourLastError =
                        "Button refresh " + ex.GetType().Name + ": " + ex.Message;
                }
            }
        }

        private void CompleteIslandRumourCacheRefresh(long now)
        {
            // Remove only buttons that truly disappeared from AtlasPanel.Buttons. An empty Rumors
            // dictionary is treated as a transient UI/cache state and never deletes a valid card.
            for (int i = _islandRumourSnapshots.Count - 1; i >= 0; i--)
            {
                var snapshot = _islandRumourSnapshots[i];
                if (_islandRumourSeenButtonKeys.Contains(snapshot.Key))
                    continue;

                _islandRumourSnapshots.RemoveAt(i);
                _islandRumourSnapshotsByKey.Remove(snapshot.Key);
            }

            int rumourCount = 0;
            for (int i = 0; i < _islandRumourSnapshots.Count; i++)
                rumourCount += _islandRumourSnapshots[i].Rumours.Length;

            _islandRumourLastButtonCount = _islandRumourCacheScanButtonCount;
            _islandRumourLastNodeCount = _islandRumourSnapshots.Count;
            _islandRumourLastRumourCount = rumourCount;
            _islandRumourLastSource = _islandRumourCacheScanFirstSource.Length == 0
                ? "AtlasButtonNode.Rumors"
                : _islandRumourCacheScanFirstSource;
            _lastIslandRumourRefreshMs = now;
            CancelIslandRumourCacheRefresh();
        }

        private static double ElapsedIslandRumourMilliseconds(long startTimestamp)
        {
            return (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;
        }

        private void RefreshIslandRumourLiveTooltipCache(long now)
        {
            long liveWorkStart = Stopwatch.GetTimestamp();
            bool cameraMoving = IsIslandRumourCameraMoving(now);
            int pollInterval = cameraMoving
                ? IslandRumourLiveTooltipRefreshWhileMovingMs
                : IslandRumourLiveTooltipRefreshMs;
            int discoveryInterval = cameraMoving
                ? IslandRumourTooltipDiscoveryWhileMovingMs
                : IslandRumourTooltipDiscoveryMs;

            bool pollDue =
                now - _lastIslandRumourLiveTooltipRefreshMs >= pollInterval;
            bool discoveryDue =
                now - _lastIslandRumourTooltipDiscoveryMs >= discoveryInterval;
            bool polledThisCall = false;
            long pollStart = 0;

            if (pollDue)
            {
                pollStart = Stopwatch.GetTimestamp();
                polledThisCall = true;
                _lastIslandRumourLiveTooltipRefreshMs = now;
                _islandRumourLiveBestScoreByKey.Clear();
                _islandRumourPollBestByKey.Clear();

                // Poll only atlas buttons that the game currently marks as visible. Hidden
                // AtlasButtonNode wrappers can retain large stale Tooltip/Children trees; walking
                // those wrappers was the source of the recurring 50-115 ms live-poll spikes.
                for (int i = 0; i < _islandRumourLiveTooltipCandidates.Count;)
                {
                    var button = _islandRumourLiveTooltipCandidates[i];
                    try
                    {
                        if (!IsIslandRumourButtonEligibleForLiveTooltipScan(button) ||
                            !TryReadIslandRumoursFromTooltip(
                                button,
                                _islandRumourReadScratch,
                                out int visibilityScore) ||
                            visibilityScore < IslandRumourVisibleTooltipScore)
                        {
                            _islandRumourLiveTooltipCandidates.RemoveAt(i);
                            continue;
                        }

                        StageIslandRumourTooltipCandidate(
                            _islandRumourPollBestByKey,
                            button,
                            _islandRumourReadScratch,
                            visibilityScore);
                        if (_islandRumourTooltipDiscoveryInProgress)
                        {
                            StageIslandRumourTooltipCandidate(
                                _islandRumourTooltipDiscoveryBestByKey,
                                button,
                                _islandRumourReadScratch,
                                visibilityScore);
                        }

                        ApplyBestLiveIslandRumourCandidate(
                            button,
                            _islandRumourReadScratch,
                            visibilityScore,
                            now);
                        i++;
                    }
                    catch
                    {
                        // ExileCore rebuilt or released this remote UI object. Drop it and let
                        // the throttled discovery pass find the replacement.
                        _islandRumourLiveTooltipCandidates.RemoveAt(i);
                    }
                }

                ReportProfileElapsedTicks(
                    "Island Rumours tooltip poll",
                    Stopwatch.GetTimestamp() - pollStart);
            }

            long publishStart = Stopwatch.GetTimestamp();
            ProcessIslandRumourTooltipPublishSlice(now);
            ReportProfileElapsedTicks(
                "Island Rumours tooltip publish",
                Stopwatch.GetTimestamp() - publishStart);

            if (cameraMoving &&
                ElapsedIslandRumourMilliseconds(liveWorkStart) >= IslandRumourMovingTotalWorkBudgetMs)
            {
                return;
            }

            if (!_islandRumourTooltipDiscoveryInProgress && discoveryDue)
            {
                var buttons = _atlasPanel?.Buttons;
                if (buttons != null && buttons.Count != 0)
                    BeginIslandRumourTooltipDiscovery(buttons, polledThisCall);
            }

            if (_islandRumourTooltipDiscoveryInProgress)
            {
                long discoveryStart = Stopwatch.GetTimestamp();
                ProcessIslandRumourTooltipDiscoverySlice(now, cameraMoving);
                ReportProfileElapsedTicks(
                    "Island Rumours tooltip discovery",
                    Stopwatch.GetTimestamp() - discoveryStart);
            }
        }

        private void BeginIslandRumourTooltipDiscovery(
            IReadOnlyList<AtlasButtonNode> buttons,
            bool seedFromCurrentPoll)
        {
            _islandRumourTooltipDiscoveryButtons.Clear();
            for (int i = 0; i < buttons.Count; i++)
                _islandRumourTooltipDiscoveryButtons.Add(buttons[i]);
            _islandRumourTooltipDiscoveryIndex = 0;
            _islandRumourTooltipDiscoveryInProgress = true;
            _islandRumourTooltipDiscoveryVisibleCandidates.Clear();
            _islandRumourTooltipDiscoveryBestByKey.Clear();
            if (seedFromCurrentPoll)
            {
                foreach (var pair in _islandRumourPollBestByKey)
                    _islandRumourTooltipDiscoveryBestByKey[pair.Key] = pair.Value;
            }
        }

        private void ProcessIslandRumourTooltipDiscoverySlice(long now, bool cameraMoving)
        {
            var buttons = _islandRumourTooltipDiscoveryButtons;
            if (buttons.Count == 0)
            {
                CancelIslandRumourTooltipDiscovery();
                return;
            }

            int maximumButtons = cameraMoving
                ? IslandRumourTooltipDiscoveryMaxButtonsPerMovingTick
                : IslandRumourTooltipDiscoveryMaxButtonsPerTick;
            long sliceStart = Stopwatch.GetTimestamp();
            int processed = 0;

            while (_islandRumourTooltipDiscoveryIndex < buttons.Count && processed < maximumButtons)
            {
                AtlasButtonNode? button;
                try
                {
                    button = buttons[_islandRumourTooltipDiscoveryIndex++];
                }
                catch
                {
                    processed++;
                    continue;
                }

                processed++;
                if (button != null)
                    DiscoverIslandRumourTooltipCandidate(button);

                if (processed > 0 &&
                    ElapsedIslandRumourMilliseconds(sliceStart) >= IslandRumourTooltipDiscoveryTimeBudgetMs)
                {
                    break;
                }
            }

            if (_islandRumourTooltipDiscoveryIndex >= buttons.Count)
                CompleteIslandRumourTooltipDiscovery(now);
        }

        private void DiscoverIslandRumourTooltipCandidate(AtlasButtonNode button)
        {
            try
            {
                // The live child parser is intentionally screen-local. The direct Rumors cache
                // still covers every atlas button; Tooltip/Children is visited only for buttons
                // whose AtlasButtonNode.IsVisible flag is currently true.
                if (!IsIslandRumourButtonEligibleForLiveTooltipScan(button) ||
                    !TryReadIslandRumoursFromTooltip(
                        button,
                        _islandRumourReadScratch,
                        out int visibilityScore))
                {
                    return;
                }

                if (visibilityScore >= IslandRumourVisibleTooltipScore)
                    _islandRumourTooltipDiscoveryVisibleCandidates.Add(button);

                StageIslandRumourTooltipCandidate(
                    _islandRumourTooltipDiscoveryBestByKey,
                    button,
                    _islandRumourReadScratch,
                    visibilityScore);
            }
            catch
            {
                // A single invalid remote element must not abort discovery for all buttons.
            }
        }

        private static void StageIslandRumourTooltipCandidate(
            Dictionary<long, IslandRumourTooltipDiscoveryCandidate> destination,
            AtlasButtonNode button,
            IReadOnlyList<string> rumours,
            int visibilityScore)
        {
            long buttonKey = GetIslandRumourButtonKey(button);
            if (destination.TryGetValue(buttonKey, out var current) &&
                visibilityScore < current.VisibilityScore)
            {
                return;
            }

            // Equal scores deliberately keep the later candidate, exactly like the original
            // all-buttons loop. Only scheduling changed; the winner rules and parser are untouched.
            destination[buttonKey] = new IslandRumourTooltipDiscoveryCandidate(
                button,
                rumours,
                visibilityScore);
        }

        private void CompleteIslandRumourTooltipDiscovery(long now)
        {
            _islandRumourLiveTooltipCandidates.Clear();
            _islandRumourLiveTooltipCandidates.AddRange(
                _islandRumourTooltipDiscoveryVisibleCandidates);

            // Preserve the exact candidate selection produced by discovery, but never apply the
            // whole atlas in a single Tick. A later pass replaces an older pending candidate for
            // the same logical button, so delayed publication cannot regress to stale text.
            foreach (var pair in _islandRumourTooltipDiscoveryBestByKey)
            {
                var candidate = pair.Value;
                if (_islandRumourSnapshotsByKey.TryGetValue(pair.Key, out var snapshot) &&
                    candidate.Signature.Equals(snapshot.Signature, StringComparison.Ordinal))
                {
                    // Reapplying an unchanged hidden tooltip only refreshed bookkeeping and
                    // repeated remote coordinate reads. The live visible path still refreshes
                    // authority immediately; unchanged global candidates require no publication.
                    // Also discard an older queued value that a newer live poll already superseded.
                    _islandRumourTooltipPublishByKey.Remove(pair.Key);
                    continue;
                }

                if (!_islandRumourTooltipPublishByKey.ContainsKey(pair.Key))
                    _islandRumourTooltipPublishKeys.Add(pair.Key);

                _islandRumourTooltipPublishByKey[pair.Key] = candidate;
            }

            _lastIslandRumourTooltipDiscoveryMs = now;
            CancelIslandRumourTooltipDiscovery();
        }

        private void ProcessIslandRumourTooltipPublishSlice(long now)
        {
            if (_islandRumourTooltipPublishByKey.Count == 0)
            {
                if (_islandRumourTooltipPublishIndex != 0 ||
                    _islandRumourTooltipPublishKeys.Count != 0)
                {
                    _islandRumourTooltipPublishKeys.Clear();
                    _islandRumourTooltipPublishIndex = 0;
                }

                return;
            }

            long sliceStart = Stopwatch.GetTimestamp();
            int published = 0;

            while (_islandRumourTooltipPublishIndex < _islandRumourTooltipPublishKeys.Count &&
                   published < IslandRumourTooltipPublishMaxItemsPerTick)
            {
                long key = _islandRumourTooltipPublishKeys[_islandRumourTooltipPublishIndex++];
                if (!_islandRumourTooltipPublishByKey.Remove(key, out var candidate))
                    continue;

                // Visibility can change after discovery but before the queued result is applied.
                // Never publish a child-scan result from a button that has already left the screen.
                if (!IsIslandRumourButtonEligibleForLiveTooltipScan(candidate.Button))
                    continue;

                candidate.CopyRumoursTo(_islandRumourReadScratch);
                ApplyLiveIslandRumours(
                    candidate.Button,
                    _islandRumourReadScratch,
                    now,
                    candidate.VisibilityScore >= IslandRumourVisibleTooltipScore);
                published++;

                if (ElapsedIslandRumourMilliseconds(sliceStart) >=
                    IslandRumourTooltipPublishTimeBudgetMs)
                {
                    break;
                }
            }

            if (_islandRumourTooltipPublishIndex < _islandRumourTooltipPublishKeys.Count)
                return;

            _islandRumourTooltipPublishKeys.Clear();
            _islandRumourTooltipPublishIndex = 0;
        }

        private void CancelIslandRumourTooltipDiscovery()
        {
            _islandRumourTooltipDiscoveryButtons.Clear();
            _islandRumourTooltipDiscoveryIndex = 0;
            _islandRumourTooltipDiscoveryInProgress = false;
            _islandRumourTooltipDiscoveryVisibleCandidates.Clear();
            _islandRumourTooltipDiscoveryBestByKey.Clear();
        }

        private void ApplyBestLiveIslandRumourCandidate(
            AtlasButtonNode button,
            IReadOnlyList<string> rumours,
            int visibilityScore,
            long now)
        {
            long buttonKey = GetIslandRumourButtonKey(button);
            if (_islandRumourLiveBestScoreByKey.TryGetValue(buttonKey, out int bestScore) &&
                visibilityScore < bestScore)
            {
                return;
            }

            // Equal scores deliberately use the later candidate. This matches v2 and handles
            // a rebuilt live tooltip being appended after its stale predecessor.
            _islandRumourLiveBestScoreByKey[buttonKey] = visibilityScore;
            ApplyLiveIslandRumours(
                button,
                rumours,
                now,
                visibilityScore >= IslandRumourVisibleTooltipScore);
        }

        private void ApplyLiveIslandRumours(
            AtlasButtonNode button,
            IReadOnlyList<string> rumours,
            long now,
            bool isVisiblyAuthoritative)
        {
            long buttonKey = GetIslandRumourButtonKey(button);
            var snapshot = UpsertIslandRumourSnapshot(
                button,
                buttonKey,
                rumours,
                now,
                isTooltipAuthoritative: isVisiblyAuthoritative);

            // Maps / GE are region constants. Apply an already cached region result immediately;
            // uncached regions are resolved by the one-item-per-tick scheduler.
            TryApplyCachedIslandRumourRegionStats(snapshot);

            _islandRumourLastSource = isVisiblyAuthoritative
                ? "Tooltip active text (live)"
                : "Tooltip active text (compatibility)";
        }

        private bool TryReadIslandRumoursFromTooltip(
            AtlasButtonNode button,
            List<string> destination,
            out int bestVisibilityScore)
        {
            destination.Clear();
            bestVisibilityScore = int.MinValue;

            // Final defensive gate shared by poll, discovery, and compatibility callers. Reading
            // IsVisible is cheap; traversing a hidden remote Tooltip/Children tree is not.
            if (!IsIslandRumourButtonEligibleForLiveTooltipScan(button))
                return false;

            var buttonChildren = button.Children;
            if (buttonChildren == null || buttonChildren.Count == 0)
                return false;

            bool found = false;
            for (int i = 0; i < buttonChildren.Count; i++)
            {
                var buttonChild = buttonChildren[i];
                var tooltip = buttonChild?.Tooltip;
                if (tooltip == null)
                    continue;

                var tooltipChildren = tooltip.Children;
                if (tooltipChildren == null || tooltipChildren.Count == 0)
                    continue;

                var tooltipRoot = tooltipChildren[0];
                var tooltipRootChildren = tooltipRoot?.Children;
                if (tooltipRootChildren == null || tooltipRootChildren.Count == 0)
                    continue;

                int branchVisibilityScore = GetIslandRumourTooltipVisibilityScore(
                    buttonChild,
                    tooltip,
                    tooltipRoot);

                // Current UI layout: the rumour lines are normally in child index 3. Do not
                // stop after this container, because a rebuilt board can leave index 3 stale
                // while the label-following container points at the new live set.
                if (tooltipRootChildren.Count > 3)
                {
                    ConsiderIslandRumourTooltipContainer(
                        tooltipRootChildren[3],
                        branchVisibilityScore,
                        destination,
                        ref bestVisibilityScore,
                        ref found);
                }

                // Layout-safe path: find every "Island Rumours" label and consider its next
                // sibling. Equal scores use the later candidate, matching the append behaviour
                // observed in the supplied recording.
                for (int siblingIndex = 0;
                     siblingIndex + 1 < tooltipRootChildren.Count;
                     siblingIndex++)
                {
                    string token = Utility.NormalizeToken(
                        ExtractIslandRumourElementText(tooltipRootChildren[siblingIndex]));
                    if (!token.Contains("islandrumours", StringComparison.Ordinal) &&
                        !token.Contains("islandrumors", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    ConsiderIslandRumourTooltipContainer(
                        tooltipRootChildren[siblingIndex + 1],
                        branchVisibilityScore,
                        destination,
                        ref bestVisibilityScore,
                        ref found);
                }
            }

            if (!found)
            {
                destination.Clear();
                bestVisibilityScore = int.MinValue;
            }

            return found;
        }

        private void ConsiderIslandRumourTooltipContainer(
            Element? container,
            int branchVisibilityScore,
            List<string> destination,
            ref int bestVisibilityScore,
            ref bool found)
        {
            if (container == null ||
                !TryReadIslandRumoursFromTextChildren(
                    container.Children,
                    _islandRumourTooltipCandidateScratch))
            {
                return;
            }

            int visibilityScore =
                branchVisibilityScore + GetIslandRumourElementVisibilityScore(container);
            if (found && visibilityScore < bestVisibilityScore)
                return;

            destination.Clear();
            destination.AddRange(_islandRumourTooltipCandidateScratch);
            bestVisibilityScore = visibilityScore;
            found = true;
        }

        private static int GetIslandRumourTooltipVisibilityScore(
            Element? buttonChild,
            Element tooltip,
            Element? tooltipRoot)
        {
            int score = GetIslandRumourElementVisibilityScore(tooltip);
            score += GetIslandRumourElementVisibilityScore(tooltipRoot);

            // The atlas button itself can stay visible while its tooltip branch is stale. Use
            // the child only as a tiny tie-breaker; it must not make a hidden tooltip authoritative.
            if (buttonChild?.IsVisible == true)
                score += 1;
            if (buttonChild?.IsVisibleLocal == true)
                score += 1;
            if (buttonChild?.IsActive == true)
                score += 1;

            return score;
        }

        private static int GetIslandRumourElementVisibilityScore(Element? element)
        {
            if (element == null)
                return 0;

            int score = 0;
            if (element.IsVisible)
                score += 8;
            if (element.IsVisibleLocal)
                score += 4;
            if (element.IsActive)
                score += 2;
            return score;
        }

        private bool TryReadIslandRumoursFromTextChildren(
            IEnumerable<Element>? children,
            List<string> destination)
        {
            destination.Clear();
            _islandRumourReadSeenScratch.Clear();

            if (children == null)
                return false;

            // ExileCore sometimes leaves the previous three text elements in the container and
            // appends a rebuilt set after the player changes a Rumour. Prefer lines that are
            // actually visible; this is the case that caused "All that glitters..." to be missed.
            foreach (var child in children)
            {
                if (child == null || GetIslandRumourElementVisibilityScore(child) == 0)
                    continue;

                AddLatestIslandRumour(
                    destination,
                    _islandRumourReadSeenScratch,
                    ExtractIslandRumourElementText(child),
                    3);
            }

            if (destination.Count >= 3)
                return true;

            // Visibility flags can lag behind the rendered UI for one frame. In that case read
            // all valid text nodes, but retain the last three rather than the first three. A
            // rebuilt live set is appended after the stale set in the current ExileCore2 UI.
            destination.Clear();
            _islandRumourReadSeenScratch.Clear();

            foreach (var child in children)
            {
                if (child == null)
                    continue;

                AddLatestIslandRumour(
                    destination,
                    _islandRumourReadSeenScratch,
                    ExtractIslandRumourElementText(child),
                    3);
            }

            return destination.Count != 0;
        }

        private static void AddLatestIslandRumour(
            List<string> destination,
            HashSet<string> seen,
            string? raw,
            int maximumCount)
        {
            string name = CleanIslandRumourName(raw);
            if (name.Length == 0)
                return;

            string token = Utility.NormalizeToken(name);
            if (token.Length == 0 || IsRejectedIslandRumourText(token))
                return;

            // When an updated tooltip set is appended, repeated entries from the new set must
            // replace their older copy rather than being rejected by a scan-wide HashSet.
            if (seen.Contains(token))
            {
                for (int i = destination.Count - 1; i >= 0; i--)
                {
                    if (!Utility.NormalizeToken(destination[i]).Equals(token, StringComparison.Ordinal))
                        continue;

                    destination.RemoveAt(i);
                    break;
                }

                seen.Remove(token);
            }

            destination.Add(name);
            seen.Add(token);

            while (destination.Count > maximumCount)
            {
                string removedToken = Utility.NormalizeToken(destination[0]);
                destination.RemoveAt(0);
                if (removedToken.Length != 0)
                    seen.Remove(removedToken);
            }
        }

        private static string ExtractIslandRumourElementText(Element? element)
        {
            if (element == null)
                return string.Empty;

            return element.TextNoTags ?? element.Text ?? string.Empty;
        }

        private void UpdateIslandRumourCameraMotion(long now)
        {
            if (!TryGetIslandRumourCameraAnchorCenter(out var center))
            {
                _islandRumourHasCameraAnchorCenter = false;
                return;
            }

            if (_islandRumourHasCameraAnchorCenter &&
                Vector2.DistanceSquared(center, _islandRumourLastCameraAnchorCenter) >=
                IslandRumourCameraMotionThresholdSquared)
            {
                _islandRumourLastCameraMovementMs = now;
            }

            _islandRumourLastCameraAnchorCenter = center;
            _islandRumourHasCameraAnchorCenter = true;
        }

        private bool TryGetIslandRumourCameraAnchorCenter(out Vector2 center)
        {
            center = default;

            if (_islandRumourCameraAnchorNode?.Element == null)
            {
                _islandRumourCameraAnchorNode = null;
                int searchCount = Math.Min(_atlasNodes.Length, 64);
                for (int i = 0; i < searchCount; i++)
                {
                    var candidate = _atlasNodes[i];
                    if (candidate?.Element == null)
                        continue;

                    var candidateCenter = candidate.Element.Center;
                    if (!float.IsFinite(candidateCenter.X) || !float.IsFinite(candidateCenter.Y))
                        continue;

                    _islandRumourCameraAnchorNode = candidate;
                    break;
                }
            }

            var element = _islandRumourCameraAnchorNode?.Element;
            if (element == null)
                return false;

            var liveCenter = element.Center;
            center = new Vector2(liveCenter.X, liveCenter.Y);
            return float.IsFinite(center.X) && float.IsFinite(center.Y);
        }

        private IslandRumourSnapshot UpsertIslandRumourSnapshot(
            AtlasButtonNode button,
            long key,
            IReadOnlyList<string> rumours,
            long now,
            bool isTooltipAuthoritative)
        {
            bool isNewSnapshot = !_islandRumourSnapshotsByKey.TryGetValue(key, out var snapshot);
            if (isNewSnapshot)
            {
                snapshot = new IslandRumourSnapshot { Key = key };
                _islandRumourSnapshotsByKey[key] = snapshot;
                _islandRumourSnapshots.Add(snapshot);
            }

            string signature = AreIslandRumourSequencesExactlyEqual(snapshot.Rumours, rumours)
                ? snapshot.Signature
                : BuildIslandRumourSignature(rumours);
            AttachIslandRumourButton(snapshot, button, isTooltipAuthoritative);

            // Tooltip text is the live source of truth while the board is visible. The short
            // grace period prevents the slower AtlasButtonNode.Rumors refresh from restoring an
            // older value, without permanently locking a snapshot to a tooltip object that the
            // game may later recycle.
            bool hasRecentVisibleTooltip =
                snapshot.LastVisibleTooltipReadMs != 0 &&
                now - snapshot.LastVisibleTooltipReadMs <= IslandRumourTooltipAuthorityGraceMs;
            bool canReplaceRumours =
                isTooltipAuthoritative || !hasRecentVisibleTooltip;
            bool rumoursChanged =
                canReplaceRumours &&
                !signature.Equals(snapshot.Signature, StringComparison.Ordinal);

            if (rumoursChanged)
            {
                var rumourArray = new string[rumours.Count];
                var tokenList = new List<string>(rumours.Count);
                var tokenSet = new HashSet<string>(StringComparer.Ordinal);

                for (int i = 0; i < rumours.Count; i++)
                {
                    string rumour = rumours[i];
                    rumourArray[i] = rumour;

                    string canonical = GetIslandRumourCanonicalName(rumour);
                    string token = Utility.NormalizeToken(canonical);
                    if (token.Length != 0 && tokenSet.Add(token))
                        tokenList.Add(token);

                    if (!string.IsNullOrWhiteSpace(canonical))
                        _observedIslandRumours.Add(canonical);
                }

                snapshot.Rumours = rumourArray;
                snapshot.Tokens = tokenList.Count == 0 ? Array.Empty<string>() : tokenList.ToArray();
                snapshot.TableRows = BuildIslandRumourTableRows(rumourArray);
                snapshot.Signature = signature;
                snapshot.TableLayout = null;
            }

            if (isTooltipAuthoritative)
                snapshot.LastVisibleTooltipReadMs = now;

            // Atlas coordinates are constants for a logical island. Reading these remote members
            // on every unchanged tooltip publication was expensive and added no new information.
            if (!snapshot.Coordinate.HasValue &&
                TryReadIntPair(button.Coordinate, out var coordinate))
            {
                snapshot.Coordinate = coordinate;
            }

            if (!snapshot.RegionCoordinate.HasValue &&
                TryReadIntPair(button.RegionCoordinate, out var regionCoordinate))
            {
                snapshot.RegionCoordinate = regionCoordinate;
            }

            // Maps / GE are immutable for a region. Reuse an existing region result immediately,
            // then resolve the small strongly typed RegionNodes list for a visible card without
            // waiting for the round-robin compatibility worker. This keeps the footer effectively
            // instant while preserving the slower RegionNodeElements fallback for edge cases.
            if (!TryApplyCachedIslandRumourRegionStats(snapshot) &&
                IsIslandRumourButtonVisible(button))
            {
                TryResolveIslandRumourRegionStatsFast(snapshot, button, now);
            }

            snapshot.LastSeenMs = now;
            return snapshot;
        }

        private static void AttachIslandRumourButton(
            IslandRumourSnapshot snapshot,
            AtlasButtonNode button,
            bool isTooltipAuthoritative)
        {
            if (ReferenceEquals(snapshot.Button, button))
                return;

            if (snapshot.Button == null || isTooltipAuthoritative)
            {
                snapshot.Button = button;
                return;
            }

            // ExileCore may temporarily expose both the retiring and rebuilt AtlasButtonNode for
            // the same island. Never replace a currently visible owner with an invisible stale
            // wrapper; doing so used to make the card disappear or get rendered twice.
            bool currentVisible = IsIslandRumourButtonVisible(snapshot.Button);
            if (!currentVisible)
                snapshot.Button = button;
        }

        private static bool IsIslandRumourButtonVisible(AtlasButtonNode? button)
        {
            if (button == null)
                return false;

            try
            {
                return button.IsVisible;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsIslandRumourButtonEligibleForLiveTooltipScan(AtlasButtonNode? button)
        {
            // Deliberately use the AtlasButtonNode-level IsVisible flag identified in the
            // ExileCore2 inspector. IsVisibleLocal and visibility flags inside stale tooltip
            // branches are not sufficient to qualify a button for the expensive live scan.
            return IsIslandRumourButtonVisible(button);
        }

        private static bool AreIslandRumourSequencesExactlyEqual(
            IReadOnlyList<string> current,
            IReadOnlyList<string> incoming)
        {
            if (current.Count != incoming.Count)
                return false;

            for (int i = 0; i < current.Count; i++)
            {
                if (!string.Equals(current[i], incoming[i], StringComparison.Ordinal))
                    return false;
            }

            return true;
        }

        private static string BuildIslandRumourSignature(IReadOnlyList<string> rumours)
        {
            if (rumours.Count == 0)
                return string.Empty;

            string first = Utility.NormalizeToken(rumours[0]);
            if (rumours.Count == 1)
                return first;

            string second = Utility.NormalizeToken(rumours[1]);
            if (rumours.Count == 2)
                return string.Concat(first, "\u001f", second);

            string third = Utility.NormalizeToken(rumours[2]);
            if (rumours.Count == 3)
                return string.Concat(first, "\u001f", second, "\u001f", third);

            var normalized = new string[rumours.Count];
            normalized[0] = first;
            normalized[1] = second;
            normalized[2] = third;
            for (int i = 3; i < rumours.Count; i++)
                normalized[i] = Utility.NormalizeToken(rumours[i]);
            return string.Join("\u001f", normalized);
        }

        private void ReadActiveIslandRumours(
            AtlasButtonNode button,
            List<string> destination,
            bool allowCompatibilityFallback,
            out string source)
        {
            destination.Clear();
            _islandRumourReadSeenScratch.Clear();

            // Exact fast path confirmed from the supplied ExileCore2.dll:
            // AtlasButtonNode.Rumors -> Dictionary<string, int>.
            var groupedRumours = button.Rumors;
            if (groupedRumours != null)
            {
                foreach (var entry in groupedRumours)
                    AddCleanIslandRumour(destination, _islandRumourReadSeenScratch, entry.Key);
            }

            if (destination.Count != 0)
            {
                source = "AtlasButtonNode.Rumors";
                return;
            }

            if (!allowCompatibilityFallback)
            {
                source = "AtlasButtonNode.Rumors (temporarily empty)";
                return;
            }

            // Compatibility fallback is used only for a button that has never produced a snapshot.
            // It is not repeated for already cached buttons, so it cannot create panning spikes.
            AddIslandRumoursFromRegionNodeSequence(button.RegionNodeElements, destination);
            if (destination.Count != 0)
            {
                source = "RegionNodeElements.Map.Rumor fallback";
                return;
            }

            AddIslandRumoursFromRegionNodeSequence(button.RegionNodes, destination);
            source = destination.Count == 0
                ? "AtlasButtonNode.Rumors (empty)"
                : "RegionNodes.Element.Map.Rumor fallback";
        }

        private void AddIslandRumoursFromRegionNodeSequence(
            IEnumerable? nodes,
            List<string> destination)
        {
            if (nodes == null)
                return;

            foreach (var node in nodes)
            {
                if (node == null)
                    continue;

                AddCleanIslandRumour(
                    destination,
                    _islandRumourReadSeenScratch,
                    ExtractRegionNodeRumour(node));
                if (destination.Count >= 3)
                    break;
            }
        }



        private void EnsureIslandRumourAtlasLookupReady()
        {
            if (_nodeByCoord.Count != 0 || _atlasPanel == null)
                return;

            if (_atlasNodes.Length == 0)
                _atlasNodes = _atlasPanel.Descriptions?.ToArray() ?? Array.Empty<AtlasNodeDescription>();

            if (_atlasNodes.Length != 0)
                RefreshGraphCaches();
        }

        private void ProcessIslandRumourRegionStats(long now, bool cameraMoving)
        {
            if (!AreIslandRumourRegionStatsRequired() || _islandRumourSnapshots.Count == 0)
                return;

            // A card that was rendered in the previous frame gets first priority. The list is
            // screen-local and normally contains only one to three entries, so this avoids a
            // visible footer waiting behind hundreds of off-screen Atlas buttons.
            for (int i = 0; i < _islandRumourRenderCandidates.Count; i++)
            {
                var visibleSnapshot = _islandRumourRenderCandidates[i].Snapshot;
                if (visibleSnapshot.RegionStatsResolved ||
                    now < visibleSnapshot.NextRegionStatsAttemptMs ||
                    visibleSnapshot.Button == null)
                {
                    continue;
                }

                if (TryResolveIslandRumourRegionStatsFast(
                        visibleSnapshot,
                        visibleSnapshot.Button,
                        now))
                {
                    return;
                }

                // If this ExileCore2 build has not populated RegionNodes yet, resolve the one
                // currently displayed card through the compatibility list as soon as panning
                // stops. This path is never run for every hidden button.
                if (!cameraMoving)
                {
                    EnsureIslandRumourAtlasLookupReady();
                    var visibleStats = ReadIslandRumourRegionStats(
                        visibleSnapshot.Button.RegionNodeElements,
                        "RegionNodeElements");
                    if (visibleStats.MapCount > 0)
                    {
                        CompleteIslandRumourRegionStats(visibleSnapshot, visibleStats);
                        return;
                    }

                    visibleSnapshot.NextRegionStatsAttemptMs =
                        now + IslandRumourRegionStatsRetryMs;
                    return;
                }
            }

            int snapshotCount = _islandRumourSnapshots.Count;
            if (_islandRumourRegionStatsCursor >= snapshotCount)
                _islandRumourRegionStatsCursor = 0;

            for (int examined = 0; examined < snapshotCount; examined++)
            {
                int index = _islandRumourRegionStatsCursor++;
                if (_islandRumourRegionStatsCursor >= snapshotCount)
                    _islandRumourRegionStatsCursor = 0;

                var snapshot = _islandRumourSnapshots[index];
                if (snapshot.RegionStatsResolved || now < snapshot.NextRegionStatsAttemptMs)
                    continue;

                if (TryApplyCachedIslandRumourRegionStats(snapshot))
                    return;

                var button = snapshot.Button;
                if (button == null)
                {
                    snapshot.NextRegionStatsAttemptMs = now + IslandRumourRegionStatsRetryMs;
                    return;
                }

                // RegionNodes is a small, strongly typed list and is safe to read while the Atlas
                // is moving. It is the normal path and avoids the old 40-50 second wait caused by
                // the camera gate plus round-robin retries.
                if (TryResolveIslandRumourRegionStatsFast(snapshot, button, now))
                    return;

                // RegionNodeElements is only a compatibility fallback. It can touch remote UI
                // wrappers and reflection, so keep that heavier path away from camera movement.
                if (cameraMoving)
                    return;

                EnsureIslandRumourAtlasLookupReady();
                var stats = ReadIslandRumourRegionStats(
                    button.RegionNodeElements,
                    "RegionNodeElements");
                if (stats.MapCount > 0)
                {
                    CompleteIslandRumourRegionStats(snapshot, stats);
                }
                else
                {
                    snapshot.NextRegionStatsAttemptMs = now + IslandRumourRegionStatsRetryMs;
                }

                return;
            }
        }

        private bool TryResolveIslandRumourRegionStatsFast(
            IslandRumourSnapshot snapshot,
            AtlasButtonNode button,
            long now)
        {
            if (!AreIslandRumourRegionStatsRequired() || snapshot.RegionStatsResolved)
                return snapshot.RegionStatsResolved;

            if (TryApplyCachedIslandRumourRegionStats(snapshot))
                return true;

            if (now < snapshot.NextRegionStatsAttemptMs)
                return false;

            var stats = ReadIslandRumourRegionNodeStats(button.RegionNodes, "RegionNodes");
            if (stats.MapCount <= 0)
            {
                snapshot.NextRegionStatsAttemptMs = now + IslandRumourRegionStatsFastRetryMs;
                return false;
            }

            CompleteIslandRumourRegionStats(snapshot, stats);
            return true;
        }

        private bool AreIslandRumourRegionStatsRequired()
        {
            return Settings.ShowIslandRumourRegionStats.Value ||
                   Settings.FilterIslandRumourTablesByGrandExpedition.Value;
        }

        private void CompleteIslandRumourRegionStats(
            IslandRumourSnapshot snapshot,
            IslandRumourRegionStats stats)
        {
            ApplyIslandRumourRegionStats(snapshot, stats);
            snapshot.RegionStatsResolved = true;
            snapshot.NextRegionStatsAttemptMs = long.MaxValue;

            if (snapshot.RegionCoordinate.HasValue)
                _islandRumourRegionStatsByCoordinate[snapshot.RegionCoordinate.Value] = stats;
        }

        private bool TryApplyCachedIslandRumourRegionStats(IslandRumourSnapshot snapshot)
        {
            if (snapshot.RegionStatsResolved || !snapshot.RegionCoordinate.HasValue)
                return snapshot.RegionStatsResolved;

            if (!_islandRumourRegionStatsByCoordinate.TryGetValue(
                    snapshot.RegionCoordinate.Value,
                    out var stats))
            {
                return false;
            }

            ApplyIslandRumourRegionStats(snapshot, stats);
            snapshot.RegionStatsResolved = true;
            snapshot.NextRegionStatsAttemptMs = long.MaxValue;
            return true;
        }

        private void ApplyIslandRumourRegionStats(IslandRumourSnapshot snapshot, IslandRumourRegionStats stats)
        {
            string statsText = stats.MapCount > 0
                ? $"Maps {stats.MapCount}  |  GE {stats.GrandExpeditionCount}"
                : string.Empty;
            if (!statsText.Equals(snapshot.RegionStatsText, StringComparison.Ordinal))
                snapshot.TableLayout = null;

            snapshot.RegionMapCount = stats.MapCount;
            snapshot.RegionGrandExpeditionCount = stats.GrandExpeditionCount;
            snapshot.RegionStatsSource = stats.Source;
            snapshot.RegionStatsText = statsText;
        }

        private IslandRumourRegionStats ReadIslandRumourRegionStats(AtlasButtonNode button)
        {
            // RegionNodes is already a strongly typed List<AtlasNodeDescription> in the supplied
            // ExileCore2 build. Prefer it over RegionNodeElements so the normal path performs no
            // reflection, coordinate probing, or element-reference search.
            var stats = ReadIslandRumourRegionNodeStats(button.RegionNodes, "RegionNodes");
            if (stats.MapCount > 0)
                return stats;

            return ReadIslandRumourRegionStats(
                button.RegionNodeElements,
                "RegionNodeElements");
        }

        private IslandRumourRegionStats ReadIslandRumourRegionNodeStats(
            IReadOnlyList<AtlasNodeDescription>? nodes,
            string source)
        {
            if (nodes == null || nodes.Count == 0)
                return new IslandRumourRegionStats(0, 0, string.Empty);

            int mapCount = 0;
            int grandExpeditionCount = 0;
            _islandRumourRegionSeenScratch.Clear();

            for (int i = 0; i < nodes.Count; i++)
            {
                var atlasNode = nodes[i];
                if (atlasNode?.Element == null)
                    continue;

                long key = GetAtlasNodeStableKey(atlasNode);
                if (!_islandRumourRegionSeenScratch.Add(key) || !IsCountableRegionMap(atlasNode))
                    continue;

                mapCount++;
                if (IsGrandExpeditionRegionMap(atlasNode))
                    grandExpeditionCount++;
            }

            return new IslandRumourRegionStats(
                mapCount,
                grandExpeditionCount,
                mapCount == 0 ? string.Empty : source);
        }

        private IslandRumourRegionStats ReadIslandRumourRegionStats(IEnumerable? nodes, string source)
        {
            if (nodes == null)
                return new IslandRumourRegionStats(0, 0, string.Empty);

            int mapCount = 0;
            int grandExpeditionCount = 0;
            _islandRumourRegionSeenScratch.Clear();

            foreach (var node in nodes)
            {
                if (node == null || !TryResolveRegionAtlasNode(node, out var atlasNode))
                    continue;

                var key = GetAtlasNodeStableKey(atlasNode);
                if (!_islandRumourRegionSeenScratch.Add(key))
                    continue;

                if (!IsCountableRegionMap(atlasNode))
                    continue;

                mapCount++;
                if (IsGrandExpeditionRegionMap(atlasNode))
                    grandExpeditionCount++;
            }

            return new IslandRumourRegionStats(mapCount, grandExpeditionCount, mapCount == 0 ? string.Empty : source);
        }

        private bool TryResolveRegionAtlasNode(object regionItem, out AtlasNodeDescription atlasNode)
        {
            if (regionItem is AtlasNodeDescription direct && direct.Element != null)
            {
                atlasNode = direct;
                return true;
            }

            var node = GetDebugMember(regionItem, "Node");
            if (node is AtlasNodeDescription nodeDescription && nodeDescription.Element != null)
            {
                atlasNode = nodeDescription;
                return true;
            }

            var description = GetDebugMember(regionItem, "Description") ?? GetDebugMember(regionItem, "AtlasNodeDescription");
            if (description is AtlasNodeDescription descriptionNode && descriptionNode.Element != null)
            {
                atlasNode = descriptionNode;
                return true;
            }

            if (TryFindAtlasNodeFromCoordinateMembers(regionItem, out atlasNode))
                return true;

            var element = GetDebugMember(regionItem, "Element") ?? regionItem;
            if (element is AtlasNodeDescription elementDescription && elementDescription.Element != null)
            {
                atlasNode = elementDescription;
                return true;
            }

            if (TryFindAtlasNodeFromCoordinateMembers(element, out atlasNode))
                return true;

            if (TryFindAtlasNodeByElementReference(element, out atlasNode))
                return true;

            atlasNode = null!;
            return false;
        }

        private bool TryFindAtlasNodeFromCoordinateMembers(object? value, out AtlasNodeDescription atlasNode)
        {
            atlasNode = null!;
            if (value == null)
                return false;

            if (TryReadIntPair(value, out var directCoordinate) &&
                TryFindAtlasNodeByCoord(directCoordinate.x, directCoordinate.y, out var directNode) &&
                directNode?.Element != null)
            {
                atlasNode = directNode;
                return true;
            }

            foreach (var memberName in RegionCoordinateMemberNames)
            {
                if (!TryReadIntPair(GetDebugMember(value, memberName), out var coordinate))
                    continue;

                if (TryFindAtlasNodeByCoord(coordinate.x, coordinate.y, out var node) && node?.Element != null)
                {
                    atlasNode = node;
                    return true;
                }
            }

            return false;
        }

        private bool TryFindAtlasNodeByElementReference(object? element, out AtlasNodeDescription atlasNode)
        {
            atlasNode = null!;
            if (element == null)
                return false;

            for (int i = 0; i < _atlasNodes.Length; i++)
            {
                var node = _atlasNodes[i];
                if (node?.Element == null)
                    continue;

                if (ReferenceEquals(node.Element, element))
                {
                    atlasNode = node;
                    return true;
                }
            }

            return false;
        }

        private static long GetAtlasNodeStableKey(AtlasNodeDescription node)
        {
            var coordinate = node.Coordinate;
            return ((long)coordinate.X << 32) ^ (uint)coordinate.Y;
        }

        private static bool IsCountableRegionMap(AtlasNodeDescription node)
        {
            if (node?.Element == null)
                return false;

            if (Utility.TryGetTowerName(node, out _))
                return false;

            return true;
        }

        private static bool IsGrandExpeditionRegionMap(AtlasNodeDescription node)
        {
            var mechanicNames = Utility.TryGetMechanicNames(node);
            for (int i = 0; i < mechanicNames.Count; i++)
            {
                if (mechanicNames[i].Equals("Grand Expedition", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return (Utility.TryGetSpecialFlags(node) & Utility.SpecialFlags.AreaContainsExpedition) != 0;
        }

        private static readonly string[] RegionCoordinateMemberNames =
        {
            "Coordinate",
            "RegionCoordinate",
            "Coords",
            "Coord"
        };

        private static string ExtractRegionNodeRumour(object node)
        {
            var element = GetDebugMember(node, "Element") ?? node;
            var map = GetDebugMember(element, "Map") ?? GetDebugMember(node, "Map");
            var rumor = GetDebugMember(map, "Rumor") ?? GetDebugMember(map, "Rumour");
            if (rumor is string rumorText && !string.IsNullOrWhiteSpace(rumorText))
                return rumorText;

            var direct = GetDebugMember(element, "Rumor") ?? GetDebugMember(element, "Rumour");
            return direct as string ?? string.Empty;
        }

        private static void AddCleanIslandRumour(List<string> result, HashSet<string> seen, string? raw)
        {
            var name = CleanIslandRumourName(raw);
            if (name.Length == 0)
                return;

            var token = Utility.NormalizeToken(name);
            if (token.Length == 0 || IsRejectedIslandRumourText(token) || !seen.Add(token))
                return;

            result.Add(name);
        }

        private static bool IsRejectedIslandRumourText(string token)
        {
            return token is "islandrumours" or
                            "islandrumors" or
                            "requires" or
                            "expeditionlogbook" or
                            "usealogbooktochartthearea" or
                            "unchartedwaters";
        }

        private static string CleanIslandRumourName(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            var text = raw.Trim();
            if (text.Length >= 2 && text[0] == '[')
            {
                int comma = text.IndexOf(',');
                if (comma > 1)
                    text = text.Substring(1, comma - 1).Trim();
            }

            text = text.Trim('[', ']', '"', '\'').Trim();
            while (text.EndsWith("...", StringComparison.Ordinal))
                text = text.Substring(0, text.Length - 3).TrimEnd();
            if (text.Length == 0)
                return string.Empty;

            if (text.Equals("null", StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            return text;
        }

        private static long GetIslandRumourButtonKey(AtlasButtonNode button)
        {
            if (button.Marker != 0)
                return button.Marker;

            if (button.Address != 0)
                return button.Address;

            if (TryReadIntPair(button.Coordinate, out var coordinate))
                return ((long)coordinate.x << 32) ^ (uint)coordinate.y;

            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(button);
        }

        private static bool TryGetIslandRumourButtonCenter(AtlasButtonNode? button, out Vector2 center)
        {
            center = default;
            if (button == null)
                return false;

            try
            {
                if (!button.IsVisible)
                    return false;

                var liveCenter = button.Center;
                center = new Vector2(liveCenter.X, liveCenter.Y);
                return float.IsFinite(center.X) &&
                       float.IsFinite(center.Y) &&
                       center.X > 0f &&
                       center.Y > 0f;
            }
            catch
            {
                center = default;
                return false;
            }
        }

private static bool TryReadIntPair(object? value, out (int x, int y) result)
        {
            result = default;
            if (value == null)
                return false;

            try
            {
                var x = GetDebugMember(value, "X");
                var y = GetDebugMember(value, "Y");
                if (x == null || y == null)
                    return false;

                result = (Convert.ToInt32(x), Convert.ToInt32(y));
                return true;
            }
            catch
            {
                result = default;
                return false;
            }
        }

        private void RenderIslandRumourLabels()
        {
            if (!Settings.IslandRumoursEnabled.Value ||
                !Settings.ShowIslandRumourLabels.Value ||
                _islandRumourSnapshots.Count == 0)
            {
                return;
            }

            var displaySize = _currentRenderDisplaySize;
            int maxLabels = Settings.IslandRumourLiveTooltipScanEnabled.Value
                ? 3
                : int.MaxValue;
            int offsetY = Settings.IslandRumourLabelOffsetY.Value;
            int radius = Settings.NodeRadius.Value;
            float visibleNodeMargin = Math.Max(24f, radius + 8f);

            // ExileCore can expose an old and a rebuilt AtlasButtonNode for the same island for a
            // few frames. Rendering both cards at the same position alpha-blends the panel twice,
            // which looks like a dark/light pulse. Reconcile candidates by logical anchor and by
            // near-identical screen position before issuing any draw calls.
            _islandRumourRenderCandidates.Clear();
            for (int i = 0; i < _islandRumourSnapshots.Count; i++)
            {
                var snapshot = _islandRumourSnapshots[i];
                if (snapshot.TableRows.Length == 0 ||
                    !PassesIslandRumourGrandExpeditionFilter(snapshot) ||
                    !TryGetIslandRumourButtonCenter(snapshot.Button, out var center))
                {
                    continue;
                }

                // Never pin labels from off-screen atlas buttons to a viewport corner.
                // A hidden/off-screen node is simply skipped until its real button is visible again.
                if (center.X < visibleNodeMargin || center.Y < visibleNodeMargin ||
                    center.X > displaySize.X - visibleNodeMargin ||
                    center.Y > displaySize.Y - visibleNodeMargin)
                {
                    continue;
                }

                int existingIndex = FindIslandRumourRenderCandidate(snapshot, center);
                if (existingIndex < 0)
                {
                    _islandRumourRenderCandidates.Add(
                        new IslandRumourRenderCandidate(snapshot, center));
                    continue;
                }

                var existing = _islandRumourRenderCandidates[existingIndex];
                if (IsBetterIslandRumourRenderSnapshot(snapshot, existing.Snapshot))
                {
                    _islandRumourRenderCandidates[existingIndex] =
                        new IslandRumourRenderCandidate(snapshot, center);
                }
            }

            if (_islandRumourRenderCandidates.Count == 0)
                return;

            var drawList = ImGui.GetBackgroundDrawList();
            for (int i = 0; i < _islandRumourRenderCandidates.Count; i++)
            {
                var candidate = _islandRumourRenderCandidates[i];
                RenderIslandRumourLabelTable(
                    drawList,
                    candidate.Snapshot,
                    candidate.Center,
                    radius,
                    offsetY,
                    maxLabels);
            }
        }

        private bool PassesIslandRumourGrandExpeditionFilter(IslandRumourSnapshot snapshot)
        {
            if (!Settings.FilterIslandRumourTablesByGrandExpedition.Value)
                return true;

            int minimum = Math.Clamp(
                Settings.IslandRumourMinimumGrandExpeditionCount.Value,
                Settings.IslandRumourMinimumGrandExpeditionCount.Min,
                Settings.IslandRumourMinimumGrandExpeditionCount.Max);
            return snapshot.RegionStatsResolved &&
                   snapshot.RegionGrandExpeditionCount >= minimum;
        }

        private int FindIslandRumourRenderCandidate(
            IslandRumourSnapshot snapshot,
            Vector2 center)
        {
            const float duplicateCenterDistanceSquared = 9f;

            for (int i = 0; i < _islandRumourRenderCandidates.Count; i++)
            {
                var existing = _islandRumourRenderCandidates[i];
                if (AreSameIslandRumourLogicalAnchor(snapshot, existing.Snapshot) ||
                    Vector2.DistanceSquared(center, existing.Center) <= duplicateCenterDistanceSquared)
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool AreSameIslandRumourLogicalAnchor(
            IslandRumourSnapshot first,
            IslandRumourSnapshot second)
        {
            return first.Coordinate.HasValue &&
                   second.Coordinate.HasValue &&
                   first.RegionCoordinate.HasValue &&
                   second.RegionCoordinate.HasValue &&
                   first.Coordinate.Value.Equals(second.Coordinate.Value) &&
                   first.RegionCoordinate.Value.Equals(second.RegionCoordinate.Value);
        }

        private static bool IsBetterIslandRumourRenderSnapshot(
            IslandRumourSnapshot candidate,
            IslandRumourSnapshot current)
        {
            if (candidate.LastVisibleTooltipReadMs != current.LastVisibleTooltipReadMs)
                return candidate.LastVisibleTooltipReadMs > current.LastVisibleTooltipReadMs;

            if (candidate.LastSeenMs != current.LastSeenMs)
                return candidate.LastSeenMs > current.LastSeenMs;

            if (candidate.RegionStatsResolved != current.RegionStatsResolved)
                return candidate.RegionStatsResolved;

            if (candidate.Rumours.Length != current.Rumours.Length)
                return candidate.Rumours.Length > current.Rumours.Length;

            // Keep the existing winner on a complete tie so the selected owner does not oscillate.
            return false;
        }

        private void RenderIslandRumourLabelTable(ImDrawListPtr drawList, IslandRumourSnapshot snapshot, Vector2 center, int radius, int offsetY, int maxLabels)
        {
            var font = ImGui.GetFont();
            var layout = GetOrBuildIslandRumourTableLayout(snapshot, maxLabels, font);
            if (layout.RowCount <= 0)
                return;

            float panelWidth = layout.PanelWidth;
            float panelHeight = layout.PanelHeight;
            float aboveTop = center.Y - radius - offsetY - panelHeight;
            float belowTop = center.Y + radius + Math.Max(10f, offsetY * 0.35f);
            bool placeAbove = aboveTop >= IslandRumourViewportMargin;
            float top;
            if (placeAbove)
            {
                top = aboveTop;
            }
            else if (belowTop + panelHeight <= _currentRenderDisplaySize.Y - IslandRumourViewportMargin)
            {
                top = belowTop;
            }
            else
            {
                return;
            }

            float desiredLeft = center.X - panelWidth / 2f;
            float left = Math.Clamp(
                desiredLeft,
                IslandRumourViewportMargin,
                Math.Max(IslandRumourViewportMargin, _currentRenderDisplaySize.X - panelWidth - IslandRumourViewportMargin));
            float maximumHorizontalShift = Math.Min(84f, panelWidth * 0.22f);
            if (Math.Abs(left - desiredLeft) > maximumHorizontalShift)
                return;

            // Keep geometry on the live sub-pixel button position. Text is still pixel-snapped
            // inside DrawIslandRumourTableText, so the table follows the Atlas smoothly without
            // making glyphs blurry or introducing interpolation lag.
            var panelMin = new Vector2(left, top);
            var panelMax = new Vector2(left + panelWidth, top + panelHeight);
            float globalOpacity = Math.Clamp(Settings.Opacity.Value, 0f, 1f);
            float backgroundOpacity = Math.Clamp(
                Settings.IslandRumourLabelBackgroundOpacity.Value,
                Settings.IslandRumourLabelBackgroundOpacity.Min,
                Settings.IslandRumourLabelBackgroundOpacity.Max);

            bool matchesGrandExpeditionFilter =
                Settings.FilterIslandRumourTablesByGrandExpedition.Value &&
                PassesIslandRumourGrandExpeditionFilter(snapshot);
            var baseAccentColor = matchesGrandExpeditionFilter
                ? Settings.IslandRumourGrandExpeditionFilterColor.Value
                : Color.FromArgb(218, 171, 66);
            var accentColor = WithIslandRumourAlpha(baseAccentColor, globalOpacity * 0.95f);
            var shadowColor = WithIslandRumourAlpha(Color.Black, globalOpacity * backgroundOpacity * 0.62f);
            var backgroundColor = WithIslandRumourAlpha(Color.FromArgb(15, 19, 25), globalOpacity * backgroundOpacity);
            var headerBackground = WithIslandRumourAlpha(Color.FromArgb(32, 39, 50), globalOpacity * Math.Min(1f, backgroundOpacity + 0.02f));
            var alternateRowBackground = WithIslandRumourAlpha(Color.FromArgb(255, 255, 255), globalOpacity * 0.035f);
            var borderColor = matchesGrandExpeditionFilter
                ? WithIslandRumourAlpha(baseAccentColor, globalOpacity * 0.76f)
                : WithIslandRumourAlpha(Color.FromArgb(164, 175, 194), globalOpacity * 0.66f);
            var dividerColor = WithIslandRumourAlpha(Color.FromArgb(180, 192, 210), globalOpacity * 0.24f);
            var headerTextColor = WithIslandRumourAlpha(Color.FromArgb(220, 226, 236), globalOpacity * 0.92f);
            var modsColor = WithIslandRumourAlpha(Color.FromArgb(242, 219, 154), globalOpacity * 0.96f);
            var mapTypeColor = WithIslandRumourAlpha(Color.FromArgb(159, 220, 239), globalOpacity * 0.96f);
            var statsColor = Utility.WithOpacity(
                matchesGrandExpeditionFilter
                    ? baseAccentColor
                    : Settings.IslandRumourRegionStatsColor.Value,
                globalOpacity);

            const float rounding = 6f;
            drawList.AddRectFilled(
                panelMin + new Vector2(3f, 3f),
                panelMax + new Vector2(3f, 3f),
                GetCachedImGuiColor(shadowColor),
                rounding);

            float connectorX = Math.Clamp(center.X, panelMin.X + 18f, panelMax.X - 18f);
            float connectorStartY = placeAbove ? panelMax.Y : panelMin.Y;
            float connectorEndY = placeAbove ? center.Y - radius : center.Y + radius;
            drawList.AddLine(
                new Vector2(connectorX, connectorStartY),
                new Vector2(center.X, connectorEndY),
                GetCachedImGuiColor(WithIslandRumourAlpha(baseAccentColor, globalOpacity * 0.62f)));

            drawList.AddRectFilled(panelMin, panelMax, GetCachedImGuiColor(backgroundColor), rounding);
            drawList.AddRect(panelMin, panelMax, GetCachedImGuiColor(borderColor), rounding);
            drawList.AddRectFilled(
                panelMin,
                new Vector2(panelMax.X, panelMin.Y + IslandRumourTopAccentHeight),
                GetCachedImGuiColor(accentColor),
                rounding);

            float contentLeft = panelMin.X + IslandRumourPanelPaddingX;
            float contentRight = panelMax.X - IslandRumourPanelPaddingX;
            float headerTop = panelMin.Y + IslandRumourTopAccentHeight + IslandRumourPanelPaddingY;
            float headerBottom = headerTop + layout.HeaderHeight;
            drawList.AddRectFilled(
                new Vector2(contentLeft - 3f, headerTop),
                new Vector2(contentRight + 3f, headerBottom),
                GetCachedImGuiColor(headerBackground),
                3f);

            var columnLefts = _islandRumourColumnLefts;
            var columnWidths = layout.ColumnWidths;
            columnLefts[0] = contentLeft;
            columnLefts[1] = contentLeft + columnWidths[0] + IslandRumourColumnGap;
            columnLefts[2] = columnLefts[1] + columnWidths[1] + IslandRumourColumnGap;
            columnLefts[3] = columnLefts[2] + columnWidths[2] + IslandRumourColumnGap;

            float headerTextY = headerTop + (layout.HeaderHeight - layout.HeaderLineHeight) / 2f;
            for (int i = 0; i < layout.Headers.Length; i++)
            {
                DrawIslandRumourTableText(
                    drawList,
                    font,
                    layout.HeaderFontSize,
                    new Vector2(columnLefts[i] + IslandRumourCellPaddingX, headerTextY),
                    headerTextColor,
                    layout.Headers[i],
                    globalOpacity,
                    strongShadow: false);
            }

            float rowsTop = headerBottom;
            for (int i = 0; i < layout.Rows.Length; i++)
            {
                var rowLayout = layout.Rows[i];
                float rowTop = rowsTop + i * layout.RowHeight;
                float rowBottom = rowTop + layout.RowHeight;
                if ((i & 1) != 0)
                {
                    drawList.AddRectFilled(
                        new Vector2(contentLeft - 3f, rowTop),
                        new Vector2(contentRight + 3f, rowBottom),
                        GetCachedImGuiColor(alternateRowBackground));
                }

                var rumourBaseColor = GetIslandRumourColor(rowLayout.Row.Rumour);
                var rumourColor = Utility.WithOpacity(rumourBaseColor, globalOpacity);
                float textY = rowTop + (layout.RowHeight - layout.ValueLineHeight) / 2f;

                if (Settings.IslandRumourRowAccents.Value)
                {
                    var rowAccent = WithIslandRumourAlpha(rumourBaseColor, globalOpacity * 0.92f);
                    drawList.AddRectFilled(
                        SnapTextPos(new Vector2(panelMin.X + 1f, rowTop + 3f)),
                        SnapTextPos(new Vector2(panelMin.X + 4f, rowBottom - 3f)),
                        GetCachedImGuiColor(rowAccent),
                        1.5f);
                }

                DrawIslandRumourTableText(drawList, font, layout.FontSize, new Vector2(columnLefts[0] + IslandRumourCellPaddingX, textY), rumourColor, rowLayout.Rumour, globalOpacity, strongShadow: true);
                DrawIslandRumourTableText(drawList, font, layout.FontSize, new Vector2(columnLefts[1] + IslandRumourCellPaddingX, textY), modsColor, rowLayout.Mods, globalOpacity, strongShadow: true);
                DrawIslandRumourTableText(drawList, font, layout.FontSize, new Vector2(columnLefts[2] + IslandRumourCellPaddingX, textY), mapTypeColor, rowLayout.MapType, globalOpacity, strongShadow: true);

                float badgeWidth = Math.Max(30f, Math.Min(columnWidths[3] - 4f, rowLayout.TierSize.X + 14f));
                float badgeHeight = Math.Min(layout.RowHeight - 7f, layout.ValueLineHeight + 6f);
                float badgeLeft = columnLefts[3] + (columnWidths[3] - badgeWidth) / 2f;
                float badgeTop = rowTop + (layout.RowHeight - badgeHeight) / 2f;
                var badgeBackground = WithIslandRumourAlpha(rumourBaseColor, globalOpacity * 0.24f);
                var badgeBorder = WithIslandRumourAlpha(rumourBaseColor, globalOpacity * 0.76f);
                drawList.AddRectFilled(
                    SnapTextPos(new Vector2(badgeLeft, badgeTop)),
                    SnapTextPos(new Vector2(badgeLeft + badgeWidth, badgeTop + badgeHeight)),
                    GetCachedImGuiColor(badgeBackground),
                    4f);
                drawList.AddRect(
                    SnapTextPos(new Vector2(badgeLeft, badgeTop)),
                    SnapTextPos(new Vector2(badgeLeft + badgeWidth, badgeTop + badgeHeight)),
                    GetCachedImGuiColor(badgeBorder),
                    4f);
                DrawIslandRumourTableText(
                    drawList,
                    font,
                    layout.FontSize,
                    new Vector2(
                        badgeLeft + (badgeWidth - rowLayout.TierSize.X) / 2f,
                        badgeTop + (badgeHeight - rowLayout.TierSize.Y) / 2f),
                    rumourColor,
                    rowLayout.Tier,
                    globalOpacity,
                    strongShadow: true);

                if (i + 1 < layout.Rows.Length)
                {
                    drawList.AddLine(
                        SnapTextPos(new Vector2(contentLeft, rowBottom)),
                        SnapTextPos(new Vector2(contentRight, rowBottom)),
                        GetCachedImGuiColor(dividerColor));
                }
            }

            float tableBottom = rowsTop + layout.RowHeight * layout.RowCount;
            float verticalTop = headerTop + 5f;
            float verticalBottom = tableBottom - 5f;
            for (int i = 1; i < columnLefts.Length; i++)
            {
                float separatorX = columnLefts[i] - IslandRumourColumnGap / 2f;
                drawList.AddLine(
                    SnapTextPos(new Vector2(separatorX, verticalTop)),
                    SnapTextPos(new Vector2(separatorX, verticalBottom)),
                    GetCachedImGuiColor(dividerColor));
            }

            if (!layout.ShowRegionStats)
                return;

            float footerTop = tableBottom;
            drawList.AddLine(
                SnapTextPos(new Vector2(contentLeft, footerTop)),
                SnapTextPos(new Vector2(contentRight, footerTop)),
                GetCachedImGuiColor(WithIslandRumourAlpha(baseAccentColor, globalOpacity * 0.48f)));

            DrawIslandRumourTableText(
                drawList,
                font,
                layout.FooterFontSize,
                new Vector2(
                    panelMin.X + (panelWidth - layout.StatsSize.X) / 2f,
                    footerTop + (layout.FooterHeight - layout.FooterLineHeight) / 2f),
                statsColor,
                layout.FittedStats,
                globalOpacity,
                strongShadow: true);
        }

        private IslandRumourTableLayout GetOrBuildIslandRumourTableLayout(
            IslandRumourSnapshot snapshot,
            int maxLabels,
            ImFontPtr font)
        {
            int fontSize = Math.Clamp(
                Settings.IslandRumourLabelFontSize.Value,
                Settings.IslandRumourLabelFontSize.Min,
                Settings.IslandRumourLabelFontSize.Max);
            int maxPanelWidth = Math.Clamp(
                Settings.IslandRumourLabelMaxWidth.Value,
                Settings.IslandRumourLabelMaxWidth.Min,
                Settings.IslandRumourLabelMaxWidth.Max);
            int configuredRowHeight = Math.Clamp(
                Settings.IslandRumourLabelSpacing.Value,
                Settings.IslandRumourLabelSpacing.Min,
                Settings.IslandRumourLabelSpacing.Max);
            int rowCount = Math.Min(maxLabels, snapshot.TableRows.Length);
            bool showRegionStats =
                Settings.ShowIslandRumourRegionStats.Value &&
                !string.IsNullOrEmpty(snapshot.RegionStatsText);

            var cached = snapshot.TableLayout;
            if (cached != null &&
                cached.Signature.Equals(snapshot.Signature, StringComparison.Ordinal) &&
                cached.RegionStatsText.Equals(snapshot.RegionStatsText, StringComparison.Ordinal) &&
                cached.FontSize == fontSize &&
                cached.MaxPanelWidth == maxPanelWidth &&
                cached.ConfiguredRowHeight == configuredRowHeight &&
                cached.MaxLabels == maxLabels &&
                cached.RowCount == rowCount &&
                cached.ShowRegionStats == showRegionStats)
            {
                return cached;
            }

            var layout = new IslandRumourTableLayout
            {
                Signature = snapshot.Signature,
                RegionStatsText = snapshot.RegionStatsText,
                FontSize = fontSize,
                HeaderFontSize = Math.Max(12, fontSize - 2),
                FooterFontSize = Math.Max(12, fontSize - 1),
                MaxPanelWidth = maxPanelWidth,
                ConfiguredRowHeight = configuredRowHeight,
                MaxLabels = maxLabels,
                RowCount = rowCount,
                ShowRegionStats = showRegionStats
            };

            if (rowCount <= 0)
            {
                snapshot.TableLayout = layout;
                return layout;
            }

            layout.ValueLineHeight = Math.Max(fontSize, MeasureIslandRumourText(font, fontSize, "Ag").Y);
            layout.HeaderLineHeight = Math.Max(layout.HeaderFontSize, MeasureIslandRumourText(font, layout.HeaderFontSize, "Ag").Y);
            layout.FooterLineHeight = Math.Max(layout.FooterFontSize, MeasureIslandRumourText(font, layout.FooterFontSize, "Ag").Y);
            layout.HeaderHeight = layout.HeaderLineHeight + 11f;
            layout.RowHeight = Math.Max(configuredRowHeight, layout.ValueLineHeight + 11f);

            var naturalWidths = _islandRumourNaturalColumnWidths;
            naturalWidths[0] = MeasureIslandRumourText(font, layout.HeaderFontSize, IslandRumourTableHeaders[0]).X + IslandRumourCellPaddingX * 2f;
            naturalWidths[1] = MeasureIslandRumourText(font, layout.HeaderFontSize, IslandRumourTableHeaders[1]).X + IslandRumourCellPaddingX * 2f;
            naturalWidths[2] = MeasureIslandRumourText(font, layout.HeaderFontSize, IslandRumourTableHeaders[2]).X + IslandRumourCellPaddingX * 2f;
            naturalWidths[3] = MeasureIslandRumourText(font, layout.HeaderFontSize, IslandRumourTableHeaders[3]).X + IslandRumourCellPaddingX * 2f;

            for (int i = 0; i < rowCount; i++)
            {
                var row = snapshot.TableRows[i];
                naturalWidths[0] = Math.Max(naturalWidths[0], MeasureIslandRumourText(font, fontSize, row.Rumour).X + IslandRumourCellPaddingX * 2f);
                naturalWidths[1] = Math.Max(naturalWidths[1], MeasureIslandRumourText(font, fontSize, row.Mods).X + IslandRumourCellPaddingX * 2f);
                naturalWidths[2] = Math.Max(naturalWidths[2], MeasureIslandRumourText(font, fontSize, row.MapType).X + IslandRumourCellPaddingX * 2f);
                naturalWidths[3] = Math.Max(naturalWidths[3], MeasureIslandRumourText(font, fontSize, row.Tier).X + 14f);
            }

            var columnWidths = layout.ColumnWidths;
            for (int i = 0; i < columnWidths.Length; i++)
            {
                columnWidths[i] = Math.Clamp(
                    naturalWidths[i],
                    IslandRumourMinimumColumnWidths[i],
                    IslandRumourMaximumColumnWidths[i]);
            }

            float minimumColumnsWidth =
                IslandRumourMinimumColumnWidths[0] +
                IslandRumourMinimumColumnWidths[1] +
                IslandRumourMinimumColumnWidths[2] +
                IslandRumourMinimumColumnWidths[3];
            float availableColumnsWidth = Math.Max(
                minimumColumnsWidth,
                maxPanelWidth - IslandRumourPanelPaddingX * 2f - IslandRumourColumnGap * 3f);
            ShrinkIslandRumourColumnsToFit(columnWidths, IslandRumourMinimumColumnWidths, availableColumnsWidth);

            for (int i = 0; i < layout.Headers.Length; i++)
            {
                layout.Headers[i] = FitIslandRumourText(
                    font,
                    IslandRumourTableHeaders[i],
                    layout.HeaderFontSize,
                    columnWidths[i] - IslandRumourCellPaddingX * 2f);
            }

            var rows = new IslandRumourTableRowLayout[rowCount];
            for (int i = 0; i < rowCount; i++)
            {
                var row = snapshot.TableRows[i];
                string rumour = FitIslandRumourText(font, row.Rumour, fontSize, columnWidths[0] - IslandRumourCellPaddingX * 2f);
                string mods = FitIslandRumourText(font, row.Mods, fontSize, columnWidths[1] - IslandRumourCellPaddingX * 2f);
                string mapType = FitIslandRumourText(font, row.MapType, fontSize, columnWidths[2] - IslandRumourCellPaddingX * 2f);
                string tier = FitIslandRumourText(font, row.Tier, fontSize, columnWidths[3] - 12f);
                rows[i] = new IslandRumourTableRowLayout(
                    row,
                    rumour,
                    mods,
                    mapType,
                    tier,
                    MeasureIslandRumourText(font, fontSize, tier));
            }
            layout.Rows = rows;

            layout.FooterHeight = showRegionStats ? layout.FooterLineHeight + 13f : 0f;
            float columnsWidth =
                columnWidths[0] + columnWidths[1] + columnWidths[2] + columnWidths[3] +
                IslandRumourColumnGap * 3f;
            layout.PanelWidth = Math.Min(maxPanelWidth, columnsWidth + IslandRumourPanelPaddingX * 2f);
            layout.PanelHeight =
                IslandRumourTopAccentHeight + IslandRumourPanelPaddingY * 2f +
                layout.HeaderHeight + layout.RowHeight * rowCount + layout.FooterHeight;

            if (showRegionStats)
            {
                float statsMaxWidth = layout.PanelWidth - IslandRumourPanelPaddingX * 2f;
                layout.FittedStats = FitIslandRumourText(
                    font,
                    snapshot.RegionStatsText,
                    layout.FooterFontSize,
                    statsMaxWidth);
                layout.StatsSize = MeasureIslandRumourText(font, layout.FooterFontSize, layout.FittedStats);
            }

            snapshot.TableLayout = layout;
            return layout;
        }

        private static void ShrinkIslandRumourColumnsToFit(float[] widths, float[] minimumWidths, float availableWidth)
        {
            float totalWidth = widths[0] + widths[1] + widths[2] + widths[3];
            if (totalWidth <= availableWidth)
                return;

            // Long descriptive columns give up space first; Tier stays stable and readable.
            float overflow = totalWidth - availableWidth;
            foreach (int index in IslandRumourColumnShrinkOrder)
            {
                float shrinkable = Math.Max(0f, widths[index] - minimumWidths[index]);
                float shrink = Math.Min(shrinkable, overflow);
                widths[index] -= shrink;
                overflow -= shrink;
                if (overflow <= 0.01f)
                    break;
            }
        }

        private void DrawIslandRumourTableText(
            ImDrawListPtr drawList,
            ImFontPtr font,
            float fontSize,
            Vector2 position,
            Color color,
            string text,
            float globalOpacity,
            bool strongShadow)
        {
            if (string.IsNullOrEmpty(text))
                return;

            position = SnapTextPos(position);
            float shadowOpacity = strongShadow ? 0.90f : 0.66f;
            var shadow = WithIslandRumourAlpha(Color.Black, globalOpacity * shadowOpacity);
            drawList.AddText(font, fontSize, position + Vector2.One, GetCachedImGuiColor(shadow), text, 0f);
            drawList.AddText(font, fontSize, position, GetCachedImGuiColor(color), text, 0f);
        }

        private Vector2 MeasureIslandRumourText(ImFontPtr font, int fontSize, string text)
        {
            if (string.IsNullOrEmpty(text))
                return Vector2.Zero;

            var key = (text, fontSize);
            if (_islandRumourTextSizeCache.TryGetValue(key, out var size))
                return size;

            size = font.CalcTextSizeA(fontSize, float.MaxValue, 0f, text);
            if (_islandRumourTextSizeCache.Count >= IslandRumourTextCacheMaxEntries)
                _islandRumourTextSizeCache.Clear();
            _islandRumourTextSizeCache[key] = size;
            return size;
        }

        private string FitIslandRumourText(ImFontPtr font, string text, int fontSize, float maxWidth)
        {
            if (string.IsNullOrEmpty(text) || maxWidth <= 0f)
                return string.Empty;

            int widthKey = Math.Max(1, (int)MathF.Floor(maxWidth));
            var key = (text, fontSize, widthKey);
            if (_islandRumourFittedTextCache.TryGetValue(key, out var fitted))
                return fitted;

            if (MeasureIslandRumourText(font, fontSize, text).X <= maxWidth)
            {
                fitted = text;
            }
            else
            {
                const string ellipsis = "...";
                float ellipsisWidth = MeasureIslandRumourText(font, fontSize, ellipsis).X;
                if (ellipsisWidth >= maxWidth)
                {
                    fitted = ellipsis;
                }
                else
                {
                    int low = 0;
                    int high = text.Length;
                    while (low < high)
                    {
                        int mid = (low + high + 1) / 2;
                        string candidate = text[..mid].TrimEnd() + ellipsis;
                        if (MeasureIslandRumourText(font, fontSize, candidate).X <= maxWidth)
                            low = mid;
                        else
                            high = mid - 1;
                    }

                    fitted = low <= 0 ? ellipsis : text[..low].TrimEnd() + ellipsis;
                }
            }

            if (_islandRumourFittedTextCache.Count >= IslandRumourTextCacheMaxEntries)
                _islandRumourFittedTextCache.Clear();
            _islandRumourFittedTextCache[key] = fitted;
            return fitted;
        }

        private static Color WithIslandRumourAlpha(Color color, float opacity)
        {
            int alpha = (int)Math.Round(Math.Clamp(opacity, 0f, 1f) * 255f);
            return Color.FromArgb(alpha, color.R, color.G, color.B);
        }

        private IslandRumourTableRow[] BuildIslandRumourTableRows(string[] rumours)
        {
            if (rumours.Length == 0)
                return Array.Empty<IslandRumourTableRow>();

            var rows = new List<IslandRumourTableRow>(rumours.Length);
            for (int i = 0; i < rumours.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(rumours[i]))
                    rows.Add(BuildIslandRumourTableRow(rumours[i]));
            }

            return rows.Count == 0 ? Array.Empty<IslandRumourTableRow>() : rows.ToArray();
        }

        private IslandRumourTableRow BuildIslandRumourTableRow(string rawName)
        {
            if (TryGetIslandRumourDefinition(rawName, out var definition))
            {
                return new IslandRumourTableRow(
                    definition.Name,
                    EmptyIslandRumourCell(definition.Mods),
                    EmptyIslandRumourCell(definition.MapType),
                    GetCompactIslandRumourTier(definition.Rating));
            }

            var canonical = GetIslandRumourCanonicalName(rawName);
            return new IslandRumourTableRow(canonical, "-", "-", "-");
        }

        private static string GetCompactIslandRumourTier(string rating)
        {
            if (string.IsNullOrWhiteSpace(rating))
                return "-";

            string trimmed = rating.Trim();
            int suffixIndex = trimmed.IndexOf('(');
            if (suffixIndex > 0)
                trimmed = trimmed[..suffixIndex].TrimEnd();
            return trimmed.Length == 0 ? "-" : trimmed;
        }

        private static string EmptyIslandRumourCell(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value;
        }

        private List<string> BuildIslandRumourCatalog()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in _observedIslandRumours)
            {
                var trimmed = GetIslandRumourCanonicalName(name);
                var token = Utility.NormalizeToken(trimmed);
                if (!string.IsNullOrWhiteSpace(trimmed) && token.Length != 0 && !IsRejectedIslandRumourText(token))
                    names.Add(trimmed);
            }

            var result = names.ToList();
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }
    }
}
