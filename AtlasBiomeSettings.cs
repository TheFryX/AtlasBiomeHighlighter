using System.Collections.Generic;
using System.Drawing;
using ExileCore2.Shared.Interfaces;
using ExileCore2.Shared.Nodes;

namespace AtlasBiomeHighlighter
{
    public class AtlasBiomeSettings : ISettings
    {
        public ToggleNode Enable { get; set; } = new(false);

        public RangeNode<int> AtlasRefreshMs { get; set; } = new(5000, 500, 30000);
        public RangeNode<int> ScreenRefreshMs { get; set; } = new(500, 50, 5000);
        public RangeNode<int> NodeRadius { get; set; } = new(20, 4, 40);
        public RangeNode<int> RingThickness { get; set; } = new(3, 1, 12);
        public RangeNode<float> Opacity { get; set; } = new(0.8f, 0.05f, 1.0f);
        public ToggleNode ShowLabels { get; set; } = new(true);
        public ToggleNode DebugMode { get; set; } = new(false);


        // Special highlights (strict/low-noise)
        public ToggleNode HighlightDeadlyBoss { get; set; } = new(true);
        public ColorNode DeadlyBossRingColor { get; set; } = new(Color.FromArgb(220, 60, 60));
        public ToggleNode HighlightCorruptedNexus { get; set; } = new(true);
        public ColorNode CorruptedNexusRingColor { get; set; } = new(Color.FromArgb(160, 32, 240));
        public ToggleNode HighlightUniqueMaps { get; set; } = new(true);
        public ColorNode UniqueMapRingColor { get; set; } = new(Color.FromArgb(255, 165, 0));
        public ToggleNode HighlightPreferredMaps { get; set; } = new(true);
        public ColorNode PreferredMapRingColor { get; set; } = new(Color.FromArgb(0, 206, 209)); // teal
        
        // User-selectable preferred map names
        public Dictionary<string, ToggleNode> PreferredMaps { get; set; } = new()
        {
            ["Blooming Field"] = new(false),
            ["Savannah"] = new(false),
            ["Fortress"] = new(false),
            ["Penitentiary"] = new(false),
            ["Lost Towers"] = new(false),
            ["Sandspit"] = new(false),
            ["Forge"] = new(false),
            ["Sulphuric Caverns"] = new(false),
            ["Mire"] = new(false),
            ["Woodland"] = new(false),
            ["Sump"] = new(false),
            ["Willow"] = new(false),
            ["Headland"] = new(false),
            ["Lofty Summit"] = new(false),
            ["Necropolis"] = new(false),
            ["Crypt"] = new(false),
            ["Steaming Springs"] = new(false),
            ["Seepage"] = new(false),
            ["Riverside"] = new(false),
            ["Steppe"] = new(false),
            ["Slick"] = new(false),
            ["Spider Woods"] = new(false),
            ["Marrow"] = new(false),
            ["Vaal City"] = new(false),
            ["Bloodwood"] = new(false),
            ["Cenotes"] = new(false),
            ["Hidden Grotto"] = new(false),
            ["Ravine"] = new(false),
            ["Alpine Ridge"] = new(false),
            ["Augury"] = new(false),
            ["Bastille"] = new(false),
            ["Creek"] = new(false),
            ["Crimson Shores"] = new(false),
            ["Decay"] = new(false),
            ["Derelict Mansion"] = new(false),
            ["Deserted"] = new(false),
            ["Grimhaven"] = new(false),
            ["Hive"] = new(false),
            ["Inferno"] = new(false),
            ["Mineshaft"] = new(false),
            ["Oasis"] = new(false),
            ["Outlands"] = new(false),
            ["Rockpools"] = new(false),
            ["Sinking Spire"] = new(false),
            ["Vaal Village"] = new(false),
            ["Rustbowl"] = new(false),
            ["Backwash"] = new(false),
            ["Burial Bog"] = new(false),
            ["Wetlands"] = new(false),
            ["Sun Temple"] = new(false),
            ["Channel"] = new(false),
            ["Molten Vault"] = new(false),
            ["The Assembly"] = new(false),
            ["Mesa"] = new(false),
            ["Bluff"] = new(false),
            ["Azmerian Ranges"] = new(false),
            ["Frozen Falls"] = new(false),
            ["Trenches"] = new(false),
            ["Farmlands Hideout"] = new(false),
            ["Prison Hideout"] = new(false),
            ["Ornate Chambers"] = new(false),
            ["Canyon"] = new(false),
            ["Confluence"] = new(false),
            ["Razed Fields"] = new(false),
            ["Rugosa"] = new(false),
            ["Riverhold"] = new(false),
            ["Digsite"] = new(false),
            ["Ice Cave"] = new(false),
            ["Overgrown"] = new(false),
            ["Stronghold"] = new(false),
            ["Rupture"] = new(false),
            ["Spring"] = new(false),
            ["Wayward Isle"] = new(false),
            ["Epitaph"] = new(false),
            ["Cliffside"] = new(false),
            ["Sinkhole"] = new(false),
            ["Caldera"] = new(false),
            ["Flotsam"] = new(false),
        };

