using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Threading;
using System.Threading.Tasks;

using ExileCore2;
using ExileCore2.PoEMemory.Elements.AtlasElements;
using ExileCore2.Shared.Nodes;
using GameOffsets2.Native;

using RectangleF = ExileCore2.Shared.RectangleF;

namespace AtlasBiomeHighlighter
{
    public partial class AtlasBiomeHighlighter
    {
        
        private readonly Dictionary<(int x, int y), AtlasNodeDescription> _nodeByCoord = new(1024);
        private readonly Dictionary<(int x, int y), List<(int x, int y)>> _neighborsByCoord = new(1024);
        private readonly List<List<(int x, int y)>> _neighborListPool = new(1024);

        private readonly List<(int x, int y)> _shortestPath = new(64);
        private readonly List<List<(int x, int y)>> _shortestPaths = new(16);
        private readonly Dictionary<(int x, int y), int> _shortestPathStepByCoord = new(2048);
        private readonly Dictionary<(int x, int y), int> _atlasRouteStepDistanceByCoord = new(2048);

        
        
        
        
        private readonly Dictionary<(int x, int y), PathGraphNode> _pathGraphNodes = new(2048);
        private readonly Dictionary<(int x, int y), HashSet<(int x, int y)>> _pathGraphAdjacency = new(2048);

        private sealed class PathGraphNode
        {
            public (int x, int y) Coord;
            public string? Name;
            public bool Visited;
            public bool Unlocked;
            public bool Active;
            public bool HasConnections;
        }

        private (int x, int y)? _selectedWaypointCoord;
        private bool _waypointPanelOpen;
        private bool _waypointPanelAdvancedMode;

        
        private string _atlasSearch = string.Empty;
        private string _mechanicSearch = string.Empty;
        private string _favoriteMapSearch = string.Empty;
        private int _navigatorMinSteps;
        private int _navigatorMaxSteps;

        private const int WaypointAtlasBuildBudgetPerFrame = 32;

        private readonly List<WaypointAtlasRow> _waypointAtlasRows = new(256);
        private readonly List<WaypointMechanicRow> _waypointMechanicRows = new(256);
        private string _waypointAtlasCachedSearch = "\u0000";
        private bool _waypointAtlasCachedUnlockedOnly;
        private bool _waypointAtlasCachedHideCompleted;
        private bool _waypointAtlasCachedHideAttempted;
        private bool _waypointAtlasCachedHideLocked;
        private int _waypointAtlasCachedMaxItems;
        private int _waypointAtlasCachedNodeCount = -1;
        private string _waypointMechanicCachedSearch = "\u0000";
        private bool _waypointMechanicCachedUnlockedOnly;
        private bool _waypointMechanicCachedHideCompleted;
        private bool _waypointMechanicCachedHideAttempted;
        private bool _waypointMechanicCachedHideLocked;
        private int _waypointMechanicCachedMaxItems;
        private int _waypointMechanicCachedNodeCount = -1;
        private int _waypointAtlasBuildIndex;
        private int _waypointMechanicBuildIndex;
        private bool _waypointAtlasBuildActive;
        private bool _waypointMechanicBuildActive;

        private readonly struct WaypointAtlasRow
        {
            public WaypointAtlasRow(AtlasNodeDescription node, string name, string biome, int x, int y)
            {
                Node = node;
                Name = name;
                Biome = biome;
                X = x;
                Y = y;
            }

            public AtlasNodeDescription Node { get; }
            public string Name { get; }
            public string Biome { get; }
            public int X { get; }
            public int Y { get; }
        }

        private readonly struct WaypointMechanicRow
        {
            public WaypointMechanicRow(AtlasNodeDescription node, string mapName, string mechanics, string biome, int x, int y)
            {
                Node = node;
                MapName = mapName;
                Mechanics = mechanics;
                Biome = biome;
                X = x;
                Y = y;
            }

            public AtlasNodeDescription Node { get; }
            public string MapName { get; }
            public string Mechanics { get; }
            public string Biome { get; }
            public int X { get; }
            public int Y { get; }
        }

        
        private bool _towerRangeActive;
        private Vector2i _towerRangeOrigin;


        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int nSize, out IntPtr lpNumberOfBytesWritten);

