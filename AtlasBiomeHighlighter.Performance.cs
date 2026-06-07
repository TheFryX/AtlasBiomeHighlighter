using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ExileCore2.PoEMemory.Elements.AtlasElements;

namespace AtlasBiomeHighlighter
{
    public partial class AtlasBiomeHighlighter
    {
        
        private List<NodeRenderInfo> _visibleNodeInfos = new(256);

        
        
        private List<AtlasNodeDescription> _visibleNodesNext = new(256);
        private List<NodeRenderInfo> _visibleNodeInfosNext = new(256);
        private Dictionary<(int x, int y), NodeStatus> _statusByCoordNext = new(1024);
        private int _visibleCacheBuildIndex;
        private int _visibleCacheBuildAtlasLength = -1;
        private bool _visibleCacheBuildInProgress;
        private const int VisibleCacheBuildBudgetPerTick = 96;
        private const int VisibleCacheBuildMinItemsBeforeTimeSlice = 16;
        private const double VisibleCacheBuildTimeBudgetMs = 2.25;

        
        private Dictionary<(int x, int y), NodeStatus> _statusByCoord = new(1024);

        
        
        
        private readonly List<ConnectionRenderSegment> _visibleConnectionSegments = new(768);

        private readonly struct ConnectionRenderSegment
        {
            public ConnectionRenderSegment(
                AtlasNodeDescription source,
                AtlasNodeDescription target,
                bool sourceUnlocked,
                bool targetUnlocked,
                bool sourceVisited,
                bool targetVisited)
            {
                Source = source;
                Target = target;
                SourceUnlocked = sourceUnlocked;
                TargetUnlocked = targetUnlocked;
                SourceVisited = sourceVisited;
                TargetVisited = targetVisited;
            }

            public AtlasNodeDescription Source { get; }
            public AtlasNodeDescription Target { get; }
            public bool SourceUnlocked { get; }
            public bool TargetUnlocked { get; }
            public bool SourceVisited { get; }
            public bool TargetVisited { get; }
        }

        private readonly struct NodeStatus
        {
            public NodeStatus(bool unlocked, bool visited, bool completed)
            {
                Unlocked = unlocked;
                Visited = visited;
                Completed = completed;
            }

            public bool Unlocked { get; }
            public bool Visited { get; }
            public bool Completed { get; }
        }

        private readonly struct NodeRenderInfo
        {
            public NodeRenderInfo(
                AtlasNodeDescription node,
                Biome biome,
                string biomeDisplay,
                Utility.SpecialFlags specialFlags,
                bool completed,
                bool attempted,
                bool locked,
                bool visited,
                bool unlocked,
                string? mapName,
                string? uniqueName,
                string nameToken,
                string idToken,
                string[] mechanicNames,
                string[] mechanicTokens)
            {
                Node = node;
                Biome = biome;
                BiomeDisplay = biomeDisplay;
                SpecialFlags = specialFlags;
                Completed = completed;
                Attempted = attempted;
                Locked = locked;
                Visited = visited;
                Unlocked = unlocked;
                MapName = mapName;
                UniqueName = uniqueName;
                NameToken = nameToken;
                IdToken = idToken;
                MechanicNames = mechanicNames ?? Array.Empty<string>();
                MechanicTokens = mechanicTokens ?? Array.Empty<string>();
            }

            public AtlasNodeDescription Node { get; }
            public Biome Biome { get; }
            public string BiomeDisplay { get; }
            public Utility.SpecialFlags SpecialFlags { get; }
            public bool Completed { get; }
            public bool Attempted { get; }
            public bool Locked { get; }
            public bool Visited { get; }
            public bool Unlocked { get; }
            public string? MapName { get; }
            public string? UniqueName { get; }
            public string NameToken { get; }
            public string IdToken { get; }
            public string[] MechanicNames { get; }
            public string[] MechanicTokens { get; }
        }

        
        
        
        
        
        
