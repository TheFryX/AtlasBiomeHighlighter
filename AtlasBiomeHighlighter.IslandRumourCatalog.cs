using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using ExileCore2.Shared.Nodes;

namespace AtlasBiomeHighlighter
{
    public partial class AtlasBiomeHighlighter
    {
        private sealed class IslandRumourDefinition
        {
            public IslandRumourDefinition(string category, string name, string mapType, string mods, string rating, params string[] aliases)
            {
                Category = category;
                Name = name;
                MapType = mapType;
                Mods = mods;
                Rating = rating;
                Aliases = aliases ?? Array.Empty<string>();
            }

            public string Category { get; }
            public string Name { get; }
            public string MapType { get; }
            public string Mods { get; }
            public string Rating { get; }
            public string[] Aliases { get; }
        }

        private static readonly IslandRumourDefinition[] IslandRumourDefinitions =
        {
            new("Rumors", "Fallen Stars", "Moor", "Runestones", "S+", "Fallen stars", "Fallen stars..."),
            new("Rumors", "Cold as ice", "Frigid Bluffs", "Old Expedition", "A+", "Cold as ice..."),
            new("Rumors", "Nothin' to drink", "Stagnant Basin", "Oil", "A", "Nothing to drink", "Nothin' to drink..."),
            new("Rumors", "Endless cliffs", "Craggy Peninsula", "Rarity/Rogue Exiles", "A", "Endless Cliffs", "Endless cliffs..."),
            new("Rumors", "Sulphite!", "Scorched Cay", "Increased Rarity", "A"),
            new("Rumors", "Unknown ruins", "Exhumed Ruins", "Precursor Leylines", "B", "Unknown Ruins", "Unknown ruins..."),
            new("Rumors", "Somethin' fishy", "Bleached Shoals", "Gold", "B", "Something Fishy", "Somethin' fishy..."),
            new("Rumors", "Warm but risky", "Lush Island", "Exp/Beyond/Hoards", "B", "It's Warm", "Warm but risky..."),
            new("Rumors", "Bleak and awful", "Barren Atoll", "Strongbox", "B", "Bleak and awful..."),
            new("Rumors", "It's dry at least", "Sloughed Gully", "Monster effectiveness", "D", "It's Dry At Least", "It's dry at least..."),
            new("Rumors", "Wild roaming free", "Grazed Prairie", "Azmeri Spirits", "D", "Wild,Roaming Free", "Wild roaming free..."),

            new("Unique Maps", "Reflective waters", "Lake of Kalandra", "Ring Bases", "A", "Reflective Waters", "Reflective waters..."),
            new("Unique Maps", "All that glitters", "Castaway", "Gold", "A", "All that Glitters", "All that glitters..."),
            new("Unique Maps", "Almost paradise", "Untainted paradise", "Exp", "C", "Almost paradise..."),
            new("Unique Maps", "A good fellow", "Moment of Zen", "Seer", "C", "A good fellow..."),

            new("Bosses", "Origin of the fall", "Obscure Island", "Olroth", "A", "Origin of the Fall", "Origin of the fall..."),
            new("Bosses", "Stardrinker", "Secluded Temple", "Utred", "A", "Stardrinker..."),
            new("Bosses", "Last To Fall", "Mournful Cliffside", "Vorana", "B", "The last to fall", "The last to fall...", "Last to fall..."),
            new("Bosses", "End of the circle", "Sprawling Jungle", "Medved", "B", "End of the Circle", "End of the circle..."),

            new("Sagas", "Aldurs", "", "Buffs expeditions", "S+(is a gamble)", "Aldurs..."),
            new("Sagas", "Olroth", "Obscure Island", "Boss Node", "A", "Olroth..."),
            new("Sagas", "Utred", "Secluded Temple", "Boss Node", "B+", "Utred..."),
            new("Sagas", "Medved", "Strange Jungle", "Boss Node", "B+", "Medved..."),
            new("Sagas", "Vorana", "Mournful Cliffside", "Boss Node", "B+", "Vorana...")
        };

        private static readonly Dictionary<string, IslandRumourDefinition> IslandRumourDefinitionByToken =
            BuildIslandRumourDefinitionLookup();

