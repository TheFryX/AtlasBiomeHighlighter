using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Linq;
using ExileCore2.PoEMemory.Elements.AtlasElements;
using Vector2 = System.Numerics.Vector2;

namespace AtlasBiomeHighlighter
{
    internal static class Utility
    {
	        
	        
	        
	        
	        public static string NormalizeToken(string? value)
	        {
	            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
	            var span = value.AsSpan();
	            
	            
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

	        
	        
	        
	        
        public const string RemovedPrecursorTowerName = "Precursor Tower";
        public const string LegacySinkingSpireName = "Sinking Spire";
        public const string CurrentSwampTowerName = "Swamp Tower";

        public static string PreferredKeyToToken(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return string.Empty;
            var main = GetPreferredKeyMainPart(key);
            return NormalizeToken(CanonicalTowerDisplayName(main));
        }

        public static string PreferredKeyToDisplayName(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return string.Empty;
            return CanonicalTowerDisplayName(GetPreferredKeyMainPart(key));
        }

        public static string CanonicalTowerDisplayName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;

            var trimmed = name.Trim();
            return trimmed.Equals(LegacySinkingSpireName, StringComparison.OrdinalIgnoreCase)
                ? CurrentSwampTowerName
                : trimmed;
        }

        private static string GetPreferredKeyMainPart(string key)
        {
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
            try
            {
                if (node?.Element is null)
                    return false;

                var c = node.Element.Center;
                return c.X > 0 && c.X < width && c.Y > 0 && c.Y < height;
            }
            catch
            {
                return false;
            }
        }

        private const BindingFlags InstanceAnyVisibility = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags InstanceAnyVisibilityIgnoreCase = InstanceAnyVisibility | BindingFlags.IgnoreCase;

        private static readonly object ReflectionCacheLock = new();
        private static readonly Dictionary<(Type type, string name), MemberInfo?> MemberCache = new();
        private static readonly Dictionary<Type, PropertyInfo[]> PropertyCache = new();
        private static readonly Dictionary<Type, FieldInfo[]> FieldCache = new();

        private static object? GetMember(object? obj, string name)
        {
            if (obj is null) return null;

            var t = obj.GetType();
            var key = (t, name);
            MemberInfo? member;

            lock (ReflectionCacheLock)
            {
                if (!MemberCache.TryGetValue(key, out member))
                {
                    member = t.GetProperty(name, InstanceAnyVisibilityIgnoreCase);
                    if (member is PropertyInfo property && property.GetIndexParameters().Length != 0)
                        member = null;

                    member ??= t.GetField(name, InstanceAnyVisibilityIgnoreCase);
                    MemberCache[key] = member;
                }
            }

            try
            {
                return member switch
                {
                    PropertyInfo property => property.GetValue(obj),
                    FieldInfo field => field.GetValue(obj),
                    _ => null
                };
            }
            catch
            {
                return null;
            }
        }

        private static PropertyInfo[] GetCachedProperties(Type type)
        {
            lock (ReflectionCacheLock)
            {
                if (!PropertyCache.TryGetValue(type, out var properties))
                {
                    properties = type.GetProperties(InstanceAnyVisibility);
                    PropertyCache[type] = properties;
                }

                return properties;
            }
        }

        private static FieldInfo[] GetCachedFields(Type type)
        {
            lock (ReflectionCacheLock)
            {
                if (!FieldCache.TryGetValue(type, out var fields))
                {
                    fields = type.GetFields(InstanceAnyVisibility);
                    FieldCache[type] = fields;
                }

                return fields;
            }
        }

