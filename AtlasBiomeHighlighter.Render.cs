using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Numerics;
using ExileCore2;
using ExileCore2.PoEMemory.Elements.AtlasElements;
using ImGuiNET;

namespace AtlasBiomeHighlighter
{
    public partial class AtlasBiomeHighlighter
    {
        private const int NodeCircleSegments = 12;
        private const int MechanicCircleSegments = 8;
        private const int WaypointCircleSegments = 20;
        private const float RingViewportPadding = 80f;
        private Vector2 _currentRenderDisplaySize;
        private readonly Dictionary<string, Vector2> _labelSizeCache = new(StringComparer.Ordinal);
        private const int LabelSizeCacheMaxEntries = 2048;

        
        
        private readonly Dictionary<int, uint> _imguiColorCache = new(128);
        private const int ImGuiColorCacheMaxEntries = 512;

        
        
        
        private readonly Dictionary<(int x, int y), ConnectionRenderNode> _connectionRenderNodeCache = new(2048);

        
        
        
        
        private readonly Dictionary<(int x, int y), Vector2> _shortestPathStableScreenByCoord = new(2048);
        private const float ConnectionViewportPadding = 96f;
        private const float MinConnectionLengthSquared = 4f;
        private long _lastConnectionDiagnosticLogMs;

        private readonly struct ConnectionRenderNode
        {
            public ConnectionRenderNode(Vector2 center, bool unlocked, bool visited)
            {
                Center = center;
                Unlocked = unlocked;
                Visited = visited;
            }

            public Vector2 Center { get; }
            public bool Unlocked { get; }
            public bool Visited { get; }
        }

        public override void Render()
        {
            using var renderProfile = ProfileScope("Render total");
            if (!Settings.Enable.Value) return;
            if (_atlasPanel == null || !_atlasPanel.IsVisible) return;

            
            UpdateViewportSize();
            _currentRenderDisplaySize = ImGui.GetIO().DisplaySize;

            
            try
            {
                if (Settings.DrawMapConnections.Value)
                    using (ProfileScope("Render map connections"))
                    {
                        RenderMapConnections();
                    }
            }
            catch { }

            
            try
            {
                if (Settings.WaypointsEnabled.Value)
                {
                    using (ProfileScope("Render waypoints"))
                    {
                        RenderWaypoints();
                        RenderWaypointArrows();
                    }
                }
                if (Settings.DrawShortestPath.Value)
                    using (ProfileScope("Render shortest path"))
                    {
                        RenderShortestPath();
                    }
                if (Settings.DrawTowerRange.Value)
                    using (ProfileScope("Render tower range"))
                    {
                        RenderTowerRange();
                    }
            }
            catch { }

            bool profileRenderSections = Settings.DebugMode.Value && Settings.PerformanceProfiling.Value;
            long renderNodeFilterTicks = 0;
            long renderNodeRingsTicks = 0;
            long renderNodeLabelsTicks = 0;
            long renderNodeTotalTicks = 0;

            if (Settings.HighlightPreferredMaps.Value)
                EnsurePreferredCacheUpToDate();

            bool anyMechanicHighlightsEnabled = HasAnyMechanicHighlightEnabled();
            bool anyTowerHighlightsEnabled = HasAnyTowerHighlightEnabled();
            ImDrawListPtr modernLabelDrawList = default;
            if (Settings.ShowLabels.Value && Settings.ModernLabelCards.Value)
            {
                modernLabelDrawList = ImGui.GetBackgroundDrawList();
                BeginModernNodeLabels();
                PrepareAtlasSignalOriginCollisionGuard();
            }

            using (ProfileScope("Render node overlays"))
            {
            foreach (var info in _visibleNodeInfos)
            {
                long __nodeStart = profileRenderSections ? Stopwatch.GetTimestamp() : 0;
                long __filterStart = __nodeStart;
                var nd = info.Node;
                if (nd?.Element is null)
                    continue;

                
                if (Settings.HideCompletedMaps.Value && info.Completed) continue;
                if (Settings.HideAttemptedMaps.Value && info.Attempted) continue;
                if (Settings.HideLockedMaps.Value && info.Locked) continue;

                var biome = info.Biome;

                
                bool biomeVisible = biome != Biome.Unknown && Settings.Visible.TryGetValue(biome, out var on) && on.Value;
                var sflags = info.SpecialFlags;
                var mechanicNames = info.MechanicNames;
                bool mechanicWanted = anyMechanicHighlightsEnabled && IsAnyMechanicHighlightEnabled(mechanicNames);
                string? highlightedTowerName = null;
                bool towerWanted = anyTowerHighlightsEnabled && TryGetHighlightedTowerName(info.Node, out highlightedTowerName);
                bool specialWanted =
                    mechanicWanted ||
                    towerWanted ||
                    ((sflags & Utility.SpecialFlags.UniqueMap) != 0 && Settings.HighlightUniqueMaps.Value) ||
                    ((sflags & Utility.SpecialFlags.DeadlyBoss) != 0 && Settings.HighlightDeadlyBoss.Value) ||
                    ((sflags & Utility.SpecialFlags.MomentofZen) != 0 && Settings.HighlightMomentofZen.Value) ||
                    ((sflags & Utility.SpecialFlags.CorruptedNexus) != 0 && Settings.HighlightCorruptedNexus.Value) ||
                    ((sflags & Utility.SpecialFlags.Cleansed) != 0 && Settings.HighlightCleansed.Value) ||
                    ((sflags & Utility.SpecialFlags.AreaContainsAbyss) != 0 && Settings.HighlightAreaContainsAbyss.Value) ||
                    ((sflags & Utility.SpecialFlags.AreaContainsExpedition) != 0 && Settings.HighlightAreaContainsExpedition.Value);

                
                bool preferredWanted = false;
                string? preferredMatchedToken = null;
                if (Settings.HighlightPreferredMaps.Value)
                {
                    
                    
                    if (info.NameToken.Length != 0 && _preferredTokensExact.Contains(info.NameToken))
                    {
                        preferredWanted = true;
                        preferredMatchedToken = info.NameToken;
                    }
                    else if (info.IdToken.Length != 0 && _preferredTokensExact.Contains(info.IdToken))
                    {
                        preferredWanted = true;
                        preferredMatchedToken = info.IdToken;
                    }
                    else if (_preferredMechanicTokensExact.Count != 0)
                    {
                        var mechanicTokens = info.MechanicTokens;
                        for (int mt = 0; mt < mechanicTokens.Length; mt++)
                        {
                            var token = mechanicTokens[mt];
                            if (token.Length != 0 && _preferredMechanicTokensExact.Contains(token))
                            {
                                preferredWanted = true;
                                preferredMatchedToken = token;
                                break;
                            }
                        }
                    }
                }

                string preferredDisplayName = preferredWanted ? GetPreferredDisplayName(preferredMatchedToken) : string.Empty;

                if (preferredWanted)
                    DebugPreferredMapHit(nd, preferredMatchedToken ?? string.Empty, preferredMatchedToken == null ? null : GetPreferredTag(preferredMatchedToken), info.MapName, biome, sflags);

                bool renderOverlay = biomeVisible || specialWanted || preferredWanted;
                bool renderMapNameLabelOnly =
                    Settings.ShowLabels.Value &&
                    Settings.ShowMapNames.Value &&
                    !string.IsNullOrWhiteSpace(info.MapName);

                
                
                
                if (!renderOverlay && !renderMapNameLabelOnly)
                    continue;

                Settings.Colors.TryGetValue(biome, out var colorNode);

                if (profileRenderSections)
                    renderNodeFilterTicks += Stopwatch.GetTimestamp() - __filterStart;

                long __ringsStart = profileRenderSections ? Stopwatch.GetTimestamp() : 0;
                var baseColor = colorNode?.Value ?? Settings.LabelTextColor.Value;
                var ringColor = Utility.WithOpacity(baseColor, Settings.Opacity.Value);
                
                // Element.Center is a live memory-backed property. Read it once so X and Y,
                // every ring and the queued Atlas Signal label use one coherent frame snapshot.
                var centerValue = nd.Element.Center;
                var center = new Vector2(centerValue.X, centerValue.Y);
                if (Settings.ShowLabels.Value && Settings.ModernLabelCards.Value)
                {
                    string? placeholderReason = IsInvalidAtlasSignalPlaceholder(info, center)
                        ? "known-placeholder-geometry"
                        : IsAtlasSignalOriginCollision(info, center)
                            ? "origin-collision"
                            : null;
                    if (placeholderReason != null)
                    {
                        RecordFilteredAtlasSignalPlaceholder(info, center, placeholderReason);
                        continue;
                    }
                }
                var radius = Settings.NodeRadius.Value;
                var thickness = Settings.RingThickness.Value;

                
                
                
                var displaySize = _currentRenderDisplaySize;
                const float overlayPadding = 96f;
                if (renderOverlay &&
                    (center.X < -overlayPadding || center.Y < -overlayPadding ||
                     center.X > displaySize.X + overlayPadding || center.Y > displaySize.Y + overlayPadding))
                    renderOverlay = false;

                bool modernLabelAnchorVisible = !Settings.ModernLabelCards.Value || IsModernLabelAnchorInsideViewport(center);
                if (!modernLabelAnchorVisible)
                    renderMapNameLabelOnly = false;

                if (!renderOverlay && !renderMapNameLabelOnly)
                    continue;

                int extra = 0;
                bool nonMechanicSpecialWanted = specialWanted && !mechanicWanted && !towerWanted;
                bool drawBaseRing = renderOverlay && (biomeVisible || preferredWanted || nonMechanicSpecialWanted);

                
                
                
                if (drawBaseRing)
                    DrawCircleFast(center, radius, ringColor, thickness, NodeCircleSegments);


                if (renderOverlay && preferredWanted)
                {
                    var c = Utility.WithOpacity(Settings.PreferredMapRingColor.Value, Settings.Opacity.Value * Settings.SpecialAlphaMultiplier.Value);
                    DrawCircleFast(center, radius + (++extra) * 2, c, Settings.SpecialRingThickness.Value, NodeCircleSegments);
                }

                if (renderOverlay && (sflags & Utility.SpecialFlags.UniqueMap) != 0 && Settings.HighlightUniqueMaps.Value)
                {
                    var c = Utility.WithOpacity(Settings.UniqueMapRingColor.Value, Settings.Opacity.Value * Settings.SpecialAlphaMultiplier.Value);
                    DrawCircleFast(center, radius + (++extra) * 2, c, Settings.SpecialRingThickness.Value, NodeCircleSegments);
                }
                if (renderOverlay && (sflags & Utility.SpecialFlags.DeadlyBoss) != 0 && Settings.HighlightDeadlyBoss.Value)
                {
                    var c = Utility.WithOpacity(Settings.DeadlyBossRingColor.Value, Settings.Opacity.Value * Settings.SpecialAlphaMultiplier.Value);
                    DrawCircleFast(center, radius + (++extra) * 2, c, Settings.SpecialRingThickness.Value, NodeCircleSegments);
                }
                if (renderOverlay && (sflags & Utility.SpecialFlags.MomentofZen) != 0 && Settings.HighlightMomentofZen.Value)
                {
                    var c = Utility.WithOpacity(Settings.MomentofZenRingColor.Value, Settings.Opacity.Value * Settings.SpecialAlphaMultiplier.Value);
                    DrawCircleFast(center, radius + (++extra) * 2, c, Settings.SpecialRingThickness.Value, NodeCircleSegments);
                }
                if (renderOverlay && (sflags & Utility.SpecialFlags.CorruptedNexus) != 0 && Settings.HighlightCorruptedNexus.Value)
                {
                    var c = Utility.WithOpacity(Settings.CorruptedNexusRingColor.Value, Settings.Opacity.Value * Settings.SpecialAlphaMultiplier.Value);
                    DrawCircleFast(center, radius + (++extra) * 2, c, Settings.SpecialRingThickness.Value, NodeCircleSegments);
                }
                if (renderOverlay && (sflags & Utility.SpecialFlags.Cleansed) != 0 && Settings.HighlightCleansed.Value)
                {
                    var c = Utility.WithOpacity(Settings.CleansedRingColor.Value, Settings.Opacity.Value * Settings.SpecialAlphaMultiplier.Value);
                    DrawCircleFast(center, radius + (++extra) * 2, c, Settings.SpecialRingThickness.Value, NodeCircleSegments);
                }

                if (renderOverlay && (sflags & Utility.SpecialFlags.AreaContainsAbyss) != 0 && Settings.HighlightAreaContainsAbyss.Value)
                {
                    var c = Utility.WithOpacity(Settings.AreaContainsAbyssRingColor.Value, Settings.Opacity.Value * Settings.SpecialAlphaMultiplier.Value);
                    DrawCircleFast(center, radius + (++extra) * 2, c, Settings.SpecialRingThickness.Value, NodeCircleSegments);
                }

                if (renderOverlay && (sflags & Utility.SpecialFlags.AreaContainsExpedition) != 0 && Settings.HighlightAreaContainsExpedition.Value)
                {
                    var c = Utility.WithOpacity(Settings.AreaContainsExpeditionRingColor.Value, Settings.Opacity.Value * Settings.SpecialAlphaMultiplier.Value);
                    DrawCircleFast(center, radius + (++extra) * 2, c, Settings.SpecialRingThickness.Value, NodeCircleSegments);
                }

                if (renderOverlay && mechanicWanted)
                {
                    var c = Utility.WithOpacity(Settings.MechanicHighlightRingColor.Value, Settings.Opacity.Value * Settings.SpecialAlphaMultiplier.Value);
                    DrawCircleFast(center, radius + (++extra) * 2, c, Settings.SpecialRingThickness.Value, MechanicCircleSegments);
                }

                if (renderOverlay && towerWanted && !string.IsNullOrWhiteSpace(highlightedTowerName))
                {
                    var towerColor = GetTowerHighlightColor(highlightedTowerName);
                    var c = Utility.WithOpacity(towerColor, Settings.Opacity.Value * Settings.SpecialAlphaMultiplier.Value);
                    DrawCircleFast(center, radius + (++extra) * 2, c, Settings.SpecialRingThickness.Value, NodeCircleSegments);
                }

                if (profileRenderSections)
                    renderNodeRingsTicks += Stopwatch.GetTimestamp() - __ringsStart;

                if (Settings.ShowLabels.Value)
                {
                    long __labelsStart = profileRenderSections ? Stopwatch.GetTimestamp() : 0;

                    if (Settings.ModernLabelCards.Value)
                    {
                        QueueModernNodeLabel(
                            info,
                            center,
                            radius,
                            baseColor,
                            biomeVisible,
                            preferredWanted,
                            preferredMatchedToken,
                            preferredDisplayName,
                            mechanicWanted,
                            towerWanted,
                            highlightedTowerName);
                    }
                    else
                    {
                        string text;
                        bool labelContainsMapName = false;
                        bool hasMapName = !string.IsNullOrWhiteSpace(info.MapName);

                        if (biomeVisible && hasMapName)
                        {
                        
                        
                            var mapNameText = Settings.ShowMapStatus.Value && Settings.ShowMapNames.Value
                                ? $"{GetMapStatusPrefix(info.Completed, info.Attempted, info.Locked, info.Visited, info.Unlocked)} - {info.MapName}"
                                : info.MapName;
                            text = $"{mapNameText} - {info.BiomeDisplay}";
                            labelContainsMapName = true;
                        }
                        else if (Settings.PreferMapNameForDeadly.Value &&
                            (sflags & Utility.SpecialFlags.DeadlyBoss) != 0 &&
                            hasMapName)
                        {
                            text = info.MapName!;
                            labelContainsMapName = true;
                        }
                        else if (preferredWanted && !string.IsNullOrWhiteSpace(preferredDisplayName) && !hasMapName)
                        {
                            text = preferredDisplayName;
                            labelContainsMapName = true;
                        }
                        else if (Settings.ShowUniqueNameOnLabel.Value &&
                                 (sflags & Utility.SpecialFlags.UniqueMap) != 0 &&
                                 !string.IsNullOrWhiteSpace(info.UniqueName))
                        {
                            text = info.UniqueName!;
                        }
                        else
                        {
                            if (Settings.ShowMapNames.Value && hasMapName)
                            {
                            
                            
                                var mapNameText = info.MapName!;
                                text = Settings.ShowMapStatus.Value
                                    ? $"{GetMapStatusPrefix(info.Completed, info.Attempted, info.Locked, info.Visited, info.Unlocked)} - {mapNameText}"
                                    : mapNameText;
                                labelContainsMapName = true;
                            }
                            else
                            {
                                text = info.BiomeDisplay;
                            }
                        }

                    
                    
                    
                    

                        if (Settings.ShowSpecialTag.Value)
                        {
                            if ((sflags & Utility.SpecialFlags.DeadlyBoss) != 0) text += " [Deadly]";
                            if ((sflags & Utility.SpecialFlags.MomentofZen) != 0) text += " [Moment Of Zen]";
                            if ((sflags & Utility.SpecialFlags.Cleansed) != 0) text += " [Cleansed]";
                            if ((sflags & Utility.SpecialFlags.CorruptedNexus) != 0) text += " [Corrupted]";
                            if ((sflags & Utility.SpecialFlags.AreaContainsAbyss) != 0) text += " [Abyss]";
                            if ((sflags & Utility.SpecialFlags.AreaContainsExpedition) != 0) text += " [Expedition]";
                            foreach (var mechanicName in mechanicNames)
                            {
                                if (IsMechanicHighlightEnabled(mechanicName))
                                    text += " [" + mechanicName + "]";
                            }
                            if (towerWanted && !string.IsNullOrWhiteSpace(highlightedTowerName)) text += " [" + highlightedTowerName + "]";
                            if ((sflags & Utility.SpecialFlags.UniqueMap) != 0 && !(Settings.ShowUniqueNameOnLabel.Value)) text += " [Unique]";
                            if (preferredWanted) text += " " + GetPreferredTag(preferredMatchedToken);
                        }

                        var size = MeasureTextCached(text);
                        var offsetY = labelContainsMapName ? Settings.MapNameOffsetY.Value : Settings.LabelOffset.Value;
                        var pos = new Vector2(center.X - size.X / 2f, center.Y - (radius + offsetY));

                    
                        var textColor = Settings.LabelUseBiomeColor.Value ? ringColor : Settings.LabelTextColor.Value;

                    
                        if (labelContainsMapName && !(info.Visited || info.Unlocked))
                        {
                            textColor = System.Drawing.Color.FromArgb(
                                textColor.A,
                                (int)(textColor.R * 0.55f),
                                (int)(textColor.G * 0.55f),
                                (int)(textColor.B * 0.55f));
                        }

                    
                    
                    
                        DrawTextWithLabelSettings(text, pos, textColor);
                    }

                    if (profileRenderSections)
                        renderNodeLabelsTicks += Stopwatch.GetTimestamp() - __labelsStart;
                }

                if (profileRenderSections)
                    renderNodeTotalTicks += Stopwatch.GetTimestamp() - __nodeStart;
            }
            }

            if (Settings.ShowLabels.Value && Settings.ModernLabelCards.Value)
            {
                long __modernLabelsStart = profileRenderSections ? Stopwatch.GetTimestamp() : 0;
                RenderQueuedModernNodeLabels(modernLabelDrawList);
                if (profileRenderSections)
                    renderNodeLabelsTicks += Stopwatch.GetTimestamp() - __modernLabelsStart;
            }

            if (profileRenderSections)
            {
                ReportProfileElapsedTicks("Render node filter/match", renderNodeFilterTicks);
                ReportProfileElapsedTicks("Render node rings", renderNodeRingsTicks);
                ReportProfileElapsedTicks("Render node labels", renderNodeLabelsTicks);
                ReportProfileElapsedTicks("Render node loop total", renderNodeTotalTicks);
            }

            try
            {
                using (ProfileScope("Render Island Rumours"))
                {
                    RenderIslandRumourLabels();
                }
            }
            catch { }

            try
            {
                using (ProfileScope("Render preferred guides"))
                {
                    RenderPreferredGuides();
                }
            }
            catch {  }

            try
            {
                using (ProfileScope("Render waypoint panel"))
                {
                    RenderWaypointPanel();
                }
            }
            catch { }
        }

        

