using System;
using System.Collections.Generic;
using System.Numerics;
using ExileCore2.PoEMemory.Elements.AtlasElements;

namespace AtlasBiomeHighlighter
{
    public partial class AtlasBiomeHighlighter
    {
        private const int PreferredGuideScanBudgetPerTick = 6;
        private const int PreferredGuidePruneMs = 2_500;
        private const int PreferredGuidePruneBudgetPerTick = 8;
        private const int PreferredGuideMaxCachedTargets = 256;

        private readonly List<AtlasNodeDescription> _preferredGuideNodes = new(64);
        private readonly HashSet<(int x, int y)> _preferredGuideCoords = new();
        private int _preferredGuideScanIndex;
        private int _preferredGuidePrefHash;
        private long _preferredGuideLastPruneMs;
        private int _preferredGuidePruneIndex;

        private void UpdatePreferredGuideDiscovery()
        {
            if (!Settings.HighlightPreferredMaps.Value || !Settings.PreferredGuideLines.Value)
                return;

            EnsurePreferredCacheUpToDate();

            if (_preferredGuidePrefHash != _preferredCacheHash)
            {
                _preferredGuidePrefHash = _preferredCacheHash;
                ResetPreferredGuideDiscovery();
            }

            if (_preferredTokensExact.Count == 0 || _atlasNodes.Length == 0)
                return;

            if (_preferredGuideScanIndex >= _atlasNodes.Length)
                _preferredGuideScanIndex = 0;

            int budget = Math.Min(PreferredGuideScanBudgetPerTick, _atlasNodes.Length);
            for (int i = 0; i < budget; i++)
            {
                if (_preferredGuideScanIndex >= _atlasNodes.Length)
                    _preferredGuideScanIndex = 0;

                var node = _atlasNodes[_preferredGuideScanIndex++];
                if (node?.Element is null)
                    continue;

                var coord = (node.Coordinate.X, node.Coordinate.Y);
                if (_preferredGuideCoords.Contains(coord))
                    continue;

                if (!IsPreferredGuideCandidate(node))
                    continue;

                _preferredGuideCoords.Add(coord);
                _preferredGuideNodes.Add(node);

                if (_preferredGuideNodes.Count >= PreferredGuideMaxCachedTargets)
                    break;
            }

            long now = Environment.TickCount64;
            if (now - _preferredGuideLastPruneMs >= PreferredGuidePruneMs)
            {
                _preferredGuideLastPruneMs = now;
                PrunePreferredGuideTargets();
            }
        }

        private void ResetPreferredGuideDiscovery()
        {
            _preferredGuideNodes.Clear();
            _preferredGuideCoords.Clear();
            _preferredGuideScanIndex = 0;
            _preferredGuideLastPruneMs = 0;
            _preferredGuidePruneIndex = 0;
        }

        private bool IsPreferredGuideCandidate(AtlasNodeDescription node)
        {
            if (node?.Element is null)
                return false;

            if (!TryGetCachedNodeTokens(node, out var nameToken, out var idToken))
                return false;

            bool match = (nameToken.Length != 0 && _preferredTokensExact.Contains(nameToken)) ||
                         (idToken.Length != 0 && _preferredTokensExact.Contains(idToken));
            if (!match)
                return false;

            // Status reads are intentionally delayed until after an exact Preferred match.
            // This avoids checking completed/attempted/locked for the whole atlas every frame.
            if (Settings.HideCompletedMaps.Value && Utility.IsMapCompleted(node)) return false;
            if (Settings.HideAttemptedMaps.Value && Utility.IsMapAttempted(node)) return false;
            if (Settings.HideLockedMaps.Value && Utility.IsMapLocked(node)) return false;

            return true;
        }

        private void PrunePreferredGuideTargets()
        {
            // Stage2b: prune incrementally. Full pruning can call status/name helpers for many
            // cached targets at once and was visible as Preferred-guide discovery spikes.
            int count = _preferredGuideNodes.Count;
            if (count == 0)
            {
                _preferredGuidePruneIndex = 0;
                return;
            }

            int processed = 0;
            while (processed < PreferredGuidePruneBudgetPerTick && _preferredGuideNodes.Count > 0)
            {
                if (_preferredGuidePruneIndex >= _preferredGuideNodes.Count)
                    _preferredGuidePruneIndex = 0;

                var node = _preferredGuideNodes[_preferredGuidePruneIndex];
                if (node?.Element is null || !IsPreferredGuideCandidate(node))
                {
                    if (node != null)
                        _preferredGuideCoords.Remove((node.Coordinate.X, node.Coordinate.Y));
                    _preferredGuideNodes.RemoveAt(_preferredGuidePruneIndex);
                    // Keep index on the shifted item.
                }
                else
                {
                    _preferredGuidePruneIndex++;
                }

                processed++;
            }
        }

