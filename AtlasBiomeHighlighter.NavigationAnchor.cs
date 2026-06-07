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

                int panOffset = GetStage37PanOffset(helper, new List<string>());
                if (panOffset <= 0)
                    panOffset = 0x490;

                bool closeHandle;
                var handle = GetStage35WritableProcessHandle(new List<string>(), out closeHandle);
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

        
        
        
        
        private bool TryGetStableNavigationTargetCenter(AtlasNodeDescription node, out Vector2 center, bool updateAnchorFromLive = true)
        {
            center = default;
            if (node == null)
                return false;

            if (!TryGetRawNodeCenter(node, out var rawCenter))
                return false;

            if (!TryReadNavigationPan(out var pan))
            {
                center = rawCenter;
                return true;
            }

            var coord = node.Coordinate;
            var key = (coord.X, coord.Y);
            bool onScreen = IsNodeActuallyOnScreen(rawCenter);
            bool rawSuspicious = IsNavigationTargetSuspicious(rawCenter);

            if (updateAnchorFromLive && onScreen && !rawSuspicious)
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

            if (_navigationAnchorByCoord.TryGetValue(key, out var anchor))
            {
                center = ApplyPanDeltaToAnchor(anchor.Center, pan, anchor.Pan);
                if (float.IsFinite(center.X) && float.IsFinite(center.Y))
                    return true;
            }

            
            _navigationAnchorByCoord[key] = new NavigationAnchor
            {
                Center = rawCenter,
                Pan = pan,
                Ms = Environment.TickCount64,
                WasVisible = onScreen
            };
            center = rawCenter;
            return true;
        }

        private void ResetNavigationTargetAnchors()
        {
            _navigationAnchorByCoord.Clear();
            _navigationCachedPanOk = false;
        }
    }
}
