using System;
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
        public override void Render()
        {
            if (!Settings.Enable.Value) return;
            if (_atlasPanel == null || !_atlasPanel.IsVisible) return;

            // Ensure BorderX/BorderY reflect current window/overlay size even if settings changed.
            UpdateViewportSize();

            // Keep preferred-map matching caches hot-path allocation-free.
            EnsurePreferredCacheUpToDate();

            // Preferred map directional guides
            try
            {
                if (Settings.HighlightPreferredMaps.Value && Settings.PreferredGuideLines.Value)
                {
                    var origin = new System.Numerics.Vector2(BorderX / 2f, BorderY / 2f); // AtlasPanel.Element.Center unavailable in this API; use screen center
                    var ringColor = Settings.PreferredMapRingColor.Value;
                    int drawn = 0;

                    foreach (var nd in _atlasNodes)
                    {
                        if (drawn >= Settings.PreferredGuideLimit.Value) break;
                        if (nd?.Element is null) continue;

                        // Determine if preferred using cached tokens (no per-frame string allocations).
                        if (!TryGetCachedNodeTokens(nd, out var nameToken, out _)) continue;
                        if (nameToken.Length == 0) continue;

                        bool match = _preferredTokensExact.Contains(nameToken);
                        if (!match)
                        {
                            for (int i = 0; i < _preferredTokensList.Length; i++)
                            {
                                if (Utility.TokenContainsEitherWay(nameToken, _preferredTokensList[i])) { match = true; break; }
                            }
                        }
                        if (!match) continue;

                    if (Settings.HideCompletedMaps.Value && Utility.IsMapCompleted(nd)) continue;
                    if (Settings.HideAttemptedMaps.Value && Utility.IsMapAttempted(nd)) continue;
                    if (Settings.HideLockedMaps.Value && Utility.IsMapLocked(nd)) continue;
                        // Skip offscreen/onscreen based on setting
                        var pos = new Vector2(nd.Element.Center.X, nd.Element.Center.Y);
                        bool onScreen = pos.X > 0 && pos.X < BorderX && pos.Y > 0 && pos.Y < BorderY;
                        if (Settings.PreferredGuideOnlyOffscreen.Value && onScreen) continue;

                        // If offscreen, clamp endpoint to screen bounds
                        var to = pos;
                        if (!onScreen)
                        {
                            var dir = System.Numerics.Vector2.Normalize(pos - origin);
                            // clamp to edge leaving margin
                            float margin = 8f;
                            float x = dir.X > 0 ? BorderX - margin : margin;
                            float y = origin.Y + dir.Y * 10000f; // extend long then clamp
                            // compute intersection with screen rectangle
                            var end = origin + dir * 10000f;
                            // clamp line end to screen rect
                            float t = 10000f;
                            // Intersections with 4 sides
                            if (dir.X != 0)
                            {
                                float tx = ((dir.X > 0 ? (BorderX - margin) : margin) - origin.X) / dir.X;
                                t = (float)System.Math.Min(t, System.Math.Max(0.0, tx));
                            }
                            if (dir.Y != 0)
                            {
                                float ty = ((dir.Y > 0 ? (BorderY - margin) : margin) - origin.Y) / dir.Y;
                                t = (float)System.Math.Min(t, System.Math.Max(0.0, ty));
                            }
                            to = origin + dir * t;
                        }

                        DrawArrow(origin, to, Settings.PreferredGuideThickness.Value, ringColor, Settings.PreferredArrowSize.Value);
                        drawn++;
                    }
                }
            }
            catch
            {
                /* never break base overlay */
            }

            // Map connections
            try
            {
                if (Settings.DrawMapConnections.Value)
                    RenderMapConnections();
            }
            catch { }

            // Waypoints + shortest path
            try
            {
                if (Settings.WaypointsEnabled.Value)
                    RenderWaypoints();
                    RenderWaypointArrows();
                if (Settings.DrawShortestPath.Value)
                    RenderShortestPath();
                if (Settings.DrawTowerRange.Value)
                    RenderTowerRange();
            }
            catch { }

            foreach (var info in _visibleNodeInfos)
            {
                var nd = info.Node;
                if (nd?.Element is null)
                    continue;

                // User filters (cached flags; no per-frame memory reads).
                if (Settings.HideCompletedMaps.Value && info.Completed) continue;
                if (Settings.HideAttemptedMaps.Value && info.Attempted) continue;
                if (Settings.HideLockedMaps.Value && info.Locked) continue;

                var biome = info.Biome;

                // Specials bypass biome filter.
                bool biomeVisible = Settings.Visible.TryGetValue(biome, out var on) && on.Value;
                var sflags = info.SpecialFlags;
                bool isDeadly = (sflags & Utility.SpecialFlags.DeadlyBoss) != 0;

                bool specialWanted =
                    ((sflags & Utility.SpecialFlags.UniqueMap) != 0 && Settings.HighlightUniqueMaps.Value) ||
                    ((sflags & Utility.SpecialFlags.DeadlyBoss) != 0 && Settings.HighlightDeadlyBoss.Value) ||
                    ((sflags & Utility.SpecialFlags.AbyssOverrun) != 0 && Settings.HighlightAbyssOverrun.Value) ||
                    ((sflags & Utility.SpecialFlags.MomentofZen) != 0 && Settings.HighlightMomentofZen.Value) ||
                    ((sflags & Utility.SpecialFlags.CorruptedNexus) != 0 && Settings.HighlightCorruptedNexus.Value) ||
                    ((sflags & Utility.SpecialFlags.Cleansed) != 0 && Settings.HighlightCleansed.Value);

                // Preferred maps (token matching is cache-only; no API calls).
                bool preferredWanted = false;
                string? preferredMatchedToken = null;
                if (Settings.HighlightPreferredMaps.Value && !isDeadly && TryGetCachedNodeTokens(nd, out var nameToken2, out var idToken2))
                {
                    if (nameToken2.Length != 0 && _preferredTokensExact.Contains(nameToken2))
                    {
                        preferredWanted = true;
                        preferredMatchedToken = nameToken2;
                    }
                    else
                    {
                        for (int i = 0; i < _preferredTokensList.Length; i++)
                        {
                            var keyToken = _preferredTokensList[i];
                            if (keyToken.Length == 0) continue;

                            if (nameToken2.Length != 0 && Utility.TokenContainsEitherWay(nameToken2, keyToken))
                            {
                                preferredWanted = true;
                                preferredMatchedToken = keyToken;
                                break;
                            }

                            if (!preferredWanted && idToken2.Length != 0 && idToken2.Contains(keyToken, StringComparison.Ordinal))
                            {
                                preferredWanted = true;
                                preferredMatchedToken = keyToken;
                                break;
                            }
                        }
                    }
                }

                if (!biomeVisible && !(specialWanted || preferredWanted))
                    continue;
                if (!Settings.Colors.TryGetValue(biome, out var colorNode))
                    continue;

                var ringColor = Utility.WithOpacity(colorNode.Value, Settings.Opacity.Value);
                // Screen-space position changes while panning/zooming the atlas. Don't cache it; read it from the element each frame.
                var center = new Vector2(info.Node.Element.Center.X, info.Node.Element.Center.Y);
                var radius = Settings.NodeRadius.Value;
                var thickness = Settings.RingThickness.Value;

                Graphics.DrawCircle(center, radius, ringColor, thickness, 24);

                int extra = 0;

                if (preferredWanted)
                {
                    var c = Utility.WithOpacity(Settings.PreferredMapRingColor.Value, Settings.Opacity.Value * Settings.SpecialAlphaMultiplier.Value);
                    Graphics.DrawCircle(center, radius + (++extra) * 2, c, Settings.SpecialRingThickness.Value, 24);
                }

                if ((sflags & Utility.SpecialFlags.UniqueMap) != 0 && Settings.HighlightUniqueMaps.Value)
                {
                    var c = Utility.WithOpacity(Settings.UniqueMapRingColor.Value, Settings.Opacity.Value * Settings.SpecialAlphaMultiplier.Value);
                    Graphics.DrawCircle(center, radius + (++extra) * 2, c, Settings.SpecialRingThickness.Value, 24);
                }
                if ((sflags & Utility.SpecialFlags.DeadlyBoss) != 0 && Settings.HighlightDeadlyBoss.Value)
                {
                    var c = Utility.WithOpacity(Settings.DeadlyBossRingColor.Value, Settings.Opacity.Value * Settings.SpecialAlphaMultiplier.Value);
                    Graphics.DrawCircle(center, radius + (++extra) * 2, c, Settings.SpecialRingThickness.Value, 24);
                }
                if ((sflags & Utility.SpecialFlags.AbyssOverrun) != 0 && Settings.HighlightAbyssOverrun.Value)
                {
                    var c = Utility.WithOpacity(Settings.AbyssOverrunRingColor.Value, Settings.Opacity.Value * Settings.SpecialAlphaMultiplier.Value);
                    Graphics.DrawCircle(center, radius + (++extra) * 2, c, Settings.SpecialRingThickness.Value, 24);
                }
                if ((sflags & Utility.SpecialFlags.MomentofZen) != 0 && Settings.HighlightMomentofZen.Value)
                {
                    var c = Utility.WithOpacity(Settings.MomentofZenRingColor.Value, Settings.Opacity.Value * Settings.SpecialAlphaMultiplier.Value);
                    Graphics.DrawCircle(center, radius + (++extra) * 2, c, Settings.SpecialRingThickness.Value, 24);
                }
                if ((sflags & Utility.SpecialFlags.CorruptedNexus) != 0 && Settings.HighlightCorruptedNexus.Value)
                {
                    var c = Utility.WithOpacity(Settings.CorruptedNexusRingColor.Value, Settings.Opacity.Value * Settings.SpecialAlphaMultiplier.Value);
                    Graphics.DrawCircle(center, radius + (++extra) * 2, c, Settings.SpecialRingThickness.Value, 24);
                }
                if ((sflags & Utility.SpecialFlags.Cleansed) != 0 && Settings.HighlightCleansed.Value)
                {
                    var c = Utility.WithOpacity(Settings.CleansedRingColor.Value, Settings.Opacity.Value * Settings.SpecialAlphaMultiplier.Value);
                    Graphics.DrawCircle(center, radius + (++extra) * 2, c, Settings.SpecialRingThickness.Value, 24);
                }

                if (Settings.ShowLabels.Value)
                {
                    string text;

                    if (Settings.PreferMapNameForDeadly.Value &&
                        (sflags & Utility.SpecialFlags.DeadlyBoss) != 0 &&
                        !string.IsNullOrWhiteSpace(info.MapName))
                    {
                        text = info.MapName!;
                    }
                    else if (Settings.ShowUniqueNameOnLabel.Value &&
                             (sflags & Utility.SpecialFlags.UniqueMap) != 0 &&
                             !string.IsNullOrWhiteSpace(info.UniqueName))
                    {
                        text = info.UniqueName!;
                    }
                    else
                    {
                        if (Settings.ShowMapNames.Value && !string.IsNullOrWhiteSpace(info.MapName))
                        {
                            // UX: when map names are enabled, show "MapName - Biome" to keep biome context.
                            text = string.IsNullOrWhiteSpace(info.BiomeDisplay) ? info.MapName! : (info.MapName! + " - " + info.BiomeDisplay);
                        }
                        else
                        {
                            text = info.BiomeDisplay;
                        }
                    }

                    // If a special/unique-name path selected the label text, keep biome context when map names are enabled.
                    if (Settings.ShowMapNames.Value && text.IndexOf(" - ", StringComparison.Ordinal) < 0 && !string.IsNullOrWhiteSpace(info.BiomeDisplay))
                        text = text + " - " + info.BiomeDisplay;

                    if (Settings.ShowSpecialTag.Value)
                    {
                        if ((sflags & Utility.SpecialFlags.DeadlyBoss) != 0) text += " [Deadly]";
                        if ((sflags & Utility.SpecialFlags.AbyssOverrun) != 0) text += " [Abyss]";
                        if ((sflags & Utility.SpecialFlags.MomentofZen) != 0) text += " [Moment Of Zen]";
                        if ((sflags & Utility.SpecialFlags.Cleansed) != 0) text += " [Cleansed]";
                        if ((sflags & Utility.SpecialFlags.CorruptedNexus) != 0) text += " [Corrupted]";
                        if ((sflags & Utility.SpecialFlags.UniqueMap) != 0 && !(Settings.ShowUniqueNameOnLabel.Value)) text += " [Unique]";
                        if (preferredWanted) text += " " + GetPreferredTag(preferredMatchedToken);
                    }

                    var size = Graphics.MeasureText(text);
                    var offsetY = Settings.ShowMapNames.Value ? Settings.MapNameOffsetY.Value : Settings.LabelOffset.Value;
                    var pos = new Vector2(center.X - size.X / 2f, center.Y - (radius + offsetY));

                    // Apply Label Settings.
                    var textColor = Settings.LabelUseBiomeColor.Value ? ringColor : Settings.LabelTextColor.Value;

                    // Darken unreached nodes when showing map names.
                    if (Settings.ShowMapNames.Value && !(info.Visited || info.Unlocked))
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
                }
            }

            try { RenderPreferredGuides(); } catch { /* keep overlay alive */ }

            try { RenderWaypointPanel(); } catch { }
        }

        

        private void RenderMapConnections()
        {
            if (_neighborsByCoord.Count == 0) return;
            int thickness = Settings.ConnectionThickness.Value;

            foreach (var nd in _visibleNodes)
            {
                if (nd?.Element is null) continue;
                if (!TryGetCoordinate(nd, out var c)) continue;
                var srcKey = (x: c.X, y: c.Y);
                if (!_neighborsByCoord.TryGetValue(srcKey, out var nbs)) continue;

                var srcPos = new Vector2(nd.Element.Center.X, nd.Element.Center.Y);

                for (int i = 0; i < nbs.Count; i++)
                {
                    var dstKey = nbs[i];
                    // Prevent double-draw (lexicographic).
                    if (dstKey.x < srcKey.x || (dstKey.x == srcKey.x && dstKey.y <= srcKey.y)) continue;

                    if (!_nodeByCoord.TryGetValue(dstKey, out var dstNd) || dstNd?.Element is null) continue;

                    bool srcUnlocked, srcVisited, srcCompleted;
                    bool dstUnlocked, dstVisited, dstCompleted;
                    TryGetStatus(srcKey, out srcUnlocked, out srcVisited, out srcCompleted);
                    TryGetStatus(dstKey, out dstUnlocked, out dstVisited, out dstCompleted);

                    if (!Settings.DrawVisitedConnections.Value && (srcVisited || dstVisited))
                        continue;

                    var color = (srcUnlocked && dstUnlocked) ? Settings.ConnectionColor.Value : Settings.ConnectionColorLocked.Value;
                    var dstPos = new Vector2(dstNd.Element.Center.X, dstNd.Element.Center.Y);
                    Graphics.DrawLine(srcPos, dstPos, thickness, color);
                }
            }
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
                Graphics.DrawCircle(center, Settings.WaypointRingRadius.Value, color, thickness, 32);

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
            var col = ImGui.ColorConvertFloat4ToU32(new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f));
            dl.AddTriangleFilled(p1, p2, p3, col);
        }

        private void DrawCenteredLabelWithSettings(string text, Vector2 center, float radius, int offsetY, Color textColor)
        {
            // Match the main overlay label look: centered, outlined, optional bold.
            // Note: Graphics.MeasureText is available across ExileCore2 builds, but font size is controlled by the game/UI.
            var size = Graphics.MeasureText(text);
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
                int t = Settings.LabelOutlineThickness.Value;
                for (int dx = -t; dx <= t; dx++)
                    for (int dy = -t; dy <= t; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        Graphics.DrawText(text, new Vector2(pos.X + dx, pos.Y + dy), Color.Black);
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
            bool biomeVisible = Settings.Visible.TryGetValue(biome, out var on) && on.Value;
            var sflags = Utility.TryGetSpecialFlags(nd);
            bool isDeadly = (sflags & Utility.SpecialFlags.DeadlyBoss) != 0;
            bool specialWanted =
                ((sflags & Utility.SpecialFlags.UniqueMap) != 0 && Settings.HighlightUniqueMaps.Value) ||
                ((sflags & Utility.SpecialFlags.DeadlyBoss) != 0 && Settings.HighlightDeadlyBoss.Value) ||
                ((sflags & Utility.SpecialFlags.AbyssOverrun) != 0 && Settings.HighlightAbyssOverrun.Value) ||
                ((sflags & Utility.SpecialFlags.MomentofZen) != 0 && Settings.HighlightMomentofZen.Value) ||
                ((sflags & Utility.SpecialFlags.CorruptedNexus) != 0 && Settings.HighlightCorruptedNexus.Value) ||
                ((sflags & Utility.SpecialFlags.Cleansed) != 0 && Settings.HighlightCleansed.Value);

            bool preferredWanted = false;
            if (Settings.HighlightPreferredMaps.Value && !isDeadly && TryGetCachedNodeTokens(nd, out var nameToken, out var idToken))
            {
                if (nameToken.Length != 0 && _preferredTokensExact.Contains(nameToken))
                {
                    preferredWanted = true;
                }
                else
                {
                    for (int i = 0; i < _preferredTokensList.Length; i++)
                    {
                        var keyToken = _preferredTokensList[i];
                        if (keyToken.Length == 0) continue;
                        if (nameToken.Length != 0 && Utility.TokenContainsEitherWay(nameToken, keyToken))
                        {
                            preferredWanted = true;
                            break;
                        }
                        if (!preferredWanted && idToken.Length != 0 && idToken.Contains(keyToken, System.StringComparison.Ordinal))
                        {
                            preferredWanted = true;
                            break;
                        }
                    }
                }
            }

            if (!biomeVisible && !(specialWanted || preferredWanted))
                return false;

            // The main overlay also requires a biome color entry to proceed.
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
                Graphics.DrawCircle(pos, Settings.NodeRadius.Value + 10, col, 2, 32);
                Graphics.DrawLine(originPos, pos, 1, col);
                count++;
            }

            var label = originIsTower ? $"{count} maps in tower range" : $"{count} towers in range";
            Graphics.DrawText(label, originPos + new Vector2(12, 12), col);
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
                    ImGuiNET.ImGui.TableSetupColumn("Map Name", ImGuiNET.ImGuiTableColumnFlags.WidthStretch);
                    ImGuiNET.ImGui.TableSetupColumn("Biome", ImGuiNET.ImGuiTableColumnFlags.WidthFixed, 130);
                    ImGuiNET.ImGui.TableSetupColumn("X", ImGuiNET.ImGuiTableColumnFlags.WidthFixed, 45);
                    ImGuiNET.ImGui.TableSetupColumn("Y", ImGuiNET.ImGuiTableColumnFlags.WidthFixed, 45);
                    ImGuiNET.ImGui.TableSetupColumn("Way", ImGuiNET.ImGuiTableColumnFlags.WidthFixed, 55);
                    ImGuiNET.ImGui.TableHeadersRow();

                    var searchTok = Utility.NormalizeToken(_atlasSearch);
                    int shown = 0;
                    if (_atlasNodes != null)
                    {
                        // We iterate deterministically; users can find quickly with Search.
                        for (int i = 0; i < _atlasNodes.Length && shown < Settings.WaypointAtlasMaxItems.Value; i++)
                        {
                            var nd = _atlasNodes[i];
                            if (nd?.Element is null) continue;
                            if (IsTower(nd.Element)) continue; // Atlas section lists maps; towers are handled by tower-range.

                            // Respect the same progress filters used by the main overlay so this list only shows maps
                            // that can be created (i.e., not completed/attempted/locked when those options are enabled).
                            if (Settings.HideCompletedMaps.Value && Utility.IsMapCompleted(nd))
                                continue;
                            if (Settings.HideAttemptedMaps.Value && Utility.IsMapAttempted(nd))
                                continue;
                            if (Settings.HideLockedMaps.Value && Utility.IsMapLocked(nd))
                                continue;

                            if (Settings.WaypointAtlasUnlockedOnly.Value && !(Utility.TryIsUnlocked(nd, out var un) && un))
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
                            bool hasWp = wps.Any(w => w.X == coord.X && w.Y == coord.Y);

                            ImGuiNET.ImGui.PushID(i);
                            ImGuiNET.ImGui.TableNextRow();

                            ImGuiNET.ImGui.TableNextColumn();
                            ImGuiNET.ImGui.TextUnformatted(mapName!);

                            ImGuiNET.ImGui.TableNextColumn();
                            ImGuiNET.ImGui.TextUnformatted(Utility.TryGetBiome(nd).ToString());

                            ImGuiNET.ImGui.TableNextColumn();
                            ImGuiNET.ImGui.TextUnformatted(coord.X.ToString());

                            ImGuiNET.ImGui.TableNextColumn();
                            ImGuiNET.ImGui.TextUnformatted(coord.Y.ToString());

                            ImGuiNET.ImGui.TableNextColumn();
                            if (ImGuiNET.ImGui.SmallButton(hasWp ? "Del" : "Way"))
                            {
                                if (hasWp)
                                {
                                    for (int wi = wps.Count - 1; wi >= 0; wi--)
                                        if (wps[wi].X == coord.X && wps[wi].Y == coord.Y)
                                            wps.RemoveAt(wi);
                                    SyncSelectedWaypoint();
                                }
                                else
                                {
                                    AddWaypoint(nd);
                                }
                            }

                            ImGuiNET.ImGui.PopID();
                            shown++;
                        }
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