        private bool RebuildVisibleCaches()
        {
            if (_atlasNodes.Length == 0)
            {
                _visibleNodes.Clear();
                _visibleNodeInfos.Clear();
                _statusByCoord.Clear();
                _visibleConnectionSegments.Clear();
                ResetVisibleCacheBuild();
                return true;
            }

            if (!_visibleCacheBuildInProgress || _visibleCacheBuildAtlasLength != _atlasNodes.Length)
            {
                _visibleNodesNext.Clear();
                _visibleNodeInfosNext.Clear();
                _statusByCoordNext.Clear();
                _visibleCacheBuildIndex = 0;
                _visibleCacheBuildAtlasLength = _atlasNodes.Length;
                _visibleCacheBuildInProgress = true;
            }

            
            
            long buildStartTimestamp = Stopwatch.GetTimestamp();
            int processed = 0;
            int i = _visibleCacheBuildIndex;

            while (i < _atlasNodes.Length && processed < VisibleCacheBuildBudgetPerTick)
            {
                var nd = _atlasNodes[i];
                i++;
                processed++;

                if (nd?.Element is null)
                    continue;

                bool unlocked = Utility.TryIsUnlocked(nd, out var uu) && uu;
                bool visited = Utility.TryIsVisited(nd, out var vv) && vv;
                bool completed = Utility.IsMapCompleted(nd);

                var c = nd.Coordinate;
                _statusByCoordNext[(c.X, c.Y)] = new NodeStatus(unlocked, visited, completed);

                if (Utility.IsInScreen(nd, BorderX, BorderY))
                {
                    _visibleNodesNext.Add(nd);

                    
                    var biome = Utility.TryGetBiome(nd);
                    var biomeDisplay = BiomeUtils.Display(biome);
                    var sflags = Utility.TryGetSpecialFlags(nd);

                    bool attempted = Utility.IsMapAttempted(nd);
                    bool locked = Utility.IsMapLocked(nd);

                    string? mapName = null;
                    string nameToken = string.Empty;
                    if (Utility.TryGetAnyMapName(nd, sflags, out var mn) && !string.IsNullOrWhiteSpace(mn))
                    {
                        mapName = mn;
                        nameToken = Utility.NormalizeToken(mn);
                    }

                    string idToken = string.Empty;
                    if (Utility.TryGetNodeId(nd, out var nodeId) && !string.IsNullOrWhiteSpace(nodeId))
                        idToken = Utility.NormalizeToken(nodeId);

                    string? uniqueName = null;
                    if ((sflags & Utility.SpecialFlags.UniqueMap) != 0 && Utility.TryGetUniqueNameFromId(nd, out var un) && !string.IsNullOrWhiteSpace(un))
                        uniqueName = un;

                    var mechanicNames = Utility.TryGetMechanicNames(nd);
                    var mechanicArray = mechanicNames.Count == 0
                        ? Array.Empty<string>()
                        : mechanicNames.ToArray();
                    var mechanicTokenArray = mechanicArray.Length == 0
                        ? Array.Empty<string>()
                        : mechanicArray.Select(Utility.NormalizeToken).Where(t => t.Length != 0).Distinct(StringComparer.Ordinal).ToArray();

                    _visibleNodeInfosNext.Add(new NodeRenderInfo(
                        nd,
                        biome,
                        biomeDisplay,
                        sflags,
                        completed,
                        attempted,
                        locked,
                        visited,
                        unlocked,
                        mapName,
                        uniqueName,
                        nameToken,
                        idToken,
                        mechanicArray,
                        mechanicTokenArray));
                }

                if (processed >= VisibleCacheBuildMinItemsBeforeTimeSlice && (processed & 15) == 0)
                {
                    double elapsedMs = (Stopwatch.GetTimestamp() - buildStartTimestamp) * 1000.0 / Stopwatch.Frequency;
                    if (elapsedMs >= VisibleCacheBuildTimeBudgetMs)
                        break;
                }
            }

            _visibleCacheBuildIndex = i;
            if (_visibleCacheBuildIndex < _atlasNodes.Length)
                return false;

            PublishVisibleCaches();
            ResetVisibleCacheBuild();
            return true;
        }

        private void PublishVisibleCaches()
        {
            
            (_visibleNodes, _visibleNodesNext) = (_visibleNodesNext, _visibleNodes);
            (_visibleNodeInfos, _visibleNodeInfosNext) = (_visibleNodeInfosNext, _visibleNodeInfos);
            (_statusByCoord, _statusByCoordNext) = (_statusByCoordNext, _statusByCoord);

            RebuildVisibleConnectionSegments();
        }

        private void RebuildVisibleConnectionSegments()
        {
            _visibleConnectionSegments.Clear();

            if (_neighborsByCoord.Count == 0 || _visibleNodeInfos.Count == 0)
                return;

            for (int i = 0; i < _visibleNodeInfos.Count; i++)
            {
                var info = _visibleNodeInfos[i];
                var nd = info.Node;
                if (nd?.Element is null)
                    continue;

                var c = nd.Coordinate;
                var srcKey = (x: c.X, y: c.Y);
                if (!_neighborsByCoord.TryGetValue(srcKey, out var neighbors))
                    continue;

                for (int j = 0; j < neighbors.Count; j++)
                {
                    var dstKey = neighbors[j];

                    
                    if (dstKey.x < srcKey.x || (dstKey.x == srcKey.x && dstKey.y <= srcKey.y))
                        continue;

                    if (!_nodeByCoord.TryGetValue(dstKey, out var dst) || dst?.Element is null)
                        continue;

                    TryGetStatus(dstKey, out var dstUnlocked, out var dstVisited, out _);
                    _visibleConnectionSegments.Add(new ConnectionRenderSegment(
                        nd,
                        dst,
                        info.Unlocked,
                        dstUnlocked,
                        info.Visited,
                        dstVisited));
                }
            }
        }

        private void ResetVisibleCacheBuild()
        {
            _visibleNodesNext.Clear();
            _visibleNodeInfosNext.Clear();
            _statusByCoordNext.Clear();
            _visibleCacheBuildIndex = 0;
            _visibleCacheBuildAtlasLength = -1;
            _visibleCacheBuildInProgress = false;
        }

        private bool TryGetStatus((int x, int y) coord, out bool unlocked, out bool visited, out bool completed)
        {
            if (_statusByCoord.TryGetValue(coord, out var s))
            {
                unlocked = s.Unlocked;
                visited = s.Visited;
                completed = s.Completed;
                return true;
            }
            unlocked = visited = completed = false;
            return false;
        }
    }
}