        private void RenderMapConnections()
        {
            if (_visibleConnectionSegments.Count == 0) return;

            bool diagnostics = Settings.DebugMode.Value && Settings.PerformanceProfiling.Value;
            long totalStart = diagnostics ? Stopwatch.GetTimestamp() : 0;
            long setupTicks = 0;
            long loopTicks = 0;
            long cullTicks = 0;
            long drawTicks = 0;
            int visitedSkipped = 0;
            int offscreenSkipped = 0;
            int tinySkipped = 0;
            int staleSkipped = 0;
            int drawnConnections = 0;

            long setupStart = diagnostics ? Stopwatch.GetTimestamp() : 0;

            int thickness = Settings.ConnectionThickness.Value;
            bool drawVisitedConnections = Settings.DrawVisitedConnections.Value;
            var unlockedColor = Settings.ConnectionColor.Value;
            var lockedColor = Settings.ConnectionColorLocked.Value;

            var displaySize = ImGui.GetIO().DisplaySize;
            var drawList = ImGui.GetBackgroundDrawList();
            uint unlockedPackedColor = GetCachedImGuiColor(unlockedColor);
            uint lockedPackedColor = GetCachedImGuiColor(lockedColor);

            float minX = -ConnectionViewportPadding;
            float minY = -ConnectionViewportPadding;
            float maxX = displaySize.X + ConnectionViewportPadding;
            float maxY = displaySize.Y + ConnectionViewportPadding;

            if (diagnostics) setupTicks = Stopwatch.GetTimestamp() - setupStart;
            long loopStart = diagnostics ? Stopwatch.GetTimestamp() : 0;

            
            
            for (int i = 0; i < _visibleConnectionSegments.Count; i++)
            {
                var segment = _visibleConnectionSegments[i];
                var srcElement = segment.Source.Element;
                var dstElement = segment.Target.Element;
                if (srcElement is null || dstElement is null)
                {
                    staleSkipped++;
                    continue;
                }

                if (!drawVisitedConnections && (segment.SourceVisited || segment.TargetVisited))
                {
                    visitedSkipped++;
                    continue;
                }

                var a = new Vector2(srcElement.Center.X, srcElement.Center.Y);
                var b = new Vector2(dstElement.Center.X, dstElement.Center.Y);

                long cullStart = diagnostics ? Stopwatch.GetTimestamp() : 0;

                if ((a.X < minX && b.X < minX) ||
                    (a.X > maxX && b.X > maxX) ||
                    (a.Y < minY && b.Y < minY) ||
                    (a.Y > maxY && b.Y > maxY))
                {
                    if (diagnostics) cullTicks += Stopwatch.GetTimestamp() - cullStart;
                    offscreenSkipped++;
                    continue;
                }

                if (Vector2.DistanceSquared(a, b) <= MinConnectionLengthSquared)
                {
                    if (diagnostics) cullTicks += Stopwatch.GetTimestamp() - cullStart;
                    tinySkipped++;
                    continue;
                }

                if (diagnostics) cullTicks += Stopwatch.GetTimestamp() - cullStart;

                long drawStart = diagnostics ? Stopwatch.GetTimestamp() : 0;
                drawList.AddLine(a, b, (segment.SourceUnlocked && segment.TargetUnlocked) ? unlockedPackedColor : lockedPackedColor, thickness);
                if (diagnostics) drawTicks += Stopwatch.GetTimestamp() - drawStart;
                drawnConnections++;
            }

            if (!diagnostics)
                return;

            loopTicks = Stopwatch.GetTimestamp() - loopStart;
            long totalTicks = Stopwatch.GetTimestamp() - totalStart;

            ReportProfileElapsedTicks("Connections setup", setupTicks);
            ReportProfileElapsedTicks("Connections loop total", loopTicks);
            ReportProfileElapsedTicks("Connections culling", cullTicks);
            ReportProfileElapsedTicks("Connections draw AddLine", drawTicks);

            double totalMs = totalTicks * 1000.0 / Stopwatch.Frequency;
            if (totalMs >= Settings.PerformanceSpikeThresholdMs.Value)
                ReportConnectionDiagnosticSummary(totalMs, _visibleConnectionSegments.Count, drawnConnections, visitedSkipped, offscreenSkipped, tinySkipped, staleSkipped);
        }

