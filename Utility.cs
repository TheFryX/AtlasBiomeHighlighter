using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using ExileCore2.PoEMemory.Elements.AtlasElements;
using Vector2 = System.Numerics.Vector2;

namespace AtlasBiomeHighlighter
{
    internal static class Utility
    {
	        /// <summary>
	        /// Normalizes a user-facing or internal identifier into a comparison token:
	        /// lower-case, alphanumeric only (spaces/punctuation removed).
	        /// </summary>
	        public static string NormalizeToken(string? value)
	        {
	            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
	            var span = value.AsSpan();
	            // Fast path: if already simple, avoid allocating intermediary builders.
	            // We still allocate the final string.
	            Span<char> buf = span.Length <= 256 ? stackalloc char[span.Length] : new char[span.Length];
	            int n = 0;
	            for (int i = 0; i < span.Length; i++)
	            {
	                var ch = span[i];
	                if (char.IsLetterOrDigit(ch))
	                    buf[n++] = char.ToLowerInvariant(ch);
	            }
	            return n == 0 ? string.Empty : new string(buf.Slice(0, n));
	        }

	        /// <summary>
	        /// Takes a PreferredMaps dictionary key (option label) and returns the token used for matching.
	        /// Convention: everything before '-' is treated as the map name; suffix is a tag (e.g., "- Best").
	        /// </summary>
	        public static string PreferredKeyToToken(string key)
	        {
	            if (string.IsNullOrWhiteSpace(key)) return string.Empty;
	            var main = key;
	            var dash = key.IndexOf('-');
	            if (dash >= 0) main = key.Substring(0, dash);
	            return NormalizeToken(main);
	        }


                /// <summary>
                /// Extracts a human-friendly display name from a Preferred maps option key.
                /// Example: "Savannah - Best" -> "Savannah".
                /// </summary>
                public static string PreferredKeyToDisplayName(string key)
                {
                    if (string.IsNullOrWhiteSpace(key)) return string.Empty;
                    var main = key;
                    var dash = key.IndexOf('-');
                    if (dash >= 0) main = key.Substring(0, dash);
                    return main.Trim();
                }

	        public static bool TokenContainsEitherWay(string a, string b)
	        {
	            if (a.Length == 0 || b.Length == 0) return false;
	            return a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal);
	        }
        public static bool IsInScreen(AtlasNodeDescription node, int width, int height)
        {
            var c = node.Element.Center;
            return c.X > 0 && c.X < width && c.Y > 0 && c.Y < height;
        }

        private static object? GetMember(object? obj, string name)
        {
            if (obj is null) return null;
            var t = obj.GetType();
            var p = t.GetProperty(name, BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.IgnoreCase);
            if (p != null) return p.GetValue(obj);
            var f = t.GetField(name, BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.IgnoreCase);
            if (f != null) return f.GetValue(obj);
            return null;
        }

        private static string? ExtractString(object? v)
        {
            if (v is null) return null;
            if (v is string s) return s;
            try { return v.ToString(); } catch { return null; }
        }

        
        private static readonly System.Collections.Generic.Dictionary<string,string> UniqueMapNames =
            new(System.StringComparer.OrdinalIgnoreCase)
            {
                ["MapUniqueCastaway"] = "Castaway",
                ["MapUniqueUntaintedParadise"] = "Untainted Paradise",
                ["MapUniqueWildwood"] = "The Viridian Wildwood",
                ["MapUniqueVault"] = "Vaults of Kamasa",
                ["MapUniqueSelenite"] = "The Silent Cave",
                ["MapUniqueLake"] = "The Fractured Lake",
                ["MapUniqueMegalith"] = "The Ezomyte Megaliths",
                // Unique bosses (if we ever want label override)
                ["MapCavernCity"] = "Sacred Reservoir",
                ["MapUberBoss_JadeCitadel"] = "The Jade Isles",
                ["MapVaalVault"] = "Sealed Vault",
                ["MapDerelictMansion"] = "Derelict Mansion",
                ["MapUberBoss_IronCitadel"] = "The Iron Citadel",
                ["MapUberBoss_StoneCitadel"] = "The Stone Citadel",
                ["MapUberBoss_CopperCitadel"] = "The Copper Citadel",
            };