        [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "ReadProcessMemory")]
        private static extern bool ReadProcessMemoryStage32(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int dwSize, out IntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "OpenProcess")]
        private static extern IntPtr OpenProcessStage35(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualProtectEx(IntPtr hProcess, IntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        private const int Stage35ProcessVmRead = 0x0010;
        private const int Stage35ProcessVmWrite = 0x0020;
        private const int Stage35ProcessVmOperation = 0x0008;
        private const int Stage35ProcessQueryInformation = 0x0400;
        private const int Stage35ProcessQueryLimitedInformation = 0x1000;
        private const int Stage35ProcessAllAccess = 0x1F0FFF;
        private const uint Stage37PageExecuteReadWrite = 0x40;

        private const uint MouseEventMove = 0x0001;
        private const uint MouseEventLeftDown = 0x0002;
        private const uint MouseEventLeftUp = 0x0004;

        private static int _waypointJumpInProgress;

        private static readonly bool[] _prevKeyStates = new bool[256];

        private static bool IsKeyPressedOnce(Keys key)
        {
            if (key == Keys.None) return false;

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
            RegisterHotkey(Settings.ToggleShortestPathHotkey);
            RegisterHotkey(Settings.ShowTowerRangeHotkey);
            RegisterHotkey(Settings.PreferredGuideLinesToggleHotkey);
        }

        
        
        
        
        private static void RegisterHotkey(HotkeyNode hotkey)
        {
            
            Input.RegisterKey(hotkey);
            hotkey.OnValueChanged += () => Input.RegisterKey(hotkey);
        }

        private void RefreshGraphCaches()
        {
            _nodeByCoord.Clear();
            ReleaseNeighborLists();

            if (_atlasPanel is null) return;

            
            
            foreach (var nd in _atlasNodes)
            {
                if (nd?.Element is null) continue;
                var c = nd.Coordinate;
                _nodeByCoord[(c.X, c.Y)] = nd;
            }

            try
            {
                var points = _atlasPanel.Points;
                if (points != null)
                {
                    foreach (var p in points)
                    {
                        var src = p.Source;
                        var srcKey = (src.X, src.Y);
                        if (!_neighborsByCoord.TryGetValue(srcKey, out var list))
                        {
                            list = RentNeighborList();
                            _neighborsByCoord[srcKey] = list;
                        }

                        foreach (var target in p.Targets)
                        {
                            if (target == default)
                                continue;

                            AddNeighbor(list, target);

                            
                            
                            
                            var targetKey = (target.X, target.Y);
                            if (!_neighborsByCoord.TryGetValue(targetKey, out var reverseList))
                            {
                                reverseList = RentNeighborList();
                                _neighborsByCoord[targetKey] = reverseList;
                            }

                            AddNeighbor(reverseList, src);
                        }
                    }
                }
            }
            catch
            {
                
                
                ReleaseNeighborLists();
            }

            UpdatePersistentPathGraphCache();

            static void AddNeighbor(List<(int, int)> list, Vector2i v)
            {
                
                
                var k = (v.X, v.Y);
                if (!list.Contains(k)) list.Add(k);
            }
        }

        private void UpdatePersistentPathGraphCache()
        {
            
            
            
            foreach (var kv in _nodeByCoord)
            {
                var nd = kv.Value;
                if (nd?.Element is null)
                    continue;

                var coord = kv.Key;
                string? name = null;
                try { name = nd.Element.Area?.Name; } catch { }
                if (string.IsNullOrWhiteSpace(name) && Utility.TryGetAnyMapName(nd, out var resolvedName))
                    name = resolvedName;

                bool visited = Utility.TryIsVisited(nd, out var v) && v;
                bool unlocked = Utility.TryIsUnlocked(nd, out var u) && u;
                bool active = false;
                try { active = nd.Element.IsActive; } catch { }

                if (!_pathGraphNodes.TryGetValue(coord, out var node))
                {
                    node = new PathGraphNode { Coord = coord };
                    _pathGraphNodes[coord] = node;
                }

                
                
                if (node.Visited && !visited && !string.IsNullOrWhiteSpace(node.Name) &&
                    !string.IsNullOrWhiteSpace(name) &&
                    string.Equals(node.Name, name, StringComparison.Ordinal))
                {
                    _pathGraphNodes.Clear();
                    _pathGraphAdjacency.Clear();
                    node = new PathGraphNode { Coord = coord };
                    _pathGraphNodes[coord] = node;
                }

                node.Name = name;
                node.Visited = visited;
                node.Unlocked = unlocked;
                node.Active = active;
            }

            foreach (var kv in _neighborsByCoord)
            {
                var src = kv.Key;
                if (!_pathGraphNodes.ContainsKey(src))
                    continue;

                foreach (var dst in kv.Value)
                {
                    if (!_pathGraphNodes.ContainsKey(dst))
                        continue;

                    AddPersistentPathEdge(src, dst);
                    AddPersistentPathEdge(dst, src);
                }
            }
        }

        private void AddPersistentPathEdge((int x, int y) src, (int x, int y) dst)
        {
            if (!_pathGraphAdjacency.TryGetValue(src, out var set))
            {
                set = new HashSet<(int x, int y)>();
                _pathGraphAdjacency[src] = set;
            }

            if (set.Add(dst) && _pathGraphNodes.TryGetValue(src, out var node))
                node.HasConnections = true;
        }

        private List<(int x, int y)> RentNeighborList()
        {
            int last = _neighborListPool.Count - 1;
            if (last < 0)
                return new List<(int x, int y)>(6);

            var list = _neighborListPool[last];
            _neighborListPool.RemoveAt(last);
            return list;
        }

        private void ReleaseNeighborLists()
        {
            if (_neighborsByCoord.Count == 0)
                return;

            foreach (var list in _neighborsByCoord.Values)
            {
                list.Clear();
                _neighborListPool.Add(list);
            }

            _neighborsByCoord.Clear();
        }

        private bool TryGetCoordinate(AtlasNodeDescription nd, out Vector2i coord)
        {
            
            
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
                
                var t = ingameState.GetType();
                var hover = t.GetProperty("UIHover")?.GetValue(ingameState)
                            ?? t.GetProperty("UIHoverElement")?.GetValue(ingameState);
                if (hover == null) return false;

                var addrProp = hover.GetType().GetProperty("Address");
                if (addrProp == null) return false;
                if (addrProp.GetValue(hover) is not long addr || addr == 0) return false;

                
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
            if (_capturingPreferredGuideHotkey || _capturingWaypointHotkey)
                return;

            if (IsKeyPressedOnce(Settings.PreferredGuideLinesToggleHotkey.Value))
                Settings.PreferredGuideLines.Value = !Settings.PreferredGuideLines.Value;

            if (IsKeyPressedOnce(Settings.ShowTowerRangeHotkey.Value))
            {
                var nd = GetClosestNodeToCursor(allowTowers: true, requireHitTest: true);
                if (nd != null && TryGetCoordinate(nd, out var c))
                {
                    
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

            if (IsKeyPressedOnce(Settings.ToggleShortestPathHotkey.Value))
            {
                Settings.DrawShortestPath.Value = !Settings.DrawShortestPath.Value;
                if (!Settings.DrawShortestPath.Value)
                    ClearShortestPathCache();
            }

            if (IsKeyPressedOnce(Settings.AddWaypointHotkey.Value))
            {
                
                var nd = GetClosestNodeToCursor(allowTowers: false, requireHitTest: true);
                if (nd != null) AddWaypoint(nd, null, true);
            }

            if (IsKeyPressedOnce(Settings.DeleteWaypointHotkey.Value))
            {
                var nd = GetClosestNodeToCursor(allowTowers: false, requireHitTest: true);
                if (nd != null) RemoveWaypoint(nd);
            }
        }

        private void QueueWaypointJumpToCoord(int x, int y)
        {
            if (!Settings.WaypointJumpEnabled.Value)
                return;

            if (Interlocked.CompareExchange(ref _waypointJumpInProgress, 1, 0) != 0)
                return;

            try
            {
                Task.Run(() =>
                {
                    try
                    {
                        
                        Thread.Sleep(80);
                        PerformWaypointJumpToCoord(x, y);
                    }
                    catch
                    {
                        
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _waypointJumpInProgress, 0);
                    }
                });
            }
            catch
            {
                Interlocked.Exchange(ref _waypointJumpInProgress, 0);
            }
        }

        private void PerformWaypointJumpToCoord(int x, int y)
        {
            
            
            
            var trace = new List<string>(128);
            trace.Add("============================================================");
            trace.Add($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            trace.Add("[Stage26c JUMP TRACE - no mouse]");
            trace.Add($"Requested coord: {x},{y}");
            trace.Add($"AtlasPanel null: {_atlasPanel is null}");
            trace.Add($"AtlasNodes.Length: {_atlasNodes?.Length ?? 0}");
            trace.Add($"NodeByCoord.Count: {_nodeByCoord?.Count ?? 0}");

            try
            {
                if (_atlasPanel is null)
                {
                    trace.Add("ABORT: AtlasPanel is null / atlas not ready.");
                    WriteJumpTrace(trace);
                    return;
                }

                trace.Add($"AtlasPanel.Center: {_atlasPanel.Center}");
                trace.Add($"AtlasPanel.Position: {_atlasPanel.Position}");
                trace.Add($"AtlasPanel.ScrollOffset: {_atlasPanel.ScrollOffset}");
                trace.Add($"AtlasPanel.Scale: {_atlasPanel.Scale}");

                var byCoord = _nodeByCoord.TryGetValue((x, y), out var cached) ? cached : null;
                trace.Add($"NodeByCoord hit: {byCoord is not null}");
                if (byCoord?.Element is not null)
                    trace.Add($"NodeByCoord element: center={byCoord.Element.Center} visible={byCoord.Element.IsVisible} visibleLocal={byCoord.Element.IsVisibleLocal} path={SafeText(() => byCoord.Element.PathFromRoot)} name={SafeNodeName(byCoord)}");

                AtlasNodeDescription? nd = null;
                string source = "none";
                if (byCoord?.Element is not null)
                {
                    nd = byCoord;
                    source = "_nodeByCoord";
                }
                else
                {
                    for (int i = 0; i < (_atlasNodes?.Length ?? 0); i++)
                    {
                        var n = _atlasNodes[i];
                        if (n?.Element is null) continue;
                        var c = n.Coordinate;
                        if (c.X == x && c.Y == y)
                        {
                            nd = n;
                            source = $"_atlasNodes[{i}]";
                            break;
                        }
                    }
                }

                if (nd?.Element is null)
                {
                    trace.Add("ABORT: target node not found in _nodeByCoord or _atlasNodes.");
                    DumpNearestCoordsForTrace(trace, x, y, 12);
                    WriteJumpTrace(trace);
                    return;
                }

                var atlasCenter = new Vector2(_atlasPanel.Center.X, _atlasPanel.Center.Y);
                bool initialCalibrated = TryGetJumpNavigationTargetCenter(nd, atlasCenter, out var targetCenter, forceTransformRebuild: true, out var initialTargetMode);
                if (!initialCalibrated)
                {
                    trace.Add("ABORT: no trusted jump target. Raw Element.Center is not used here because it can become a ghost/infinite target near atlas corners.");
                    WriteJumpTrace(trace);
                    return;
                }
                var deltaToCenter = atlasCenter - targetCenter;
                var dist = deltaToCenter.Length();
                trace.Add($"Target source: {source}");
                trace.Add($"Target name: {SafeNodeName(nd)}");
                trace.Add($"Target areaId: {SafeText(() => nd.Element?.Area?.Id)}");
                trace.Add($"Target areaName: {SafeText(() => nd.Element?.Area?.Name)}");
                trace.Add($"Target coord: {nd.Coordinate.X},{nd.Coordinate.Y}");
                trace.Add($"Target path: {SafeText(() => nd.Element.PathFromRoot)}");
                trace.Add($"Target visible: {nd.Element.IsVisible} visibleLocal={nd.Element.IsVisibleLocal} active={nd.Element.IsActive}");
                trace.Add($"Target center: {targetCenter.X:0.##},{targetCenter.Y:0.##}");
                trace.Add($"Target calibrated by visible-node transform: {initialCalibrated} mode={initialTargetMode}");
                trace.Add($"Screen/atlas center: {atlasCenter.X:0.##},{atlasCenter.Y:0.##}");
                trace.Add($"Needed delta target->center: dx={deltaToCenter.X:0.##} dy={deltaToCenter.Y:0.##} dist={dist:0.##}");
                trace.Add($"TargetSuspicious: {IsNavigationTargetSuspicious(targetCenter)}");
                AppendNavigationDebug("JumpStart", nd, atlasCenter, targetCenter, $"jump request coord={x},{y} dist={dist:0.##}", true);
                trace.Add($"Stop distance setting: {Settings.WaypointJumpStopDistance.Value}");

                if (dist <= Math.Clamp(Settings.WaypointJumpStopDistance.Value, 24, 180))
                {
                    trace.Add("DONE: target is already close to atlas center.");
                    WriteJumpTrace(trace);
                    return;
                }

                trace.Add("Mouse injection: DISABLED in Stage26c.");
                trace.Add("Attempting safe no-mouse managed/reflection jump hooks...");

                bool changed = TryNoMouseAtlasTransformJump(nd, atlasCenter, targetCenter, trace);
                trace.Add($"No-mouse transform attempt result: {changed}");

                Thread.Sleep(120);
                try
                {
                    var after = new Vector2(nd.Element.Center.X, nd.Element.Center.Y);
                    var afterDist = (after - atlasCenter).Length();
                    trace.Add($"After 120ms target center: {after.X:0.##},{after.Y:0.##} distToCenter={afterDist:0.##}");
                }
                catch (Exception ex)
                {
                    trace.Add("After-read failed: " + ex.GetType().Name + ": " + ex.Message);
                }

                if (!changed)
                {
                    trace.Add("ABORT REASON: No writable managed/native atlas pan hook exposed by current ExileCore2 wrappers.");
                    trace.Add("Next target: search lower-level Camera/Matrix/Native memory fields, not UI Element ScrollOffset.");
                }
            }
            catch (Exception ex)
            {
                trace.Add("ERROR: " + ex.GetType().FullName + ": " + ex.Message);
                trace.Add(ex.StackTrace ?? string.Empty);
            }
            finally
            {
                WriteJumpTrace(trace);
            }
        }

        private bool TryGetJumpNavigationTargetCenter(AtlasNodeDescription node, Vector2 atlasCenter, out Vector2 center, bool forceTransformRebuild, out string mode)
        {
            center = default;
            mode = "none";
            if (node?.Element is null)
                return false;

            
            
            
            if (TryGetRawNodeCenter(node, out var raw) && IsNodeActuallyOnScreen(raw) && !IsNavigationTargetSuspicious(raw))
            {
                center = raw;
                mode = "raw-visible";
                return true;
            }

            if (TryGetCalibratedNavigationTargetCenter(node, out var calibrated, allowRawOnScreen: false, forceTransformRebuild: forceTransformRebuild))
            {
                center = ClampJumpSteeringTarget(atlasCenter, calibrated);
                mode = "calibrated-steer";
                return true;
            }

            if (TryGetStableNavigationTargetCenter(node, out var stable, updateAnchorFromLive: true) && !IsNavigationTargetSuspicious(stable))
            {
                center = IsNodeActuallyOnScreen(stable) ? stable : ClampJumpSteeringTarget(atlasCenter, stable);
                mode = IsNodeActuallyOnScreen(stable) ? "stable-visible" : "stable-steer";
                return true;
            }

            return false;
        }

        private Vector2 ClampJumpSteeringTarget(Vector2 origin, Vector2 target)
        {
            var delta = target - origin;
            if (!float.IsFinite(delta.X) || !float.IsFinite(delta.Y) || delta.LengthSquared() < 1f)
                return origin;

            
            
            
            
            float maxRadius = MathF.Max(480f, MathF.Min(BorderX, BorderY) * 0.58f);
            float len = delta.Length();
            const float minSteeringRadius = 220f;
            if (len > maxRadius)
            {
                delta *= maxRadius / len;
            }
            else if (len < minSteeringRadius)
            {
                
                
                
                delta *= minSteeringRadius / len;
            }

            var p = origin + delta;
            float margin = 72f;
            p.X = Math.Clamp(p.X, margin, Math.Max(margin, BorderX - margin));
            p.Y = Math.Clamp(p.Y, margin, Math.Max(margin, BorderY - margin));
            return p;
        }

        private bool TryNoMouseAtlasTransformJump(AtlasNodeDescription nd, Vector2 atlasCenter, Vector2 targetCenter, List<string> trace)
        {
            
            
            
            
            
            
            bool anySuccess = false;
            try
            {
                object? atlas = _atlasPanel;
                object? worldMap = GameController?.IngameState?.IngameUi?.WorldMap;
                object? camera = TryGetPublicValue(atlas, "Camera", trace, "AtlasPanel.Camera");
                object? snapshot = TryGetPublicValue(camera, "Snapshot", trace, "Camera.Snapshot");
                object? matrix = TryGetPublicValue(snapshot, "Matrix", trace, "Snapshot.Matrix");

                DumpInterestingSetters(trace, "WorldMap", worldMap);
                DumpInterestingSetters(trace, "AtlasPanel", atlas);
                DumpInterestingSetters(trace, "Camera", camera);
                DumpInterestingSetters(trace, "Snapshot", snapshot);
                DumpInterestingSetters(trace, "Matrix", matrix);

                var desiredScreenDelta = atlasCenter - targetCenter;
                trace.Add($"Desired screen delta if writable transform exists: {desiredScreenDelta.X:0.##},{desiredScreenDelta.Y:0.##}");

                
                
                if (Settings.WaypointJumpMemoryWriteTest.Value)
                {
                    var memoryWriteOk = TryStage29CameraMemoryWriteStep(camera, nd, atlasCenter, targetCenter, trace);
                    trace.Add($"Stage29 memory write step result: {memoryWriteOk}");
                    if (memoryWriteOk)
                        anySuccess = true;
                }
                else
                {
                    trace.Add("Stage29 memory write step skipped: WaypointJumpMemoryWriteTest is disabled.");
                }

                
                
                DumpStage30MemoryWriterDiscovery(trace, atlas, worldMap, camera);

                
                
                foreach (var objNameObj in new[] { ("WorldMap", worldMap), ("AtlasPanel", atlas), ("Camera", camera), ("Snapshot", snapshot) })
                {
                    string objName = objNameObj.Item1;
                    object? obj = objNameObj.Item2;
                    if (obj is null) continue;
                    foreach (var m in obj.GetType().GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                    {
                        string name = m.Name.ToLowerInvariant();
                        if (!(name.Contains("pan") || name.Contains("jump") || name.Contains("center") || name.Contains("focus") || name.Contains("move") || name.Contains("scroll")))
                            continue;
                        if (m.IsSpecialName) continue;
                        var ps = m.GetParameters();
                        trace.Add($"Candidate method {objName}.{m.Name}({string.Join(", ", ps.Select(p => p.ParameterType.Name + " " + p.Name))})");

                        
                        try
                        {
                            object? result = null;
                            if (ps.Length == 1 && ps[0].ParameterType == typeof(Vector2))
                            {
                                result = m.Invoke(obj, new object[] { desiredScreenDelta });
                                trace.Add($"  invoked with Vector2 delta -> {result ?? "<null>"}");
                                anySuccess = true;
                            }
                            else if (ps.Length == 2 && ps[0].ParameterType == typeof(float) && ps[1].ParameterType == typeof(float))
                            {
                                result = m.Invoke(obj, new object[] { desiredScreenDelta.X, desiredScreenDelta.Y });
                                trace.Add($"  invoked with float dx/dy -> {result ?? "<null>"}");
                                anySuccess = true;
                            }
                        }
                        catch (Exception ex)
                        {
                            trace.Add($"  invoke failed: {ex.GetType().Name}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                trace.Add("TryNoMouseAtlasTransformJump failed: " + ex.GetType().Name + ": " + ex.Message);
            }
            return anySuccess;
        }


        private bool TryStage29CameraMemoryWriteStep(object? camera, AtlasNodeDescription nd, Vector2 atlasCenter, Vector2 initialTargetCenter, List<string> trace)
        {
            trace.Add("[Stage41 SMOOTH FAST PANOFFSET JUMP - no mouse]");
            trace.Add("Purpose: Stage41 removes visible calibration probes from Stage40 and uses a cached/default PanOffset basis for smooth bookmark-like flight.");
            trace.Add("No mouse injection. Uses CameraHelper+PanOffset +0x000/+0x004 with adaptive acceleration and braking.");

            IntPtr processHandle = IntPtr.Zero;
            bool closeHandle = false;
            try
            {
                object? atlasPanel = _atlasPanel;
                object? cameraHelper = atlasPanel is null ? null : TryGetMemberNoTrace(atlasPanel, "CameraHelper");
                if (cameraHelper is null)
                {
                    trace.Add("Stage41 abort: AtlasPanel.CameraHelper is null.");
                    return false;
                }

                long helperAddress = Convert.ToInt64(TryGetMemberNoTrace(cameraHelper, "Address") ?? 0L);
                int panOffset = GetStage37PanOffset(cameraHelper, trace);
                if (panOffset <= 0)
                {
                    panOffset = 0x490;
                    trace.Add("Stage41 PanOffset fallback forced to 0x490/1168 because reflection returned 0.");
                }
                if (helperAddress <= 0 || panOffset <= 0)
                {
                    trace.Add($"Stage41 abort: invalid CameraHelper address/panOffset address=0x{helperAddress:X} panOffset={panOffset}");
                    return false;
                }

                processHandle = GetStage35WritableProcessHandle(trace, out closeHandle);
                if (processHandle == IntPtr.Zero)
                {
                    trace.Add("Stage41 abort: could not get writable PathOfExile process handle.");
                    return false;
                }

                long panStruct = helperAddress + panOffset;
                float stopDistance = Math.Clamp(Settings.WaypointJumpStopDistance.Value, 24, 120);
                float configuredMaxStep = Settings.WaypointJumpMemoryWriteMaxStep.Value;
                
                
                float maxStep = Math.Clamp(Math.Max(configuredMaxStep, 1536), 512, 8192);
                int maxIterations = Math.Clamp(Math.Max(Settings.WaypointJumpIterations.Value, 900), 120, 1400);
                int delayMs = Math.Clamp(Settings.WaypointJumpDelayMs.Value, 8, 14);
                bool wroteAny = false;

                
                
                
                
                Vector2 xCol = new Vector2(-0.09375f, 0.046875f);
                Vector2 yCol = new Vector2(0.06250f, 0.046875f);
                float det = xCol.X * yCol.Y - yCol.X * xCol.Y;

                float AdaptiveStepForDistance(float dist)
                {
                    float step;
                    if (dist > 5000) step = 8192;
                    else if (dist > 3200) step = 6144;
                    else if (dist > 1800) step = 4096;
                    else if (dist > 1000) step = 3072;
                    else if (dist > 600) step = 2048;
                    else if (dist > 350) step = 1536;
                    else if (dist > 220) step = 1024;
                    else if (dist > 140) step = 640;
                    else if (dist > 90) step = 320;
                    else step = 120;
                    return Math.Min(step, maxStep);
                }

                float DampingForDistance(float dist)
                {
                    if (dist > 1000) return 0.98f;
                    if (dist > 600) return 0.94f;
                    if (dist > 350) return 0.88f;
                    if (dist > 220) return 0.78f;
                    if (dist > 140) return 0.62f;
                    if (dist > 90) return 0.46f;
                    return 0.28f;
                }

                trace.Add($"CameraHelper.Address: 0x{helperAddress:X}");
                trace.Add($"PanOffset: 0x{panOffset:X}; PanStruct=0x{panStruct:X}");
                trace.Add($"Stage41 settings: maxStep={maxStep:0.##} stopDistance={stopDistance:0.##} maxIterations={maxIterations} delayMs={delayMs}");
                trace.Add($"Stage53 jump-safe resolver active. v20 smooth carry: a single click keeps flying across steer->visible handoff until the real target reaches stopDistance.");
                trace.Add($"Stage41 basis: xCol={xCol.X:0.#####},{xCol.Y:0.#####} yCol={yCol.X:0.#####},{yCol.Y:0.#####} det={det:0.######}");

                Vector2 ReadTargetCenter(out string targetMode)
                {
                    targetMode = "fallback";
                    try
                    {
                        
                        
                        
                        
                        if (TryGetJumpNavigationTargetCenter(nd, atlasCenter, out var jumpCenter, forceTransformRebuild: true, out targetMode))
                            return jumpCenter;
                    }
                    catch { }
                    return initialTargetCenter;
                }


                static bool IsSteeringMode(string? mode)
                {
                    return mode != null && mode.EndsWith("-steer", StringComparison.OrdinalIgnoreCase);
                }

                bool ReadPan(out Vector2 pan, out float active)
                {
                    pan = Vector2.Zero;
                    active = 0;
                    bool ok = ReadProcessBytesStage32(processHandle, panStruct, 0x40, out var bytes, out var read, out var err);
                    if (!ok || bytes == null || bytes.Length < 0x30)
                    {
                        trace.Add($"Stage41 ReadPan failed ok={ok} bytes={read} err={err}");
                        return false;
                    }
                    pan = new Vector2(ReadFloatFromBytes(bytes, 0x00), ReadFloatFromBytes(bytes, 0x04));
                    active = ReadFloatFromBytes(bytes, 0x2C);
                    return true;
                }

                bool WritePan(Vector2 pan, string label, bool verbose = true)
                {
                    byte[] xy = new byte[8];
                    Array.Copy(BitConverter.GetBytes(pan.X), 0, xy, 0, 4);
                    Array.Copy(BitConverter.GetBytes(pan.Y), 0, xy, 4, 4);
                    bool okXY = WriteBytesToProcess(processHandle, panStruct, xy, trace, label + " PanOffset +0x000/+0x004");
                    bool okActive = WriteBytesToProcess(processHandle, panStruct + 0x2C, BitConverter.GetBytes(1.0f), trace, label + " PanOffset +0x02C active=1");
                    wroteAny |= okXY;
                    return okXY && okActive;
                }

                var initialCenter = ReadTargetCenter(out var initialTargetMode);
                float initialDist = (atlasCenter - initialCenter).Length();
                if (!ReadPan(out var startPan, out var startActive))
                {
                    trace.Add("Stage41 abort: cannot read base PanOffset.");
                    return false;
                }
                trace.Add($"Initial target center: {initialCenter.X:0.##},{initialCenter.Y:0.##} dist={initialDist:0.##} atlasCenter={atlasCenter.X:0.##},{atlasCenter.Y:0.##}");
                trace.Add($"Initial PanOffset: {startPan.X:0.###},{startPan.Y:0.###}, active={startActive:0.###}");

                
                WritePan(startPan, "Stage41 activate pan state");
                Thread.Sleep(Math.Max(4, delayMs));

                int staleOrBadSteps = 0;
                int noProgressSteps = 0;
                int consecutiveWorseSteps = 0;
                Vector2 bestPan = startPan;
                float bestDist = initialDist;
                Vector2 lastUsefulDesiredScreenMove = Vector2.Zero;
                const float noProgressEpsilon = 2.0f;
                const float hardPanTravelLimit = 36000f;
                const float hardSteeringPanTravelLimit = 900000f;
                const int steeringNoProgressLimit = 5000;
                bool everSteered = IsSteeringMode(initialTargetMode);
                bool longCarryJump = everSteered || initialDist > Math.Max(stopDistance * 5.0f, 900f);
                trace.Add($"Stage54 jump safety active: noProgressEps={noProgressEpsilon:0.##} hardPanTravelLimit={hardPanTravelLimit:0.##} steeringLimit={hardSteeringPanTravelLimit:0.##} oneClickCarry=True longCarry={longCarryJump}");

                for (int i = 1; i <= maxIterations; i++)
                {
                    Vector2 currentTarget = ReadTargetCenter(out var currentTargetMode);
                    bool steeringMode = IsSteeringMode(currentTargetMode);
                    everSteered |= steeringMode;
                    Vector2 desiredScreenMove = atlasCenter - currentTarget;
                    float dist = desiredScreenMove.Length();
                    if (dist > stopDistance || !steeringMode)
                    {
                        if (desiredScreenMove.LengthSquared() > 1f)
                            lastUsefulDesiredScreenMove = desiredScreenMove;
                    }
                    else if (steeringMode && lastUsefulDesiredScreenMove.LengthSquared() > 1f)
                    {
                        
                        
                        
                        desiredScreenMove = Vector2.Normalize(lastUsefulDesiredScreenMove) * 420f;
                        dist = desiredScreenMove.Length();
                    }
                    if (NavigationDebugEnabled() && (i <= 5 || i % 20 == 0 || IsNavigationTargetSuspicious(currentTarget)))
                    {
                        AppendNavigationDebug("JumpIter", nd, atlasCenter, currentTarget, $"iter={i} dist={dist:0.##} suspicious={IsNavigationTargetSuspicious(currentTarget)}", true);
                        trace.Add($"NavDebug iter {i}: currentTarget={currentTarget.X:0.##},{currentTarget.Y:0.##} mode={currentTargetMode} suspicious={IsNavigationTargetSuspicious(currentTarget)}");
                    }
                    if (dist <= stopDistance && !steeringMode)
                    {
                        trace.Add($"Stage41 DONE before iter {i}: real target within stop distance. center={currentTarget.X:0.##},{currentTarget.Y:0.##} dist={dist:0.##} mode={currentTargetMode}");
                        return true;
                    }
                    if (dist <= stopDistance && steeringMode)
                    {
                        
                        
                        
                        trace.Add($"Stage41 continue before iter {i}: steering pseudo-target reached but real node is not visible yet. mode={currentTargetMode} dist={dist:0.##}");
                    }

                    float panDx = ( desiredScreenMove.X * yCol.Y - yCol.X * desiredScreenMove.Y) / det;
                    float panDy = ( xCol.X * desiredScreenMove.Y - desiredScreenMove.X * xCol.Y) / det;
                    Vector2 panDelta = new Vector2(panDx, panDy);

                    float adaptiveStep = AdaptiveStepForDistance(dist);
                    
                    
                    float len = panDelta.Length();
                    if (len > adaptiveStep)
                        panDelta = Vector2.Normalize(panDelta) * adaptiveStep;
                    else
                        panDelta *= DampingForDistance(dist);

                    if (!ReadPan(out var panBefore, out var activeBefore))
                    {
                        trace.Add($"Stage41 abort iter {i}: cannot read PanOffset before write.");
                        return wroteAny;
                    }

                    Vector2 panAfter = panBefore + panDelta;
                    if (i <= 8 || i % 10 == 0 || dist < 180)
                    {
                        trace.Add($"Stage41 iter {i}: target={currentTarget.X:0.##},{currentTarget.Y:0.##} dist={dist:0.##} adaptiveStep={adaptiveStep:0.##} desiredScreen={desiredScreenMove.X:0.##},{desiredScreenMove.Y:0.##} solvedPan={panDx:0.##},{panDy:0.##} appliedPan={panDelta.X:0.##},{panDelta.Y:0.##}");
                        trace.Add($"  Pan before={panBefore.X:0.###},{panBefore.Y:0.###}, active={activeBefore:0.###}; pan after={panAfter.X:0.###},{panAfter.Y:0.###}");
                    }

                    bool ok = WritePan(panAfter, $"Stage41 iter {i}");
                    Thread.Sleep(delayMs);

                    Vector2 afterTarget = ReadTargetCenter(out var afterTargetMode);
                    bool afterSteeringMode = IsSteeringMode(afterTargetMode);
                    float afterDist = (atlasCenter - afterTarget).Length();
                    float deltaDist = afterDist - dist;

                    if (!float.IsFinite(afterTarget.X) || !float.IsFinite(afterTarget.Y) || !float.IsFinite(afterDist))
                    {
                        trace.Add($"Stage54 safety stop iter {i}: invalid target after write. Leaving current pan to avoid snap-back. target={afterTarget.X:0.##},{afterTarget.Y:0.##} dist={afterDist:0.##}");
                        return wroteAny;
                    }

                    if (IsNavigationTargetSuspicious(afterTarget))
                    {
                        trace.Add($"Stage54 safety stop iter {i}: suspicious target after write. Leaving current pan instead of restoring to start. target={afterTarget.X:0.##},{afterTarget.Y:0.##} dist={afterDist:0.##}");
                        return wroteAny;
                    }

                    float travelFromStart = (panAfter - startPan).Length();
                    everSteered |= afterSteeringMode;
                    
                    
                    
                    
                    
                    
                    
                    float activeHardPanTravelLimit = (everSteered || longCarryJump) ? hardSteeringPanTravelLimit : hardPanTravelLimit;
                    
                    
                    
                    
                    if (travelFromStart > activeHardPanTravelLimit && afterDist > Math.Max(stopDistance * 3.0f, 240f))
                    {
                        trace.Add($"Stage54 safety stop iter {i}: PanOffset travelled too far ({travelFromStart:0.##}, limit={activeHardPanTravelLimit:0.##}, mode={currentTargetMode}->{afterTargetMode}, everSteered={everSteered}, longCarry={longCarryJump}). Stopping without restore to prevent snap-back/infinite flight.");
                        return wroteAny;
                    }
                    if (NavigationDebugEnabled() && (i <= 5 || i % 20 == 0 || deltaDist > 40 || IsNavigationTargetSuspicious(afterTarget)))
                    {
                        AppendNavigationDebug("JumpAfterIter", nd, atlasCenter, afterTarget, $"iter={i} afterDist={afterDist:0.##} deltaDist={deltaDist:0.##} suspicious={IsNavigationTargetSuspicious(afterTarget)}", true);
                    }
                    if (i <= 8 || i % 10 == 0 || afterDist < 180 || deltaDist > 20)
                        trace.Add($"  Target after iter {i}: {afterTarget.X:0.##},{afterTarget.Y:0.##} mode={afterTargetMode} dist={afterDist:0.##} deltaDist={deltaDist:0.##} ok={ok}");

                    if (afterDist <= stopDistance && !afterSteeringMode)
                    {
                        trace.Add($"Stage41 DONE after iter {i}: real target within stop distance. mode={afterTargetMode}");
                        return true;
                    }
                    if (afterDist <= stopDistance && afterSteeringMode)
                    {
                        trace.Add($"Stage41 continue after iter {i}: steering pseudo-target within stop distance; continuing until real target is visible/centered. mode={afterTargetMode}");
                    }

                    if (afterDist + noProgressEpsilon < bestDist)
                    {
                        bestDist = afterDist;
                        bestPan = panAfter;
                        noProgressSteps = 0;
                        consecutiveWorseSteps = 0;
                    }
                    else
                    {
                        if (Math.Abs(deltaDist) <= noProgressEpsilon)
                            noProgressSteps++;
                        if (deltaDist > noProgressEpsilon)
                            consecutiveWorseSteps++;
                    }

                    
                    
                    
                    int activeNoProgressLimit = (steeringMode || afterSteeringMode || everSteered || longCarryJump) ? steeringNoProgressLimit : 14;
                    int activeWorseLimit = (steeringMode || afterSteeringMode || everSteered || longCarryJump) ? 1200 : 8;
                    if (!(steeringMode || afterSteeringMode || everSteered || longCarryJump) && dist > Math.Max(stopDistance * 2.0f, 180f) && noProgressSteps >= activeNoProgressLimit)
                    {
                        trace.Add($"Stage54 safety stop iter {i}: no target progress for {noProgressSteps} steps. bestDist={bestDist:0.##}; mode={currentTargetMode}->{afterTargetMode}; stopping without restore.");
                        return wroteAny;
                    }

                    if (!(steeringMode || afterSteeringMode || everSteered || longCarryJump) && dist > Math.Max(stopDistance * 2.0f, 180f) && consecutiveWorseSteps >= activeWorseLimit)
                    {
                        trace.Add($"Stage54 safety stop iter {i}: target worsened {consecutiveWorseSteps} times. bestDist={bestDist:0.##}; mode={currentTargetMode}->{afterTargetMode}; stopping without restore.");
                        return wroteAny;
                    }

                    
                    if (!steeringMode && i <= 3 && deltaDist > 45)
                    {
                        xCol = -xCol;
                        yCol = -yCol;
                        det = xCol.X * yCol.Y - yCol.X * xCol.Y;
                        WritePan(panBefore, "Stage41 restore and flip basis after bad early step");
                        Thread.Sleep(delayMs);
                        trace.Add("  Stage41 safety: early step worsened strongly; restored and flipped basis signs.");
                        continue;
                    }

                    
                    if (!steeringMode && !everSteered && !longCarryJump && deltaDist > 12 && dist < 300)
                    {
                        WritePan(panBefore, "Stage41 restore after close overshoot");
                        Thread.Sleep(delayMs);
                        staleOrBadSteps++;
                        if (staleOrBadSteps >= 3)
                        {
                            trace.Add("Stage41 stop: repeated close-range oscillation, leaving last stable position.");
                            return true;
                        }
                    }
                    else if (deltaDist < -2)
                    {
                        staleOrBadSteps = 0;
                    }
                }

                var finalTarget = ReadTargetCenter(out var finalTargetMode);
                trace.Add($"Stage41 final target center: {finalTarget.X:0.##},{finalTarget.Y:0.##} dist={(atlasCenter-finalTarget).Length():0.##}");
                trace.Add($"Stage41 result: wroteAny={wroteAny}. v20 smooth one-click carry keeps moving until the real target is centered or the hard safety budget is hit.");
                return wroteAny;
            }
            catch (Exception ex)
            {
                trace.Add("Stage41 smooth adaptive write loop failed: " + ex.GetType().Name + ": " + ex.Message);
                return false;
            }
            finally
            {
                if (closeHandle && processHandle != IntPtr.Zero)
                {
                    try { CloseHandle(processHandle); } catch { }
                }
            }
        }

        private static int GetStage37PanOffset(object cameraHelper, List<string> trace)
        {
            try
            {
                var t = cameraHelper.GetType();
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                var p = t.GetProperty("PanOffset", flags);
                if (p != null && p.GetIndexParameters().Length == 0)
                {
                    object? v = p.GetValue(p.GetGetMethod(true)?.IsStatic == true ? null : cameraHelper);
                    if (v != null)
                    {
                        int n = Convert.ToInt32(v);
                        trace.Add($"Stage37 PanOffset read via property {t.FullName}.PanOffset = {n} / 0x{n:X}");
                        return n;
                    }
                }
                var f = t.GetField("PanOffset", flags);
                if (f != null)
                {
                    object? v = f.GetValue(f.IsStatic ? null : cameraHelper);
                    if (v != null)
                    {
                        int n = Convert.ToInt32(v);
                        trace.Add($"Stage37 PanOffset read via field {t.FullName}.PanOffset = {n} / 0x{n:X}");
                        return n;
                    }
                }
            }
            catch (Exception ex)
            {
                trace.Add("Stage37 PanOffset reflection failed: " + ex.GetType().Name + ": " + ex.Message);
            }
            return 0;
        }

        private static float ReadFloatFromBytes(byte[]? bytes, int offset)
        {
            try
            {
                if (bytes is null || offset < 0 || bytes.Length < offset + 4) return float.NaN;
                return BitConverter.ToSingle(bytes, offset);
            }
            catch { return float.NaN; }
        }

        private IntPtr GetStage35WritableProcessHandle(List<string> trace, out bool closeHandle)
        {
            closeHandle = false;
            try
            {
                
                var names = new[]
                {
                    "PathOfExile", "PathOfExileSteam", "PathOfExileEGS", "PathOfExile_x64",
                    "PathOfExile2", "PathOfExile2Steam", "PathOfExile2_x64"
                };

                Process? best = null;
                long expectedMainWindow = 0;
                try
                {
                    var window = TryGetMemberNoTrace(GameController!, "Window");
                    if (window != null)
                    {
                        var mainWindow = TryGetMemberNoTrace(window, "Process") as Process;
                        if (mainWindow != null)
                        {
                            best = mainWindow;
                            trace.Add($"Stage35 process candidate from GameController.Window.Process: {best.ProcessName} pid={best.Id}");
                        }
                    }
                }
                catch { }

                if (best == null)
                {
                    foreach (var name in names)
                    {
                        Process[] list;
                        try { list = Process.GetProcessesByName(name); }
                        catch { continue; }
                        if (list.Length > 0)
                        {
                            best = list[0];
                            break;
                        }
                    }
                }

                if (best == null)
                {
                    trace.Add("Stage35 process discovery failed: no PathOfExile process found.");
                    return IntPtr.Zero;
                }

                trace.Add($"Stage35 process selected: {best.ProcessName} pid={best.Id} mainWindow=0x{SafeMainWindowHandle(best):X}");
                int access = Stage35ProcessAllAccess; 
                var h = OpenProcessStage35(access, false, best.Id);
                int err = Marshal.GetLastWin32Error();
                trace.Add($"Stage35 OpenProcess access=0x{access:X} -> 0x{h.ToInt64():X} lastError={err}");
                if (h != IntPtr.Zero)
                {
                    closeHandle = true;
                    return h;
                }

                try
                {
                    var ph = best.Handle;
                    trace.Add($"Stage35 fallback Process.Handle=0x{ph.ToInt64():X}");
                    return ph;
                }
                catch (Exception ex)
                {
                    trace.Add("Stage35 fallback Process.Handle failed: " + ex.GetType().Name + ": " + ex.Message);
                }
            }
            catch (Exception ex)
            {
                trace.Add("Stage35 process handle discovery failed: " + ex.GetType().Name + ": " + ex.Message);
            }
            return IntPtr.Zero;
        }

        private static long SafeMainWindowHandle(Process p)
        {
            try { return p.MainWindowHandle.ToInt64(); }
            catch { return 0; }
        }


        private void DumpStage30MemoryWriterDiscovery(List<string> trace, object? atlasPanel, object? worldMap, object? camera)
        {
            trace.Add("[Stage30 memory writer / pan hook discovery]");
            trace.Add("Purpose: find ExileCore2/GameController writer wrappers or AtlasCameraHelper pan members. No writes are performed here.");
            try
            {
                object? cameraHelper = TryGetMemberNoTrace(atlasPanel!, "CameraHelper");
                object? game = TryGetMemberNoTrace(GameController!, "Game");
                object? memory = FindFirstInterestingMember(GameController, "memory", 2, trace, "GameController.MemorySearch");
                object? process = FindFirstInterestingMember(GameController, "process", 2, trace, "GameController.ProcessSearch");
                object? area = FindFirstInterestingMember(GameController, "area", 1, trace, "GameController.AreaSearch");

                DumpStage30ObjectSummary(trace, "Plugin(this)", this, 0, true);
                DumpStage30ObjectSummary(trace, "GameController", GameController, 0, true);
                DumpStage30ObjectSummary(trace, "GameController.Game", game, 0, true);
                DumpStage30ObjectSummary(trace, "WorldMap", worldMap, 0, true);
                DumpStage30ObjectSummary(trace, "AtlasPanel", atlasPanel, 0, true);
                DumpStage30ObjectSummary(trace, "AtlasPanel.CameraHelper", cameraHelper, 0, true);
                DumpStage30ObjectSummary(trace, "AtlasPanel.Camera", camera, 0, true);
                DumpStage30ObjectSummary(trace, "FoundMemoryCandidate", memory, 0, true);
                DumpStage30ObjectSummary(trace, "FoundProcessCandidate", process, 0, true);
                DumpStage30ObjectSummary(trace, "FoundAreaCandidate", area, 0, false);

                DumpStage30CandidateMethods(trace, "Plugin(this)", this);
                DumpStage30CandidateMethods(trace, "GameController", GameController);
                DumpStage30CandidateMethods(trace, "GameController.Game", game);
                DumpStage30CandidateMethods(trace, "FoundMemoryCandidate", memory);
                DumpStage30CandidateMethods(trace, "FoundProcessCandidate", process);
                DumpStage30CandidateMethods(trace, "AtlasPanel.CameraHelper", cameraHelper);
                DumpStage30CandidateMethods(trace, "AtlasPanel", atlasPanel);
                DumpStage30CandidateMethods(trace, "Camera", camera);

                if (cameraHelper != null)
                {
                    var helperType = cameraHelper.GetType();
                    var panOffsetField = helperType.GetField("PanOffset", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    var panOffset = panOffsetField?.GetValue(null);
                    trace.Add($"AtlasCameraHelper.PanOffset static value: {panOffset ?? "<null>"}");
                    if (panOffset is int po && po > 0)
                    {
                        var helperAddressObj = TryGetMemberNoTrace(cameraHelper, "Address");
                        long helperAddress = 0;
                        try { helperAddress = Convert.ToInt64(helperAddressObj); } catch { }
                        trace.Add($"AtlasCameraHelper.Address=0x{helperAddress:X}; Address+PanOffset=0x{(helperAddress + po):X}");
                        trace.Add("Next check: dump/read memory at CameraHelper.Address + PanOffset to see if it is a pan target struct.");
                    }
                }

                trace.Add("Stage30 conclusion: if no writer method appears below, direct camera write needs ExileCore2's internal Memory object/handle, not kernel32.GetCurrentProcess().");
            }
            catch (Exception ex)
            {
                trace.Add("Stage30 discovery failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private object? FindFirstInterestingMember(object? root, string wanted, int maxDepth, List<string> trace, string label)
        {
            try
            {
                var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
                var q = new Queue<(object obj, string path, int depth)>();
                if (root != null) q.Enqueue((root, label, 0));
                while (q.Count > 0)
                {
                    var (obj, path, depth) = q.Dequeue();
                    if (!seen.Add(obj)) continue;
                    var t = obj.GetType();
                    string typeName = t.FullName ?? t.Name;
                    if (typeName.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        trace.Add($"{label}: found by type at {path}: {typeName} value={SafeToString(obj)}");
                        return obj;
                    }
                    if (depth >= maxDepth) continue;
                    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                    foreach (var p in t.GetProperties(flags))
                    {
                        if (p.GetIndexParameters().Length != 0) continue;
                        string n = p.Name;
                        bool interesting = n.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0 || (p.PropertyType.FullName ?? p.PropertyType.Name).IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0;
                        if (!interesting && depth > 0) continue;
                        object? v = null;
                        try { v = p.GetValue(obj); } catch { }
                        if (v == null) continue;
                        trace.Add($"{label}: candidate prop {path}.{n} type={p.PropertyType.FullName} value={SafeToString(v)}");
                        if (interesting) return v;
                        if (!IsPrimitiveLike(v.GetType())) q.Enqueue((v, path + "." + n, depth + 1));
                    }
                    foreach (var f in t.GetFields(flags))
                    {
                        string n = f.Name;
                        bool interesting = n.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0 || (f.FieldType.FullName ?? f.FieldType.Name).IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0;
                        if (!interesting && depth > 0) continue;
                        object? v = null;
                        try { v = f.GetValue(obj); } catch { }
                        if (v == null) continue;
                        trace.Add($"{label}: candidate field {path}.{n} type={f.FieldType.FullName} value={SafeToString(v)}");
                        if (interesting) return v;
                        if (!IsPrimitiveLike(v.GetType())) q.Enqueue((v, path + "." + n, depth + 1));
                    }
                }
            }
            catch (Exception ex)
            {
                trace.Add($"{label}: search failed {ex.GetType().Name}: {ex.Message}");
            }
            return null;
        }

        private void DumpStage30ObjectSummary(List<string> trace, string label, object? obj, int maxMembers, bool compact)
        {
            if (obj == null)
            {
                trace.Add($"--- {label}: <null> ---");
                return;
            }
            var t = obj.GetType();
            trace.Add($"--- {label} ---");
            trace.Add($"Type: {t.FullName ?? t.Name}");
            trace.Add($"Value: {SafeToString(obj)}");
            string[] names = { "Address", "Process", "ProcessHandle", "Handle", "MainWindowHandle", "Memory", "M", "TheGame", "Game", "Area", "Offsets", "CameraOffsets", "PanOffset" };
            foreach (var name in names)
            {
                object? v = null;
                bool found = false;
                try
                {
                    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                    var p = t.GetProperty(name, flags);
                    if (p != null && p.GetIndexParameters().Length == 0) { v = p.GetValue(obj); found = true; }
                    var f = t.GetField(name, flags);
                    if (!found && f != null) { v = f.GetValue(obj); found = true; }
                }
                catch (Exception ex) { trace.Add($"  {name}: read failed {ex.GetType().Name}"); }
                if (found) trace.Add($"  {name}: {SafeToString(v)} type={v?.GetType().FullName ?? "<null>"}");
            }

            if (maxMembers > 0 || !compact)
            {
                DumpStage30CandidateMembers(trace, label, obj, maxMembers <= 0 ? 80 : maxMembers);
            }
        }

        private void DumpStage30CandidateMembers(List<string> trace, string label, object obj, int max)
        {
            try
            {
                int written = 0;
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                foreach (var p in obj.GetType().GetProperties(flags))
                {
                    if (p.GetIndexParameters().Length != 0) continue;
                    if (!IsStage30InterestingName(p.Name, p.PropertyType.FullName ?? p.PropertyType.Name)) continue;
                    object? v = null;
                    try { v = p.GetValue(obj); } catch { }
                    trace.Add($"  PROP {p.PropertyType.Name} {p.Name} canWrite={p.CanWrite} value={SafeToString(v)}");
                    if (++written >= max) break;
                }
                foreach (var f in obj.GetType().GetFields(flags))
                {
                    if (!IsStage30InterestingName(f.Name, f.FieldType.FullName ?? f.FieldType.Name)) continue;
                    object? v = null;
                    try { v = f.GetValue(obj); } catch { }
                    trace.Add($"  FIELD {f.FieldType.Name} {f.Name} readonly={f.IsInitOnly} static={f.IsStatic} value={SafeToString(v)}");
                    if (++written >= max) break;
                }
            }
            catch (Exception ex)
            {
                trace.Add($"  member dump failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void DumpStage30CandidateMethods(List<string> trace, string label, object? obj)
        {
            if (obj == null) return;
            try
            {
                int written = 0;
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                foreach (var m in obj.GetType().GetMethods(flags))
                {
                    if (m.IsSpecialName) continue;
                    string n = m.Name;
                    var ps = m.GetParameters();
                    string sig = string.Join(", ", ps.Select(x => x.ParameterType.Name + " " + x.Name));
                    string ret = m.ReturnType.Name;
                    bool interesting = IsStage30InterestingName(n, sig + " " + ret);
                    if (!interesting) continue;
                    trace.Add($"  METHOD {label}.{n}({sig}) -> {ret}");
                    if (++written >= 80) break;
                }
                if (written == 0) trace.Add($"  METHOD {label}: no memory/write/pan/process candidates found.");
            }
            catch (Exception ex)
            {
                trace.Add($"  method dump failed for {label}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static bool IsStage30InterestingName(string name, string typeOrSig)
        {
            string s = (name + " " + typeOrSig).ToLowerInvariant();
            return s.Contains("memory") || s.Contains("write") || s.Contains("process") || s.Contains("handle") ||
                   s.Contains("camera") || s.Contains("pan") || s.Contains("offset") || s.Contains("pointer") ||
                   s.Contains("read") || s.Contains("native") || s.Contains("move") || s.Contains("worldtoscreen");
        }

        private static bool IsPrimitiveLike(Type t)
        {
            return t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) ||
                   t.FullName?.StartsWith("System.Numerics.") == true ||
                   t.FullName?.StartsWith("System.Drawing.") == true;
        }

        private static string SafeToString(object? obj)
        {
            if (obj == null) return "<null>";
            try { return obj.ToString() ?? "<null>"; }
            catch { return "<ToString failed>"; }
        }

        

        private static object? TryGetMemberNoTrace(object obj, string name)
        {
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            var p = obj.GetType().GetProperty(name, flags);
            if (p is not null) return p.GetValue(obj);
            var f = obj.GetType().GetField(name, flags);
            return f?.GetValue(obj);
        }

        private static T ReadFieldValue<T>(object obj, string fieldName, T fallback)
        {
            try
            {
                var f = obj.GetType().GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (f?.GetValue(obj) is T v) return v;
            }
            catch { }
            return fallback;
        }

        private static int GetFieldOffset(Type type, string fieldName)
        {
            try { return (int)Marshal.OffsetOf(type, fieldName); }
            catch { return -1; }
        }

        private static bool WriteBytes(long address, byte[] bytes, List<string> trace, string label)
        {
            try
            {
                var ok = WriteProcessMemory(GetCurrentProcess(), new IntPtr(address), bytes, bytes.Length, out var written);
                int err = Marshal.GetLastWin32Error();
                trace.Add($"WriteProcessMemory {label} addr=0x{address:X} size={bytes.Length} ok={ok} written={written} lastError={err}");
                return ok && written.ToInt64() == bytes.Length;
            }
            catch (Exception ex)
            {
                trace.Add($"WriteProcessMemory {label} failed: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        private static bool ReadProcessBytesStage32(IntPtr processHandle, long address, int size, out byte[] bytes, out int bytesRead, out int lastError)
        {
            bytes = new byte[Math.Max(0, size)];
            bytesRead = 0;
            lastError = 0;

            if (processHandle == IntPtr.Zero || address == 0 || size <= 0)
                return false;

            bool ok = ReadProcessMemoryStage32(processHandle, new IntPtr(address), bytes, size, out var readPtr);
            bytesRead = readPtr.ToInt32();

            if (!ok)
            {
                lastError = Marshal.GetLastWin32Error();
                if (bytesRead > 0 && bytesRead < bytes.Length)
                {
                    var partial = new byte[bytesRead];
                    Array.Copy(bytes, partial, bytesRead);
                    bytes = partial;
                }
                return false;
            }

            if (bytesRead > 0 && bytesRead < bytes.Length)
            {
                var partial = new byte[bytesRead];
                Array.Copy(bytes, partial, bytesRead);
                bytes = partial;
            }

            return true;
        }

        private static bool WriteBytesToProcess(IntPtr processHandle, long address, byte[] bytes, List<string> trace, string label)
        {
            try
            {
                if (processHandle == IntPtr.Zero)
                {
                    trace.Add($"WriteProcessMemory {label} skipped: processHandle=0");
                    return false;
                }

                uint oldProtect = 0;
                bool protectOk = false;
                try { protectOk = VirtualProtectEx(processHandle, new IntPtr(address), (UIntPtr)bytes.Length, Stage37PageExecuteReadWrite, out oldProtect); }
                catch { protectOk = false; }

                var ok = WriteProcessMemory(processHandle, new IntPtr(address), bytes, bytes.Length, out var written);
                int err = Marshal.GetLastWin32Error();

                if (protectOk)
                {
                    try { VirtualProtectEx(processHandle, new IntPtr(address), (UIntPtr)bytes.Length, oldProtect, out _); } catch { }
                }

                trace.Add($"WriteProcessMemory {label} handle=0x{processHandle.ToInt64():X} addr=0x{address:X} size={bytes.Length} protectOk={protectOk} oldProtect=0x{oldProtect:X} ok={ok} written={written} lastError={err}");
                return ok && written.ToInt64() == bytes.Length;
            }
            catch (Exception ex)
            {
                trace.Add($"WriteProcessMemory {label} failed: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        private static byte[] Vector2ToBytes(Vector2 v)
        {
            var b = new byte[8];
            Buffer.BlockCopy(BitConverter.GetBytes(v.X), 0, b, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(v.Y), 0, b, 4, 4);
            return b;
        }

        private static byte[] Vector3ToBytes(Vector3 v)
        {
            var b = new byte[12];
            Buffer.BlockCopy(BitConverter.GetBytes(v.X), 0, b, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(v.Y), 0, b, 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(v.Z), 0, b, 8, 4);
            return b;
        }

        private static byte[] MatrixToBytes(Matrix4x4 m)
        {
            var vals = new[]
            {
                m.M11, m.M12, m.M13, m.M14,
                m.M21, m.M22, m.M23, m.M24,
                m.M31, m.M32, m.M33, m.M34,
                m.M41, m.M42, m.M43, m.M44
            };
            var b = new byte[64];
            for (int i = 0; i < vals.Length; i++)
                Buffer.BlockCopy(BitConverter.GetBytes(vals[i]), 0, b, i * 4, 4);
            return b;
        }

        private static object? TryGetPublicValue(object? obj, string propertyName, List<string> trace, string label)
        {
            if (obj is null)
            {
                trace.Add(label + ": <null parent>");
                return null;
            }
            try
            {
                var p = obj.GetType().GetProperty(propertyName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (p is null)
                {
                    trace.Add(label + ": property not found on " + obj.GetType().FullName);
                    return null;
                }
                var value = p.GetValue(obj);
                trace.Add(label + ": " + (value?.ToString() ?? "<null>") + " type=" + (value?.GetType().FullName ?? "<null>"));
                return value;
            }
            catch (Exception ex)
            {
                trace.Add(label + ": read failed " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        private static void DumpInterestingSetters(List<string> trace, string label, object? obj)
        {
            if (obj is null)
            {
                trace.Add($"[{label}] <null>");
                return;
            }
            try
            {
                var type = obj.GetType();
                trace.Add($"[{label}] type={type.FullName}");
                foreach (var p in type.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                {
                    string n = p.Name.ToLowerInvariant();
                    if (!(n.Contains("camera") || n.Contains("view") || n.Contains("matrix") || n.Contains("transform") || n.Contains("translation") || n.Contains("position") || n.Contains("offset") || n.Contains("center") || n.Contains("scroll") || n.Contains("pan")))
                        continue;
                    trace.Add($"  PROP {p.PropertyType.Name} {p.Name} canWrite={p.CanWrite} value={SafeObj(() => p.GetValue(obj))}");
                }
                foreach (var f in type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                {
                    string n = f.Name.ToLowerInvariant();
                    if (!(n.Contains("camera") || n.Contains("view") || n.Contains("matrix") || n.Contains("transform") || n.Contains("translation") || n.Contains("position") || n.Contains("offset") || n.Contains("center") || n.Contains("scroll") || n.Contains("pan")))
                        continue;
                    trace.Add($"  FIELD {f.FieldType.Name} {f.Name} readonly={f.IsInitOnly} value={SafeObj(() => f.GetValue(obj))}");
                }
            }
            catch (Exception ex)
            {
                trace.Add($"[{label}] dump failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static string SafeObj(Func<object?> fn)
        {
            try { return fn()?.ToString() ?? "<null>"; }
            catch { return "<err>"; }
        }

        private string SafeNodeName(AtlasNodeDescription n)
        {
            try
            {
                if (Utility.TryGetAnyMapName(n, out var name) && !string.IsNullOrWhiteSpace(name))
                    return name!;
            }
            catch { }

            try
            {
                var area = n.Element?.Area;
                if (!string.IsNullOrWhiteSpace(area?.Name)) return area!.Name;
                if (!string.IsNullOrWhiteSpace(area?.Id)) return area!.Id;
            }
            catch { }

            try { return $"coord {n.Coordinate.X},{n.Coordinate.Y}"; }
            catch { return "<unknown>"; }
        }

        private static string SafeText(Func<object?> fn)
        {
            try { return fn()?.ToString() ?? ""; }
            catch { return "<err>"; }
        }

        private void DumpNearestCoordsForTrace(List<string> trace, int x, int y, int max)
        {
            try
            {
                var rows = new List<(int dist, int x, int y, string name, bool hasElement)>();
                for (int i = 0; i < (_atlasNodes?.Length ?? 0); i++)
                {
                    var n = _atlasNodes[i];
                    if (n is null) continue;
                    var c = n.Coordinate;
                    int dx = c.X - x;
                    int dy = c.Y - y;
                    rows.Add((dx * dx + dy * dy, c.X, c.Y, SafeNodeName(n), n.Element is not null));
                }
                foreach (var r in rows.OrderBy(r => r.dist).Take(max))
                    trace.Add($"Nearest coord: {r.x},{r.y} distSq={r.dist} name='{r.name}' hasElement={r.hasElement}");
            }
            catch (Exception ex)
            {
                trace.Add("DumpNearestCoords failed: " + ex.Message);
            }
        }

        private void WriteJumpTrace(List<string> lines)
        {
            try
            {
                var path = System.IO.Path.Combine(DirectoryFullName, "AtlasBiomeHighlighter.JumpTrace.txt");
                System.IO.File.AppendAllLines(path, lines);
            }
            catch { }
        }

        private bool IsUsableScreenPoint(Vector2 p)
        {
            if (float.IsNaN(p.X) || float.IsNaN(p.Y) || float.IsInfinity(p.X) || float.IsInfinity(p.Y))
                return false;

            
            return p.X >= 80 && p.X <= BorderX - 80 && p.Y >= 120 && p.Y <= BorderY - 120;
        }

        private bool TryFindAtlasNodeByCoord(int x, int y, out AtlasNodeDescription? nd)
        {
            if (_nodeByCoord.TryGetValue((x, y), out nd) && nd?.Element is not null)
                return true;

            for (int i = 0; i < _atlasNodes.Length; i++)
            {
                var n = _atlasNodes[i];
                if (n?.Element is null) continue;
                var c = n.Coordinate;
                if (c.X == x && c.Y == y)
                {
                    nd = n;
                    return true;
                }
            }

            nd = null;
            return false;
        }

        private static void InjectAtlasDrag(int startX, int startY, Vector2 drag, int steps)
        {
            SetCursorPos(startX, startY);
            Thread.Sleep(18);

            mouse_event(MouseEventLeftDown, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(25);

            float lastX = 0f;
            float lastY = 0f;
            for (int i = 1; i <= steps; i++)
            {
                float t = i / (float)steps;
                
                float eased = t * t * (3f - 2f * t);
                float curX = drag.X * eased;
                float curY = drag.Y * eased;
                int dx = (int)Math.Round(curX - lastX);
                int dy = (int)Math.Round(curY - lastY);
                lastX += dx;
                lastY += dy;

                if (dx != 0 || dy != 0)
                    mouse_event(MouseEventMove, dx, dy, 0, UIntPtr.Zero);

                Thread.Sleep(8);
            }

            Thread.Sleep(20);
            mouse_event(MouseEventLeftUp, 0, 0, 0, UIntPtr.Zero);
        }

        private bool TryGetTowerRangeOrigin(out Vector2i origin)
        {
            origin = _towerRangeOrigin;
            return _towerRangeActive;
        }

        private void AddWaypoint(AtlasNodeDescription nd)
        {
            AddWaypoint(nd, null, false);
        }

        private void AddWaypoint(AtlasNodeDescription nd, string? displayName)
        {
            AddWaypoint(nd, displayName, false);
        }

        private void AddWaypoint(AtlasNodeDescription nd, string? displayName, bool route)
        {
            AddWaypoint(nd, displayName, route, false, null, null);
        }

        private void AddWaypoint(AtlasNodeDescription nd, string? displayName, bool route, bool autoFavoriteMap, string? favoriteMapName, Color? colorOverride)
        {
            if (!TryGetCoordinate(nd, out var c)) return;
            var wps = Settings.Waypoints;
            for (int i = 0; i < wps.Count; i++)
            {
                if (wps[i].X != c.X || wps[i].Y != c.Y)
                    continue;

                var existing = wps[i];
                bool changed = false;

                if (route && (!existing.Enabled || !existing.Selected))
                {
                    existing.Enabled = true;
                    existing.Selected = true;
                    changed = true;
                }

                if (autoFavoriteMap && existing.AutoFavoriteMap)
                {
                    var favoriteName = favoriteMapName?.Trim() ?? string.Empty;
                    var display = !string.IsNullOrWhiteSpace(displayName) ? displayName.Trim() : favoriteName;
                    int favoriteColor = (colorOverride ?? Settings.FavoriteMapWaypointColor.Value).ToArgb();

                    if (!existing.Enabled)
                    {
                        existing.Enabled = true;
                        changed = true;
                    }

                    if (!existing.ShowLabel)
                    {
                        existing.ShowLabel = true;
                        changed = true;
                    }

                    if (existing.ColorArgb != favoriteColor)
                    {
                        existing.ColorArgb = favoriteColor;
                        changed = true;
                    }

                    if (!string.IsNullOrWhiteSpace(display) && !string.Equals(existing.Name, display, StringComparison.Ordinal))
                    {
                        existing.Name = display;
                        changed = true;
                    }

                    if (!string.Equals(existing.FavoriteMapName, favoriteName, StringComparison.OrdinalIgnoreCase))
                    {
                        existing.FavoriteMapName = favoriteName;
                        changed = true;
                    }
                }

                if (changed)
                {
                    wps[i] = existing;
                    if (existing.Selected)
                        SyncSelectedWaypoint();
                }

                return;
            }


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
            catch {  }


            int towersCount = TryCountNearbyTowers(c);
            var resolvedName = !string.IsNullOrWhiteSpace(displayName)
                ? displayName.Trim()
                : (Utility.TryGetAnyMapName(nd, out var nm) ? (nm ?? string.Empty) : string.Empty);

            var wp = new AtlasWaypoint
            {
                X = c.X,
                Y = c.Y,
                PositionX = camX,
                PositionY = camY,
                TowersCount = towersCount,
                ColorArgb = (colorOverride ?? Settings.DefaultWaypointColor.Value).ToArgb(),
                
                
                Selected = route,
                Enabled = true,
                ShowLabel = true,
                Name = resolvedName,
                AutoFavoriteMap = autoFavoriteMap,
                FavoriteMapName = favoriteMapName?.Trim() ?? string.Empty
            };
            wps.Add(wp);
            if (route)
                SyncSelectedWaypoint();
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
                if (wps[i].Enabled && wps[i].Selected)
                {
                    _selectedWaypointCoord = (wps[i].X, wps[i].Y);
                    return;
                }
            }
        }

        private IEnumerable<(int x, int y)> EnumerateSelectedWaypointCoords()
        {
            var wps = Settings.Waypoints;
            if (wps is null || wps.Count == 0)
                yield break;

            var seen = new HashSet<(int x, int y)>();
            for (int i = 0; i < wps.Count; i++)
            {
                var wp = wps[i];
                if (!wp.Enabled || !wp.Selected)
                    continue;

                var coord = (wp.X, wp.Y);
                if (seen.Add(coord))
                    yield return coord;
            }
        }

        private void ClearShortestPathCache()
        {
            _shortestPath.Clear();
            _shortestPaths.Clear();
            _shortestPathStepByCoord.Clear();
            _atlasRouteStepDistanceByCoord.Clear();
            _shortestPathStableScreenByCoord.Clear();
        }

        private void RecomputeShortestPathIfNeeded()
        {
            ClearShortestPathCache();
            SyncSelectedWaypoint();

            if (!Settings.DrawShortestPath.Value) return;
            if (_pathGraphAdjacency.Count == 0) return;

            BuildAtlasRouteStepDistances(_atlasRouteStepDistanceByCoord);

            foreach (var target in EnumerateSelectedWaypointCoords())
            {
                if (!_pathGraphNodes.ContainsKey(target))
                    continue;

                var path = new List<(int x, int y)>(64);
                BfsFromAtlasFrontier(target, path);
                if (path.Count < 2)
                    continue;

                _shortestPaths.Add(path);

                for (int step = 0; step < path.Count; step++)
                {
                    var coord = path[step];
                    if (!_shortestPathStepByCoord.TryGetValue(coord, out var oldStep) || step < oldStep)
                        _shortestPathStepByCoord[coord] = step;
                }

                
                if (_shortestPath.Count == 0)
                    _shortestPath.AddRange(path);
            }
        }

        private bool TryGetAtlasRouteSteps(int x, int y, out int steps)
        {
            steps = 0;
            if (!Settings.DrawShortestPath.Value)
                return false;

            var coord = (x, y);
            if (_shortestPathStepByCoord.TryGetValue(coord, out steps))
                return true;

            return _atlasRouteStepDistanceByCoord.TryGetValue(coord, out steps);
        }

        private void BuildAtlasRouteStepDistances(Dictionary<(int x, int y), int> distOut)
        {
            distOut.Clear();
            if (_pathGraphNodes.Count == 0 || _pathGraphAdjacency.Count == 0)
                return;

            var q = new Queue<(int x, int y)>(512);
            bool haveConnections = _pathGraphAdjacency.Count > 0;

            void Seed(Func<PathGraphNode, bool> predicate)
            {
                foreach (var node in _pathGraphNodes.Values)
                {
                    if (!predicate(node))
                        continue;

                    if (haveConnections && !node.HasConnections)
                        continue;

                    if (!distOut.TryAdd(node.Coord, 0))
                        continue;

                    q.Enqueue(node.Coord);
                }
            }

            
            
            Seed(n => n.Visited);
            if (q.Count == 0)
                Seed(n => n.Active);
            if (q.Count == 0)
                Seed(n => n.Unlocked);

            while (q.Count > 0)
            {
                var cur = q.Dequeue();
                if (!_pathGraphAdjacency.TryGetValue(cur, out var nbs))
                    continue;

                int nextDist = distOut[cur] + 1;
                foreach (var nb in nbs)
                {
                    if (!_pathGraphNodes.ContainsKey(nb))
                        continue;

                    if (!distOut.TryAdd(nb, nextDist))
                        continue;

                    q.Enqueue(nb);
                }
            }
        }

        private void BfsFromAtlasFrontier((int x, int y) goal, List<(int x, int y)> pathOut)
        {
            
            
            
            var q = new Queue<(int x, int y)>(512);
            var dist = new Dictionary<(int x, int y), int>(_pathGraphNodes.Count);
            var prev = new Dictionary<(int x, int y), (int x, int y)>(_pathGraphNodes.Count);

            bool haveConnections = _pathGraphAdjacency.Count > 0;

            void Seed(Func<PathGraphNode, bool> predicate)
            {
                foreach (var node in _pathGraphNodes.Values)
                {
                    if (!predicate(node))
                        continue;

                    if (haveConnections && !node.HasConnections)
                        continue;

                    if (!dist.TryAdd(node.Coord, 0))
                        continue;

                    prev[node.Coord] = node.Coord;
                    q.Enqueue(node.Coord);
                }
            }

            
            Seed(n => n.Visited);

            if (q.Count == 0)
                Seed(n => n.Active);
            if (q.Count == 0)
                Seed(n => n.Unlocked);

            while (q.Count > 0)
            {
                var cur = q.Dequeue();
                if (cur.Equals(goal))
                    break;

                if (!_pathGraphAdjacency.TryGetValue(cur, out var nbs))
                    continue;

                int nextDist = dist[cur] + 1;
                foreach (var nb in nbs)
                {
                    if (!_pathGraphNodes.ContainsKey(nb))
                        continue;

                    if (!dist.TryAdd(nb, nextDist))
                        continue;

                    prev[nb] = cur;
                    q.Enqueue(nb);
                }
            }

            if (!prev.ContainsKey(goal))
                return;

            var stack = new Stack<(int x, int y)>();
            var it = goal;
            int guard = 0;

            while (true)
            {
                stack.Push(it);
                if (!prev.TryGetValue(it, out var parent) || parent.Equals(it))
                    break;

                it = parent;
                if (++guard > 100000)
                    return;
            }

            while (stack.Count > 0)
                pathOut.Add(stack.Pop());
        }

    }
}