        private void ReportConnectionDiagnosticSummary(
            double totalMs,
            int preparedSegments,
            int drawnConnections,
            int visitedSkipped,
            int offscreenSkipped,
            int tinySkipped,
            int staleSkipped)
        {
            if (!Settings.DebugMode.Value || !Settings.PerformanceProfiling.Value)
                return;

            long now = Environment.TickCount64;
            if (now - _lastConnectionDiagnosticLogMs < 500)
                return;

            _lastConnectionDiagnosticLogMs = now;
            WritePerformanceSpikeDetails(
                "Connections diagnostics",
                totalMs,
                $"prepared={preparedSegments}, drawn={drawnConnections}, visitedSkip={visitedSkipped}, offscreenSkip={offscreenSkipped}, tinySkip={tinySkipped}, staleSkip={staleSkipped}");
        }

        private void RenderWaypoints()
        {
            if (!Settings.ShowWaypointsOnAtlas.Value) return;
            var wps = Settings.Waypoints;
            if (wps is null || wps.Count == 0) return;

            for (int i = 0; i < wps.Count; i++)
            {
                var wp = wps[i];
                if (!wp.Enabled) continue;
                var key = (wp.X, wp.Y);
                if (!_nodeByCoord.TryGetValue(key, out var nd) || nd?.Element is null) continue;
                var center = new Vector2(nd.Element.Center.X, nd.Element.Center.Y);
                var color = Color.FromArgb(wp.ColorArgb);
                var thickness = Settings.WaypointRingThickness.Value + (wp.Selected ? 1 : 0);
                DrawCircleFast(center, Settings.WaypointRingRadius.Value, color, thickness, WaypointCircleSegments);

                
                var top = center + new Vector2(0, -Settings.WaypointRingRadius.Value - 6);
                var left = top + new Vector2(-6, 12);
                var right = top + new Vector2(6, 12);
                DrawTriangleFilled(top, left, right, color);
            }
        }

        private void RenderWaypointArrows()
        {
            if (!Settings.ShowWaypointArrowsOnAtlas.Value) return;
            var wps = Settings.Waypoints;
            if (wps is null || wps.Count == 0) return;
            var ds = ImGui.GetIO().DisplaySize;
            var w = ds.X;
            var h = ds.Y;

            for (int wi = 0; wi < wps.Count; wi++)
            {
                var wp = wps[wi];
                if (!wp.Enabled) continue;

                var coordinate = (wp.X, wp.Y);
                _nodeByCoord.TryGetValue(coordinate, out var liveNode);

                if (!TryGetCalibratedNavigationTargetCenter(
                        wp.X,
                        wp.Y,
                        liveNode,
                        out var target,
                        allowRawOnScreen: true,
                        forceTransformRebuild: false,
                        allowFarOffscreen: true))
                {
                    continue;
                }

                var colorArrow = Color.FromArgb(wp.ColorArgb);
                var debugOrigin = new Vector2(w * 0.5f, h * 0.5f);
                bool debugOnScreen = target.X > 0 && target.X < w && target.Y > 0 && target.Y < h;
                if (liveNode != null && (!debugOnScreen || IsNavigationTargetSuspicious(target)))
                    AppendNavigationDebug("WaypointArrow", liveNode, debugOrigin, target, debugOnScreen ? "suspicious on-screen waypoint target" : $"off-screen waypoint target wp={wp.Name} coord={wp.X},{wp.Y}");

                
                if (target.X > 0 && target.X < w && target.Y > 0 && target.Y < h)
                {
                    
                    
                    
                    
                    if (!wp.ShowLabel || liveNode?.Element is null)
                        continue;

                    var mapLabel = liveNode.Element.Area?.Name;
                    if (string.IsNullOrWhiteSpace(mapLabel) && Utility.TryGetAnyMapName(liveNode, out var nm))
                        mapLabel = nm;

                    var waypointLabel = wp.Name?.Trim();
                    var hasCustomWaypointLabel = !string.IsNullOrWhiteSpace(waypointLabel) &&
                        !string.Equals(waypointLabel, mapLabel, StringComparison.OrdinalIgnoreCase);

                    if (!hasCustomWaypointLabel && WouldMainOverlayRenderMapNameLabel(liveNode))
                        continue;

                    var label = hasCustomWaypointLabel ? waypointLabel : mapLabel;
                    if (string.IsNullOrWhiteSpace(label))
                        label = waypointLabel;

                    if (!string.IsNullOrWhiteSpace(label))
                    {
                        var textColor = Settings.LabelUseBiomeColor.Value ? colorArrow : Settings.LabelTextColor.Value;
                        DrawCenteredLabelWithSettings(
                            label!,
                            target,
                            Settings.WaypointRingRadius.Value,
                            Settings.MapNameOffsetY.Value,
                            textColor);
                    }
                    continue;
                }

                
                var centerScreen = new Vector2(w * 0.5f, h * 0.5f);
                var dir = target - centerScreen;
                if (dir.LengthSquared() < 0.001f) continue;
                dir = Vector2.Normalize(dir);

                float margin = 30f;
                var edge = new Vector2(
                    Clamp(centerScreen.X + dir.X * 99999f, margin, w - margin),
                    Clamp(centerScreen.Y + dir.Y * 99999f, margin, h - margin));

                
                float t = float.PositiveInfinity;
                if (Math.Abs(dir.X) > 1e-5f)
                {
                    var tx1 = (margin - centerScreen.X) / dir.X;
                    var tx2 = ((w - margin) - centerScreen.X) / dir.X;
                    if (tx1 > 0) t = Math.Min(t, tx1);
                    if (tx2 > 0) t = Math.Min(t, tx2);
                }
                if (Math.Abs(dir.Y) > 1e-5f)
                {
                    var ty1 = (margin - centerScreen.Y) / dir.Y;
                    var ty2 = ((h - margin) - centerScreen.Y) / dir.Y;
                    if (ty1 > 0) t = Math.Min(t, ty1);
                    if (ty2 > 0) t = Math.Min(t, ty2);
                }
                if (float.IsFinite(t)) edge = centerScreen + dir * t;

                var back = edge - dir * 24f;
                var perp = new Vector2(-dir.Y, dir.X);
                var p1 = edge;
                var p2 = back + perp * 12f;
                var p3 = back - perp * 12f;

                DrawTriangleFilled(p1, p2, p3, colorArrow);
			}
		}

        private static float Clamp(float v, float min, float max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        private static void DrawTriangleFilled(Vector2 p1, Vector2 p2, Vector2 p3, Color color)
        {
            
            
            var dl = ImGui.GetBackgroundDrawList();
            var col = ToImGuiColor(color);
            dl.AddTriangleFilled(p1, p2, p3, col);
        }

        private static uint ToImGuiColor(Color color)
        {
            return ImGui.ColorConvertFloat4ToU32(new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f));
        }

        private void DrawCircleFast(Vector2 center, float radius, Color color, float thickness, int segments)
        {
            
            
            
            
            
            if (radius <= 0f || thickness <= 0f || color.A <= 0)
                return;

            var displaySize = _currentRenderDisplaySize;
            if (displaySize.X <= 0f || displaySize.Y <= 0f)
                displaySize = ImGui.GetIO().DisplaySize;

            var pad = radius + RingViewportPadding;
            if (center.X < -pad || center.Y < -pad || center.X > displaySize.X + pad || center.Y > displaySize.Y + pad)
                return;

            if (Settings.FastRingRendering.Value)
                segments = GetAdaptiveRingSegments(radius, thickness, segments);

            if (segments < 5)
                segments = 5;

            var dl = ImGui.GetBackgroundDrawList();
            dl.AddCircle(center, radius, GetCachedImGuiColor(color), segments, thickness);
        }

        private int GetAdaptiveRingSegments(float radius, float thickness, int requestedSegments)
        {
            
            
            
            int maxSegments = Settings.FastRingMaxSegments.Value;
            if (maxSegments < 5) maxSegments = 5;
            if (maxSegments > 32) maxSegments = 32;

            int adaptive;
            if (radius <= 14f) adaptive = 5;
            else if (radius <= 22f) adaptive = 6;
            else if (radius <= 30f) adaptive = 7;
            else if (radius <= 42f) adaptive = 8;
            else adaptive = 10;

            if (thickness >= 5f && adaptive > 8)
                adaptive = 8;

            int result = requestedSegments;
            if (result > adaptive) result = adaptive;
            if (result > maxSegments) result = maxSegments;
            return result;
        }

        private uint GetCachedImGuiColor(Color color)
        {
            int key = color.ToArgb();
            if (_imguiColorCache.TryGetValue(key, out var packed))
                return packed;

            packed = ToImGuiColor(color);
            if (_imguiColorCache.Count >= ImGuiColorCacheMaxEntries)
                _imguiColorCache.Clear();

            _imguiColorCache[key] = packed;
            return packed;
        }

        private Vector2 MeasureTextCached(string text)
        {
            if (string.IsNullOrEmpty(text))
                return Vector2.Zero;

            if (_labelSizeCache.TryGetValue(text, out var size))
                return size;

            size = Graphics.MeasureText(text);
            if (_labelSizeCache.Count >= LabelSizeCacheMaxEntries)
                _labelSizeCache.Clear();

            _labelSizeCache[text] = size;
            return size;
        }

        private void DrawCenteredLabelWithSettings(string text, Vector2 center, float radius, int offsetY, Color textColor)
        {
            
            
            var size = MeasureTextCached(text);
            var pos = new Vector2(center.X - size.X / 2f, center.Y - (radius + offsetY));
            DrawTextWithLabelSettings(text, pos, textColor);
        }

        private static Vector2 SnapTextPos(Vector2 pos)
        {
            return new Vector2((float)Math.Round(pos.X), (float)Math.Round(pos.Y));
        }

