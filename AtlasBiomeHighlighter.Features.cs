using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Windows.Forms;

using ExileCore2;
using ExileCore2.PoEMemory.Elements.AtlasElements;
using ExileCore2.Shared.Nodes;
using GameOffsets2.Native;

using RectangleF = ExileCore2.Shared.RectangleF;

namespace AtlasBiomeHighlighter
{
    public partial class AtlasBiomeHighlighter
    {
        // ===== Graph / waypoint state =====
        private readonly Dictionary<(int x, int y), AtlasNodeDescription> _nodeByCoord = new(1024);
        private readonly Dictionary<(int x, int y), List<(int x, int y)>> _neighborsByCoord = new(1024);

        private readonly List<(int x, int y)> _shortestPath = new(64);
        private (int x, int y)? _selectedWaypointCoord;
        private bool _waypointPanelOpen;

        // Waypoint panel atlas list UI state.
        private string _atlasSearch = string.Empty;

        // Tower range display (toggled by hotkey; kept simple/reliable).
        private bool _towerRangeActive;
        private Vector2i _towerRangeOrigin;


        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private static readonly bool[] _prevKeyStates = new bool[256];

        private static bool IsKeyPressedOnce(Keys key)
        {
            int vk = (int)key;
            if ((uint)vk >= (uint)_prevKeyStates.Length) return false;

            bool cur = (GetAsyncKeyState(vk) & 0x8000) != 0;
            bool triggered = cur && !_prevKeyStates[vk];
            _prevKeyStates[vk] = cur;
            return triggered;
        }

        private void RegisterRequestedHotkeys()
        {
            RegisterHotkey(Settings.AddWaypointHotkey);
            RegisterHotkey(Settings.DeleteWaypointHotkey);
            RegisterHotkey(Settings.ToggleWaypointPanelHotkey);
            RegisterHotkey(Settings.ShowTowerRangeHotkey);
        }

        /// <summary>
        /// ExileCore requires explicit registration for hotkeys to ensure <see cref="HotkeyNode.PressedOnce"/>
        /// works reliably and to keep bindings updated when the user changes them.
        /// </summary>
        private static void RegisterHotkey(HotkeyNode hotkey)
        {
            // Input is provided by ExileCore2.
            Input.RegisterKey(hotkey);
            hotkey.OnValueChanged += () => Input.RegisterKey(hotkey);
        }

        private void RefreshGraphCaches()
        {
            _nodeByCoord.Clear();
            _neighborsByCoord.Clear();

            if (_atlasPanel is null) return;

            // 1) Map nodes -> coord dictionary.
            // IMPORTANT: AtlasNodeDescription exposes Coordinate directly; avoid reflection.
            foreach (var nd in _atlasNodes)
            {
                if (nd?.Element is null) continue;
                var c = nd.Coordinate;
                _nodeByCoord[(c.X, c.Y)] = nd;
            }


            try
            {
                var points = _atlasPanel.Points?.ToArray();
                if (points == null || points.Length == 0) return;

                foreach (var p in points)
                {
                    var src = p.Source;
                    var srcKey = (src.X, src.Y);
                    if (!_neighborsByCoord.TryGetValue(srcKey, out var list))
                        _neighborsByCoord[srcKey] = list = new List<(int, int)>(6);

                    foreach (var target in p.Targets)
                    {
                        if (target == default)
                            continue;

                        AddNeighbor(list, target);

                        // Keep the graph usable for shortest-path routing even if the API exposes
                        // only one direction for a connection. RenderMapConnections already avoids
                        // drawing duplicates.
                        var targetKey = (target.X, target.Y);
                        if (!_neighborsByCoord.TryGetValue(targetKey, out var reverseList))
                            _neighborsByCoord[targetKey] = reverseList = new List<(int, int)>(6);
                        AddNeighbor(reverseList, src);
                    }
                }
            }
            catch
            {
                // Points unavailable in this ExileCore build.
                // We keep caches empty; callers will simply skip drawing connections/path.
            }

            static void AddNeighbor(List<(int, int)> list, Vector2i v)
            {
                // IMPORTANT: (0,0) can be a valid atlas coordinate in some builds.
                // Do NOT treat default(Vector2i) as "missing".
                var k = (v.X, v.Y);
                if (!list.Contains(k)) list.Add(k);
            }
        }

        private bool TryGetCoordinate(AtlasNodeDescription nd, out Vector2i coord)
        {
            // NOTE: In some ExileCore2 builds, (0,0) is a valid coordinate.
            // We therefore never treat default(Vector2i) as "invalid".
            coord = nd.Coordinate;
            return true;
        }