        public static Biome TryGetBiome(AtlasNodeDescription nd)
        {
            // Prefer direct .Biome if available
            var candidate = GetMember(nd.Element, "Biome");
            var name = ExtractString(candidate);
            var parsed = BiomeUtils.ParseOrUnknown(name);
            if (parsed != Biome.Unknown) return parsed;

            // Try common hops
            foreach (var hopName in new[] {"AtlasPanelNode", "Node", "Area"})
            {
                var hop = GetMember(nd.Element, hopName);
                if (hop == null) continue;
                var n2 = ExtractString(GetMember(hop, "Biome"));
                var p2 = BiomeUtils.ParseOrUnknown(n2);
                if (p2 != Biome.Unknown) return p2;
            }

            // Fallback: Id sometimes carries the hint (citadels etc.)
            var id = ExtractString(GetMember(nd.Element, "Id")) ?? ExtractString(GetMember(GetMember(nd.Element,"Area"), "Id"));
            var pid = BiomeUtils.ParseOrUnknown(id);
            if (pid != Biome.Unknown) return pid;

            return Biome.Unknown;
        }

        [System.Flags]
        public enum SpecialFlags
        {
            None = 0,
            DeadlyBoss = 1 << 0,
            CorruptedNexus = 1 << 1,
            UniqueMap = 1 << 2,
            AbyssOverrun = 1 << 4,
            MomentofZen = 1 << 5,
            Cleansed = 1 << 6,
        }

