using System;
using System.Collections.Generic;
using ExileCore2.PoEMemory.Elements.AtlasElements;

namespace AtlasBiomeHighlighter
{
    public partial class AtlasBiomeHighlighter
    {
        // Cached per-node data rebuilt on the (throttled) ScreenRefreshMs tick.
        private readonly List<NodeRenderInfo> _visibleNodeInfos = new(256);

        // Cached flags by coordinate for graph rendering/pathfinding (connections, etc.).
        private readonly Dictionary<(int x, int y), NodeStatus> _statusByCoord = new(1024);

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
                string? uniqueName)
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
        }

        /// <summary>
        /// Rebuilds visible-node caches and per-coordinate status caches.
        /// This is called from Tick() on the ScreenRefreshMs cadence, not per-frame.
        /// </summary>
        private void RebuildVisibleCaches()
        {
            _visibleNodes.Clear();
            _visibleNodeInfos.Clear();
            _statusByCoord.Clear();

            // Fill coord status cache for ALL nodes (cheap enough at the throttled rate; avoids per-frame memory reads).
            for (int i = 0; i < _atlasNodes.Length; i++)
            {
                var nd = _atlasNodes[i];
                if (nd?.Element is null) continue;

                bool unlocked = Utility.TryIsUnlocked(nd, out var uu) && uu;
                bool visited = Utility.TryIsVisited(nd, out var vv) && vv;
                bool completed = Utility.IsMapCompleted(nd);

                var c = nd.Coordinate;
                _statusByCoord[(c.X, c.Y)] = new NodeStatus(unlocked, visited, completed);

                if (!Utility.IsInScreen(nd, BorderX, BorderY))
                    continue;

                _visibleNodes.Add(nd);

                // NodeRenderInfo caches the things we otherwise would query repeatedly every Render().
                var biome = Utility.TryGetBiome(nd);
                var biomeDisplay = BiomeUtils.Display(biome);

                var sflags = Utility.TryGetSpecialFlags(nd);

                bool attempted = Utility.IsMapAttempted(nd);
                bool locked = Utility.IsMapLocked(nd);

                string? mapName = null;
                if (Utility.TryGetAnyMapName(nd, out var mn) && !string.IsNullOrWhiteSpace(mn))
                    mapName = mn;

                string? uniqueName = null;
                if ((sflags & Utility.SpecialFlags.UniqueMap) != 0 && Utility.TryGetUniqueNameFromId(nd, out var un) && !string.IsNullOrWhiteSpace(un))
                    uniqueName = un;

                _visibleNodeInfos.Add(new NodeRenderInfo(
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
                    uniqueName));
            }
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