        public RangeNode<int> SpecialRingThickness { get; set; } = new(4, 1, 12);
        public RangeNode<float> SpecialAlphaMultiplier { get; set; } = new(0.85f, 0.1f, 1.0f);
        public ToggleNode ShowUniqueNameOnLabel { get; set; } = new(true);
        public ToggleNode ShowSpecialTag { get; set; } = new(true);
public ExileCore2.Shared.Nodes.ColorNode PowerfulBossRingColor { get; set; } = new(System.Drawing.Color.FromArgb(255, 140, 0));
        public ToggleNode HighlightAbyssOverrun { get; set; } = new(true);
        public ExileCore2.Shared.Nodes.ColorNode AbyssOverrunRingColor { get; set; } = new(System.Drawing.Color.FromArgb(0, 206, 209));
        public ToggleNode HighlightMomentofZen { get; set; } = new(true);
        public ExileCore2.Shared.Nodes.ColorNode MomentofZenRingColor { get; set; } = new(System.Drawing.Color.FromArgb(255, 208, 207));
        public ToggleNode PreferMapNameForDeadly { get; set; } = new(true);

        // Minimal but readable labels
        public RangeNode<int> LabelOffset { get; set; } = new(20, -40, 100);
        public ToggleNode LabelUseBiomeColor { get; set; } = new(false);
        public ColorNode LabelTextColor { get; set; } = new(Color.White);
        public ToggleNode LabelOutline { get; set; } = new(true);
        public RangeNode<int> LabelOutlineThickness { get; set; } = new(2, 1, 6);
        public ToggleNode LabelBold { get; set; } = new(true);

        public Dictionary<Biome, ToggleNode> Visible { get; } = new()
        {
            [Biome.Water] = new(true),
            [Biome.Swamp] = new(true),
            [Biome.Mountain] = new(true),
            [Biome.Forest] = new(true),
            [Biome.Desert] = new(true),
            [Biome.Grass] = new(true),
            [Biome.Citadel_Stone] = new(true),
            [Biome.Citadel_Iron] = new(true),
            [Biome.Citadel_Copper] = new(true),
            [Biome.Unknown] = new(true)
        };

        public Dictionary<Biome, ColorNode> Colors { get; } = new()
        {
            [Biome.Water] = new(Color.FromArgb(30, 144, 255)),
            [Biome.Swamp] = new(Color.FromArgb(47, 79, 47)),
            [Biome.Mountain] = new(Color.FromArgb(128, 128, 128)),
            [Biome.Forest] = new(Color.FromArgb(34, 139, 34)),
            [Biome.Desert] = new(Color.FromArgb(218, 165, 32)),
            [Biome.Grass] = new(Color.FromArgb(124, 252, 0)),
            [Biome.Citadel_Stone] = new(Color.FromArgb(169, 169, 169)),
            [Biome.Citadel_Iron] = new(Color.FromArgb(70, 130, 180)),
            [Biome.Citadel_Copper] = new(Color.FromArgb(184, 115, 51)),
            [Biome.Unknown] = new(Color.FromArgb(186, 85, 211))
        };
    }
}