        private void DrawTextWithLabelSettings(string text, Vector2 pos, Color textColor)
        {
            
            
            pos = SnapTextPos(pos);

            if (Settings.LabelOutline.Value)
            {
                
                
                
                int t = Math.Max(1, Settings.LabelOutlineThickness.Value);
                for (int d = 1; d <= t; d++)
                {
                    Graphics.DrawText(text, new Vector2(pos.X - d, pos.Y), Color.Black);
                    Graphics.DrawText(text, new Vector2(pos.X + d, pos.Y), Color.Black);
                    Graphics.DrawText(text, new Vector2(pos.X, pos.Y - d), Color.Black);
                    Graphics.DrawText(text, new Vector2(pos.X, pos.Y + d), Color.Black);
                    Graphics.DrawText(text, new Vector2(pos.X - d, pos.Y - d), Color.Black);
                    Graphics.DrawText(text, new Vector2(pos.X + d, pos.Y - d), Color.Black);
                    Graphics.DrawText(text, new Vector2(pos.X - d, pos.Y + d), Color.Black);
                    Graphics.DrawText(text, new Vector2(pos.X + d, pos.Y + d), Color.Black);
                }
            }

            if (Settings.LabelBold.Value)
            {
                
                
                Graphics.DrawText(text, new Vector2(pos.X, pos.Y + 1), textColor);
            }

            Graphics.DrawText(text, pos, textColor);
        }

        private void RenderShortestPath()
        {
            if (_shortestPaths.Count == 0) return;

            int thickness = Settings.ShortestPathThickness.Value;
            var io = ImGui.GetIO();
            float viewportDiagonal = MathF.Sqrt(io.DisplaySize.X * io.DisplaySize.X + io.DisplaySize.Y * io.DisplaySize.Y);

            
            
            
            float maxSegmentLength = MathF.Max(520f, viewportDiagonal * 0.42f);
            float maxSegmentLengthSq = maxSegmentLength * maxSegmentLength;
            float maxSaneCoordinate = MathF.Max(io.DisplaySize.X, io.DisplaySize.Y) * 3.0f;

            for (int pathIndex = 0; pathIndex < _shortestPaths.Count; pathIndex++)
            {
                var path = _shortestPaths[pathIndex];
                if (path.Count < 2)
                    continue;

                var color = GetShortestPathRenderColor(path[^1]);

                Vector2 prevPos = default;
                (int x, int y) prevCoord = default;
                bool hasPrev = false;

                for (int i = 0; i < path.Count; i++)
                {
                    var coord = path[i];
                    if (!TryGetShortestPathScreenPosition(coord, maxSaneCoordinate, out var pos))
                    {
                        hasPrev = false;
                        continue;
                    }

                    if (hasPrev)
                    {
                        Vector2 drawFrom = prevPos;
                        Vector2 drawTo = pos;
                        float distanceSq = Vector2.DistanceSquared(drawFrom, drawTo);

                        
                        
                        
                        if (distanceSq > maxSegmentLengthSq)
                        {
                            if (_shortestPathStableScreenByCoord.TryGetValue(prevCoord, out var stableFrom))
                            {
                                float stableDistSq = Vector2.DistanceSquared(stableFrom, drawTo);
                                if (stableDistSq <= maxSegmentLengthSq)
                                {
                                    drawFrom = stableFrom;
                                    distanceSq = stableDistSq;
                                }
                            }
                        }

                        if (distanceSq > maxSegmentLengthSq && _shortestPathStableScreenByCoord.TryGetValue(coord, out var stableTo))
                        {
                            float stableDistSq = Vector2.DistanceSquared(drawFrom, stableTo);
                            if (stableDistSq <= maxSegmentLengthSq)
                            {
                                drawTo = stableTo;
                                distanceSq = stableDistSq;
                            }
                        }

                        if (distanceSq <= maxSegmentLengthSq && distanceSq > MinConnectionLengthSquared)
                        {
                            Graphics.DrawLine(drawFrom, drawTo, thickness, color);
                            _shortestPathStableScreenByCoord[prevCoord] = drawFrom;
                            _shortestPathStableScreenByCoord[coord] = drawTo;
                        }
                    }

                    prevPos = pos;
                    prevCoord = coord;
                    hasPrev = true;
                }

                
                
                var last = path[^1];
                if (TryGetShortestPathScreenPosition(last, maxSaneCoordinate, out var p))
                {
                    var label = $"{path.Count - 1} steps";
                    var textSize = ImGui.CalcTextSize(label);
                    
                    
                    
                    
                    var ringRadius = Settings.WaypointRingRadius.Value;
                    var yOffset = ringRadius + 4f;
                    if (yOffset < 20f) yOffset = 20f;
                    else if (yOffset > 34f) yOffset = 34f;
                    var labelPos = new Vector2(p.X - textSize.X * 0.5f, p.Y + yOffset);
                    Graphics.DrawText(label, labelPos, color);
                }
            }
        }

        private Color GetShortestPathRenderColor((int x, int y) targetCoord)
        {
            var wps = Settings.Waypoints;
            if (wps is null || wps.Count == 0)
                return Settings.ShortestPathColor.Value;

            for (int i = 0; i < wps.Count; i++)
            {
                var wp = wps[i];
                if (!wp.Enabled || !wp.Selected || !wp.AutoFavoriteMap)
                    continue;

                if (wp.X == targetCoord.x && wp.Y == targetCoord.y)
                    return Color.FromArgb(wp.ColorArgb);
            }

            return Settings.ShortestPathColor.Value;
        }

        private bool TryGetShortestPathScreenPosition((int x, int y) coord, float maxSaneCoordinate, out Vector2 pos)
        {
            pos = default;

            if (!_nodeByCoord.TryGetValue(coord, out var nd) || nd?.Element is null)
            {
                
                
                
                return _shortestPathStableScreenByCoord.TryGetValue(coord, out pos);
            }

            pos = new Vector2(nd.Element.Center.X, nd.Element.Center.Y);
            if (!IsSaneShortestPathPosition(pos, maxSaneCoordinate))
            {
                return _shortestPathStableScreenByCoord.TryGetValue(coord, out pos);
            }

            
            
            _shortestPathStableScreenByCoord[coord] = pos;
            return true;
        }

        private static bool IsSaneShortestPathPosition(Vector2 pos, float maxSaneCoordinate)
        {
            if (float.IsNaN(pos.X) || float.IsNaN(pos.Y) || float.IsInfinity(pos.X) || float.IsInfinity(pos.Y))
                return false;

            return MathF.Abs(pos.X) <= maxSaneCoordinate && MathF.Abs(pos.Y) <= maxSaneCoordinate;
        }

        private string GetMapStatusPrefix(bool completed, bool attempted, bool locked, bool visited, bool unlocked)
        {
            if (locked)
                return "Locked";

            if (completed)
                return "Completed";

            if (attempted)
                return "Attempted";

            if (unlocked || visited)
                return "Runnable";

            return "Locked";
        }

        
        
        
        
        private bool WouldMainOverlayRenderMapNameLabel(AtlasNodeDescription nd)
        {
            if (!(Settings.ShowLabels.Value && Settings.ShowMapNames.Value))
                return false;

            
            if (Settings.HideCompletedMaps.Value && Utility.IsMapCompleted(nd))
                return false;
            if (Settings.HideAttemptedMaps.Value && Utility.IsMapAttempted(nd))
                return false;
            if (Settings.HideLockedMaps.Value && Utility.IsMapLocked(nd))
                return false;

            var biome = Utility.TryGetBiome(nd);

            
            bool biomeVisible = biome != Biome.Unknown && Settings.Visible.TryGetValue(biome, out var on) && on.Value;
            var sflags = Utility.TryGetSpecialFlags(nd);
            bool specialWanted =
                (HasAnyMechanicHighlightEnabled() && IsAnyMechanicHighlightEnabled(Utility.TryGetMechanicNames(nd))) ||
                ((sflags & Utility.SpecialFlags.UniqueMap) != 0 && Settings.HighlightUniqueMaps.Value) ||
                ((sflags & Utility.SpecialFlags.DeadlyBoss) != 0 && Settings.HighlightDeadlyBoss.Value) ||
                ((sflags & Utility.SpecialFlags.MomentofZen) != 0 && Settings.HighlightMomentofZen.Value) ||
                ((sflags & Utility.SpecialFlags.CorruptedNexus) != 0 && Settings.HighlightCorruptedNexus.Value) ||
                ((sflags & Utility.SpecialFlags.Cleansed) != 0 && Settings.HighlightCleansed.Value) ||
                ((sflags & Utility.SpecialFlags.AreaContainsAbyss) != 0 && Settings.HighlightAreaContainsAbyss.Value) ||
                ((sflags & Utility.SpecialFlags.AreaContainsExpedition) != 0 && Settings.HighlightAreaContainsExpedition.Value);

            bool preferredWanted = false;
            if (Settings.HighlightPreferredMaps.Value && TryGetCachedNodeTokens(nd, out var nameToken, out var idToken))
            {
                
                preferredWanted =
                    (nameToken.Length != 0 && _preferredTokensExact.Contains(nameToken)) ||
                    (idToken.Length != 0 && _preferredTokensExact.Contains(idToken));
            }

            
            
            if (Utility.TryGetAnyMapName(nd, sflags, out var mapName) && !string.IsNullOrWhiteSpace(mapName))
                return true;

            if (!biomeVisible && !(specialWanted || preferredWanted))
                return false;

            
            return Settings.Colors.ContainsKey(biome);
        }

        private bool HasAnyTowerHighlightEnabled()
        {
            if (Settings.TowerHighlights == null || Settings.TowerHighlights.Count == 0)
                return false;

            foreach (var kvp in Settings.TowerHighlights)
            {
                if (kvp.Value != null && kvp.Value.Value)
                    return true;
            }

            return false;
        }

        private bool TryGetHighlightedTowerName(AtlasNodeDescription nd, out string? towerName)
        {
            towerName = null;
            if (Settings.TowerHighlights == null)
                return false;

            if (!Utility.TryGetTowerName(nd, out towerName) || string.IsNullOrWhiteSpace(towerName))
                return false;

            return Settings.TowerHighlights.TryGetValue(towerName, out var node) && node.Value;
        }

        private System.Drawing.Color GetTowerHighlightColor(string towerName)
        {
            if (Settings.TowerHighlightColors != null &&
                Settings.TowerHighlightColors.TryGetValue(towerName, out var colorNode) &&
                colorNode != null)
            {
                return colorNode.Value;
            }

            return Settings.TowerHighlightRingColor.Value;
        }

        private bool HasAnyMechanicHighlightEnabled()
        {
            if (Settings.MechanicHighlights == null || Settings.MechanicHighlights.Count == 0)
                return false;

            foreach (var kvp in Settings.MechanicHighlights)
            {
                if (kvp.Value != null && kvp.Value.Value)
                    return true;
            }

            return false;
        }

        private bool IsMechanicHighlightEnabled(string name)
        {
            return Settings.MechanicHighlights != null && Settings.MechanicHighlights.TryGetValue(name, out var node) && node.Value;
        }

        private bool IsAnyMechanicHighlightEnabled(IReadOnlyList<string> names)
        {
            if (names == null || names.Count == 0)
                return false;

            for (int i = 0; i < names.Count; i++)
            {
                if (IsMechanicHighlightEnabled(names[i]))
                    return true;
            }

            return false;
        }