        private static readonly Color IslandRumourDefaultSPlusColor = Color.FromArgb(255, 210, 80);
        private static readonly Color IslandRumourDefaultAPlusColor = Color.FromArgb(190, 140, 255);
        private static readonly Color IslandRumourDefaultAColor = Color.FromArgb(90, 220, 130);
        private static readonly Color IslandRumourDefaultBPlusColor = Color.FromArgb(90, 205, 255);
        private static readonly Color IslandRumourDefaultBColor = Color.FromArgb(80, 185, 255);
        private static readonly Color IslandRumourDefaultCColor = Color.FromArgb(210, 210, 210);
        private static readonly Color IslandRumourDefaultDColor = Color.FromArgb(230, 120, 120);

        private static Dictionary<string, IslandRumourDefinition> BuildIslandRumourDefinitionLookup()
        {
            var result = new Dictionary<string, IslandRumourDefinition>(StringComparer.Ordinal);

            foreach (var definition in IslandRumourDefinitions)
            {
                Add(definition.Name, definition);
                foreach (var alias in definition.Aliases)
                    Add(alias, definition);
            }

            return result;

            void Add(string value, IslandRumourDefinition definition)
            {
                var token = Utility.NormalizeToken(value);
                if (token.Length != 0 && !result.ContainsKey(token))
                    result[token] = definition;
            }
        }

        private static Color GetDefaultIslandRumourColor(IslandRumourDefinition definition)
        {
            var rating = definition.Rating.Trim();
            if (rating.StartsWith("S+", StringComparison.OrdinalIgnoreCase)) return IslandRumourDefaultSPlusColor;
            if (rating.Equals("A+", StringComparison.OrdinalIgnoreCase)) return IslandRumourDefaultAPlusColor;
            if (rating.Equals("A", StringComparison.OrdinalIgnoreCase)) return IslandRumourDefaultAColor;
            if (rating.Equals("B+", StringComparison.OrdinalIgnoreCase)) return IslandRumourDefaultBPlusColor;
            if (rating.Equals("B", StringComparison.OrdinalIgnoreCase)) return IslandRumourDefaultBColor;
            if (rating.Equals("C", StringComparison.OrdinalIgnoreCase)) return IslandRumourDefaultCColor;
            if (rating.Equals("D", StringComparison.OrdinalIgnoreCase)) return IslandRumourDefaultDColor;
            return Color.FromArgb(255, 220, 80);
        }

        private static bool TryGetIslandRumourDefinition(string? name, out IslandRumourDefinition definition)
        {
            var token = Utility.NormalizeToken(name);
            if (token.Length != 0 && IslandRumourDefinitionByToken.TryGetValue(token, out definition))
                return true;

            definition = null!;
            return false;
        }

        private static string GetIslandRumourCanonicalName(string? name)
        {
            return TryGetIslandRumourDefinition(name, out var definition)
                ? definition.Name
                : CleanIslandRumourName(name);
        }

        private static string GetIslandRumourToken(string? name)
        {
            return Utility.NormalizeToken(GetIslandRumourCanonicalName(name));
        }

        private static List<IslandRumourDefinition> BuildIslandRumourDefinitionList()
        {
            return IslandRumourDefinitions.ToList();
        }

        private void EnsureIslandRumourColorSettings()
        {
            Settings.IslandRumourColors ??= new Dictionary<string, ColorNode>(StringComparer.OrdinalIgnoreCase);

            foreach (var definition in IslandRumourDefinitions)
            {
                if (!Settings.IslandRumourColors.ContainsKey(definition.Name))
                    Settings.IslandRumourColors[definition.Name] = new ColorNode(GetDefaultIslandRumourColor(definition));
            }
        }

        private Color GetIslandRumourColor(string? name)
        {
            if (!Settings.IslandRumourUseIndividualColors.Value)
                return Settings.IslandRumourTextColor.Value;

            EnsureIslandRumourColorSettings();

            if (TryGetIslandRumourDefinition(name, out var definition) &&
                Settings.IslandRumourColors.TryGetValue(definition.Name, out var colorNode))
            {
                return colorNode.Value;
            }

            return Settings.IslandRumourTextColor.Value;
        }

        private bool TryGetPreferredIslandRumourMatch(IslandRumourSnapshot snapshot, out string matchedName)
        {
            matchedName = string.Empty;
            if (_preferredRumourTokensExact.Count == 0 || snapshot.Rumours.Length == 0)
                return false;

            for (int i = 0; i < snapshot.Rumours.Length; i++)
            {
                var canonical = GetIslandRumourCanonicalName(snapshot.Rumours[i]);
                var token = Utility.NormalizeToken(canonical);
                if (token.Length != 0 && _preferredRumourTokensExact.Contains(token))
                {
                    matchedName = canonical;
                    return true;
                }
            }

            return false;
        }
    }
}
