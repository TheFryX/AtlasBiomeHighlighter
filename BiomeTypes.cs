using System;
using System.Collections.Generic;

namespace AtlasBiomeHighlighter
{
    public enum Biome
    {
        Water,
        Swamp,
        Mountain,
        Forest,
        Desert,
        Grass,
        Citadel_Stone,
        Citadel_Iron,
        Citadel_Copper,
        Unknown
    }

    public static class BiomeUtils
    {
        private static readonly Dictionary<Biome, string> _display = new()
        {
            [Biome.Water] = "Water",
            [Biome.Swamp] = "Swamp",
            [Biome.Mountain] = "Mountain",
            [Biome.Forest] = "Forest",
            [Biome.Desert] = "Desert",
            [Biome.Grass] = "Grass",
            [Biome.Citadel_Stone] = "The Stone Citadel",
            [Biome.Citadel_Iron] = "The Iron Citadel",
            [Biome.Citadel_Copper] = "The Copper Citadel",
            [Biome.Unknown] = "Unknown"
        };

        public static string Display(Biome b) => _display.TryGetValue(b, out var s) ? s : b.ToString();

        public static Biome ParseOrUnknown(string? src)
        {
            if (string.IsNullOrWhiteSpace(src)) return Biome.Unknown;
            var s = src.Trim();
            var u = s.ToUpperInvariant();

            if (u.Contains("MAPUBERBOSS_IRONCITADEL") || u.Contains("EZOMYTECITY")) return Biome.Citadel_Iron;
            if (u.Contains("MAPUBERBOSS_STONECITADEL") || u.Contains("VAALCITY")) return Biome.Citadel_Stone;
            if (u.Contains("MAPUBERBOSS_COPPERCITADEL") || u.Contains("FARIDUNCITY")) return Biome.Citadel_Copper;

            if (u.Contains("WATER")) return Biome.Water;
            if (u.Contains("SWAMP")) return Biome.Swamp;
            if (u.Contains("MOUNTAIN")) return Biome.Mountain;
            if (u.Contains("FOREST")) return Biome.Forest;
            if (u.Contains("DESERT")) return Biome.Desert;
            if (u.Contains("GRASS")) return Biome.Grass;

            return Biome.Unknown;
        }
    }
}