        private void RenderTowerRange()
        {
            if (!TryGetTowerRangeOrigin(out var origin)) return;
            if (!_nodeByCoord.TryGetValue((origin.X, origin.Y), out var originNd) || originNd?.Element is null) return;


            const int range = 11;
            var col = Settings.TowerRangeColor.Value;
            if (!Settings.DrawTowerRange.Value) return;
            var originPos = new Vector2(originNd.Element.Center.X, originNd.Element.Center.Y);

            
            
            
            bool originIsTower = IsTower(originNd.Element);


            if (!originIsTower && originNd.Element.IsVisited)
                return;

            int count = 0;
            foreach (var kv in _nodeByCoord)
            {
                var nd = kv.Value;
                if (nd?.Element is null) continue;
                if (!TryGetCoordinate(nd, out var c)) continue;

                
                if (c.X == origin.X && c.Y == origin.Y) continue;

                if (Distance(origin, c) > range) continue;

                bool isTower = IsTower(nd.Element);
                if (originIsTower)
                {
                    if (isTower) continue; 
                    
                    if (nd.Element.Area?.Name?.Equals("Lost Towers", StringComparison.OrdinalIgnoreCase) == true)
                        continue;

                    if (nd.Element.IsVisited) continue;
                }
                else
                {
                    if (!isTower) continue; 

                    if (nd.Element.IsVisited) continue;
                }

                var pos = new Vector2(nd.Element.Center.X, nd.Element.Center.Y);
                DrawCircleFast(pos, Settings.NodeRadius.Value + 10, col, 2, 20);
                Graphics.DrawLine(originPos, pos, 1, col);
                count++;
            }

            var label = originIsTower ? $"{count} maps in tower range" : $"{count} towers in range";
            Graphics.DrawText(label, originPos + new Vector2(12, 12), col);
        }


        private void InvalidateWaypointAtlasCache()
        {
            _waypointAtlasRows.Clear();
            _waypointAtlasBuildIndex = 0;
            _waypointAtlasBuildActive = true;
            _waypointAtlasCachedSearch = Utility.NormalizeToken(_atlasSearch);
            _waypointAtlasCachedUnlockedOnly = Settings.WaypointAtlasUnlockedOnly.Value;
            _waypointAtlasCachedHideCompleted = Settings.HideCompletedMaps.Value;
            _waypointAtlasCachedHideAttempted = Settings.HideAttemptedMaps.Value;
            _waypointAtlasCachedHideLocked = Settings.HideLockedMaps.Value;
            _waypointAtlasCachedMaxItems = Settings.WaypointAtlasMaxItems.Value;
            _waypointAtlasCachedNodeCount = _atlasNodes?.Length ?? 0;
        }

        private void InvalidateWaypointMechanicCache()
        {
            _waypointMechanicRows.Clear();
            _waypointMechanicBuildIndex = 0;
            _waypointMechanicBuildActive = true;
            _waypointMechanicCachedSearch = Utility.NormalizeToken(_mechanicSearch);
            _waypointMechanicCachedUnlockedOnly = Settings.WaypointAtlasUnlockedOnly.Value;
            _waypointMechanicCachedHideCompleted = Settings.HideCompletedMaps.Value;
            _waypointMechanicCachedHideAttempted = Settings.HideAttemptedMaps.Value;
            _waypointMechanicCachedHideLocked = Settings.HideLockedMaps.Value;
            _waypointMechanicCachedMaxItems = Settings.WaypointAtlasMaxItems.Value;
            _waypointMechanicCachedNodeCount = _atlasNodes?.Length ?? 0;
        }

        private void EnsureWaypointAtlasCacheCurrent()
        {
            var search = Utility.NormalizeToken(_atlasSearch);
            var nodeCount = _atlasNodes?.Length ?? 0;

            if (!_waypointAtlasBuildActive &&
                _waypointAtlasCachedSearch == search &&
                _waypointAtlasCachedUnlockedOnly == Settings.WaypointAtlasUnlockedOnly.Value &&
                _waypointAtlasCachedHideCompleted == Settings.HideCompletedMaps.Value &&
                _waypointAtlasCachedHideAttempted == Settings.HideAttemptedMaps.Value &&
                _waypointAtlasCachedHideLocked == Settings.HideLockedMaps.Value &&
                _waypointAtlasCachedMaxItems == Settings.WaypointAtlasMaxItems.Value &&
                _waypointAtlasCachedNodeCount == nodeCount)
            {
                return;
            }

            if (_waypointAtlasCachedSearch != search ||
                _waypointAtlasCachedUnlockedOnly != Settings.WaypointAtlasUnlockedOnly.Value ||
                _waypointAtlasCachedHideCompleted != Settings.HideCompletedMaps.Value ||
                _waypointAtlasCachedHideAttempted != Settings.HideAttemptedMaps.Value ||
                _waypointAtlasCachedHideLocked != Settings.HideLockedMaps.Value ||
                _waypointAtlasCachedMaxItems != Settings.WaypointAtlasMaxItems.Value ||
                _waypointAtlasCachedNodeCount != nodeCount)
            {
                InvalidateWaypointAtlasCache();
            }
        }

        private void EnsureWaypointMechanicCacheCurrent()
        {
            var search = Utility.NormalizeToken(_mechanicSearch);
            var nodeCount = _atlasNodes?.Length ?? 0;

            if (!_waypointMechanicBuildActive &&
                _waypointMechanicCachedSearch == search &&
                _waypointMechanicCachedUnlockedOnly == Settings.WaypointAtlasUnlockedOnly.Value &&
                _waypointMechanicCachedHideCompleted == Settings.HideCompletedMaps.Value &&
                _waypointMechanicCachedHideAttempted == Settings.HideAttemptedMaps.Value &&
                _waypointMechanicCachedHideLocked == Settings.HideLockedMaps.Value &&
                _waypointMechanicCachedMaxItems == Settings.WaypointAtlasMaxItems.Value &&
                _waypointMechanicCachedNodeCount == nodeCount)
            {
                return;
            }

            if (_waypointMechanicCachedSearch != search ||
                _waypointMechanicCachedUnlockedOnly != Settings.WaypointAtlasUnlockedOnly.Value ||
                _waypointMechanicCachedHideCompleted != Settings.HideCompletedMaps.Value ||
                _waypointMechanicCachedHideAttempted != Settings.HideAttemptedMaps.Value ||
                _waypointMechanicCachedHideLocked != Settings.HideLockedMaps.Value ||
                _waypointMechanicCachedMaxItems != Settings.WaypointAtlasMaxItems.Value ||
                _waypointMechanicCachedNodeCount != nodeCount)
            {
                InvalidateWaypointMechanicCache();
            }
        }

        private void ProcessWaypointAtlasCacheBudget()
        {
            EnsureWaypointAtlasCacheCurrent();

            if (!_waypointAtlasBuildActive || _atlasNodes == null)
                return;

            using var waypointAtlasBuildProfile = ProfileScope("Build waypoint atlas search cache");

            var maxItems = Settings.WaypointAtlasMaxItems.Value;
            var searchTok = _waypointAtlasCachedSearch;
            int processed = 0;

            while (_waypointAtlasBuildIndex < _atlasNodes.Length &&
                   processed < WaypointAtlasBuildBudgetPerFrame)
            {
                var nd = _atlasNodes[_waypointAtlasBuildIndex++];
                processed++;

                if (nd?.Element is null) continue;

                if (_waypointAtlasCachedHideCompleted && Utility.IsMapCompleted(nd))
                    continue;
                if (_waypointAtlasCachedHideAttempted && Utility.IsMapAttempted(nd))
                    continue;
                if (_waypointAtlasCachedHideLocked && Utility.IsMapLocked(nd))
                    continue;
                if (_waypointAtlasCachedUnlockedOnly && !(Utility.TryIsUnlocked(nd, out var un) && un))
                    continue;

                if (!Utility.TryGetAnyMapName(nd, out var mapName) || string.IsNullOrWhiteSpace(mapName))
                    continue;

                if (searchTok.Length != 0)
                {
                    var tok = Utility.NormalizeToken(mapName);
                    if (!tok.Contains(searchTok, StringComparison.Ordinal))
                        continue;
                }

                var coord = nd.Coordinate;
                _waypointAtlasRows.Add(new WaypointAtlasRow(nd, mapName, Utility.TryGetBiome(nd).ToString(), coord.X, coord.Y));
            }

            if (_waypointAtlasBuildIndex >= _atlasNodes.Length)
            {
                SortWaypointAtlasRowsBySteps();
                _waypointAtlasBuildActive = false;
            }
        }

        private void ProcessWaypointMechanicCacheBudget()
        {
            EnsureWaypointMechanicCacheCurrent();

            if (!_waypointMechanicBuildActive || _atlasNodes == null)
                return;

            using var waypointMechanicBuildProfile = ProfileScope("Build waypoint mechanic search cache");

            var maxItems = Settings.WaypointAtlasMaxItems.Value;
            var searchTok = _waypointMechanicCachedSearch;
            int processed = 0;

            while (_waypointMechanicBuildIndex < _atlasNodes.Length &&
                   processed < WaypointAtlasBuildBudgetPerFrame)
            {
                var nd = _atlasNodes[_waypointMechanicBuildIndex++];
                processed++;

                if (nd?.Element is null) continue;

                if (_waypointMechanicCachedHideCompleted && Utility.IsMapCompleted(nd))
                    continue;
                if (_waypointMechanicCachedHideAttempted && Utility.IsMapAttempted(nd))
                    continue;
                if (_waypointMechanicCachedHideLocked && Utility.IsMapLocked(nd))
                    continue;
                if (_waypointMechanicCachedUnlockedOnly && !(Utility.TryIsUnlocked(nd, out var un) && un))
                    continue;

                var mechanicNames = Utility.TryGetMechanicNames(nd);
                if (mechanicNames.Count == 0)
                    continue;

                if (!Utility.TryGetAnyMapName(nd, out var mapName) || string.IsNullOrWhiteSpace(mapName))
                    mapName = $"Atlas node {nd.Coordinate.X},{nd.Coordinate.Y}";

                var mechanicText = string.Join(", ", mechanicNames);

                if (searchTok.Length != 0)
                {
                    var mapTok = Utility.NormalizeToken(mapName);
                    var mechanicTok = Utility.NormalizeToken(mechanicText);
                    if (!mapTok.Contains(searchTok, StringComparison.Ordinal) &&
                        !mechanicTok.Contains(searchTok, StringComparison.Ordinal))
                    {
                        continue;
                    }
                }

                var coord = nd.Coordinate;
                _waypointMechanicRows.Add(new WaypointMechanicRow(nd, mapName, mechanicText, Utility.TryGetBiome(nd).ToString(), coord.X, coord.Y));
            }

            if (_waypointMechanicBuildIndex >= _atlasNodes.Length)
            {
                SortWaypointMechanicRowsBySteps();
                _waypointMechanicBuildActive = false;
            }
        }


        private void SortWaypointAtlasRowsBySteps()
        {
            _waypointAtlasRows.Sort((a, b) => CompareAtlasNavigatorRows(a.X, a.Y, a.Name, b.X, b.Y, b.Name));
        }

        private void SortWaypointMechanicRowsBySteps()
        {
            _waypointMechanicRows.Sort((a, b) => CompareAtlasNavigatorRows(a.X, a.Y, a.MapName, b.X, b.Y, b.MapName));
        }

        private int CompareAtlasNavigatorRows(int ax, int ay, string aName, int bx, int by, string bName)
        {
            bool hasA = TryGetAtlasRouteSteps(ax, ay, out var aSteps);
            bool hasB = TryGetAtlasRouteSteps(bx, by, out var bSteps);

            if (hasA && hasB)
            {
                int bySteps = aSteps.CompareTo(bSteps);
                if (bySteps != 0) return bySteps;
            }
            else if (hasA)
            {
                return -1;
            }
            else if (hasB)
            {
                return 1;
            }

            int byName = string.Compare(aName, bName, StringComparison.OrdinalIgnoreCase);
            if (byName != 0) return byName;

            int byX = ax.CompareTo(bx);
            return byX != 0 ? byX : ay.CompareTo(by);
        }

