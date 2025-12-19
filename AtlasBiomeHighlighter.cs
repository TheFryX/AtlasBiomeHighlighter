using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ExileCore2;
using ExileCore2.PoEMemory.Elements.AtlasElements;

namespace AtlasBiomeHighlighter
{
    public partial class AtlasBiomeHighlighter : BaseSettingsPlugin<AtlasBiomeSettings>
    {
        private ExileCore2.PoEMemory.Elements.AtlasElements.AtlasPanel? _atlasPanel;
        private AtlasNodeDescription[] _atlasNodes = Array.Empty<AtlasNodeDescription>();
        private readonly System.Collections.Generic.List<AtlasNodeDescription> _visibleNodes = new();

        private readonly Stopwatch _atlasRefreshSw = new();
        private readonly Stopwatch _screenRefreshSw = new();

        private const int BorderX = 1920;
        private const int BorderY = 1080;

        // ===== Preferred maps matching caches =====
        // Keep Render() hot-path allocation-free by caching enabled PreferredMaps tokens.
        private int _preferredCacheHash;
        private string[] _preferredTokensList = Array.Empty<string>();
        private HashSet<string> _preferredTokensExact = new(StringComparer.Ordinal);

        // Maps a normalized preferred token to a cached tag label (e.g. "[Preferred Savannah]").
        private readonly Dictionary<string, string> _preferredTokenToTag = new(StringComparer.Ordinal);

        // Cache normalized node tokens by Atlas UI element address.
        private readonly Dictionary<long, NodeTokenCache> _nodeTokenCache = new(512);
        private int _nodeTokenCacheFrame;
        private const int NodeTokenCacheMaxEntries = 4096;

        private readonly struct NodeTokenCache
        {
            public NodeTokenCache(string nameToken, string idToken, int lastSeenFrame)
            {
                NameToken = nameToken;
                IdToken = idToken;
                LastSeenFrame = lastSeenFrame;
            }

            public string NameToken { get; }
            public string IdToken { get; }
            public int LastSeenFrame { get; }
        }

        public override bool Initialise()
        {
            _atlasRefreshSw.Start();
            _screenRefreshSw.Start();
            MigratePreferredGroupsIfNeeded();
            ResetAtlasCache();
            return true;
        }

        private void MigratePreferredGroupsIfNeeded()
        {
            // Backwards compatibility: older configs stored preferred selection in PreferredMaps (ToggleNodes).
            // Newer configs store selections inside PreferredMapGroups.
            if (Settings.PreferredMapGroups != null && Settings.PreferredMapGroups.Count > 0)
                return;

            var g = new PreferredMapGroup { Name = "Default", Enabled = true };
            foreach (var kv in Settings.PreferredMaps)
            {
                if (kv.Value?.Value == true)
                    g.Maps.Add(kv.Key);
            }

            Settings.PreferredMapGroups = new List<PreferredMapGroup> { g };
            // Leave the old toggles as-is (for users who downgrade), but logic will use groups.
        }

        private void ResetAtlasCache()
        {
            _atlasNodes = Array.Empty<AtlasNodeDescription>();
            _visibleNodes.Clear();
            _atlasRefreshSw.Restart();
            _screenRefreshSw.Restart();
        }

        private void EnsurePreferredCacheUpToDate()
        {
            // Compute a stable hash of enabled group selections.
            int h = 17;
            int enabledCount = 0;
            var groups = Settings.PreferredMapGroups;
            if (groups != null)
            {
                for (int gi = 0; gi < groups.Count; gi++)
                {
                    var g = groups[gi];
                    if (g == null || !g.Enabled) continue;
                    foreach (var key in g.Maps)
                    {
                        enabledCount++;
                        h = unchecked(h * 31 + StringComparer.OrdinalIgnoreCase.GetHashCode(key));
                    }
                }
            }
            h = unchecked(h * 31 + enabledCount);

            if (h == _preferredCacheHash) return;
            _preferredCacheHash = h;

            // Rebuild caches with minimal allocations.
            _preferredTokensExact.Clear();
            _preferredTokenToTag.Clear();
            var list = new List<string>(enabledCount);
            if (groups != null)
            {
                for (int gi = 0; gi < groups.Count; gi++)
                {
                    var g = groups[gi];
                    if (g == null || !g.Enabled) continue;
                    foreach (var key in g.Maps)
                    {
                        var token = Utility.PreferredKeyToToken(key);
                        if (token.Length == 0) continue;

                        if (_preferredTokensExact.Add(token))
                        {
                            list.Add(token);
                            // Cache display tag for labels (uses the first enabled occurrence).
                            var display = Utility.PreferredKeyToDisplayName(key);
                            _preferredTokenToTag[token] = display.Length == 0 ? "[Preferred]" : $"[Preferred {display}]";
                        }
                        else if (!_preferredTokenToTag.ContainsKey(token))
                        {
                            var display = Utility.PreferredKeyToDisplayName(key);
                            _preferredTokenToTag[token] = display.Length == 0 ? "[Preferred]" : $"[Preferred {display}]";
                        }
                    }
                }
            }
            _preferredTokensList = list.Count == 0 ? Array.Empty<string>() : list.ToArray();
        }

