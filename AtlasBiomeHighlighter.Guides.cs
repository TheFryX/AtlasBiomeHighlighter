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
        private const int PreferredGuideImmediateSeedLimit = 256;

        private readonly List<AtlasNodeDescription> _preferredGuideNodes = new(64);
        private readonly HashSet<(int x, int y)> _preferredGuideCoords = new();
        private int _preferredGuideScanIndex;
        private int _preferredGuidePrefHash;
        private long _preferredGuideLastPruneMs;
        private int _preferredGuidePruneIndex;
        private int _preferredGuideImmediateSeedHash;
        private int _preferredGuideImmediateSeedAtlasCount;

        private struct PreferredCoordTransform
        {
            public float Ax;
            public float Bx;
            public float Cx;
            public float Ay;
            public float By;
            public float Cy;
            public long Ms;
            public bool Valid;
        }

        private PreferredCoordTransform _preferredCoordTransform;
        private long _preferredCoordTransformLastBuildMs;


        private void UpdatePreferredGuideDiscovery()
        {
            
            Settings.PreferredGuideOnlyOffscreen.Value = false;
            Settings.PreferredGuideFromScreenCenter.Value = true;

            if (!Settings.HighlightPreferredMaps.Value)
                return;

            EnsurePreferredCacheUpToDate();

            if (!Settings.PreferredGuideLines.Value)
                return;

            if (_preferredGuidePrefHash != _preferredCacheHash)
            {
                _preferredGuidePrefHash = _preferredCacheHash;
                ResetPreferredGuideDiscovery();

                
                
                
                
                
                SeedPreferredGuideTargetsImmediate();
            }

            if ((_preferredTokensExact.Count == 0 && _preferredMechanicTokensExact.Count == 0) || _atlasNodes.Length == 0)
                return;

            if (_preferredGuideImmediateSeedHash != _preferredCacheHash ||
                _preferredGuideImmediateSeedAtlasCount != _atlasNodes.Length ||
                _preferredGuideNodes.Count == 0)
            {
                SeedPreferredGuideTargetsImmediate();
            }

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
            _preferredGuideImmediateSeedHash = 0;
            _preferredGuideImmediateSeedAtlasCount = 0;
        }

        private void SeedPreferredGuideTargetsImmediate()
        {
            _preferredGuideImmediateSeedHash = _preferredCacheHash;
            _preferredGuideImmediateSeedAtlasCount = _atlasNodes.Length;

            if ((_preferredTokensExact.Count == 0 && _preferredMechanicTokensExact.Count == 0) || _atlasNodes.Length == 0)
                return;

            
            if (_visibleNodeInfos != null && _visibleNodeInfos.Count != 0)
            {
                for (int i = 0; i < _visibleNodeInfos.Count && _preferredGuideNodes.Count < PreferredGuideImmediateSeedLimit; i++)
                {
                    var info = _visibleNodeInfos[i];
                    var node = info.Node;
                    if (node?.Element is null)
                        continue;

                    var coord = (node.Coordinate.X, node.Coordinate.Y);
                    if (_preferredGuideCoords.Contains(coord))
                        continue;

                    if (!IsPreferredGuideCandidate(info))
                        continue;

                    _preferredGuideCoords.Add(coord);
                    _preferredGuideNodes.Add(node);
                }
            }

            
            
            for (int i = 0; i < _atlasNodes.Length && _preferredGuideNodes.Count < PreferredGuideImmediateSeedLimit; i++)
            {
                var node = _atlasNodes[i];
                if (node?.Element is null)
                    continue;

                var coord = (node.Coordinate.X, node.Coordinate.Y);
                if (_preferredGuideCoords.Contains(coord))
                    continue;

                if (!IsPreferredGuideCandidate(node))
                    continue;

                _preferredGuideCoords.Add(coord);
                _preferredGuideNodes.Add(node);
            }
        }

        private bool IsPreferredGuideCandidate(AtlasNodeDescription node)
        {
            if (node?.Element is null)
                return false;

            bool match = false;

            if (TryGetCachedNodeTokens(node, out var nameToken, out var idToken))
            {
                match = (nameToken.Length != 0 && _preferredTokensExact.Contains(nameToken)) ||
                        (idToken.Length != 0 && _preferredTokensExact.Contains(idToken));
            }

            if (!match && _preferredMechanicTokensExact.Count != 0)
            {
                var mechanicNames = Utility.TryGetMechanicNames(node);
                for (int i = 0; i < mechanicNames.Count; i++)
                {
                    var token = Utility.NormalizeToken(mechanicNames[i]);
                    if (token.Length != 0 && _preferredMechanicTokensExact.Contains(token))
                    {
                        match = true;
                        break;
                    }
                }
            }

            if (!match)
                return false;

            
            
            if (Settings.HideCompletedMaps.Value && Utility.IsMapCompleted(node)) return false;
            if (Settings.HideAttemptedMaps.Value && Utility.IsMapAttempted(node)) return false;
            if (Settings.HideLockedMaps.Value && Utility.IsMapLocked(node)) return false;

            return true;
        }

        private bool IsPreferredGuideCandidate(NodeRenderInfo info)
        {
            var node = info.Node;
            if (node?.Element is null)
                return false;

            bool match = (info.NameToken.Length != 0 && _preferredTokensExact.Contains(info.NameToken)) ||
                         (info.IdToken.Length != 0 && _preferredTokensExact.Contains(info.IdToken));

            if (!match && _preferredMechanicTokensExact.Count != 0)
            {
                var mechanicTokens = info.MechanicTokens;
                for (int i = 0; i < mechanicTokens.Length; i++)
                {
                    var token = mechanicTokens[i];
                    if (token.Length != 0 && _preferredMechanicTokensExact.Contains(token))
                    {
                        match = true;
                        break;
                    }
                }
            }

            if (!match)
                return false;

            if (Settings.HideCompletedMaps.Value && info.Completed) return false;
            if (Settings.HideAttemptedMaps.Value && info.Attempted) return false;
            if (Settings.HideLockedMaps.Value && info.Locked) return false;

            return true;
        }

        private void PrunePreferredGuideTargets()
        {
            
            
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
                    
                }
                else
                {
                    _preferredGuidePruneIndex++;
                }

                processed++;
            }
        }

        private bool TryGetPreferredCoordTransform(out PreferredCoordTransform transform, bool forceRebuild = false)
        {
            transform = _preferredCoordTransform;
            long now = Environment.TickCount64;
            if (!forceRebuild && transform.Valid && now - _preferredCoordTransformLastBuildMs < 250)
                return true;

            _preferredCoordTransformLastBuildMs = now;
            transform = default;

            var nodes = _visibleNodes;
            if (nodes == null || nodes.Count < 3)
                return false;

            double s1 = 0, sx = 0, sy = 0, sxx = 0, sxy = 0, syy = 0;
            double ssx = 0, ssxX = 0, ssxY = 0;
            double ssy = 0, ssyX = 0, ssyY = 0;
            int used = 0;

            for (int i = 0; i < nodes.Count && used < 48; i++)
            {
                var n = nodes[i];
                if (n?.Element is null)
                    continue;

                if (!TryGetRawNodeCenter(n, out var screen))
                    continue;

                if (IsNavigationTargetSuspicious(screen))
                    continue;

                if (screen.X < -128 || screen.X > BorderX + 128 || screen.Y < -128 || screen.Y > BorderY + 128)
                    continue;

                double x = n.Coordinate.X;
                double y = n.Coordinate.Y;
                s1 += 1;
                sx += x;
                sy += y;
                sxx += x * x;
                sxy += x * y;
                syy += y * y;
                ssx += screen.X;
                ssxX += screen.X * x;
                ssxY += screen.X * y;
                ssy += screen.Y;
                ssyX += screen.Y * x;
                ssyY += screen.Y * y;
                used++;
            }

            if (used < 3)
                return false;

            if (!SolvePreferredAffine(sxx, sxy, sx, sxy, syy, sy, sx, sy, s1, ssxX, ssxY, ssx, out var ax, out var bx, out var cx))
                return false;
            if (!SolvePreferredAffine(sxx, sxy, sx, sxy, syy, sy, sx, sy, s1, ssyX, ssyY, ssy, out var ay, out var by, out var cy))
                return false;

            transform = new PreferredCoordTransform
            {
                Ax = (float)ax,
                Bx = (float)bx,
                Cx = (float)cx,
                Ay = (float)ay,
                By = (float)by,
                Cy = (float)cy,
                Ms = now,
                Valid = true
            };
            _preferredCoordTransform = transform;
            return true;
        }

        private static bool SolvePreferredAffine(
            double a00, double a01, double a02,
            double a10, double a11, double a12,
            double a20, double a21, double a22,
            double b0, double b1, double b2,
            out double x0, out double x1, out double x2)
        {
            x0 = x1 = x2 = 0;

            double det = a00 * (a11 * a22 - a12 * a21)
                       - a01 * (a10 * a22 - a12 * a20)
                       + a02 * (a10 * a21 - a11 * a20);
            if (Math.Abs(det) < 0.000001)
                return false;

            double det0 = b0 * (a11 * a22 - a12 * a21)
                        - a01 * (b1 * a22 - a12 * b2)
                        + a02 * (b1 * a21 - a11 * b2);
            double det1 = a00 * (b1 * a22 - a12 * b2)
                        - b0 * (a10 * a22 - a12 * a20)
                        + a02 * (a10 * b2 - b1 * a20);
            double det2 = a00 * (a11 * b2 - b1 * a21)
                        - a01 * (a10 * b2 - b1 * a20)
                        + b0 * (a10 * a21 - a11 * a20);

            x0 = det0 / det;
            x1 = det1 / det;
            x2 = det2 / det;
            return double.IsFinite(x0) && double.IsFinite(x1) && double.IsFinite(x2);
        }

        private bool TryGetCalibratedNavigationTargetCenter(
            AtlasNodeDescription node,
            out Vector2 center,
            bool allowRawOnScreen = true,
            bool forceTransformRebuild = false)
        {
            center = default;
            if (node?.Element is null)
                return false;

            if (allowRawOnScreen && TryGetRawNodeCenter(node, out var raw) && IsNodeActuallyOnScreen(raw) && !IsNavigationTargetSuspicious(raw))
            {
                center = raw;
                return true;
            }

            if (!TryGetPreferredCoordTransform(out var t, forceTransformRebuild))
                return false;

            float x = node.Coordinate.X;
            float y = node.Coordinate.Y;
            center = new Vector2(t.Ax * x + t.Bx * y + t.Cx, t.Ay * x + t.By * y + t.Cy);
            return float.IsFinite(center.X) && float.IsFinite(center.Y) && !IsNavigationTargetSuspicious(center);
        }

        private bool TryGetPreferredGuideTargetCenter(AtlasNodeDescription node, out Vector2 center)
        {
            return TryGetCalibratedNavigationTargetCenter(node, out center, allowRawOnScreen: true, forceTransformRebuild: false);
        }


        private void EnsureVisiblePreferredGuideTargets()
        {
            
            
            
            
            
            if (_visibleNodeInfos == null || _visibleNodeInfos.Count == 0)
                return;

            for (int i = 0; i < _visibleNodeInfos.Count; i++)
            {
                var info = _visibleNodeInfos[i];
                var node = info.Node;
                if (node?.Element is null)
                    continue;

                var coord = (node.Coordinate.X, node.Coordinate.Y);
                if (_preferredGuideCoords.Contains(coord))
                {
                    PromotePreferredGuideTarget(coord);
                    continue;
                }

                if (!IsPreferredGuideCandidate(info))
                    continue;

                if (_preferredGuideNodes.Count >= PreferredGuideMaxCachedTargets)
                    RemoveOnePreferredGuideTargetForVisibleSeed();

                if (_preferredGuideNodes.Count >= PreferredGuideMaxCachedTargets)
                    break;

                _preferredGuideCoords.Add(coord);
                _preferredGuideNodes.Insert(0, node);
            }
        }


        private void PromotePreferredGuideTarget((int x, int y) coord)
        {
            
            
            for (int i = 0; i < _preferredGuideNodes.Count; i++)
            {
                var n = _preferredGuideNodes[i];
                if (n == null)
                    continue;
                if (n.Coordinate.X != coord.x || n.Coordinate.Y != coord.y)
                    continue;
                if (i == 0)
                    return;

                _preferredGuideNodes.RemoveAt(i);
                _preferredGuideNodes.Insert(0, n);
                return;
            }
        }

        private void RemoveOnePreferredGuideTargetForVisibleSeed()
        {
            
            
            for (int i = 0; i < _preferredGuideNodes.Count; i++)
            {
                var n = _preferredGuideNodes[i];
                Vector2 c = default;
                bool remove = n?.Element is null;
                if (!remove && !TryGetPreferredGuideTargetCenter(n, out c))
                    remove = true;
                if (!remove && !IsNodeActuallyOnScreen(c))
                    remove = true;

                if (remove)
                {
                    if (n != null)
                        _preferredGuideCoords.Remove((n.Coordinate.X, n.Coordinate.Y));
                    _preferredGuideNodes.RemoveAt(i);
                    return;
                }
            }

            if (_preferredGuideNodes.Count > 0)
            {
                var n = _preferredGuideNodes[0];
                if (n != null)
                    _preferredGuideCoords.Remove((n.Coordinate.X, n.Coordinate.Y));
                _preferredGuideNodes.RemoveAt(0);
            }
        }

        private void RenderPreferredGuides()
        {
            if (!Settings.HighlightPreferredMaps.Value)
                return;

            EnsurePreferredCacheUpToDate();

            if (!Settings.PreferredGuideLines.Value)
                return;
            if (_preferredTokensExact.Count == 0 && _preferredMechanicTokensExact.Count == 0)
                return;

            EnsureVisiblePreferredGuideTargets();
            if (_preferredGuideNodes.Count == 0)
                return;

            var origin = Settings.PreferredGuideFromScreenCenter.Value
                ? new Vector2(BorderX / 2f, BorderY / 2f)
                : new Vector2(_atlasPanel?.Center.X ?? BorderX / 2f, _atlasPanel?.Center.Y ?? BorderY / 2f);

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

                if (!TryGetPreferredGuideTargetCenter(node, out var pos))
                    continue;

                bool onScreen = pos.X > 0 && pos.X < BorderX && pos.Y > 0 && pos.Y < BorderY;
                if (!onScreen || IsNavigationTargetSuspicious(pos))
                    AppendNavigationDebug("PreferredGuide", node, origin, pos, onScreen ? "suspicious on-screen target" : "off-screen target used for arrow");
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

        private bool TryGetNodeScreenCenter(AtlasNodeDescription node, out Vector2 center)
        {
            center = default;
            try
            {
                if (node?.Element is null)
                    return false;

                return TryGetRawNodeCenter(node, out center);
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
