using System;
using System.Collections.Generic;
using System.Numerics;
using ExileCore2.PoEMemory.Elements.AtlasElements;

namespace AtlasBiomeHighlighter
{
    public partial class AtlasBiomeHighlighter
    {
        private AtlasNodeDescription? _guideNode;
        private Vector2 _guideSmoothedPos;
        private bool _guideHasPos;
        private int _guidePrefHash;

        private bool TryPickOrUpdateGuideTarget(HashSet<string> enabledTokens, Vector2 origin, out Vector2 targetPos)
        {
            targetPos = default;
            if (_atlasNodes == null) return false;

            if (_guideNode?.Element != null
                && TryGetCachedNodeTokens(_guideNode, out var currNameToken, out var currIdToken)
                && (enabledTokens.Contains(currNameToken) || (currIdToken.Length != 0 && enabledTokens.Contains(currIdToken))))
            {
                targetPos = new Vector2(_guideNode.Element.Center.X, _guideNode.Element.Center.Y);
            }
            else
            {
                AtlasNodeDescription? best = null;
                float bestD2 = float.PositiveInfinity;
                foreach (var node in _atlasNodes)
                {
                    if (node?.Element is null) continue;
                    if (!TryGetCachedNodeTokens(node, out var nmToken, out var idToken)) continue;
                    if (!enabledTokens.Contains(nmToken) && (idToken.Length == 0 || !enabledTokens.Contains(idToken))) continue;

                    if (Settings.HideCompletedMaps.Value && Utility.IsMapCompleted(node)) continue;
                    if (Settings.HideAttemptedMaps.Value && Utility.IsMapAttempted(node)) continue;
                    if (Settings.HideLockedMaps.Value && Utility.IsMapLocked(node)) continue;
                    var p = new Vector2(node.Element.Center.X, node.Element.Center.Y);
                    var d2 = Vector2.DistanceSquared(origin, p);
                    if (d2 < bestD2)
                    {
                        bestD2 = d2; best = node; targetPos = p;
                    }
                }
                if (best == null) return false;
                _guideNode = best;
                _guideHasPos = false;
            }

            if (!_guideHasPos)
            {
                _guideSmoothedPos = targetPos;
                _guideHasPos = true;
            }
            else
            {
                _guideSmoothedPos = Vector2.Lerp(_guideSmoothedPos, targetPos, 0.25f);
            }

            targetPos = _guideSmoothedPos;
            return true;
        }

        private void RenderPreferredGuides()
        {
            if (!Settings.HighlightPreferredMaps.Value || !Settings.PreferredGuideLines.Value)
                return;

            EnsurePreferredCacheUpToDate();

            var origin = new Vector2(BorderX / 2f, BorderY / 2f);
            var color = Settings.PreferredMapRingColor.Value;
            int thickness = Settings.PreferredGuideThickness.Value;
            int arrowSize = Settings.PreferredArrowSize.Value;

            if (_preferredCacheHash != _guidePrefHash)
            {
                _guidePrefHash = _preferredCacheHash;
                _guideNode = null;
                _guideHasPos = false;
            }

            if (_preferredTokensExact.Count == 0) return;
            if (!TryPickOrUpdateGuideTarget(_preferredTokensExact, origin, out var pos)) return;

            var dir = pos - origin;
            var len = dir.Length();
            if (len < 1f) return;
            dir /= len;

            bool onScreen = pos.X > 0 && pos.X < BorderX && pos.Y > 0 && pos.Y < BorderY;
            if (Settings.PreferredGuideOnlyOffscreen.Value && onScreen) return;

            if (!onScreen)
            {
                var to = ClampToRectEdge(origin, pos, BorderX, BorderY, 8f);
                DrawArrow(origin, to, thickness, color, arrowSize);
            }
            else
            {
                const float offset = 50f;
                var from = origin + dir * offset;
                var to = pos - dir * offset;
                Graphics.DrawLine(from, to, thickness, color);
                DrawArrow(to - dir * 1f, to, thickness, color, arrowSize);
            }
        }

        private Vector2 ClampToRectEdge(Vector2 origin, Vector2 target, float width, float height, float margin)
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