        private void RenderPreferredGuides()
        {
            if (!Settings.HighlightPreferredMaps.Value || !Settings.PreferredGuideLines.Value)
                return;

            EnsurePreferredCacheUpToDate();
            if (_preferredTokensExact.Count == 0 || _preferredGuideNodes.Count == 0)
                return;

            var origin = Settings.PreferredGuideFromScreenCenter.Value
                ? new Vector2(BorderX / 2f, BorderY / 2f)
                : new Vector2(BorderX / 2f, BorderY / 2f);

            var color = Settings.PreferredMapRingColor.Value;
            int thickness = Settings.PreferredGuideThickness.Value;
            int arrowSize = Settings.PreferredArrowSize.Value;
            int limit = Math.Min(Settings.PreferredGuideLimit.Value, _preferredGuideNodes.Count);
            int drawn = 0;

            for (int i = 0; i < _preferredGuideNodes.Count && drawn < limit; i++)
            {
                var node = _preferredGuideNodes[i];
                if (node?.Element is null)
                    continue;

                if (!TryGetNodeScreenCenter(node, out var pos))
                    continue;

                bool onScreen = pos.X > 0 && pos.X < BorderX && pos.Y > 0 && pos.Y < BorderY;
                if (Settings.PreferredGuideOnlyOffscreen.Value && onScreen)
                    continue;

                var dir = pos - origin;
                if (dir.LengthSquared() < 1f)
                    continue;

                if (!onScreen)
                {
                    var to = ClampToRectEdge(origin, pos, BorderX, BorderY, 8f);
                    DrawArrow(origin, to, thickness, color, arrowSize);
                }
                else
                {
                    dir = Vector2.Normalize(dir);
                    const float offset = 50f;
                    var from = origin + dir * offset;
                    var to = pos - dir * offset;
                    if (Vector2.DistanceSquared(from, to) > 4f)
                        Graphics.DrawLine(from, to, thickness, color);
                    DrawArrow(to - dir, to, thickness, color, arrowSize);
                }

                drawn++;
            }
        }

        private static bool TryGetNodeScreenCenter(AtlasNodeDescription node, out Vector2 center)
        {
            center = default;
            try
            {
                if (node?.Element is null)
                    return false;

                var c = node.Element.Center;
                center = new Vector2(c.X, c.Y);
                return float.IsFinite(center.X) && float.IsFinite(center.Y);
            }
            catch
            {
                return false;
            }
        }

        private static Vector2 ClampToRectEdge(Vector2 origin, Vector2 target, float width, float height, float margin)
        {
            var dir = target - origin;
            if (dir.LengthSquared() < 1f) return target;
            dir = Vector2.Normalize(dir);

            var rectMin = new Vector2(margin, margin);
            var rectMax = new Vector2(width - margin, height - margin);

            Vector2? best = null;
            float bestDist2 = float.PositiveInfinity;

            if (Math.Abs(dir.X) > float.Epsilon)
            {
                float tx = (dir.X > 0 ? (rectMax.X - origin.X) : (rectMin.X - origin.X)) / dir.X;
                if (tx > 0)
                {
                    var y = origin.Y + dir.Y * tx;
                    if (y >= rectMin.Y && y <= rectMax.Y)
                    {
                        var cand = new Vector2(origin.X + dir.X * tx, y);
                        var d2 = Vector2.DistanceSquared(origin, cand);
                        if (d2 < bestDist2) { best = cand; bestDist2 = d2; }
                    }
                }
            }

            if (Math.Abs(dir.Y) > float.Epsilon)
            {
                float ty = (dir.Y > 0 ? (rectMax.Y - origin.Y) : (rectMin.Y - origin.Y)) / dir.Y;
                if (ty > 0)
                {
                    var x = origin.X + dir.X * ty;
                    if (x >= rectMin.X && x <= rectMax.X)
                    {
                        var cand = new Vector2(x, origin.Y + dir.Y * ty);
                        var d2 = Vector2.DistanceSquared(origin, cand);
                        if (d2 < bestDist2) { best = cand; bestDist2 = d2; }
                    }
                }
            }

            if (best.HasValue) return best.Value;
            float cx = Math.Clamp(target.X, rectMin.X, rectMax.X);
            float cy = Math.Clamp(target.Y, rectMin.Y, rectMax.Y);
            return new Vector2(cx, cy);
        }
    }
}
