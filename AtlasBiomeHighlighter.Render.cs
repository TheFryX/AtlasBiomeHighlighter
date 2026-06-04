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
        private const int WaypointCircleSegments = 20;
        private readonly Dictionary<string, Vector2> _labelSizeCache = new(StringComparer.Ordinal);
        private const int LabelSizeCacheMaxEntries = 2048;

        // Stage6: ColorConvertFloat4ToU32 was executed for every ring/marker draw.
        // Colors are setting-derived and repeat heavily, so cache the packed ImGui color.
        private readonly Dictionary<int, uint> _imguiColorCache = new(128);
        private const int ImGuiColorCacheMaxEntries = 512;

        // Stage10: connection rendering is draw-call heavy and used while panning the atlas.
        // Cache screen-space endpoints/status for the current render pass so every connection
        // does not repeatedly query atlas elements, status dictionaries and node lookups.
        private readonly Dictionary<(int x, int y), ConnectionRenderNode> _connectionRenderNodeCache = new(2048);
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

            // Ensure BorderX/BorderY reflect current window/overlay size even if settings changed.
            UpdateViewportSize();

            // Map connections
            try
            {
                if (Settings.DrawMapConnections.Value)
                    using (ProfileScope("Render map connections"))
                    {
                        RenderMapConnections();
                    }
            }
            catch { }

            // Waypoints + shortest path
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

            using (ProfileScope("Render node overlays"))
            {
            foreach (var info in _visibleNodeInfos)
            {
                long __nodeStart = profileRenderSections ? Stopwatch.GetTimestamp() : 0;
                long __filterStart = __nodeStart;
                var nd = info.Node;
                if (nd?.Element is null)
                    continue;

                // User filters (cached flags; no per-frame memory reads).
                if (Settings.HideCompletedMaps.Value && info.Completed) continue;
                if (Settings.HideAttemptedMaps.Value && info.Attempted) continue;
                if (Settings.HideLockedMaps.Value && info.Locked) continue;

                var biome = info.Biome;

                // Specials bypass biome filter.
                bool biomeVisible = biome != Biome.Unknown && Settings.Visible.TryGetValue(biome, out var on) && on.Value;
                var sflags = info.SpecialFlags;
                bool isDeadly = (sflags & Utility.SpecialFlags.DeadlyBoss) != 0;

                bool specialWanted =
                    ((sflags & Utility.SpecialFlags.UniqueMap) != 0 && Settings.HighlightUniqueMaps.Value) ||
                    ((sflags & Utility.SpecialFlags.DeadlyBoss) != 0 && Settings.HighlightDeadlyBoss.Value) ||
                    ((sflags & Utility.SpecialFlags.MomentofZen) != 0 && Settings.HighlightMomentofZen.Value) ||
                    ((sflags & Utility.SpecialFlags.CorruptedNexus) != 0 && Settings.HighlightCorruptedNexus.Value) ||
                    ((sflags & Utility.SpecialFlags.Cleansed) != 0 && Settings.HighlightCleansed.Value) ||
                    ((sflags & Utility.SpecialFlags.AreaContainsAbyss) != 0 && Settings.HighlightAreaContainsAbyss.Value) ||
                    ((sflags & Utility.SpecialFlags.AreaContainsExpedition) != 0 && Settings.HighlightAreaContainsExpedition.Value);

                // Preferred maps (token matching is cache-only; no API calls).
                bool preferredWanted = false;
                string? preferredMatchedToken = null;
                if (Settings.HighlightPreferredMaps.Value && !isDeadly)
                {
                    // Stage4: tokens are built in the incremental visible-cache pass, not in Render().
                    // Render() must only do cheap HashSet lookups.
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
                }

                if (preferredWanted)
                    DebugPreferredMapHit(nd, preferredMatchedToken ?? string.Empty, preferredMatchedToken == null ? null : GetPreferredTag(preferredMatchedToken), info.MapName, biome, sflags);

                bool renderOverlay = biomeVisible || specialWanted || preferredWanted;
                bool renderMapNameLabelOnly =
                    Settings.ShowLabels.Value &&
                    Settings.ShowMapNames.Value &&
                    !string.IsNullOrWhiteSpace(info.MapName);

                // Biome visibility controls rings/highlights, not map-name labels.
                // When Show map names is enabled, users expect every resolved map name to be visible
                // even if that biome is unchecked in the Biomes section.
                if (!renderOverlay && !renderMapNameLabelOnly)
                    continue;

                Settings.Colors.TryGetValue(biome, out var colorNode);

                if (profileRenderSections)
                    renderNodeFilterTicks += Stopwatch.GetTimestamp() - __filterStart;

                long __ringsStart = profileRenderSections ? Stopwatch.GetTimestamp() : 0;
                var baseColor = colorNode?.Value ?? Settings.LabelTextColor.Value;
                var ringColor = Utility.WithOpacity(baseColor, Settings.Opacity.Value);
                // Screen-space position changes while panning/zooming the atlas. Don't cache it; read it from the element each frame.
                var center = new Vector2(info.Node.Element.Center.X, info.Node.Element.Center.Y);
                var radius = Settings.NodeRadius.Value;
                var thickness = Settings.RingThickness.Value;

                if (renderOverlay)
                    DrawCircleFast(center, radius, ringColor, thickness, NodeCircleSegments);

                int extra = 0;

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

                if (profileRenderSections)
                    renderNodeRingsTicks += Stopwatch.GetTimestamp() - __ringsStart;

                if (Settings.ShowLabels.Value)
                {
                    long __labelsStart = profileRenderSections ? Stopwatch.GetTimestamp() : 0;
                    string text;
                    bool labelContainsMapName = false;
                    bool hasMapName = !string.IsNullOrWhiteSpace(info.MapName);

                    if (biomeVisible && hasMapName)
                    {
                        // When a biome is explicitly enabled, the biome label should be descriptive
                        // regardless of the global "Show map names" toggle: "Map Name - Biome".
                        text = $"{info.MapName} - {info.BiomeDisplay}";
                        labelContainsMapName = true;
                    }
                    else if (Settings.PreferMapNameForDeadly.Value &&
                        (sflags & Utility.SpecialFlags.DeadlyBoss) != 0 &&
                        hasMapName)
                    {
                        text = info.MapName!;
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
                            // UX: when only Show map names is enabled, show every resolved map name only.
                            // The label still uses the biome color, but does not append biome text.
                            text = info.MapName!;
                            labelContainsMapName = true;
                        }
                        else
                        {
                            text = info.BiomeDisplay;
                        }
                    }

                    // Behaviour summary:
                    // - Show map names only: "Map Name" for all visible resolved maps.
                    // - Enabled biome: "Map Name - Biome" even if Show map names is disabled.
                    // - Unknown/unresolved map name falls back to the biome text.

                    if (Settings.ShowSpecialTag.Value)
                    {
                        if ((sflags & Utility.SpecialFlags.DeadlyBoss) != 0) text += " [Deadly]";
                        if ((sflags & Utility.SpecialFlags.MomentofZen) != 0) text += " [Moment Of Zen]";
                        if ((sflags & Utility.SpecialFlags.Cleansed) != 0) text += " [Cleansed]";
                        if ((sflags & Utility.SpecialFlags.CorruptedNexus) != 0) text += " [Corrupted]";
                        if ((sflags & Utility.SpecialFlags.AreaContainsAbyss) != 0) text += " [Abyss]";
                        if ((sflags & Utility.SpecialFlags.AreaContainsExpedition) != 0) text += " [Expedition]";
                        if ((sflags & Utility.SpecialFlags.UniqueMap) != 0 && !(Settings.ShowUniqueNameOnLabel.Value)) text += " [Unique]";
                        if (preferredWanted) text += " " + GetPreferredTag(preferredMatchedToken);
                    }

                    var size = MeasureTextCached(text);
                    var offsetY = labelContainsMapName ? Settings.MapNameOffsetY.Value : Settings.LabelOffset.Value;
                    var pos = new Vector2(center.X - size.X / 2f, center.Y - (radius + offsetY));

                    // Apply Label Settings.
                    var textColor = Settings.LabelUseBiomeColor.Value ? ringColor : Settings.LabelTextColor.Value;

                    // Darken unreached nodes when showing map-name labels.
                    if (labelContainsMapName && !(info.Visited || info.Unlocked))
                    {
                        textColor = System.Drawing.Color.FromArgb(
                            textColor.A,
                            (int)(textColor.R * 0.55f),
                            (int)(textColor.G * 0.55f),
                            (int)(textColor.B * 0.55f));
                    }

                    // Draw through the shared helper so long labels such as Preferred-map tags
                    // are pixel-snapped before fake-bold is applied. This avoids the doubled/garbled
                    // look caused by rendering the same long text at fractional X positions.
                    DrawTextWithLabelSettings(text, pos, textColor);
                    if (profileRenderSections)
                        renderNodeLabelsTicks += Stopwatch.GetTimestamp() - __labelsStart;
                }

                if (profileRenderSections)
                    renderNodeTotalTicks += Stopwatch.GetTimestamp() - __nodeStart;
            }
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
                using (ProfileScope("Render preferred guides"))
                {
                    RenderPreferredGuides();
                }
            }
            catch { /* keep overlay alive */ }

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
            var drawList = ImGui.GetForegroundDrawList();
            uint unlockedPackedColor = GetCachedImGuiColor(unlockedColor);
            uint lockedPackedColor = GetCachedImGuiColor(lockedColor);

            float minX = -ConnectionViewportPadding;
            float minY = -ConnectionViewportPadding;
            float maxX = displaySize.X + ConnectionViewportPadding;
            float maxY = displaySize.Y + ConnectionViewportPadding;

            if (diagnostics) setupTicks = Stopwatch.GetTimestamp() - setupStart;
            long loopStart = diagnostics ? Stopwatch.GetTimestamp() : 0;

            // Stage11: the expensive visible-node seed, neighbor walking and duplicate removal
            // are done when the visible cache is published. Render only draws prepared segments.
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
            LogMessage(
                $"[AtlasBiomeHighlighter] Connections diagnostics: total={totalMs:F2} ms, prepared={preparedSegments}, drawn={drawnConnections}, visitedSkip={visitedSkipped}, offscreenSkip={offscreenSkipped}, tinySkip={tinySkipped}, staleSkip={staleSkipped}",
                3);
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

                // Small "flag" marker (simple and cheap) to make waypoints obvious even when zoomed out.
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
                if (!_nodeByCoord.TryGetValue((wp.X, wp.Y), out var nd) || nd?.Element is null)
                    continue;

                var target = new Vector2(nd.Element.Center.X, nd.Element.Center.Y);
                var colorArrow = Color.FromArgb(wp.ColorArgb);

                // If the waypoint is on screen, show a small label near it and continue.
                if (target.X > 0 && target.X < w && target.Y > 0 && target.Y < h)
                {
                    // De-clutter: if the main overlay would already draw the map-name label for this node,
                    // do not draw a second (identical) waypoint label on top of it.
                    // Important: when biomes are disabled, the main overlay may NOT render the node at all.
                    // In that case, we still want a label for the selected waypoint.
                    if (WouldMainOverlayRenderMapNameLabel(nd))
                        continue;

                    // If the main overlay is not drawing a map-name label for this node (e.g. biomes disabled),
                    // draw waypoint labels for any waypoints that have labels enabled.
                    if (!wp.ShowLabel)
                        continue;

                    // Use the same styling as the main overlay "Label settings".
                    // Display the map name above the node (consistent with "Show map names" labels).
                    var label = nd.Element.Area?.Name;
                    if (string.IsNullOrWhiteSpace(label) && Utility.TryGetAnyMapName(nd, out var nm))
                        label = nm;
                    if (string.IsNullOrWhiteSpace(label))
                        label = wp.Name;

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

                // Draw an arrow clamped to the screen edges pointing toward the waypoint.
                var centerScreen = new Vector2(w * 0.5f, h * 0.5f);
                var dir = target - centerScreen;
                if (dir.LengthSquared() < 0.001f) continue;
                dir = Vector2.Normalize(dir);

                float margin = 30f;
                var edge = new Vector2(
                    Clamp(centerScreen.X + dir.X * 99999f, margin, w - margin),
                    Clamp(centerScreen.Y + dir.Y * 99999f, margin, h - margin));

                // Better clamp: intersect ray with rectangle.
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

                var back = edge - dir * 20f;
                var perp = new Vector2(-dir.Y, dir.X);
                var p1 = edge;
                var p2 = back + perp * 10f;
                var p3 = back - perp * 10f;

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
            // ExileCore2 Graphics doesn't expose filled triangle helpers in all builds.
            // Use ImGui foreground draw list for a cheap filled triangle.
            var dl = ImGui.GetForegroundDrawList();
            var col = ToImGuiColor(color);
            dl.AddTriangleFilled(p1, p2, p3, col);
        }

        private static uint ToImGuiColor(Color color)
        {
            return ImGui.ColorConvertFloat4ToU32(new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f));
        }

        private void DrawCircleFast(Vector2 center, float radius, Color color, float thickness, int segments)
        {
            // Stage5/6: ImGui draw-list circles are cheaper than Graphics.DrawCircle for large atlas overlays.
            // Keep segment count modest; the atlas node radius is small, so 12-20 segments is visually stable.
            // Stage6 additionally caches packed color conversion; ring rendering can call this many times per frame.
            if (radius <= 0f || thickness <= 0f || color.A <= 0)
                return;

            var dl = ImGui.GetForegroundDrawList();
            dl.AddCircle(center, radius, GetCachedImGuiColor(color), segments, thickness);
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
            // Match the main overlay label look: centered, outlined, optional bold.
            // Note: Graphics.MeasureText is available across ExileCore2 builds, but font size is controlled by the game/UI.
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
            // Graphics.DrawText does not like fake-bold on long fractional-position strings.
            // Snapping keeps labels stable when Preferred tags make the text much wider.
            pos = SnapTextPos(pos);

            if (Settings.LabelOutline.Value)
            {
                // Stage5: the old square outline rendered (2t+1)^2-1 shadow draws per label.
                // With the default thickness 2 that was 24 extra DrawText calls per label.
                // A radial 8-direction outline keeps readability while cutting thick outlines by ~3x.
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
                // Vertical fake-bold is less destructive for ExileCore's atlas font than drawing
                // the same long text at X+1, which can split thin glyphs apart.
                Graphics.DrawText(text, new Vector2(pos.X, pos.Y + 1), textColor);
            }

            Graphics.DrawText(text, pos, textColor);
        }

        private void RenderShortestPath()
        {
            if (_shortestPath.Count < 2) return;
            var color = Settings.ShortestPathColor.Value;
            int thickness = Settings.ShortestPathThickness.Value;

            Vector2 prevPos = default;
            bool hasPrev = false;

            for (int i = 0; i < _shortestPath.Count; i++)
            {
                if (!_nodeByCoord.TryGetValue(_shortestPath[i], out var nd) || nd?.Element is null) { hasPrev = false; continue; }
                var pos = new Vector2(nd.Element.Center.X, nd.Element.Center.Y);
                if (hasPrev)
                    Graphics.DrawLine(prevPos, pos, thickness, color);
                prevPos = pos;
                hasPrev = true;
            }

            // Step label near target.
            var last = _shortestPath[^1];
            if (_nodeByCoord.TryGetValue(last, out var lastNd) && lastNd?.Element is not null)
            {
                var p = new Vector2(lastNd.Element.Center.X, lastNd.Element.Center.Y);
                var label = $"{_shortestPath.Count - 1} steps";
                Graphics.DrawText(label, p + new Vector2(10, 10), color);
            }
        }

        /// <summary>
        /// Returns true if the main biome overlay loop would render the map-name label for this node.
        /// This is used to avoid double-rendering text when waypoints are enabled.
        /// </summary>
        private bool WouldMainOverlayRenderMapNameLabel(AtlasNodeDescription nd)
        {
            if (!(Settings.ShowLabels.Value && Settings.ShowMapNames.Value))
                return false;

            // Mirror the same visibility gating as the main overlay loop.
            if (Settings.HideCompletedMaps.Value && Utility.IsMapCompleted(nd))
                return false;
            if (Settings.HideAttemptedMaps.Value && Utility.IsMapAttempted(nd))
                return false;
            if (Settings.HideLockedMaps.Value && Utility.IsMapLocked(nd))
                return false;

            var biome = Utility.TryGetBiome(nd);

            // Specials bypass biome filter
            bool biomeVisible = biome != Biome.Unknown && Settings.Visible.TryGetValue(biome, out var on) && on.Value;
            var sflags = Utility.TryGetSpecialFlags(nd);
            bool isDeadly = (sflags & Utility.SpecialFlags.DeadlyBoss) != 0;
            bool specialWanted =
                ((sflags & Utility.SpecialFlags.UniqueMap) != 0 && Settings.HighlightUniqueMaps.Value) ||
                ((sflags & Utility.SpecialFlags.DeadlyBoss) != 0 && Settings.HighlightDeadlyBoss.Value) ||
                ((sflags & Utility.SpecialFlags.MomentofZen) != 0 && Settings.HighlightMomentofZen.Value) ||
                ((sflags & Utility.SpecialFlags.CorruptedNexus) != 0 && Settings.HighlightCorruptedNexus.Value) ||
                ((sflags & Utility.SpecialFlags.Cleansed) != 0 && Settings.HighlightCleansed.Value) ||
                ((sflags & Utility.SpecialFlags.AreaContainsAbyss) != 0 && Settings.HighlightAreaContainsAbyss.Value) ||
                ((sflags & Utility.SpecialFlags.AreaContainsExpedition) != 0 && Settings.HighlightAreaContainsExpedition.Value);

            bool preferredWanted = false;
            if (Settings.HighlightPreferredMaps.Value && !isDeadly && TryGetCachedNodeTokens(nd, out var nameToken, out var idToken))
            {
                // Exact-only matching prevents short map names from matching longer ones.
                preferredWanted =
                    (nameToken.Length != 0 && _preferredTokensExact.Contains(nameToken)) ||
                    (idToken.Length != 0 && _preferredTokensExact.Contains(idToken));
            }

            // Biome visibility controls rings/highlights, not map-name labels.
            // The main overlay now renders resolved map names even when the biome is unchecked.
            if (Utility.TryGetAnyMapName(nd, sflags, out var mapName) && !string.IsNullOrWhiteSpace(mapName))
                return true;

            if (!biomeVisible && !(specialWanted || preferredWanted))
                return false;

            // Highlight overlays still require a biome color entry to proceed.
            return Settings.Colors.ContainsKey(biome);
        }

        private void RenderTowerRange()
        {
            if (!TryGetTowerRangeOrigin(out var origin)) return;
            if (!_nodeByCoord.TryGetValue((origin.X, origin.Y), out var originNd) || originNd?.Element is null) return;


            const int range = 11;
            var col = Settings.TowerRangeColor.Value;
            if (!Settings.DrawTowerRange.Value) return;
            var originPos = new Vector2(originNd.Element.Center.X, originNd.Element.Center.Y);

            // Behavior:
            // - If origin is a tower: show maps in its reach.
            // - If origin is a map: show towers that can reach it.
            bool originIsTower = IsTower(originNd.Element);


            if (!originIsTower && originNd.Element.IsVisited)
                return;

            int count = 0;
            foreach (var kv in _nodeByCoord)
            {
                var nd = kv.Value;
                if (nd?.Element is null) continue;
                if (!TryGetCoordinate(nd, out var c)) continue;

                // Skip origin.
                if (c.X == origin.X && c.Y == origin.Y) continue;

                if (Distance(origin, c) > range) continue;

                bool isTower = IsTower(nd.Element);
                if (originIsTower)
                {
                    if (isTower) continue; // tower -> show maps only
                    // Skip the tower's own node name variants.
                    if (nd.Element.Area?.Name?.Equals("Lost Towers", StringComparison.OrdinalIgnoreCase) == true)
                        continue;

                    if (nd.Element.IsVisited) continue;
                }
                else
                {
                    if (!isTower) continue; // map -> show towers only

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
                   processed < WaypointAtlasBuildBudgetPerFrame &&
                   _waypointAtlasRows.Count < maxItems)
            {
                var nd = _atlasNodes[_waypointAtlasBuildIndex++];
                processed++;

                if (nd?.Element is null) continue;
                if (IsTower(nd.Element)) continue;

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

            if (_waypointAtlasBuildIndex >= _atlasNodes.Length || _waypointAtlasRows.Count >= maxItems)
                _waypointAtlasBuildActive = false;
        }

        private void RenderWaypointPanel()
        {

            if (!_waypointPanelOpen) return;
            if (!Settings.WaypointsEnabled.Value) return;

            // Allow user resizing; keep a sensible initial size.
            ImGuiNET.ImGui.SetNextWindowSize(new Vector2(780, 520), ImGuiNET.ImGuiCond.FirstUseEver);
            ImGuiNET.ImGui.SetNextWindowSizeConstraints(new Vector2(540, 320), new Vector2(float.MaxValue, float.MaxValue));

            var flags = ImGuiNET.ImGuiWindowFlags.None;
            if (!ImGuiNET.ImGui.Begin("Atlas Waypoints", ref _waypointPanelOpen, flags))
            {
                ImGuiNET.ImGui.End();
                return;
            }

            ImGuiNET.ImGui.TextDisabled("Hotkeys: Insert add, Delete remove, End toggle window");
            bool showWp = Settings.ShowWaypointsOnAtlas.Value;
            if (ImGuiNET.ImGui.Checkbox("Show Waypoints on Atlas", ref showWp))
                Settings.ShowWaypointsOnAtlas.Value = showWp;
            ImGuiNET.ImGui.SameLine();
            bool showArr = Settings.ShowWaypointArrowsOnAtlas.Value;
            if (ImGuiNET.ImGui.Checkbox("Show Waypoint Arrows on Atlas", ref showArr))
                Settings.ShowWaypointArrowsOnAtlas.Value = showArr;
            ImGuiNET.ImGui.Spacing();

            var wps = Settings.Waypoints;

            // Top actions.
            if (ImGuiNET.ImGui.Button("Clear All"))
            {
                wps.Clear();
                _selectedWaypointCoord = null;
                _shortestPath.Clear();
            }
            ImGuiNET.ImGui.SameLine();
            ImGuiNET.ImGui.TextDisabled($"Count: {wps.Count}");

            ImGuiNET.ImGui.Separator();

            // Waypoints list (top). Stretch with window, scroll when needed.
            var wpsTableFlags =
                ImGuiNET.ImGuiTableFlags.RowBg |
                ImGuiNET.ImGuiTableFlags.BordersInnerH |
                ImGuiNET.ImGuiTableFlags.ScrollY |
                ImGuiNET.ImGuiTableFlags.SizingStretchProp |
                ImGuiNET.ImGuiTableFlags.Resizable;

            // Keep this list compact but responsive to window size.
            var wpsAvail = ImGuiNET.ImGui.GetContentRegionAvail();
            var wpsTableH = Math.Min(Math.Max(140f, ImGuiNET.ImGui.GetTextLineHeightWithSpacing() * 10f), Math.Max(160f, wpsAvail.Y * 0.35f));

            if (ImGuiNET.ImGui.BeginTable("##wps", 7, wpsTableFlags, new Vector2(0, wpsTableH)))
            {
                using var waypointListProfile = ProfileScope("Render waypoint panel saved list");
                ImGuiNET.ImGui.TableSetupColumn("Sel", ImGuiNET.ImGuiTableColumnFlags.WidthFixed, 28);
                ImGuiNET.ImGui.TableSetupColumn("Lbl", ImGuiNET.ImGuiTableColumnFlags.WidthFixed, 28);
                ImGuiNET.ImGui.TableSetupColumn("Color", ImGuiNET.ImGuiTableColumnFlags.WidthFixed, 44);
                ImGuiNET.ImGui.TableSetupColumn("Name", ImGuiNET.ImGuiTableColumnFlags.WidthStretch);
                ImGuiNET.ImGui.TableSetupColumn("Coord", ImGuiNET.ImGuiTableColumnFlags.WidthFixed, 80);
                ImGuiNET.ImGui.TableSetupColumn("Towers", ImGuiNET.ImGuiTableColumnFlags.WidthFixed, 55);
                ImGuiNET.ImGui.TableSetupColumn("Del", ImGuiNET.ImGuiTableColumnFlags.WidthFixed, 35);
                ImGuiNET.ImGui.TableHeadersRow();

                for (int i = 0; i < wps.Count; i++)
                {
                    var wp = wps[i];
                    ImGuiNET.ImGui.PushID(i);
                    ImGuiNET.ImGui.TableNextRow();

                    // Selected
                    ImGuiNET.ImGui.TableNextColumn();
                    bool selected = wp.Selected;
                    if (ImGuiNET.ImGui.RadioButton("##sel", selected))
                    {
                        for (int j = 0; j < wps.Count; j++) wps[j].Selected = false;
                        wp.Selected = true;
                        wps[i] = wp;
                        _selectedWaypointCoord = (wp.X, wp.Y);
                    }

                    // Label
                    ImGuiNET.ImGui.TableNextColumn();
                    bool showLabel = wp.ShowLabel;
                    if (ImGuiNET.ImGui.Checkbox("##lbl", ref showLabel))
                    {
                        wp.ShowLabel = showLabel;
                        wps[i] = wp;
                    }

                    // Color
                    ImGuiNET.ImGui.TableNextColumn();
                    var c = System.Drawing.Color.FromArgb(wp.ColorArgb);
                    var vec = new Vector4(c.R / 255f, c.G / 255f, c.B / 255f, 1f);
                    // Square-only picker.
                    ImGuiNET.ImGui.SetNextItemWidth(ImGuiNET.ImGui.GetFrameHeight());
                    if (ImGuiNET.ImGui.ColorEdit4("##c", ref vec, ImGuiNET.ImGuiColorEditFlags.NoInputs | ImGuiNET.ImGuiColorEditFlags.NoLabel))
                    {
                        var nc = System.Drawing.Color.FromArgb((int)(vec.X * 255), (int)(vec.Y * 255), (int)(vec.Z * 255));
                        wp.ColorArgb = nc.ToArgb();
                        wps[i] = wp;
                    }

                    // Name
                    ImGuiNET.ImGui.TableNextColumn();
                    var name = wp.Name ?? string.Empty;
                    ImGuiNET.ImGui.SetNextItemWidth(-1);
                    if (ImGuiNET.ImGui.InputText("##name", ref name, 64))
                    {
                        wp.Name = name;
                        wps[i] = wp;
                    }

                    // Coord
                    ImGuiNET.ImGui.TableNextColumn();
                    ImGuiNET.ImGui.Text($"{wp.X},{wp.Y}");

                    // Towers
                    ImGuiNET.ImGui.TableNextColumn();
                    ImGuiNET.ImGui.Text(wp.TowersCount.ToString());

                    // Delete
                    ImGuiNET.ImGui.TableNextColumn();
                    if (ImGuiNET.ImGui.SmallButton("X"))
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
            if (ImGuiNET.ImGui.TreeNodeEx("Atlas", ImGuiNET.ImGuiTreeNodeFlags.DefaultOpen))
            {
                // Controls row
                ImGuiNET.ImGui.AlignTextToFramePadding();
                ImGuiNET.ImGui.Text("Sort:");
                ImGuiNET.ImGui.SameLine();
                ImGuiNET.ImGui.TextDisabled("Name");

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

                // Dedicated +/- buttons to avoid text merging at narrow widths.
                ImGuiNET.ImGui.SameLine();
                if (ImGuiNET.ImGui.SmallButton("-##max"))
                    Settings.WaypointAtlasMaxItems.Value = Math.Clamp(Settings.WaypointAtlasMaxItems.Value - 5, 5, 250);
                ImGuiNET.ImGui.SameLine();
                if (ImGuiNET.ImGui.SmallButton("+##max"))
                    Settings.WaypointAtlasMaxItems.Value = Math.Clamp(Settings.WaypointAtlasMaxItems.Value + 5, 5, 250);

                ImGuiNET.ImGui.SameLine();
                bool unlockedOnly = Settings.WaypointAtlasUnlockedOnly.Value;
                if (ImGuiNET.ImGui.Checkbox("Show Unlocked Maps Only", ref unlockedOnly))
                    Settings.WaypointAtlasUnlockedOnly.Value = unlockedOnly;

                // Search
                ImGuiNET.ImGui.Text("Search:");
                ImGuiNET.ImGui.SameLine();
                // Stretch search box with window.
                var searchWidth = Math.Max(180f, ImGuiNET.ImGui.GetContentRegionAvail().X * 0.35f);
                ImGuiNET.ImGui.SetNextItemWidth(searchWidth);
                ImGuiNET.ImGui.InputText("##search", ref _atlasSearch, 64);

                ImGuiNET.ImGui.Spacing();

                // Atlas table (bottom). Fill remaining space.
                var flags2 =
                    ImGuiNET.ImGuiTableFlags.RowBg |
                    ImGuiNET.ImGuiTableFlags.BordersInnerH |
                    ImGuiNET.ImGuiTableFlags.ScrollY |
                    ImGuiNET.ImGuiTableFlags.SizingStretchProp |
                    ImGuiNET.ImGuiTableFlags.Resizable;

                var atlasAvail = ImGuiNET.ImGui.GetContentRegionAvail();
                var tableH = Math.Max(220f, atlasAvail.Y);
                if (ImGuiNET.ImGui.BeginTable("##atlas", 5, flags2, new System.Numerics.Vector2(0, tableH)))
                {
                    using var waypointAtlasProfile = ProfileScope("Render waypoint panel atlas list");
                    ImGuiNET.ImGui.TableSetupColumn("Map Name", ImGuiNET.ImGuiTableColumnFlags.WidthStretch);
                    ImGuiNET.ImGui.TableSetupColumn("Biome", ImGuiNET.ImGuiTableColumnFlags.WidthFixed, 130);
                    ImGuiNET.ImGui.TableSetupColumn("X", ImGuiNET.ImGuiTableColumnFlags.WidthFixed, 45);
                    ImGuiNET.ImGui.TableSetupColumn("Y", ImGuiNET.ImGuiTableColumnFlags.WidthFixed, 45);
                    ImGuiNET.ImGui.TableSetupColumn("Way", ImGuiNET.ImGuiTableColumnFlags.WidthFixed, 55);
                    ImGuiNET.ImGui.TableHeadersRow();

                    ProcessWaypointAtlasCacheBudget();

                    if (_waypointAtlasBuildActive)
                    {
                        ImGuiNET.ImGui.TableNextRow();
                        ImGuiNET.ImGui.TableNextColumn();
                        ImGuiNET.ImGui.TextDisabled($"Searching... {_waypointAtlasRows.Count} shown");
                        ImGuiNET.ImGui.TableNextColumn();
                        ImGuiNET.ImGui.TextDisabled("cache");
                        ImGuiNET.ImGui.TableNextColumn();
                        ImGuiNET.ImGui.TextDisabled("-");
                        ImGuiNET.ImGui.TableNextColumn();
                        ImGuiNET.ImGui.TextDisabled("-");
                        ImGuiNET.ImGui.TableNextColumn();
                        ImGuiNET.ImGui.TextDisabled("...");
                    }

                    for (int i = 0; i < _waypointAtlasRows.Count; i++)
                    {
                        var row = _waypointAtlasRows[i];
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

                        ImGuiNET.ImGui.TableNextColumn();
                        ImGuiNET.ImGui.TextUnformatted(row.X.ToString());

                        ImGuiNET.ImGui.TableNextColumn();
                        ImGuiNET.ImGui.TextUnformatted(row.Y.ToString());

                        ImGuiNET.ImGui.TableNextColumn();
                        if (ImGuiNET.ImGui.SmallButton(hasWp ? "Del" : "Way"))
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

        // PREFERRED_GUIDES
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