        private AtlasNodeDescription? GetClosestNodeToCursor(bool allowTowers, bool requireHitTest)
        {

            try
            {

                var gs = GameController?.Game?.IngameState ?? GameController?.IngameState;
                if (gs == null) return null;


                Vector2 cursorPos;
                try
                {
                    var hoverEl = gs.UIHoverElement;
                    cursorPos = hoverEl != null
                        ? new Vector2(hoverEl.GetClientRect().Center.X, hoverEl.GetClientRect().Center.Y)
                        : new Vector2(gs.MousePosX, gs.MousePosY);
                }
                catch
                {
                    cursorPos = new Vector2(gs.MousePosX, gs.MousePosY);
                }

                // Prefer visible nodes to avoid picking off-screen/hidden descriptions.
                // (_visibleNodes is maintained in Tick() based on IsInScreen checks.)
                IReadOnlyList<AtlasNodeDescription> nodes = _visibleNodes.Count > 0 ? _visibleNodes : _atlasNodes;

                AtlasNodeDescription? best = null;
                float bestD = float.MaxValue;


                for (int i = 0; i < nodes.Count; i++)
                {
                    var nd = nodes[i];
                    if (nd?.Element is null) continue;
                    if (!allowTowers && IsTower(nd.Element)) continue;

                    var center = new Vector2(nd.Element.Center.X, nd.Element.Center.Y);
                    var d = Vector2.Distance(center, cursorPos);
                    if (d < bestD)
                    {
                        bestD = d;
                        best = nd;
                    }
                }

                return best;
            }
            catch
            {
                return null;
            }
        }

