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
    }
}
