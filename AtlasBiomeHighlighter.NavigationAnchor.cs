using System;
using System.Collections.Generic;
using System.Numerics;
using ExileCore2.PoEMemory.Elements.AtlasElements;

namespace AtlasBiomeHighlighter
{
    public partial class AtlasBiomeHighlighter
    {
        private struct NavigationAnchor
        {
            public Vector2 Center;
            public Vector2 Pan;
            public long Ms;
            public bool WasVisible;
        }

        private readonly Dictionary<(int x, int y), NavigationAnchor> _navigationAnchorByCoord = new(2048);
        private readonly List<string> _navigationPanTraceScratch = new(8);
        private Vector2 _navigationCachedPan;
        private long _navigationCachedPanMs;
        private bool _navigationCachedPanOk;

        
        
        private static readonly Vector2 NavigationPanBasisX = new(-0.09375f, 0.046875f);
        private static readonly Vector2 NavigationPanBasisY = new(0.0625f, 0.046875f);

        private bool TryReadNavigationPan(out Vector2 pan)
        {
            pan = Vector2.Zero;
            long now = Environment.TickCount64;
            if (_navigationCachedPanOk && now - _navigationCachedPanMs < 20)
            {
                pan = _navigationCachedPan;
                return true;
            }

            try
            {
                var helper = _atlasPanel?.CameraHelper;
                if (helper == null || helper.Address == 0)
                    return false;

                _navigationPanTraceScratch.Clear();
                int panOffset = GetStage37PanOffset(helper, _navigationPanTraceScratch);
                if (panOffset <= 0)
                    panOffset = 0x490;

                bool closeHandle;
                var handle = GetStage35WritableProcessHandle(_navigationPanTraceScratch, out closeHandle);
                try
                {
                    if (handle == IntPtr.Zero)
                        return false;

                    if (!ReadProcessBytesStage32(handle, helper.Address + panOffset, 0x40, out var bytes, out var read, out var err) || bytes == null || bytes.Length < 8)
                        return false;

                    pan = new Vector2(ReadFloatFromBytes(bytes, 0), ReadFloatFromBytes(bytes, 4));
                    _navigationCachedPan = pan;
                    _navigationCachedPanMs = now;
                    _navigationCachedPanOk = true;
                    return true;
                }
                finally
                {
                    if (closeHandle && handle != IntPtr.Zero)
                        CloseHandle(handle);
                }
            }
            catch
            {
                _navigationCachedPanOk = false;
                return false;
            }
        }

        private bool TryGetRawNodeCenter(AtlasNodeDescription node, out Vector2 center)
        {
            center = default;
            try
            {
                if (node?.Element == null)
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

        private bool IsNodeActuallyOnScreen(Vector2 center)
        {
            return center.X >= -64 && center.X <= BorderX + 64 && center.Y >= -64 && center.Y <= BorderY + 64;
        }

        private Vector2 ApplyPanDeltaToAnchor(Vector2 anchorCenter, Vector2 currentPan, Vector2 anchorPan)
        {
            var d = currentPan - anchorPan;
            return anchorCenter + NavigationPanBasisX * d.X + NavigationPanBasisY * d.Y;
        }

        private void UpdateNavigationTargetAnchor(int coordinateX, int coordinateY, Vector2 rawCenter)
        {
            if (!IsNodeActuallyOnScreen(rawCenter) || IsNavigationTargetSuspicious(rawCenter))
                return;

            if (!TryReadNavigationPan(out var pan))
                return;

            _navigationAnchorByCoord[(coordinateX, coordinateY)] = new NavigationAnchor
            {
                Center = rawCenter,
                Pan = pan,
                Ms = Environment.TickCount64,
                WasVisible = true
            };
        }

        
        
        
        
        private bool TryGetStableNavigationTargetCenter(
            AtlasNodeDescription node,
            out Vector2 center,
            bool updateAnchorFromLive = true,
            bool allowFarOffscreen = false)
        {
            center = default;
            if (node == null)
                return false;

            try
            {
                var coordinate = node.Coordinate;
                return TryGetStableNavigationTargetCenter(
                    coordinate.X,
                    coordinate.Y,
                    node,
                    out center,
                    updateAnchorFromLive,
                    allowFarOffscreen);
            }
            catch
            {
                return false;
            }
        }

        private bool TryGetStableNavigationTargetCenter(
            int coordinateX,
            int coordinateY,
            AtlasNodeDescription? liveNode,
            out Vector2 center,
            bool updateAnchorFromLive = true,
            bool allowFarOffscreen = false)
        {
            center = default;
            var key = (coordinateX, coordinateY);

            Vector2 rawCenter = default;
            bool hasRawCenter = liveNode != null && TryGetRawNodeCenter(liveNode, out rawCenter);
            bool hasPan = TryReadNavigationPan(out var pan);

            if (hasRawCenter)
            {
                bool onScreen = IsNodeActuallyOnScreen(rawCenter);
                bool rawSuspicious = IsNavigationTargetSuspicious(rawCenter);

                if (hasPan && updateAnchorFromLive && onScreen && !rawSuspicious)
                {
                    _navigationAnchorByCoord[key] = new NavigationAnchor
                    {
                        Center = rawCenter,
                        Pan = pan,
                        Ms = Environment.TickCount64,
                        WasVisible = true
                    };
                    center = rawCenter;
                    return true;
                }

                if (onScreen && !rawSuspicious)
                {
                    center = rawCenter;
                    return true;
                }
            }

            if (hasPan && _navigationAnchorByCoord.TryGetValue(key, out var anchor))
            {
                center = ApplyPanDeltaToAnchor(anchor.Center, pan, anchor.Pan);
                if (IsNavigationProjectionUsable(center, allowFarOffscreen))
                    return true;
            }

            if (!hasRawCenter)
                return false;

            if (hasPan)
            {
                _navigationAnchorByCoord[key] = new NavigationAnchor
                {
                    Center = rawCenter,
                    Pan = pan,
                    Ms = Environment.TickCount64,
                    WasVisible = IsNodeActuallyOnScreen(rawCenter)
                };
            }

            center = rawCenter;
            return IsNavigationProjectionUsable(center, allowFarOffscreen);
        }

        private void ResetNavigationTargetAnchors()
        {
            _navigationAnchorByCoord.Clear();
            _navigationCachedPanOk = false;
        }
    }
}