        private static bool NameContains(string value, string fragment) =>
            value.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool IsInterestingSpecialMemberName(string name)
        {
            return NameContains(name, "TEXT") ||
                   NameContains(name, "LABEL") ||
                   NameContains(name, "TOOLTIP") ||
                   NameContains(name, "STRING") ||
                   NameContains(name, "CAPTION") ||
                   NameContains(name, "TEXTURE") ||
                   NameContains(name, "ICON") ||
                   NameContains(name, "ART") ||
                   NameContains(name, "ATLASENTRY") ||
                   name.Equals("ID", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsNameMemberName(string name)
        {
            if (name.Equals("TEXTURENAME", StringComparison.OrdinalIgnoreCase) ||
                NameContains(name, "TEXTURE") ||
                NameContains(name, "ICON"))
                return false;

            return NameContains(name, "TITLE") ||
                   NameContains(name, "NAME") ||
                   NameContains(name, "HEADER") ||
                   NameContains(name, "CAPTION") ||
                   NameContains(name, "LABELTEXT");
        }

        private static bool IsChildCollectionMemberName(string name) =>
            name.Equals("Children", StringComparison.OrdinalIgnoreCase) ||
            NameContains(name, "Child");

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
                
                ["MapCavernCity"] = "Sacred Reservoir",
                ["MapUberBoss_JadeCitadel"] = "The Jade Isles",
                ["MapVaalVault"] = "Sealed Vault",
                ["MapDerelictMansion"] = "Derelict Mansion",
                ["MapUberBoss_IronCitadel"] = "The Iron Citadel",
                ["MapUberBoss_StoneCitadel"] = "The Stone Citadel",
                ["MapUberBoss_CopperCitadel"] = "The Copper Citadel",
            };


        public static readonly string[] PreferredTowerNames =
        {
            "Alpine Ridge",
            "Bluff",
            "Lost Towers",
            "Mesa",
            CurrentSwampTowerName,
        };

        private static readonly System.Collections.Generic.Dictionary<string, string> TowerNamesById =
            new(System.StringComparer.OrdinalIgnoreCase)
            {
                ["MapSwampTower"] = "Swamp Tower",
                ["MapLostTowers"] = "Lost Towers",
                ["MapMesa"] = "Mesa",
                ["MapBluff"] = "Bluff",
                ["MapAlpineRidge"] = "Alpine Ridge",
                ["MapSinkingSpire"] = CurrentSwampTowerName,
            };

        public static bool TryGetTowerName(AtlasNodeDescription nd, out string? name)
        {
            name = null;

            try
            {
                var id = ExtractString(GetMember(nd.Element, "Id")) ?? ExtractString(GetMember(GetMember(nd.Element, "Area"), "Id"));
                if (!string.IsNullOrWhiteSpace(id) && TowerNamesById.TryGetValue(id.Trim(), out var byId))
                {
                    name = byId;
                    return true;
                }

                var area = GetMember(nd.Element, "Area");
                var areaName = ExtractString(GetMember(area, "Name"));
                if (!string.IsNullOrWhiteSpace(areaName))
                {
                    foreach (var tower in PreferredTowerNames)
                    {
                        if (areaName.Equals(tower, StringComparison.OrdinalIgnoreCase))
                        {
                            name = CanonicalTowerDisplayName(tower);
                            return true;
                        }
                    }

                    if (areaName.Contains("Tower", StringComparison.OrdinalIgnoreCase) ||
                        areaName.Equals(LegacySinkingSpireName, StringComparison.OrdinalIgnoreCase))
                    {
                        name = CanonicalTowerDisplayName(areaName);
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }


        public static Biome TryGetBiome(AtlasNodeDescription nd)
        {
            
            
            
            var candidate = GetMember(nd.Element, "Biome");
            var name = ExtractString(candidate);
            var parsed = BiomeUtils.ParseOrUnknown(name);
            if (parsed != Biome.Unknown) return parsed;

            parsed = BiomeUtils.ParseOrUnknown(ExtractString(GetMember(candidate, "Id")));
            if (parsed != Biome.Unknown) return parsed;

            parsed = BiomeUtils.ParseOrUnknown(ExtractString(GetMember(candidate, "Name")));
            if (parsed != Biome.Unknown) return parsed;

            
            foreach (var hopName in new[] {"AtlasPanelNode", "Node", "Area"})
            {
                var hop = GetMember(nd.Element, hopName);
                if (hop == null) continue;
                var n2 = ExtractString(GetMember(hop, "Biome"));
                var p2 = BiomeUtils.ParseOrUnknown(n2);
                if (p2 != Biome.Unknown) return p2;
            }

            
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
            MomentofZen = 1 << 5,
            Cleansed = 1 << 6,
            AreaContainsAbyss = 1 << 7,
            AreaContainsExpedition = 1 << 8,
        }

        public sealed class MechanicDefinition
        {
            public MechanicDefinition(string name, params string[] tokens)
            {
                Name = name;
                Tokens = tokens == null || tokens.Length == 0 ? new[] { name } : tokens;
            }

            public string Name { get; }
            public string[] Tokens { get; }
        }

        public static readonly MechanicDefinition[] MapContentMechanics =
        {
            
            
            
            
            new("Overrun Abyss", "AbyssOverrun", "AtlasIconContentAbyssOverrun", "Overrun by the Abyssal", "Area contains many extra Abysses"),
            new("Great Beast", "Great Beast", "CompanionsNotable1", "GreatBeast"),
            new("Essence Trove", "Essence Trove", "AtlasIconContentEssence", "EssenceTrove"),
            new("Monstrous Treasure", "Monstrous Treasure", "AtlasIconContentStrongBox", "MonstrousTreasure"),
            new("Spirit Guide", "Spirit Guide", "AtlasIconContentAzmeriSpirit", "AzmeriSpirit", "SpiritGuide"),
            new("Arcane Hordes", "Arcane Hordes", "ItemQuantityandRarity", "ArcaneHordes"),
            new("Hunting Grounds", "Hunting Grounds", "Hunter", "HuntingGrounds"),
            new("Nature Shrines", "Nature Shrines", "HybridShrineAzmeriSpirit", "NatureShrines"),
            new("Crystalised Twinning", "Crystalised Twinning", "EssenceNotable1", "CrystalisedTwinning"),
            new("Indomitable Essence", "Indomitable Essence", "EssenceNotable2", "IndomitableEssence"),
            new("Azmeri Energisation", "Azmeri Energisation", "MoreWildWisps", "AzmeriEnergisation"),
            new("Spirit Migration", "Spirit Migration", "VividPrimalWildWisps", "SpiritMigration"),
            new("Sacred Spirit", "Sacred Spirit", "moresacredwisps", "SacredSpirit"),
            new("Ancient Trove", "Ancient Trove", "StrongboxUnique", "StrongboxNotable2", "AncientTrove"),
            new("Twice-Locked Boxes", "Twice-Locked Boxes", "StrongboxNotable1", "TwiceLockedBoxes"),
            new("Power of Faith", "Power of Faith", "Shrines", "PowerOfFaith"),
            new("Large Congregation", "Large Congregation", "ShrinesNode", "LargeCongregation"),
            new("Zealous Reverence", "Zealous Reverence", "BossNotableSpawnAdditionalShrine", "ZealousReverence"),
            new("Persistent Devotion", "Persistent Devotion", "GreedShrinenoteble", "PersistentDevotion"),
            new("Rites of the Rogues", "Rites of the Rogues", "Anarchy5", "RitesOfTheRogues"),
            new("Surprising Alliances", "Surprising Alliances", "AnarchyNode1", "SurprisingAlliances"),
            new("Azmeri Bloodline", "Azmeri Bloodline", "Anarchy4", "AzmeriBloodline"),
            new("Twinned Terrors", "Twinned Terrors", "StoneCircles", "TwinnedTerrors"),
            new("Scattered Stones", "Scattered Stones", "StoneCirclesNode", "ScatteredStones"),
            new("Map Area Modified", "Map Area Modified", "Mapnode", "MapAreaModified"),
            new("Fleeing Exile", "Fleeing Exile", "AnarchyNotable2", "FleeingExile"),
            new("Breach Hive", "Breach Hive", "BreachNotable4", "BreachHive"),
            new("Delirium", "Delirium", "AtlasIconContentDelirium", "DeliriumMirror", "DeliriumEncounter"),
            new("Grand Expedition", "Grand Expedition", "Area contains a Grand Expedition", "AreaContainsAGrandExpedition", "AreaContainsGrandExpedition", "ContainsGrandExpedition", "GrandExpedition", "ExpeditionGrand", "AtlasIconContentGrandExpedition", "AtlasLeagueGrandExpedition"),
            new("Grand Mirror", "Grand Mirror", "DeliriumGigaMirror", "GrandMirror", "GigaMirror"),
            new("Simulacrum", "Simulacrum", "DeliriumNotable7"),
            new("Chaotic Cacophony", "Chaotic Cacophony", "ElderShaperNotable1", "ChaoticCacophony"),
            new("Affluent Armies", "Affluent Armies", "ItemRarity", "BossMapDrops", "AffluentArmies"),
            new("Monstrous Treasure - Map Boss Unique", "Map Boss drops a Unique item", "ExceptionalItemsWeaponsShields", "MapBossUnique"),
            new("Trialmaster's Trainee", "Trialmaster's Trainee", "VaalNotable1", "Inscribed Ultimatum"),
            new("Sekhema's Student", "Sekhema's Student", "SorceressSandDjinnCorpseBeetles", "Djinn Barya"),
            new("Azmeri Champion", "Azmeri Champion", "BossNotableAzmeriSpirit"),
            new("Gigantic Uprising", "Gigantic Uprising", "MinionsandManaNotable"),
            new("Glimmering Mutation", "Glimmering Mutation", "CurrencyNode"),
            new("Stolen Power", "Stolen Power", "ScorchTheEarth"),
            new("Headhunters", "Headhunters", "skullcracking"),
            new("Swarming Spirits", "Swarming Spirits", "EnduranceFrenzyPowerChargeNode"),
            new("Power Struggle", "Power Struggle", "BossNotableSpawnBeyondMonsters"),
            new("Corrupted Mirage", "Corrupted Mirage", "CorruptedDefences"),
            new("Energized Ley Lines", "Energized Ley Lines", "CaptivatedInterestKeystone"),
            new("Exceptional Find", "Exceptional Find", "ExceptionalItemsBodyArmour"),
            new("Water Influence", "Water Influence", "WaterBiome"),
            new("Mountain Influence", "Mountain Influence", "MountainBiome"),
            new("Grass Influence", "Grass Influence", "GrassBiome"),
            new("Forest Influence", "Forest Influence", "ForestBiome"),
            new("Swamp Influence", "Swamp Influence", "SwampBiome"),
            new("Desert Influence", "Desert Influence", "DesertBiome"),
            new("Immured Fury", "Immured Fury", "AtlasIconContentSanctificationBoss", "ImmuredFury"),
            new("Mirage of Riches", "Mirage of Riches", "Currency2"),
            new("Wisdom's Teachings", "Wisdom's Teachings", "BossNotableGrantMoreExperience"),
            new("Tight Pockets", "Tight Pockets", "BossNotableDropMoreItems"),
            new("Fragment of Immortality", "Fragment of Immortality", "IncreaseMinionLifeNode"),
            new("Prosperous Populous", "Prosperous Populous", "ItemQuantity"),
            new("Echoes of Power", "Echoes of Power", "GenericMinionNotable"),
        };



        private sealed class NormalizedMechanicDefinition
        {
            public NormalizedMechanicDefinition(MechanicDefinition source)
            {
                Name = source.Name;
                Tokens = source.Tokens
                    .Where(token => !string.IsNullOrWhiteSpace(token))
                    .Select(token => new NormalizedMechanicToken(token, NormalizeToken(token), IsTextMechanicToken(token)))
                    .ToArray();
            }

            public string Name { get; }
            public NormalizedMechanicToken[] Tokens { get; }
        }

        private readonly struct NormalizedMechanicToken
        {
            public NormalizedMechanicToken(string raw, string normalized, bool isTextToken)
            {
                Raw = raw;
                Normalized = normalized;
                IsTextToken = isTextToken;
            }

            public string Raw { get; }
            public string Normalized { get; }
            public bool IsTextToken { get; }
        }

        private static readonly NormalizedMechanicDefinition[] NormalizedMapContentMechanics =
            MapContentMechanics.Select(m => new NormalizedMechanicDefinition(m)).ToArray();

        
        
        
        
        
        private static readonly Dictionary<string, string[]> MechanicNamesByIdentifierToken =
            BuildMechanicIdentifierLookup();

        private static readonly (string Name, string Raw)[] TextMechanicTokens =
            NormalizedMapContentMechanics
                .SelectMany(m => m.Tokens.Where(t => t.IsTextToken).Select(t => (m.Name, t.Raw)))
                .ToArray();

        private static Dictionary<string, string[]> BuildMechanicIdentifierLookup()
        {
            var temp = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var mechanic in NormalizedMapContentMechanics)
            {
                foreach (var token in mechanic.Tokens)
                {
                    if (token.IsTextToken || string.IsNullOrEmpty(token.Normalized))
                        continue;

                    if (!temp.TryGetValue(token.Normalized, out var names))
                    {
                        names = new List<string>(1);
                        temp[token.Normalized] = names;
                    }

                    if (!names.Contains(mechanic.Name, StringComparer.OrdinalIgnoreCase))
                        names.Add(mechanic.Name);
                }
            }

            return temp.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
        }

        private static bool IsTextMechanicToken(string token)
        {
            for (int i = 0; i < token.Length; i++)
            {
                char ch = token[i];
                if (char.IsWhiteSpace(ch) || ch == '\'' || ch == '-' || ch == '[' || ch == ']')
                    return true;
            }

            return false;
        }

        private static bool IdentifierTokenMatches(string normalizedValue, string normalizedToken)
        {
            if (string.IsNullOrEmpty(normalizedValue) || string.IsNullOrEmpty(normalizedToken))
                return false;

            if (normalizedValue.Equals(normalizedToken, StringComparison.OrdinalIgnoreCase))
                return true;

            
            
            if (normalizedValue.EndsWith(normalizedToken + "dds", StringComparison.OrdinalIgnoreCase) ||
                normalizedValue.EndsWith(normalizedToken + "png", StringComparison.OrdinalIgnoreCase) ||
                normalizedValue.EndsWith(normalizedToken, StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        public static IReadOnlyList<string> TryGetMechanicNames(AtlasNodeDescription nd)
        {
            
            
            
            
            
            
            
            var result = new System.Collections.Generic.List<string>(2);

            try
            {
                var root = nd.Element;
                if (root == null)
                    return result;

                var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

                void AddMechanicName(string name)
                {
                    if (seen.Add(name))
                        result.Add(name);
                }

                void AddIdentifierToken(string? token)
                {
                    if (string.IsNullOrWhiteSpace(token))
                        return;

                    string normalized = NormalizeToken(token);
                    if (normalized.Length == 0)
                        return;

                    if (MechanicNamesByIdentifierToken.TryGetValue(normalized, out var names))
                    {
                        for (int i = 0; i < names.Length; i++)
                            AddMechanicName(names[i]);
                    }
                }

                static string? ExtractFileStem(string value)
                {
                    int end = value.Length;
                    int q = value.IndexOf('?');
                    if (q >= 0) end = q;

                    int slashA = value.LastIndexOf('/', end - 1);
                    int slashB = value.LastIndexOf('\\', end - 1);
                    int slash = slashA > slashB ? slashA : slashB;
                    int start = slash >= 0 ? slash + 1 : 0;
                    int dot = value.LastIndexOf('.', end - 1, end - start);
                    int stemEnd = dot > start ? dot : end;

                    if (stemEnd <= start)
                        return null;

                    return value.Substring(start, stemEnd - start);
                }

                void AddIfMatched(string? value, bool allowTextMatch = false)
                {
                    if (string.IsNullOrWhiteSpace(value))
                        return;

                    
                    AddIdentifierToken(value);

                    
                    var stem = ExtractFileStem(value);
                    if (!string.IsNullOrWhiteSpace(stem))
                        AddIdentifierToken(stem);

                    
                    
                    if (!allowTextMatch)
                        return;

                    for (int i = 0; i < TextMechanicTokens.Length; i++)
                    {
                        var token = TextMechanicTokens[i];
                        if (value.IndexOf(token.Raw, StringComparison.OrdinalIgnoreCase) >= 0)
                            AddMechanicName(token.Name);
                    }
                }

                void AddIdentityLikeObject(object? obj)
                {
                    if (obj == null)
                        return;

                    
                    AddIfMatched(ExtractString(obj));
                    AddIfMatched(ExtractString(GetMember(obj, "Id")));
                    AddIfMatched(ExtractString(GetMember(obj, "Name")));
                    AddIfMatched(ExtractString(GetMember(obj, "AtlasIcon")));
                    AddIfMatched(ExtractString(GetMember(obj, "PassiveArt")));
                    AddIfMatched(ExtractString(GetMember(obj, "AtlasItemTexture")));
                    AddIfMatched(ExtractString(GetMember(obj, "TextureName")));
                }

                void AddIdentityCollection(object? collection)
                {
                    if (collection is System.Collections.IEnumerable enumerable && collection is not string)
                    {
                        foreach (var item in enumerable)
                            AddIdentityLikeObject(item);
                    }
                    else
                    {
                        AddIdentityLikeObject(collection);
                    }
                }

                
                AddIfMatched(ExtractString(GetMember(root, "Id")));
                AddIfMatched(ExtractString(GetMember(root, "TextureName")));

                var atlasEntry = GetMember(root, "AtlasEntry");
                AddIfMatched(ExtractString(atlasEntry), allowTextMatch: true);
                AddIfMatched(ExtractString(GetMember(atlasEntry, "Id")), allowTextMatch: true);
                AddIfMatched(ExtractString(GetMember(atlasEntry, "Name")), allowTextMatch: true);
                AddIfMatched(ExtractString(GetMember(atlasEntry, "Text")), allowTextMatch: true);
                AddIfMatched(ExtractString(GetMember(atlasEntry, "Description")), allowTextMatch: true);

                AddIdentityCollection(GetMember(root, "ContentIdentity"));
                AddIdentityCollection(GetMember(root, "AtlasChildren"));
            }
            catch { }

            return result;
        }

        public static SpecialFlags TryGetSpecialFlags(AtlasNodeDescription nd)
        {
            try
            {
                SpecialFlags flags = SpecialFlags.None;
                var root = nd.Element;
                if (root == null) return flags;

                
                string? id = ExtractString(GetMember(root, "Id")) ?? ExtractString(GetMember(GetMember(root, "Area"), "Id"));
                if (!string.IsNullOrWhiteSpace(id))
                {
                    if (id.StartsWith("MapUnique", System.StringComparison.OrdinalIgnoreCase))
                        flags |= SpecialFlags.UniqueMap;

                    
                    
                    
                    ClassifyStrict(id, ref flags);
                }

                var area = GetMember(root, "Area");
                ClassifyStrict(ExtractString(GetMember(area, "RawName")), ref flags);
                ClassifyStrict(ExtractString(GetMember(area, "Name")), ref flags);
                ClassifyStrict(ExtractString(area), ref flags);

                
                var atlasEntry = GetMember(root, "AtlasEntry");
                ClassifyStrict(ExtractString(GetMember(atlasEntry, "Id")), ref flags);
                ClassifyStrict(ExtractString(atlasEntry), ref flags);

                
                var stack = new System.Collections.Generic.Stack<object?>();
                stack.Push(root);
                while (stack.Count > 0)
                {
                    var cur = stack.Pop();
                    if (cur == null) continue;
                    var t = cur.GetType();

                    foreach (var p in GetCachedProperties(t))
                    {
                        if (p.GetIndexParameters().Length > 0) continue;
                        if (IsInterestingSpecialMemberName(p.Name))
                        {
                            string? val = null;
                            try { val = ExtractString(p.GetValue(cur)); } catch {}
                            ClassifyStrict(val, ref flags);
                        }
                    }
                    foreach (var f in GetCachedFields(t))
                    {
                        if (IsInterestingSpecialMemberName(f.Name))
                        {
                            string? val = null;
                            try { val = ExtractString(f.GetValue(cur)); } catch {}
                            ClassifyStrict(val, ref flags);
                        }
                    }

                    foreach (var p in GetCachedProperties(t))
                    {
                        if (IsChildCollectionMemberName(p.Name))
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

            
            
            if (s.Contains("ATLASLEAGUEABYSS", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("AREA CONTAINS ABYSS", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("AREA CONTAINS ABYSSES", StringComparison.OrdinalIgnoreCase))
                flags |= SpecialFlags.AreaContainsAbyss;

            
            
            
            if (s.Contains("EXPEDITIONLOGBOOK", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("ATLASLEAGUEEXPEDITION", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("CONTAINSEXPEDITION", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("KALGUURAN EXPEDITION", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("VERISIUM REMNANTS", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("ATLASICONCONTENTEXPEDITION", StringComparison.OrdinalIgnoreCase))
                flags |= SpecialFlags.AreaContainsExpedition;

            
            
            
            if (s.Contains("ATLASICONCONTENTMAPBOSSSPECIAL", StringComparison.OrdinalIgnoreCase))
                flags |= SpecialFlags.DeadlyBoss;
            if (s.Contains("DEADLY MAP BOSS", StringComparison.OrdinalIgnoreCase))
                flags |= SpecialFlags.DeadlyBoss;

            
            if (s.Contains("ATLASICONCONTENTCORRUPTIONNEXUS", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("ATLASICONCONTENTCORRUPTEDNEXUS", StringComparison.OrdinalIgnoreCase))
            {
                flags |= SpecialFlags.CorruptedNexus;
                flags &= ~SpecialFlags.UniqueMap;
            }

            
            if (s.Contains("ATLASICONCONTENTTRADER", StringComparison.OrdinalIgnoreCase))
            {
                flags |= SpecialFlags.MomentofZen;
                flags &= ~SpecialFlags.UniqueMap;
            }

            
            if (s.Contains("ATLASICONCONTENTSANCTIFICATION", StringComparison.OrdinalIgnoreCase))
            {
                flags |= SpecialFlags.Cleansed;
                flags &= ~SpecialFlags.UniqueMap;
            }
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
            return TryGetAnyMapName(nd, TryGetSpecialFlags(nd), out name);
        }

        public static bool TryGetAnyMapName(AtlasNodeDescription nd, SpecialFlags sflags, out string? name)
        {
            name = null;

	            
	            
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

            
            if (TryGetUniqueNameFromId(nd, out var unm) && !string.IsNullOrWhiteSpace(unm))
            {
                name = unm;
                return true;
            }

            
            try
            {
                var id = ExtractString(GetMember(nd.Element, "Id")) ?? ExtractString(GetMember(GetMember(nd.Element, "Area"), "Id"));
                if (!string.IsNullOrWhiteSpace(id) && TowerNamesById.TryGetValue(id.Trim(), out var tname))
                {
                    name = tname;
                    return true;
                }
            }
            catch { }

            
            try
            {
                var rootEl = nd.Element;
                var area = GetMember(rootEl, "Area");
                var areaName = ExtractString(GetMember(area, "Name"));
                if (!string.IsNullOrWhiteSpace(areaName))
                {
                    name = CanonicalTowerDisplayName(areaName);
                    return true;
                }
            } catch {}

            try
            {
                var root = nd.Element;
                if (root == null) return false;
                var stack = new System.Collections.Generic.Stack<object?>();
                stack.Push(root);

                while (stack.Count > 0)
                {
                    var cur = stack.Pop();
                    if (cur == null) continue;
                    var t = cur.GetType();

                    foreach (var p in GetCachedProperties(t))
                    {
                        if (p.GetIndexParameters().Length > 0) continue;
                        if (IsNameMemberName(p.Name))
                        {
                            try
                            {
                                var s = ExtractString(p.GetValue(cur));
                                if (!string.IsNullOrWhiteSpace(s)) { name = s.Trim(); return true; }
                            } catch {}
                        }
                    }
                    foreach (var f in GetCachedFields(t))
                    {
                        if (IsNameMemberName(f.Name))
                        {
                            try
                            {
                                var s = ExtractString(f.GetValue(cur));
                                if (!string.IsNullOrWhiteSpace(s)) { name = s.Trim(); return true; }
                            } catch {}
                        }
                    }

                    
                    foreach (var p in GetCachedProperties(t))
                    {
                        if (IsChildCollectionMemberName(p.Name))
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

                
                var locked = GetMember(root, "IsLocked") ?? GetMember(root, "Locked");
                if (locked is bool lb) return lb;

                
                var unlocked = GetMember(root, "IsUnlocked") ?? GetMember(root, "Unlocked");
                if (unlocked is bool ub) return !ub;

                
                var accessible = GetMember(root, "IsAccessible") ?? GetMember(root, "Accessible");
                if (accessible is bool ac) return !ac;

                var discovered = GetMember(root, "IsDiscovered") ?? GetMember(root, "Discovered");
                if (discovered is bool dc) return !dc;

                
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