        private bool PassesNavigatorStepsFilter(int x, int y, out int steps)
        {
            bool hasSteps = TryGetAtlasRouteSteps(x, y, out steps);
            int minSteps = Math.Max(0, _navigatorMinSteps);
            int maxSteps = Math.Max(0, _navigatorMaxSteps);

            
            if (minSteps == 0 && maxSteps == 0)
                return true;

            if (!hasSteps)
                return false;

            if (steps < minSteps)
                return false;

            return maxSteps == 0 || steps <= maxSteps;
        }

        private void DrawNavigatorStepsFilterControls(string idSuffix)
        {
            ImGuiNET.ImGui.Text("Steps:");
            ImGuiNET.ImGui.SameLine();
            ImGuiNET.ImGui.TextDisabled("min");
            ImGuiNET.ImGui.SameLine();
            ImGuiNET.ImGui.SetNextItemWidth(48);
            if (ImGuiNET.ImGui.InputInt($"##steps_min_{idSuffix}", ref _navigatorMinSteps, 0, 0))
                _navigatorMinSteps = Math.Clamp(_navigatorMinSteps, 0, 999);

            ImGuiNET.ImGui.SameLine();
            ImGuiNET.ImGui.TextDisabled("max");
            ImGuiNET.ImGui.SameLine();
            ImGuiNET.ImGui.SetNextItemWidth(48);
            if (ImGuiNET.ImGui.InputInt($"##steps_max_{idSuffix}", ref _navigatorMaxSteps, 0, 0))
                _navigatorMaxSteps = Math.Clamp(_navigatorMaxSteps, 0, 999);

            ImGuiNET.ImGui.SameLine();
            if (ImGuiNET.ImGui.SmallButton($"Clear##steps_clear_{idSuffix}"))
            {
                _navigatorMinSteps = 0;
                _navigatorMaxSteps = 0;
            }

            if (ImGuiNET.ImGui.IsItemHovered())
                ImGuiNET.ImGui.SetTooltip("0 / 0 disables the Steps filter. Max = 0 means no upper limit.");
        }