        private string GetPreferredTag(string? matchedToken)
        {
            if (matchedToken != null && matchedToken.Length != 0 && _preferredTokenToTag.TryGetValue(matchedToken, out var tag))
                return tag;
            return "[Preferred]";
        }

        private bool TryGetCachedNodeTokens(AtlasNodeDescription nd, out string nameToken, out string idToken)
        {
            nameToken = string.Empty;
            idToken = string.Empty;

            var elem = nd.Element;
            if (elem is null) return false;

            // Element.Address is stable for the lifetime of the UI element.
            long addr = elem.Address;
            _nodeTokenCacheFrame++;

            if (_nodeTokenCache.TryGetValue(addr, out var cached))
            {
                nameToken = cached.NameToken;
                idToken = cached.IdToken;
                // Refresh entry (struct is immutable; overwrite).
                _nodeTokenCache[addr] = new NodeTokenCache(nameToken, idToken, _nodeTokenCacheFrame);
                return nameToken.Length != 0 || idToken.Length != 0;
            }

            // Build tokens once.
            if (Utility.TryGetAnyMapName(nd, out var anyName) && !string.IsNullOrWhiteSpace(anyName))
                nameToken = Utility.NormalizeToken(anyName);

            if (Utility.TryGetNodeId(nd, out var nid) && !string.IsNullOrWhiteSpace(nid))
                idToken = Utility.NormalizeToken(nid);

            if (_nodeTokenCache.Count >= NodeTokenCacheMaxEntries)
            {
                // Opportunistic prune: remove old entries.
                // This keeps worst-case bounded without extra timers.
                var cutoff = _nodeTokenCacheFrame - 1024;
                var toRemove = new List<long>(64);
                foreach (var kv in _nodeTokenCache)
                {
                    if (kv.Value.LastSeenFrame < cutoff) toRemove.Add(kv.Key);
                }
                for (int i = 0; i < toRemove.Count; i++) _nodeTokenCache.Remove(toRemove[i]);
                if (_nodeTokenCache.Count >= NodeTokenCacheMaxEntries)
                    _nodeTokenCache.Clear();
            }

            _nodeTokenCache[addr] = new NodeTokenCache(nameToken, idToken, _nodeTokenCacheFrame);
            return nameToken.Length != 0 || idToken.Length != 0;
        }

        public override void Tick()
        {
            _atlasPanel = GameController?.IngameState?.IngameUi?.WorldMap?.AtlasPanel;
            if (_atlasPanel == null || !_atlasPanel.IsVisible) return;

            if (_atlasRefreshSw.ElapsedMilliseconds > Settings.AtlasRefreshMs.Value)
            {
                _atlasNodes = _atlasPanel.Descriptions?.ToArray() ?? Array.Empty<AtlasNodeDescription>();
                _atlasRefreshSw.Restart();
            }

            if (_screenRefreshSw.ElapsedMilliseconds > Settings.ScreenRefreshMs.Value)
            {
                // Prune token cache when atlas nodes change noticeably.
                // This is cheap and prevents cache growth across atlas refreshes.
                if (_nodeTokenCacheFrame > 10_000)
                {
                    _nodeTokenCacheFrame = 0;
                    _nodeTokenCache.Clear();
                }

                _visibleNodes.Clear();
                foreach (var nd in _atlasNodes)
                {
                    if (nd?.Element is null) continue;
                    if (Utility.IsInScreen(nd, BorderX, BorderY))
                        _visibleNodes.Add(nd);
                }
                _screenRefreshSw.Restart();
            }
        }
    }
}