        private bool TryGetHoveredAtlasNode(object ingameState, bool allowTowers, out AtlasNodeDescription? node)
        {
            node = null;
            try
            {
                // ExileCore2 has used a few different property names across builds.
                var t = ingameState.GetType();
                var hover = t.GetProperty("UIHover")?.GetValue(ingameState)
                            ?? t.GetProperty("UIHoverElement")?.GetValue(ingameState);
                if (hover == null) return false;

                var addrProp = hover.GetType().GetProperty("Address");
                if (addrProp == null) return false;
                if (addrProp.GetValue(hover) is not long addr || addr == 0) return false;

                // Prefer visible nodes for speed.
                var nodes = _visibleNodes.Count > 0 ? (IReadOnlyList<AtlasNodeDescription>)_visibleNodes : _atlasNodes;
                for (int i = 0; i < nodes.Count; i++)
                {
                    var nd = nodes[i];
                    var el = nd?.Element;
                    if (el == null) continue;
                    if (!allowTowers && IsTower(el)) continue;
                    if (el.Address == addr)
                    {
                        node = nd;
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private void HandleHotkeys()
        {

            if (IsKeyPressedOnce(Settings.ShowTowerRangeHotkey.Value))
            {
                var nd = GetClosestNodeToCursor(allowTowers: true, requireHitTest: true);
                if (nd != null && TryGetCoordinate(nd, out var c))
                {
                    // Toggle: same node toggles off; new node switches origin.
                    if (_towerRangeActive && c.Equals(_towerRangeOrigin))
                        _towerRangeActive = false;
                    else
                    {
                        _towerRangeOrigin = c;
                        _towerRangeActive = true;
                    }
                }
            }

            if (!Settings.WaypointsEnabled.Value) return;

            if (IsKeyPressedOnce(Settings.ToggleWaypointPanelHotkey.Value))
                _waypointPanelOpen = !_waypointPanelOpen;

            if (IsKeyPressedOnce(Settings.AddWaypointHotkey.Value))
            {
                // Waypoints should always prefer the map you are actually hovering, not a nearby Tower.
                var nd = GetClosestNodeToCursor(allowTowers: false, requireHitTest: true);
                if (nd != null) AddWaypoint(nd);
            }

            if (IsKeyPressedOnce(Settings.DeleteWaypointHotkey.Value))
            {
                var nd = GetClosestNodeToCursor(allowTowers: false, requireHitTest: true);
                if (nd != null) RemoveWaypoint(nd);
            }
        }

        private bool TryGetTowerRangeOrigin(out Vector2i origin)
        {
            origin = _towerRangeOrigin;
            return _towerRangeActive;
        }

        private void AddWaypoint(AtlasNodeDescription nd)
        {
            if (!TryGetCoordinate(nd, out var c)) return;
            var wps = Settings.Waypoints;
            if (wps.Any(w => w.X == c.X && w.Y == c.Y))
                return;


            float camX = 0, camY = 0;
            try
            {
                if (_atlasPanel != null)
                {
                    var t = _atlasPanel.Camera.Snapshot.Matrix.Translation;
                    camX = t.X;
                    camY = t.Y;
                }
            }
            catch { /* ignore */ }


            int towersCount = TryCountNearbyTowers(c);

            var wp = new AtlasWaypoint
            {
                X = c.X,
                Y = c.Y,
                PositionX = camX,
                PositionY = camY,
                TowersCount = towersCount,
                ColorArgb = Settings.DefaultWaypointColor.Value.ToArgb(),
                // Do NOT auto-select on add (Insert). Users asked to only show a marker + arrow,
                // and avoid drawing "roads" unless they explicitly select a waypoint.
                Selected = false,
                Enabled = true,
                Name = Utility.TryGetAnyMapName(nd, out var nm) ? (nm ?? string.Empty) : string.Empty
            };
            wps.Add(wp);
        }

        private void RemoveWaypoint(AtlasNodeDescription nd)
        {
            if (!TryGetCoordinate(nd, out var c)) return;
            var wps = Settings.Waypoints;
            for (int i = wps.Count - 1; i >= 0; i--)
            {
                if (wps[i].X == c.X && wps[i].Y == c.Y)
                    wps.RemoveAt(i);
            }
            SyncSelectedWaypoint();
        }


        private int TryCountNearbyTowers(Vector2i nodeCoord)
        {

            if (_atlasNodes == null || _atlasNodes.Length == 0) return 0;

            const int range = 11;

            int count = 0;
            for (int i = 0; i < _atlasNodes.Length; i++)
            {
                var nd = _atlasNodes[i];
                if (nd?.Element is null) continue;
                if (!IsTower(nd.Element)) continue;

                var c = nd.Coordinate;
                if (Distance(c, nodeCoord) <= range)
                    count++;
            }
            return count;
        }

        private static bool IsTower(AtlasPanelNode node)
        {

            try
            {
                var area = node.Area;
                var name = area?.Name;
                if (!string.IsNullOrEmpty(name) && (name.Contains("Tower", StringComparison.OrdinalIgnoreCase) ||
                                                    name.Equals("Lost Towers", StringComparison.OrdinalIgnoreCase) ||
                                                    name.Equals("Mesa", StringComparison.OrdinalIgnoreCase) ||
                                                    name.Equals("Bluff", StringComparison.OrdinalIgnoreCase) ||
                                                    name.Equals("Alpine Ridge", StringComparison.OrdinalIgnoreCase)))
                    return true;

				// Prefer stable IDs when available.
				var id = area?.Id;
				if (string.IsNullOrEmpty(id))
					return false;

                return id.Equals("MapSwampTower", StringComparison.OrdinalIgnoreCase)
                       || id.Equals("MapLostTowers", StringComparison.OrdinalIgnoreCase)
                       || id.Equals("MapMesa", StringComparison.OrdinalIgnoreCase)
                       || id.Equals("MapBluff", StringComparison.OrdinalIgnoreCase)
                       || id.Equals("MapAlpineRidge", StringComparison.OrdinalIgnoreCase)
                       || id.Contains("Tower", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static float Distance(Vector2i a, Vector2i b)
        {
            var dx = (float)(a.X - b.X);
            var dy = (float)(a.Y - b.Y);
            return (float)System.Math.Sqrt(dx * dx + dy * dy);
        }

        private void SyncSelectedWaypoint()
        {
            _selectedWaypointCoord = null;
            var wps = Settings.Waypoints;
            for (int i = 0; i < wps.Count; i++)
            {
                if (wps[i].Selected)
                {
                    _selectedWaypointCoord = (wps[i].X, wps[i].Y);
                    return;
                }
            }
        }

        private void RecomputeShortestPathIfNeeded()
        {
            _shortestPath.Clear();
            if (!Settings.DrawShortestPath.Value) return;
            if (_selectedWaypointCoord is null) return;
            if (_neighborsByCoord.Count == 0) return;

            var target = _selectedWaypointCoord.Value;
            if (!_nodeByCoord.ContainsKey(target)) return;

            // Choose nearest accessible node to screen center (visited OR unlocked).
            var origin = FindNearestAccessibleToScreenCenter();
            if (origin is null) return;

            Bfs(origin.Value, target, _shortestPath);
        }

        private (int x, int y)? FindNearestAccessibleToScreenCenter()
        {
            var screenCenter = new Vector2(BorderX / 2f, BorderY / 2f);
            float bestD = float.MaxValue;
            (int, int)? best = null;

            foreach (var kv in _nodeByCoord)
            {
                var nd = kv.Value;
                if (nd?.Element is null) continue;
                // Accessible heuristic: unlocked or visited.
                bool visited = Utility.TryIsVisited(nd, out var v) && v;
                bool unlocked = Utility.TryIsUnlocked(nd, out var u) && u;
                if (!visited && !unlocked) continue;

                var pos = new Vector2(nd.Element.Center.X, nd.Element.Center.Y);
                var d = Vector2.Distance(screenCenter, pos);
                if (d < bestD) { bestD = d; best = kv.Key; }
            }

            return best;
        }

        private void Bfs((int x, int y) start, (int x, int y) goal, List<(int x, int y)> pathOut)
        {
            var q = new Queue<(int x, int y)>(256);
            var prev = new Dictionary<(int, int), (int, int)>(1024);
            q.Enqueue(start);
            prev[start] = start;

            while (q.Count > 0)
            {
                var cur = q.Dequeue();
                if (cur.Equals(goal)) break;
                if (!_neighborsByCoord.TryGetValue(cur, out var nbs)) continue;

                for (int i = 0; i < nbs.Count; i++)
                {
                    var nb = nbs[i];
                    if (prev.ContainsKey(nb)) continue;
                    prev[nb] = cur;
                    q.Enqueue(nb);
                }
            }

            if (!prev.ContainsKey(goal)) return;

            // Reconstruct.
            var stack = new Stack<(int, int)>();
            var it = goal;
            while (!it.Equals(start))
            {
                stack.Push(it);
                it = prev[it];
            }
            stack.Push(start);
            while (stack.Count > 0) pathOut.Add(stack.Pop());
        }

        

        

        

    }
}
