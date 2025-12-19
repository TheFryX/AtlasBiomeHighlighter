using System.Drawing;
using System.Linq;
using System.Numerics;
using ExileCore2;
using ExileCore2.PoEMemory.Elements.AtlasElements;

namespace AtlasBiomeHighlighter
{
    public partial class AtlasBiomeHighlighter
    {
        public override void Render()
        {
            if (!Settings.Enable.Value) return;
            if (_atlasPanel == null || !_atlasPanel.IsVisible) return;

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
                                t = System.MathF.Min(t, System.MathF.Max(0, tx));
                            }
                            if (dir.Y != 0)
                            {
                                float ty = ((dir.Y > 0 ? (BorderY - margin) : margin) - origin.Y) / dir.Y;
                                t = System.MathF.Min(t, System.MathF.Max(0, ty));
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

            foreach (var nd in _visibleNodes)
            {
                // Hide overlay for completed nodes
                if (Settings.HideCompletedMaps.Value && Utility.IsMapCompleted(nd)) continue;
                // Hide overlay for attempted nodes
                if (Settings.HideAttemptedMaps.Value && Utility.IsMapAttempted(nd)) continue;
                // Hide overlay for locked/unavailable nodes
                if (Settings.HideLockedMaps.Value && Utility.IsMapLocked(nd)) continue;

                // Hide overlay for completed (green) nodes
                if (Settings.HideCompletedMaps.Value && Utility.IsMapCompleted(nd))
                    continue;
                // Hide overlay for attempted nodes
                if (Settings.HideAttemptedMaps.Value && Utility.IsMapAttempted(nd))
                    continue;

                // Hide overlay for completed (green) nodes
                if (Settings.HideCompletedMaps.Value && Utility.IsMapCompleted(nd))
                    continue;

                var biome = Utility.TryGetBiome(nd);

                // Specials bypass biome filter
                bool biomeVisible = Settings.Visible.TryGetValue(biome, out var on) && on.Value;
                var __sflags = Utility.TryGetSpecialFlags(nd);
                bool isDeadly = (__sflags & Utility.SpecialFlags.DeadlyBoss) != 0;
                bool specialWanted =
                    ((__sflags & Utility.SpecialFlags.UniqueMap) != 0 && Settings.HighlightUniqueMaps.Value) ||
                    ((__sflags & Utility.SpecialFlags.DeadlyBoss) != 0 && Settings.HighlightDeadlyBoss.Value) ||
                    ((__sflags & Utility.SpecialFlags.AbyssOverrun) != 0 && Settings.HighlightAbyssOverrun.Value) ||
                    ((__sflags & Utility.SpecialFlags.MomentofZen) != 0 && Settings.HighlightMomentofZen.Value) ||
                    ((__sflags & Utility.SpecialFlags.CorruptedNexus) != 0 && Settings.HighlightCorruptedNexus.Value) ||
                    ((__sflags & Utility.SpecialFlags.Cleansed) != 0 && Settings.HighlightCleansed.Value);

                // Preferred maps (by normalized name token or by id token), ignoring Deadly.
                bool preferredWanted = false;
                string? preferredMatchedToken = null;
                if (Settings.HighlightPreferredMaps.Value && !isDeadly && TryGetCachedNodeTokens(nd, out var nameToken2, out var idToken2))
                {
                    // Exact match first.
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
                            if (!preferredWanted && idToken2.Length != 0 && idToken2.Contains(keyToken, System.StringComparison.Ordinal))
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
                var center = new System.Numerics.Vector2(nd.Element.Center.X, nd.Element.Center.Y);
                var radius = Settings.NodeRadius.Value;
                var thickness = Settings.RingThickness.Value;

                Graphics.DrawCircle(center, radius, ringColor, thickness, 24);

                var sflags = __sflags;
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
                    var sfl = Utility.TryGetSpecialFlags(nd);

                    if (Settings.PreferMapNameForDeadly.Value &&
                        (sfl & Utility.SpecialFlags.DeadlyBoss) != 0 &&
                        Utility.TryGetAnyMapName(nd, out var dname) &&
                        !string.IsNullOrWhiteSpace(dname))
                    {
                        text = dname!;
                    }
                    else if (Settings.ShowUniqueNameOnLabel.Value &&
                             (sfl & Utility.SpecialFlags.UniqueMap) != 0 &&
                             Utility.TryGetUniqueNameFromId(nd, out var uname) &&
                             !string.IsNullOrWhiteSpace(uname))
                    {
                        text = uname!;
                    }
                    else
                    {
                        text = BiomeUtils.Display(biome);
                    }

                    if (Settings.ShowSpecialTag.Value)
                    {
                        var sf = Utility.TryGetSpecialFlags(nd);
                        if ((sf & Utility.SpecialFlags.DeadlyBoss) != 0) text += " [Deadly]";
                        if ((sf & Utility.SpecialFlags.AbyssOverrun) != 0) text += " [Abyss]";
                        if ((sf & Utility.SpecialFlags.MomentofZen) != 0) text += " [Moment Of Zen]";
                        if ((sf & Utility.SpecialFlags.Cleansed) != 0) text += " [Cleansed]";
                        if ((sf & Utility.SpecialFlags.CorruptedNexus) != 0) text += " [Corrupted]";
                        if ((sf & Utility.SpecialFlags.UniqueMap) != 0 && !(Settings.ShowUniqueNameOnLabel.Value)) text += " [Unique]";
                        if (preferredWanted)
                        {
                            // Include the matched preferred display name (cached) for clarity.
                            text += " " + GetPreferredTag(preferredMatchedToken);
                        }
                    }

                    var size = Graphics.MeasureText(text); // API-compatible
                    var pos = new System.Numerics.Vector2(center.X - size.X / 2f, center.Y - (radius + Settings.LabelOffset.Value));
                    var textColor = Settings.LabelUseBiomeColor.Value ? ringColor : Settings.LabelTextColor.Value;

                    // Outline
                    if (Settings.LabelOutline.Value)
                    {
                        for (int dx = -Settings.LabelOutlineThickness.Value; dx <= Settings.LabelOutlineThickness.Value; dx++)
                            for (int dy = -Settings.LabelOutlineThickness.Value; dy <= Settings.LabelOutlineThickness.Value; dy++)
                            {
                                if (dx == 0 && dy == 0) continue;
                                Graphics.DrawText(text, new System.Numerics.Vector2(pos.X + dx, pos.Y + dy), Color.Black);
                            }
                    }

                    // Bold (fake)
                    if (Settings.LabelBold.Value)
                        Graphics.DrawText(text, new System.Numerics.Vector2(pos.X + 1, pos.Y), textColor);

                    Graphics.DrawText(text, pos, textColor);
                }
            }

            try { RenderPreferredGuides(); } catch { /* keep overlay alive */ }
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