        private void RenderWaypointPanel()
        {

            if (!_waypointPanelOpen) return;
            if (!Settings.WaypointsEnabled.Value) return;

            
            ImGuiNET.ImGui.SetNextWindowSize(new Vector2(980, 680), ImGuiNET.ImGuiCond.FirstUseEver);
            ImGuiNET.ImGui.SetNextWindowSizeConstraints(new Vector2(680, 420), new Vector2(float.MaxValue, float.MaxValue));

            var flags = ImGuiNET.ImGuiWindowFlags.None;
            if (!ImGuiNET.ImGui.Begin("Atlas Navigator", ref _waypointPanelOpen, flags))
            {
                ImGuiNET.ImGui.End();
                return;
            }

            ImGuiNET.ImGui.TextDisabled("Hotkeys: Insert = add waypoint, Delete = remove hovered waypoint, End = toggle this window");
            bool showWp = Settings.ShowWaypointsOnAtlas.Value;
            if (ImGuiNET.ImGui.Checkbox("Show Waypoints", ref showWp))
                Settings.ShowWaypointsOnAtlas.Value = showWp;
            ImGuiNET.ImGui.SameLine();
            bool showArr = Settings.ShowWaypointArrowsOnAtlas.Value;
            if (ImGuiNET.ImGui.Checkbox("Show Direction Arrows", ref showArr))
                Settings.ShowWaypointArrowsOnAtlas.Value = showArr;

            bool atlasJumpEnabled = Settings.WaypointJumpEnabled.Value;
            if (ImGuiNET.ImGui.Checkbox("Enable Atlas Jump", ref atlasJumpEnabled))
                Settings.WaypointJumpEnabled.Value = atlasJumpEnabled;
            if (ImGuiNET.ImGui.IsItemHovered())
                ImGuiNET.ImGui.SetTooltip("Enables Jump buttons that pan the atlas to the selected map without moving the mouse.");

            ImGuiNET.ImGui.SameLine();
            bool drawShortestPath = Settings.DrawShortestPath.Value;
            if (ImGuiNET.ImGui.Checkbox("Draw shortest path to selected waypoint", ref drawShortestPath))
                Settings.DrawShortestPath.Value = drawShortestPath;
            if (ImGuiNET.ImGui.IsItemHovered())
                ImGuiNET.ImGui.SetTooltip("Draws the calculated route from the current atlas position to the selected waypoint.");

            ImGuiNET.ImGui.SameLine();
            DrawColorEdit("Shortest path color##navigator", Settings.ShortestPathColor.Value, c => Settings.ShortestPathColor.Value = c, false);

            ImGuiNET.ImGui.SameLine();
            bool advancedMode = _waypointPanelAdvancedMode;
            if (ImGuiNET.ImGui.Checkbox("Advanced", ref advancedMode))
                _waypointPanelAdvancedMode = advancedMode;
            if (ImGuiNET.ImGui.IsItemHovered())
                ImGuiNET.ImGui.SetTooltip("Shows technical coordinates for debugging. Hidden by default for a cleaner UI.");
            ImGuiNET.ImGui.Spacing();

            var wps = Settings.Waypoints;

            
            if (ImGuiNET.ImGui.Button("Clear All Waypoints"))
            {
                wps.Clear();
                _selectedWaypointCoord = null;
                _shortestPath.Clear();
                _shortestPaths.Clear();
            }
            ImGuiNET.ImGui.SameLine();
            ImGuiNET.ImGui.TextDisabled($"Count: {wps.Count}  |  Favorite auto: {CountAutoFavoriteWaypoints()}");

            ImGuiNET.ImGui.Separator();

            
            var wpsTableFlags =
                ImGuiNET.ImGuiTableFlags.RowBg |
                ImGuiNET.ImGuiTableFlags.BordersInnerH |
                ImGuiNET.ImGuiTableFlags.ScrollY |
                ImGuiNET.ImGuiTableFlags.SizingStretchProp |
                ImGuiNET.ImGuiTableFlags.Resizable;

            
            var wpsAvail = ImGuiNET.ImGui.GetContentRegionAvail();
            var rowHeight = ImGuiNET.ImGui.GetTextLineHeightWithSpacing();
            var targetWaypointRows = Math.Clamp(wps.Count + 4, 8, 18);
            var rowBasedWaypointHeight = rowHeight * targetWaypointRows + ImGuiNET.ImGui.GetFrameHeightWithSpacing();
            var weightedWaypointHeight = wpsAvail.Y * 0.45f;
            var maxWaypointHeight = Math.Max(180f, wpsAvail.Y * 0.62f);
            var wpsTableH = Math.Clamp(Math.Max(rowBasedWaypointHeight, weightedWaypointHeight), 170f, maxWaypointHeight);

            int waypointColumnCount = _waypointPanelAdvancedMode ? 8 : 7;
            if (ImGuiNET.ImGui.BeginTable("##wps", waypointColumnCount, wpsTableFlags, new Vector2(0, wpsTableH)))
            {
                using var waypointListProfile = ProfileScope("Render waypoint panel saved list");
                ImGuiNET.ImGui.TableSetupColumn("Route", ImGuiNET.ImGuiTableColumnFlags.WidthFixed, 54);
                ImGuiNET.ImGui.TableSetupColumn("Label", ImGuiNET.ImGuiTableColumnFlags.WidthFixed, 54);
                ImGuiNET.ImGui.TableSetupColumn("Marker", ImGuiNET.ImGuiTableColumnFlags.WidthFixed, 54);
                ImGuiNET.ImGui.TableSetupColumn("Map", ImGuiNET.ImGuiTableColumnFlags.WidthStretch);
                if (_waypointPanelAdvancedMode)
                    ImGuiNET.ImGui.TableSetupColumn("Coord", ImGuiNET.ImGuiTableColumnFlags.WidthFixed, 80);
                ImGuiNET.ImGui.TableSetupColumn("Steps", ImGuiNET.ImGuiTableColumnFlags.WidthFixed, 58);
                ImGuiNET.ImGui.TableSetupColumn("Jump", ImGuiNET.ImGuiTableColumnFlags.WidthFixed, 58);
                ImGuiNET.ImGui.TableSetupColumn("Remove", ImGuiNET.ImGuiTableColumnFlags.WidthFixed, 62);
                ImGuiNET.ImGui.TableHeadersRow();

                for (int i = 0; i < wps.Count; i++)
                {
                    var wp = wps[i];
                    ImGuiNET.ImGui.PushID(i);
                    ImGuiNET.ImGui.TableNextRow();

                    
                    ImGuiNET.ImGui.TableNextColumn();
                    bool selected = wp.Selected;
                    if (ImGuiNET.ImGui.Checkbox("##route", ref selected))
                    {
                        wp.Selected = selected;
                        wps[i] = wp;
                        SyncSelectedWaypoint();
                    }
                    if (ImGuiNET.ImGui.IsItemHovered())
                        ImGuiNET.ImGui.SetTooltip("Route: include this waypoint in the shortest-path routing. Multiple waypoints can be selected.");

                    
                    ImGuiNET.ImGui.TableNextColumn();
                    bool showLabel = wp.ShowLabel;
                    if (ImGuiNET.ImGui.Checkbox("##label", ref showLabel))
                    {
                        wp.ShowLabel = showLabel;
                        wps[i] = wp;
                    }
                    if (ImGuiNET.ImGui.IsItemHovered())
                        ImGuiNET.ImGui.SetTooltip("Label: show this map name directly on the Atlas.");

                    
                    ImGuiNET.ImGui.TableNextColumn();
                    var c = System.Drawing.Color.FromArgb(wp.ColorArgb);
                    var vec = new Vector4(c.R / 255f, c.G / 255f, c.B / 255f, 1f);
                    
                    ImGuiNET.ImGui.SetNextItemWidth(ImGuiNET.ImGui.GetFrameHeight());
                    if (ImGuiNET.ImGui.ColorEdit4("##c", ref vec, ImGuiNET.ImGuiColorEditFlags.NoInputs | ImGuiNET.ImGuiColorEditFlags.NoLabel))
                    {
                        var nc = System.Drawing.Color.FromArgb((int)(vec.X * 255), (int)(vec.Y * 255), (int)(vec.Z * 255));
                        wp.ColorArgb = nc.ToArgb();
                        wps[i] = wp;
                    }

                    
                    ImGuiNET.ImGui.TableNextColumn();
                    var name = wp.Name ?? string.Empty;
                    ImGuiNET.ImGui.SetNextItemWidth(-1);
                    if (ImGuiNET.ImGui.InputText($"##name_{i}", ref name, 64))
                    {
                        wp.Name = name;
                        wps[i] = wp;
                    }

                    
                    if (_waypointPanelAdvancedMode)
                    {
                        ImGuiNET.ImGui.TableNextColumn();
                        ImGuiNET.ImGui.Text($"{wp.X},{wp.Y}");
                    }

                    ImGuiNET.ImGui.TableNextColumn();
                    if (TryGetAtlasRouteSteps(wp.X, wp.Y, out var waypointSteps))
                        ImGuiNET.ImGui.TextUnformatted(waypointSteps.ToString());
                    else
                        ImGuiNET.ImGui.TextDisabled("-");

                    
                    ImGuiNET.ImGui.TableNextColumn();
                    if (ImGuiNET.ImGui.SmallButton((Settings.WaypointJumpEnabled.Value ? "Jump" : "Jump off") + $"##wpjump_{i}"))
                    {
                        if (Settings.WaypointJumpEnabled.Value)
                            QueueWaypointJumpToCoord(wp.X, wp.Y);
                    }
                    if (ImGuiNET.ImGui.IsItemHovered())
                        ImGuiNET.ImGui.SetTooltip("Pan atlas to this waypoint without moving the mouse.");

                    
                    ImGuiNET.ImGui.TableNextColumn();
                    if (ImGuiNET.ImGui.SmallButton("Remove"))
                    {
                        bool wasSel = wp.Selected;
                        wps.RemoveAt(i);
                        i--;
                        if (wasSel) SyncSelectedWaypoint();
                        ImGuiNET.ImGui.PopID();
                        continue;
                    }

                    ImGuiNET.ImGui.PopID();
                }

                ImGuiNET.ImGui.EndTable();
            }


            ImGuiNET.ImGui.Separator();
            DrawFavoriteMapsPanel();

            ImGuiNET.ImGui.Separator();
            bool showStepsColumn = Settings.DrawShortestPath.Value;
            if (ImGuiNET.ImGui.TreeNodeEx("Atlas Maps", ImGuiNET.ImGuiTreeNodeFlags.DefaultOpen))
            {
                
                ImGuiNET.ImGui.AlignTextToFramePadding();
                ImGuiNET.ImGui.Text("Sort:");
                ImGuiNET.ImGui.SameLine();
                ImGuiNET.ImGui.TextDisabled("Steps");

                ImGuiNET.ImGui.SameLine();
                ImGuiNET.ImGui.Dummy(new Vector2(12, 0));
                ImGuiNET.ImGui.SameLine();

                ImGuiNET.ImGui.AlignTextToFramePadding();
                ImGuiNET.ImGui.Text("Max Items:");
                ImGuiNET.ImGui.SameLine();

                int maxItems = Settings.WaypointAtlasMaxItems.Value;
                ImGuiNET.ImGui.SetNextItemWidth(50);
                if (ImGuiNET.ImGui.InputInt("##max", ref maxItems, 0, 0))
                    Settings.WaypointAtlasMaxItems.Value = Math.Clamp(maxItems, 5, 250);

                
                ImGuiNET.ImGui.SameLine();
                if (ImGuiNET.ImGui.SmallButton("-##max"))
                    Settings.WaypointAtlasMaxItems.Value = Math.Clamp(Settings.WaypointAtlasMaxItems.Value - 5, 5, 250);
                ImGuiNET.ImGui.SameLine();
                if (ImGuiNET.ImGui.SmallButton("+##max"))
                    Settings.WaypointAtlasMaxItems.Value = Math.Clamp(Settings.WaypointAtlasMaxItems.Value + 5, 5, 250);

                ImGuiNET.ImGui.SameLine();
                bool unlockedOnly = Settings.WaypointAtlasUnlockedOnly.Value;
                if (ImGuiNET.ImGui.Checkbox("Unlocked Maps Only", ref unlockedOnly))
                    Settings.WaypointAtlasUnlockedOnly.Value = unlockedOnly;

                
                ImGuiNET.ImGui.Text("Search:");
                ImGuiNET.ImGui.SameLine();
                
                var searchWidth = Math.Max(180f, ImGuiNET.ImGui.GetContentRegionAvail().X * 0.35f);
                ImGuiNET.ImGui.SetNextItemWidth(searchWidth);
                ImGuiNET.ImGui.InputText("##search", ref _atlasSearch, 64);

                DrawNavigatorStepsFilterControls("atlas");

                ImGuiNET.ImGui.Spacing();

                
                var flags2 =
                    ImGuiNET.ImGuiTableFlags.RowBg |
                    ImGuiNET.ImGuiTableFlags.BordersInnerH |
                    ImGuiNET.ImGuiTableFlags.ScrollY |
                    ImGuiNET.ImGuiTableFlags.SizingStretchProp |
                    ImGuiNET.ImGuiTableFlags.Resizable;

                var atlasAvail = ImGuiNET.ImGui.GetContentRegionAvail();
                var tableH = Math.Max(180f, atlasAvail.Y);
                int atlasColumnCount = (_waypointPanelAdvancedMode ? 6 : 4) + (showStepsColumn ? 1 : 0);
                if (ImGuiNET.ImGui.BeginTable("##atlas", atlasColumnCount, flags2, new System.Numerics.Vector2(0, tableH)))
                {
                    using var waypointAtlasProfile = ProfileScope("Render waypoint panel atlas list");
                    ImGuiNET.ImGui.TableSetupColumn("Map", ImGuiNET.ImGuiTableColumnFlags.WidthStretch);
                    ImGuiNET.ImGui.TableSetupColumn("Biome", ImGuiNET.ImGuiTableColumnFlags.WidthFixed, 130);
                    if (showStepsColumn)
                        ImGuiNET.ImGui.TableSetupColumn("Steps", ImGuiNET.ImGuiTableColumnFlags.WidthFixed, 58);
                    if (_waypointPanelAdvancedMode)
                    {
                        ImGuiNET.ImGui.TableSetupColumn("X", ImGuiNET.ImGuiTableColumnFlags.WidthFixed, 45);
                        ImGuiNET.ImGui.TableSetupColumn("Y", ImGuiNET.ImGuiTableColumnFlags.WidthFixed, 45);
                    }
                    ImGuiNET.ImGui.TableSetupColumn("Jump", ImGuiNET.ImGuiTableColumnFlags.WidthFixed, 58);
                    ImGuiNET.ImGui.TableSetupColumn("Track", ImGuiNET.ImGuiTableColumnFlags.WidthFixed, 70);
                    ImGuiNET.ImGui.TableHeadersRow();

                    ProcessWaypointAtlasCacheBudget();
                    if (!_waypointAtlasBuildActive)
                        SortWaypointAtlasRowsBySteps();
                    SyncFavoriteMapWaypointsFromCurrentAtlasRows(removeStale: !_waypointAtlasBuildActive);

                    if (_waypointAtlasBuildActive)
                    {
                        ImGuiNET.ImGui.TableNextRow();
                        ImGuiNET.ImGui.TableNextColumn();
                        ImGuiNET.ImGui.TextDisabled($"Searching... {_waypointAtlasRows.Count} shown");
                        ImGuiNET.ImGui.TableNextColumn();
                        ImGuiNET.ImGui.TextDisabled("cache");
                        if (showStepsColumn)
                        {
                            ImGuiNET.ImGui.TableNextColumn();
                            ImGuiNET.ImGui.TextDisabled("-");
                        }
                        if (_waypointPanelAdvancedMode)
                        {
                            ImGuiNET.ImGui.TableNextColumn();
                            ImGuiNET.ImGui.TextDisabled("-");
                            ImGuiNET.ImGui.TableNextColumn();
                            ImGuiNET.ImGui.TextDisabled("-");
                        }
                        ImGuiNET.ImGui.TableNextColumn();
                        ImGuiNET.ImGui.TextDisabled("...");
                        ImGuiNET.ImGui.TableNextColumn();
                        ImGuiNET.ImGui.TextDisabled("...");
                    }

                    int atlasRowsDrawn = 0;
                    for (int i = 0; i < _waypointAtlasRows.Count && atlasRowsDrawn < Settings.WaypointAtlasMaxItems.Value; i++)
                    {
                        var row = _waypointAtlasRows[i];
                        if (!PassesNavigatorStepsFilter(row.X, row.Y, out _))
                            continue;

                        atlasRowsDrawn++;
                        bool hasWp = false;
                        for (int wi = 0; wi < wps.Count; wi++)
                        {
                            if (wps[wi].X == row.X && wps[wi].Y == row.Y)
                            {
                                hasWp = true;
                                break;
                            }
                        }

                        ImGuiNET.ImGui.PushID(i);
                        ImGuiNET.ImGui.TableNextRow();

                        ImGuiNET.ImGui.TableNextColumn();
                        ImGuiNET.ImGui.TextUnformatted(row.Name);

                        ImGuiNET.ImGui.TableNextColumn();
                        ImGuiNET.ImGui.TextUnformatted(row.Biome);

                        if (showStepsColumn)
                        {
                            ImGuiNET.ImGui.TableNextColumn();
                            if (TryGetAtlasRouteSteps(row.X, row.Y, out var steps))
                                ImGuiNET.ImGui.TextUnformatted(steps.ToString());
                            else
                                ImGuiNET.ImGui.TextDisabled("-");
                        }

                        if (_waypointPanelAdvancedMode)
                        {
                            ImGuiNET.ImGui.TableNextColumn();
                            ImGuiNET.ImGui.TextUnformatted(row.X.ToString());

                            ImGuiNET.ImGui.TableNextColumn();
                            ImGuiNET.ImGui.TextUnformatted(row.Y.ToString());
                        }

                        ImGuiNET.ImGui.TableNextColumn();
                        if (ImGuiNET.ImGui.SmallButton((Settings.WaypointJumpEnabled.Value ? "Jump" : "Jump off") + $"##mapjump_{row.X}_{row.Y}"))
                        {
                            if (Settings.WaypointJumpEnabled.Value)
                                QueueWaypointJumpToCoord(row.X, row.Y);
                        }
                        if (ImGuiNET.ImGui.IsItemHovered())
                            ImGuiNET.ImGui.SetTooltip("Pan atlas to this map without moving the mouse.");

                        ImGuiNET.ImGui.TableNextColumn();
                        if (ImGuiNET.ImGui.SmallButton((hasWp ? "Untrack" : "Track") + $"##maptrack_{row.X}_{row.Y}"))
                        {
                            if (hasWp)
                            {
                                for (int wi = wps.Count - 1; wi >= 0; wi--)
                                    if (wps[wi].X == row.X && wps[wi].Y == row.Y)
                                        wps.RemoveAt(wi);
                                SyncSelectedWaypoint();
                            }
                            else
                            {
                                AddWaypoint(row.Node);
                            }
                        }

                        ImGuiNET.ImGui.PopID();
                    }

                    ImGuiNET.ImGui.EndTable();
                }

                ImGuiNET.ImGui.TreePop();
            }

            ImGuiNET.ImGui.Spacing();
            if (ImGuiNET.ImGui.TreeNodeEx("Mechanic", ImGuiNET.ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGuiNET.ImGui.TextDisabled("Detected Map Content / Mechanics from Special Highlights. Use Search below to filter by map or mechanic name.");

                ImGuiNET.ImGui.AlignTextToFramePadding();
                ImGuiNET.ImGui.Text("Sort:");
                ImGuiNET.ImGui.SameLine();
                ImGuiNET.ImGui.TextDisabled("Steps");

                ImGuiNET.ImGui.SameLine();
                ImGuiNET.ImGui.Dummy(new Vector2(12, 0));
                ImGuiNET.ImGui.SameLine();

                ImGuiNET.ImGui.AlignTextToFramePadding();
                ImGuiNET.ImGui.Text("Max Items:");
                ImGuiNET.ImGui.SameLine();

                int mechanicMaxItems = Settings.WaypointAtlasMaxItems.Value;
                ImGuiNET.ImGui.SetNextItemWidth(50);
                if (ImGuiNET.ImGui.InputInt("##mechanic_max", ref mechanicMaxItems, 0, 0))
                    Settings.WaypointAtlasMaxItems.Value = Math.Clamp(mechanicMaxItems, 5, 250);

                ImGuiNET.ImGui.SameLine();
                if (ImGuiNET.ImGui.SmallButton("-##mechanic_max"))
                    Settings.WaypointAtlasMaxItems.Value = Math.Clamp(Settings.WaypointAtlasMaxItems.Value - 5, 5, 250);
                ImGuiNET.ImGui.SameLine();
                if (ImGuiNET.ImGui.SmallButton("+##mechanic_max"))
                    Settings.WaypointAtlasMaxItems.Value = Math.Clamp(Settings.WaypointAtlasMaxItems.Value + 5, 5, 250);

                ImGuiNET.ImGui.SameLine();
                bool mechanicUnlockedOnly = Settings.WaypointAtlasUnlockedOnly.Value;
                if (ImGuiNET.ImGui.Checkbox("Unlocked Maps Only##mechanic_unlocked", ref mechanicUnlockedOnly))
                    Settings.WaypointAtlasUnlockedOnly.Value = mechanicUnlockedOnly;

                ImGuiNET.ImGui.Text("Search:");
                ImGuiNET.ImGui.SameLine();
                var mechanicSearchWidth = Math.Max(180f, ImGuiNET.ImGui.GetContentRegionAvail().X * 0.35f);
                ImGuiNET.ImGui.SetNextItemWidth(mechanicSearchWidth);
                ImGuiNET.ImGui.InputText("##mechanic_search", ref _mechanicSearch, 64);

                DrawNavigatorStepsFilterControls("mechanic");

                ImGuiNET.ImGui.Spacing();

                    var mechanicFlags =
                        ImGuiNET.ImGuiTableFlags.RowBg |
                        ImGuiNET.ImGuiTableFlags.BordersInnerH |
                        ImGuiNET.ImGuiTableFlags.ScrollY |
                        ImGuiNET.ImGuiTableFlags.SizingStretchProp |
                        ImGuiNET.ImGuiTableFlags.Resizable;

                    var mechanicAvail = ImGuiNET.ImGui.GetContentRegionAvail();
                    var mechanicTableH = Math.Max(160f, mechanicAvail.Y);
                    int mechanicColumnCount = (_waypointPanelAdvancedMode ? 7 : 5) + (showStepsColumn ? 1 : 0);
                    if (ImGuiNET.ImGui.BeginTable("##atlas_mechanics", mechanicColumnCount, mechanicFlags, new Vector2(0, mechanicTableH)))
                    {
                        using var waypointMechanicProfile = ProfileScope("Render waypoint panel mechanic list");
                        ImGuiNET.ImGui.TableSetupColumn("Mechanic", ImGuiNET.ImGuiTableColumnFlags.WidthStretch);
                        ImGuiNET.ImGui.TableSetupColumn("Map", ImGuiNET.ImGuiTableColumnFlags.WidthStretch);
                        ImGuiNET.ImGui.TableSetupColumn("Biome", ImGuiNET.ImGuiTableColumnFlags.WidthFixed, 110);
                        if (showStepsColumn)
                            ImGuiNET.ImGui.TableSetupColumn("Steps", ImGuiNET.ImGuiTableColumnFlags.WidthFixed, 58);
                        if (_waypointPanelAdvancedMode)
                        {
                            ImGuiNET.ImGui.TableSetupColumn("X", ImGuiNET.ImGuiTableColumnFlags.WidthFixed, 45);
                            ImGuiNET.ImGui.TableSetupColumn("Y", ImGuiNET.ImGuiTableColumnFlags.WidthFixed, 45);
                        }
                        ImGuiNET.ImGui.TableSetupColumn("Jump", ImGuiNET.ImGuiTableColumnFlags.WidthFixed, 58);
                        ImGuiNET.ImGui.TableSetupColumn("Track", ImGuiNET.ImGuiTableColumnFlags.WidthFixed, 70);
                        ImGuiNET.ImGui.TableHeadersRow();

                        ProcessWaypointMechanicCacheBudget();
                        if (!_waypointMechanicBuildActive)
                            SortWaypointMechanicRowsBySteps();

                        if (_waypointMechanicBuildActive)
                        {
                            ImGuiNET.ImGui.TableNextRow();
                            ImGuiNET.ImGui.TableNextColumn();
                            ImGuiNET.ImGui.TextDisabled($"Searching... {_waypointMechanicRows.Count} shown");
                            ImGuiNET.ImGui.TableNextColumn();
                            ImGuiNET.ImGui.TextDisabled("cache");
                            ImGuiNET.ImGui.TableNextColumn();
                            ImGuiNET.ImGui.TextDisabled("-");
                            if (showStepsColumn)
                            {
                                ImGuiNET.ImGui.TableNextColumn();
                                ImGuiNET.ImGui.TextDisabled("-");
                            }
                            if (_waypointPanelAdvancedMode)
                            {
                                ImGuiNET.ImGui.TableNextColumn();
                                ImGuiNET.ImGui.TextDisabled("-");
                                ImGuiNET.ImGui.TableNextColumn();
                                ImGuiNET.ImGui.TextDisabled("-");
                            }
                            ImGuiNET.ImGui.TableNextColumn();
                            ImGuiNET.ImGui.TextDisabled("...");
                            ImGuiNET.ImGui.TableNextColumn();
                            ImGuiNET.ImGui.TextDisabled("...");
                        }

                        int mechanicRowsDrawn = 0;
                        for (int i = 0; i < _waypointMechanicRows.Count && mechanicRowsDrawn < Settings.WaypointAtlasMaxItems.Value; i++)
                        {
                            var row = _waypointMechanicRows[i];
                            if (!PassesNavigatorStepsFilter(row.X, row.Y, out _))
                                continue;

                            mechanicRowsDrawn++;
                            bool hasWp = false;
                            for (int wi = 0; wi < wps.Count; wi++)
                            {
                                if (wps[wi].X == row.X && wps[wi].Y == row.Y)
                                {
                                    hasWp = true;
                                    break;
                                }
                            }

                            ImGuiNET.ImGui.PushID($"mechanic_{i}");
                            ImGuiNET.ImGui.TableNextRow();

                            ImGuiNET.ImGui.TableNextColumn();
                            ImGuiNET.ImGui.TextUnformatted(row.Mechanics);

                            ImGuiNET.ImGui.TableNextColumn();
                            ImGuiNET.ImGui.TextUnformatted(row.MapName);

                            ImGuiNET.ImGui.TableNextColumn();
                            ImGuiNET.ImGui.TextUnformatted(row.Biome);

                            if (showStepsColumn)
                            {
                                ImGuiNET.ImGui.TableNextColumn();
                                if (TryGetAtlasRouteSteps(row.X, row.Y, out var steps))
                                    ImGuiNET.ImGui.TextUnformatted(steps.ToString());
                                else
                                    ImGuiNET.ImGui.TextDisabled("-");
                            }

                            if (_waypointPanelAdvancedMode)
                            {
                                ImGuiNET.ImGui.TableNextColumn();
                                ImGuiNET.ImGui.TextUnformatted(row.X.ToString());

                                ImGuiNET.ImGui.TableNextColumn();
                                ImGuiNET.ImGui.TextUnformatted(row.Y.ToString());
                            }

                            ImGuiNET.ImGui.TableNextColumn();
                            if (ImGuiNET.ImGui.SmallButton((Settings.WaypointJumpEnabled.Value ? "Jump" : "Jump off") + $"##mechjump_{row.X}_{row.Y}"))
                            {
                                if (Settings.WaypointJumpEnabled.Value)
                                    QueueWaypointJumpToCoord(row.X, row.Y);
                            }
                            if (ImGuiNET.ImGui.IsItemHovered())
                                ImGuiNET.ImGui.SetTooltip("Pan atlas to this mechanic map without moving the mouse.");

                            ImGuiNET.ImGui.TableNextColumn();
                            if (ImGuiNET.ImGui.SmallButton((hasWp ? "Untrack" : "Track") + $"##mechtrack_{row.X}_{row.Y}"))
                            {
                                if (hasWp)
                                {
                                    for (int wi = wps.Count - 1; wi >= 0; wi--)
                                        if (wps[wi].X == row.X && wps[wi].Y == row.Y)
                                            wps.RemoveAt(wi);
                                    SyncSelectedWaypoint();
                                }
                                else
                                {
                                    var waypointName = string.IsNullOrWhiteSpace(row.Mechanics)
                                        ? row.MapName
                                        : $"{row.MapName} - {row.Mechanics}";
                                    AddWaypoint(row.Node, waypointName);
                                }
                            }
                            if (ImGuiNET.ImGui.IsItemHovered())
                                ImGuiNET.ImGui.SetTooltip("Track/untrack this mechanic map as a waypoint, so arrows and shortest-path routing can target it.");

                            ImGuiNET.ImGui.PopID();
                        }

                        ImGuiNET.ImGui.EndTable();
                    }

                ImGuiNET.ImGui.TreePop();
            }

            ImGuiNET.ImGui.End();
        }

        private static void CenterText(string text)
        {
            var colWidth = ImGuiNET.ImGui.GetColumnWidth();
            var txtWidth = ImGuiNET.ImGui.CalcTextSize(text).X;
            var indent = (colWidth - txtWidth) * 0.5f;
            if (indent > 0) ImGuiNET.ImGui.SetCursorPosX(ImGuiNET.ImGui.GetCursorPosX() + indent);
        }

        private static void DrawWorldDirectionIndicator(Vector2 worldCoord1, Vector2 worldCoord2)
        {
            var direction = worldCoord2 - worldCoord1;
            var distance = direction.Length();
            if (distance < 0.001f) distance = 0.001f;
            direction = Vector2.Normalize(direction);

            CenterText($"{distance / 1000:N0}");
            ImGuiNET.ImGui.Text($"{distance / 1000:N0}");

            ImGuiNET.ImGui.TableNextColumn();
            ImGuiNET.ImGui.TableSetColumnIndex(4);

            var arrowSize = new Vector2(45, 45);
            var center = ImGuiNET.ImGui.GetCursorScreenPos() + arrowSize / 2;

            var drawList = ImGuiNET.ImGui.GetWindowDrawList();
            var arrowEnd = center + direction * (arrowSize.X / 2 - 5);
            drawList.AddLine(center, arrowEnd, ImGuiNET.ImGui.GetColorU32(ImGuiNET.ImGuiCol.Text), 2f);

            var perp = new Vector2(-direction.Y, direction.X);
            drawList.AddTriangleFilled(
                arrowEnd,
                arrowEnd - direction * 8f + perp * 4f,
                arrowEnd - direction * 8f - perp * 4f,
                ImGuiNET.ImGui.GetColorU32(ImGuiNET.ImGuiCol.Text));

            ImGuiNET.ImGui.Dummy(arrowSize);
        }

        
        private void DrawArrow(System.Numerics.Vector2 from, System.Numerics.Vector2 to, int thickness, System.Drawing.Color color, int arrowSize)
        {
            Graphics.DrawLine(from, to, thickness, color);
            var dir = to - from;
            if (dir.Length() < 1) return;
            dir = System.Numerics.Vector2.Normalize(dir);
            var perp = new System.Numerics.Vector2(-dir.Y, dir.X);
            var tip = to;
            var left = tip - dir * arrowSize + perp * (arrowSize * 0.5f);
            var right = tip - dir * arrowSize - perp * (arrowSize * 0.5f);
            Graphics.DrawLine(tip, left, thickness, color);
            Graphics.DrawLine(tip, right, thickness, color);
        }
    }
}