        public static SpecialFlags TryGetSpecialFlags(AtlasNodeDescription nd)
        {
            try
            {
                SpecialFlags flags = SpecialFlags.None;
                var root = nd.Element;
                if (root == null) return flags;

                // Fast ID checks
                string? id = ExtractString(GetMember(root, "Id")) ?? ExtractString(GetMember(GetMember(root, "Area"), "Id"));
                if (!string.IsNullOrWhiteSpace(id))
                {
                    if (id.StartsWith("MapUnique", System.StringComparison.OrdinalIgnoreCase))
                        flags |= SpecialFlags.UniqueMap;
                }

                // Deep scan for strict markers
                var stack = new System.Collections.Generic.Stack<object?>();
                stack.Push(root);
                while (stack.Count > 0)
                {
                    var cur = stack.Pop();
                    if (cur == null) continue;
                    var t = cur.GetType();

                    foreach (var p in t.GetProperties(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic))
                    {
                        if (p.GetIndexParameters().Length > 0) continue;
                        var nameU = p.Name.ToUpperInvariant();
                        if (nameU.Contains("TEXT") || nameU.Contains("LABEL") || nameU.Contains("TOOLTIP") || nameU.Contains("STRING") || nameU.Contains("CAPTION") || nameU.Contains("TEXTURE") || nameU.Contains("ICON") || nameU.Contains("TEXTURENAME"))
                        {
                            string? val = null;
                            try { val = ExtractString(p.GetValue(cur)); } catch {}
                            ClassifyStrict(val, ref flags);
                        }
                    }
                    foreach (var f in t.GetFields(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic))
                    {
                        var nameU = f.Name.ToUpperInvariant();
                        if (nameU.Contains("TEXT") || nameU.Contains("LABEL") || nameU.Contains("TOOLTIP") || nameU.Contains("STRING") || nameU.Contains("CAPTION") || nameU.Contains("TEXTURE") || nameU.Contains("ICON") || nameU.Contains("TEXTURENAME"))
                        {
                            string? val = null;
                            try { val = ExtractString(f.GetValue(cur)); } catch {}
                            ClassifyStrict(val, ref flags);
                        }
                    }

                    foreach (var p in t.GetProperties(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic))
                    {
                        if (p.Name.Equals("Children", System.StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Child"))
                        {
                            try
                            {
                                var v = p.GetValue(cur);
                                if (v is System.Collections.IEnumerable en)
                                    foreach (var it in en) stack.Push(it);
                            }
                            catch {}
                        }
                    }
                }

                return flags;
            }
            catch { return SpecialFlags.None; }
        }

        private static void ClassifyStrict(string? s, ref SpecialFlags flags)
        {
            if (string.IsNullOrWhiteSpace(s)) return;
            var u = s.ToUpperInvariant();

            // Strict Deadly Map Boss markers:
            //  - TextureName ends with AtlasIconContentMapBossSpecial.dds
            //  - text contains EXACT 'DEADLY MAP BOSS'
            if (u.Contains("ATLASICONCONTENTMAPBOSSSPECIAL")) flags |= SpecialFlags.DeadlyBoss;
            if (u.Contains("DEADLY MAP BOSS")) flags |= SpecialFlags.DeadlyBoss;

            // Corrupted Nexus icon (per screenshot): AtlasIconContentCorruptionNexus.dds
            if (u.Contains("ATLASICONCONTENTCORRUPTIONNEXUS") || u.Contains("ATLASICONCONTENTCORRUPTEDNEXUS")) { flags |= SpecialFlags.CorruptedNexus; flags &= ~SpecialFlags.UniqueMap; }

            // Abyss overrun icon
            if (u.Contains("ATLASICONCONTENTABYSSOVERRUN")) flags |= SpecialFlags.AbyssOverrun;
            // Trader (Moment of Zen / Merchant)
            if (u.Contains("ATLASICONCONTENTTRADER")) { flags |= SpecialFlags.MomentofZen; flags &= ~SpecialFlags.UniqueMap; }

            // Cleansed/Sanctified area icon (per screenshot): AtlasIconContentSanctification.dds
            if (u.Contains("ATLASICONCONTENTSANCTIFICATION")) { flags |= SpecialFlags.Cleansed; flags &= ~SpecialFlags.UniqueMap; }
        }

        public static bool TryGetUniqueNameFromId(AtlasNodeDescription nd, out string? display)
        {
            display = null;
            string? id = ExtractString(GetMember(nd.Element, "Id")) ?? ExtractString(GetMember(GetMember(nd.Element, "Area"), "Id"));
            if (string.IsNullOrWhiteSpace(id)) return false;
            if (UniqueMapNames.TryGetValue(id, out var name)) { display = name; return true; }
            return false;
        }

        public static bool TryGetAnyMapName(AtlasNodeDescription nd, out string? name)
        {
            name = null;

	            // Some special atlas content nodes have no visible label text (Text/TextNoTags == null).
	            // For Preferred maps matching we still want a stable name.
	            var sflags = TryGetSpecialFlags(nd);
	            if ((sflags & SpecialFlags.CorruptedNexus) != 0)
	            {
	                name = "Corrupted Nexus";
	                return true;
	            }
	            if ((sflags & SpecialFlags.Cleansed) != 0)
	            {
	                name = "Cleansed";
	                return true;
	            }

            // 1) Unique Id mapping
            if (TryGetUniqueNameFromId(nd, out var unm) && !string.IsNullOrWhiteSpace(unm))
            {
                name = unm;
                return true;
            }

            // 1.5) Area.Name for normal maps
            try
            {
                var rootEl = nd.Element;
                var area = GetMember(rootEl, "Area");
                var areaName = ExtractString(GetMember(area, "Name"));
                if (!string.IsNullOrWhiteSpace(areaName))
                {
                    name = areaName.Trim();
                    return true;
                }
            } catch {}

            try
            {
                var root = nd.Element;
                if (root == null) return false;
                var stack = new System.Collections.Generic.Stack<object?>();
                stack.Push(root);

                bool IsNameProp(string nm)
                {
                    var U = nm.ToUpperInvariant();
                    if (U == "TEXTURENAME" || U.Contains("TEXTURE") || U.Contains("ICON")) return false;
                    return U.Contains("TITLE") || U.Contains("NAME") || U.Contains("HEADER") || U.Contains("CAPTION") || U.Contains("LABELTEXT");
                }

                string? AsString(object? v)
                {
                    if (v is null) return null;
                    if (v is string s) return s;
                    try { return v.ToString(); } catch { return null; }
                }

                while (stack.Count > 0)
                {
                    var cur = stack.Pop();
                    if (cur == null) continue;
                    var t = cur.GetType();

                    foreach (var p in t.GetProperties(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic))
                    {
                        if (p.GetIndexParameters().Length > 0) continue;
                        if (IsNameProp(p.Name))
                        {
                            try
                            {
                                var s = AsString(p.GetValue(cur));
                                if (!string.IsNullOrWhiteSpace(s)) { name = s.Trim(); return true; }
                            } catch {}
                        }
                    }
                    foreach (var f in t.GetFields(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic))
                    {
                        if (IsNameProp(f.Name))
                        {
                            try
                            {
                                var s = AsString(f.GetValue(cur));
                                if (!string.IsNullOrWhiteSpace(s)) { name = s.Trim(); return true; }
                            } catch {}
                        }
                    }

                    // traverse children
                    foreach (var p in t.GetProperties(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic))
                    {
                        if (p.Name.Equals("Children", System.StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Child"))
                        {
                            try
                            {
                                var v = p.GetValue(cur);
                                if (v is System.Collections.IEnumerable en)
                                    foreach (var it in en) stack.Push(it);
                            } catch {}
                        }
                    }
                }
            } catch {}

            return false;
        }
    

        public static Vector2 Offset(Vector2 v, float dx, float dy) => new(v.X + dx, v.Y + dy);

        public static Color WithOpacity(Color baseColor, float opacity01)
        {
            var a = (int)Math.Round(Math.Clamp(opacity01, 0f, 1f) * 255f);
            return Color.FromArgb(a, baseColor);
        }

        public static bool TryGetNodeId(AtlasNodeDescription nd, out string? id)
        {
            id = null;
            try
            {
                var root = nd.Element;
                if (root == null) return false;
                id = ExtractString(GetMember(root, "Id")) ?? ExtractString(GetMember(GetMember(root, "Area"), "Id"));
                return !string.IsNullOrWhiteSpace(id);
            }
            catch { return false; }
        }

        public static bool IsMapCompleted(AtlasNodeDescription nd)
        {
            try
            {
                var root = nd.Element;
                if (root == null) return false;
                var isCompletedObj = GetMember(root, "IsCompleted");
                return isCompletedObj is bool completed && completed;
            }
            catch { return false; }
        }

        public static bool IsMapAttempted(AtlasNodeDescription nd)
        {
            try
            {
                var root = nd.Element;
                if (root == null) return false;

                var direct = GetMember(root, "IsAttempted") ?? GetMember(root, "Attempted") ?? GetMember(root, "HasAttempted");
                if (direct is bool ab) return ab;

                bool visited = false;
                var vObj = GetMember(root, "IsVisited") ?? GetMember(root, "Visited");
                if (vObj is bool vb) visited = vb;

                bool unlocked = false;
                var uObj = GetMember(root, "IsUnlocked") ?? GetMember(root, "Unlocked");
                if (uObj is bool ub) unlocked = ub;

                return visited && !unlocked;
            }
            catch { return false; }
        }

        public static bool IsMapLocked(AtlasNodeDescription nd)
        {
            try
            {
                var root = nd.Element;
                if (root == null) return false;

                // Prefer explicit flags
                var locked = GetMember(root, "IsLocked") ?? GetMember(root, "Locked");
                if (locked is bool lb) return lb;

                // Unlocked flag (negated)
                var unlocked = GetMember(root, "IsUnlocked") ?? GetMember(root, "Unlocked");
                if (unlocked is bool ub) return !ub;

                // Accessibility / discovery as hints
                var accessible = GetMember(root, "IsAccessible") ?? GetMember(root, "Accessible");
                if (accessible is bool ac) return !ac;

                var discovered = GetMember(root, "IsDiscovered") ?? GetMember(root, "Discovered");
                if (discovered is bool dc) return !dc;

                // Last resort: consider "locked" when not visited & not unlocked
                bool visited = (GetMember(root, "IsVisited") ?? GetMember(root, "Visited")) is bool vb && vb;
                return !visited;
            }
            catch { return false; }
        }
    
        public static bool TryIsVisited(AtlasNodeDescription nd, out bool visited)
        {
            visited = false;
            try
            {
                var root = nd.Element;
                if (root == null) return false;
                var v = GetMember(root, "IsVisited") ?? GetMember(root, "Visited") ?? GetMember(root, "HasVisited")
                        ?? (GetMember(root, "Area") is object area ? (GetMember(area, "IsVisited") ?? GetMember(area, "Visited")) : null);
                if (v is bool b) { visited = b; return true; }
                return false;
            }
            catch { return false; }
        }

        public static bool TryIsUnlocked(AtlasNodeDescription nd, out bool unlocked)
        {
            unlocked = false;
            try
            {
                var root = nd.Element;
                if (root == null) return false;
                var u = GetMember(root, "IsUnlocked") ?? GetMember(root, "Unlocked")
                        ?? (GetMember(root, "Area") is object area ? (GetMember(area, "IsUnlocked") ?? GetMember(area, "Unlocked")) : null);
                if (u is bool b) { unlocked = b; return true; }
                var l = GetMember(root, "IsLocked") ?? GetMember(root, "Locked");
                if (l is bool lb) { unlocked = !lb; return true; }
                return false;
            }
            catch { return false; }
        }

    }
}