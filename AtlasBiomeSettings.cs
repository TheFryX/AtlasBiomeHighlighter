using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using ExileCore2.Shared.Interfaces;
using ExileCore2.Shared.Nodes;

namespace AtlasBiomeHighlighter
{
    /// <summary>
    /// Persisted user waypoint.
    /// Stored as primitive fields to keep settings JSON stable.
    /// </summary>
    public sealed class AtlasWaypoint
    {
        public int X { get; set; }
        public int Y { get; set; }
        public float PositionX { get; set; }
        public float PositionY { get; set; }
        public int TowersCount { get; set; }

        public int ColorArgb { get; set; } = Color.FromArgb(255, 255, 255, 255).ToArgb();
        public bool Selected { get; set; }
        public bool Enabled { get; set; } = true;
        /// <summary>
        /// Whether the waypoint's on-atlas label should be drawn when the main overlay is not
        /// already drawing the map-name label for that node.
        /// </summary>
        public bool ShowLabel { get; set; } = true;
        public string Name { get; set; } = string.Empty;
    }

    
    /// <summary>
    /// A user-defined group of preferred maps. Enabled groups are unioned for matching/highlighting.
    /// Persisted in settings JSON.
    /// </summary>
    public sealed class PreferredMapGroup
    {
        public string Name { get; set; } = "New Group";

        /// <summary>
        /// Whether this group contributes to the active Preferred-map set.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Selected map keys (same labels as in <see cref="AtlasBiomeSettings.PreferredMaps"/>).
        /// </summary>
        public HashSet<string> Maps { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class AtlasBiomeSettings : ISettings
    {
        public ToggleNode Enable { get; set; } = new(true);

        // Screen / viewport overrides (useful for ultrawide and windowed modes where overlay size differs).
        // 0 = auto-detect from the overlay/game window.
        public RangeNode<int> BorderX { get; set; } = new(0, 0, 10000);
        public RangeNode<int> BorderY { get; set; } = new(0, 0, 10000);

        public RangeNode<int> AtlasRefreshMs { get; set; } = new(4753, 50, 30000);
        public RangeNode<int> ScreenRefreshMs { get; set; } = new(800, 50, 5000);
        public RangeNode<int> NodeRadius { get; set; } = new(20, 4, 40);
        public RangeNode<int> RingThickness { get; set; } = new(3, 1, 12);
        public RangeNode<float> Opacity { get; set; } = new(0.8f, 0.05f, 1.0f);
        public ToggleNode ShowLabels { get; set; } = new(true);
        public ToggleNode DebugMode { get; set; } = new(false);
        public ToggleNode DebugPreferredMaps { get; set; } = new(false);
        public ToggleNode DebugPreferredDetails { get; set; } = new(false);
        public ToggleNode PerformanceProfiling { get; set; } = new(false);
        public RangeNode<int> PerformanceSpikeThresholdMs { get; set; } = new(4, 1, 50);

    // Hide overlay on completed (green) atlas nodes
        public ToggleNode HideCompletedMaps { get; set; } = new(true);

    // Hide overlay on 'Attempted' atlas nodes
        public ToggleNode HideAttemptedMaps { get; set; } = new(false);

    // Hide overlay on locked/unavailable nodes
        public ToggleNode HideLockedMaps { get; set; } = new(false);


        // Special highlights (strict/low-noise)
        public ToggleNode HighlightDeadlyBoss { get; set; } = new(true);
        public ColorNode DeadlyBossRingColor { get; set; } = new(Color.FromArgb(220, 60, 60));
        public ToggleNode HighlightCorruptedNexus { get; set; } = new(true);
        public ColorNode CorruptedNexusRingColor { get; set; } = new(Color.FromArgb(160, 32, 240));
        public ToggleNode HighlightCleansed { get; set; } = new(true);
        public ColorNode CleansedRingColor { get; set; } = new(Color.FromArgb(255, 255, 255));
        public ToggleNode HighlightUniqueMaps { get; set; } = new(true);
        public ColorNode UniqueMapRingColor { get; set; } = new(Color.FromArgb(255, 165, 0));
        public ToggleNode HighlightAreaContainsAbyss { get; set; } = new(true);
        public ColorNode AreaContainsAbyssRingColor { get; set; } = new(Color.FromArgb(120, 80, 255));
        public ToggleNode HighlightPreferredMaps { get; set; } = new(false);
        public ColorNode PreferredMapRingColor { get; set; } = new(Color.FromArgb(0, 206, 209)); // teal

        // Directional guides to Preferred maps
        public ToggleNode PreferredGuideLines { get; set; } = new(true);
        public ToggleNode PreferredGuideOnlyOffscreen { get; set; } = new(false);
        public ToggleNode PreferredGuideFromScreenCenter { get; set; } = new(true);
        public RangeNode<int> PreferredGuideThickness { get; set; } = new(2, 1, 8);
        public RangeNode<int> PreferredArrowSize { get; set; } = new(12, 6, 28);
        public RangeNode<int> PreferredGuideLimit { get; set; } = new(40, 5, 200);
    
        // Preferred map groups (tabs). Enabled groups are unioned.
        // Backwards compatible: if empty, plugin will migrate old PreferredMaps toggles into a "Default" group.
        public List<PreferredMapGroup> PreferredMapGroups { get; set; } = new();

        // Master list of user-selectable preferred map names (labels). Values are no longer used for logic
        // once groups are enabled, but are kept for compatibility and to avoid breaking saved settings.
        public Dictionary<string, ToggleNode> PreferredMaps { get; set; } = new()
        {
            ["Arid Plains"] = new(false),
            ["Augury"] = new(false),
            ["Azmerian Ranges"] = new(false),
            ["Backwash"] = new(false),
            ["Barren Atoll"] = new(false),
            ["Bastille"] = new(false),
            ["Bazaar"] = new(false),
            ["Bleached Shoals"] = new(false),
            ["Bloodwood"] = new(false),
            ["Blooming Field"] = new(false),
            ["Burial Bog"] = new(false),
            ["Caer Tarth"] = new(false),
            ["Caldera"] = new(false),
            ["Canyon"] = new(false),
            ["Cenotes"] = new(false),
            ["Channel"] = new(false),
            ["Chasm"] = new(false),
            ["Cliffside"] = new(false),
            ["Confluence"] = new(false),
            ["Craggy Peninsula"] = new(false),
            ["Creek"] = new(false),
            ["Crimson Shores"] = new(false),
            ["Crypt"] = new(false),
            ["Decay"] = new(false),
            ["Deforestation"] = new(false),
            ["Deserted"] = new(false),
            ["Digsite"] = new(false),
            ["Epitaph"] = new(false),
            ["Exhumed Ruins"] = new(false),
            ["Flotsam"] = new(false),
            ["Forge"] = new(false),
            ["Fortress"] = new(false),
            ["Frozen Falls"] = new(false),
            ["Garukhan's Tomb"] = new(false),
            ["Grazed Prairie"] = new(false),
            ["Greenhouse"] = new(false),
            ["Grimhaven"] = new(false),
            ["Headland"] = new(false),
            ["Hidden Grotto"] = new(false),
            ["Hive"] = new(false),
            ["Hive Colony"] = new(false),
            ["Hive Fortress"] = new(false),
            ["Ice Cave"] = new(false),
            ["Inferno"] = new(false),
            ["Lofty Summit"] = new(false),
            ["Lush Isle"] = new(false),
            ["Marrow"] = new(false),
            ["Mineshaft"] = new(false),
            ["Mire"] = new(false),
            ["Molten Vault"] = new(false),
            ["Moor of Fallen Skies"] = new(false),
            ["Mortuary"] = new(false),
            ["Mournful Cliffside"] = new(false),
            ["Necropolis"] = new(false),
            ["Oasis"] = new(false),
            ["Obscure Island"] = new(false),
            ["Orbala's Crossing"] = new(false),
            ["Ornate Chambers"] = new(false),
            ["Overgrown"] = new(false),
            ["Penitentiary"] = new(false),
            ["Pit"] = new(false),
            ["Plantation"] = new(false),
            ["Port"] = new(false),
            ["Precipice"] = new(false),
            ["Ravine"] = new(false),
            ["Razed Fields"] = new(false),
            ["Reservoir"] = new(false),
            ["Riverhold"] = new(false),
            ["Riverside"] = new(false),
            ["Rockpools"] = new(false),
            ["Rugosa"] = new(false),
            ["Rupture"] = new(false),
            ["Rustbowl"] = new(false),
            ["Sanctuary"] = new(false),
            ["Sandspit"] = new(false),
            ["Savannah"] = new(false),
            ["Scorched Cay"] = new(false),
            ["Secluded Temple"] = new(false),
            ["Seepage"] = new(false),
            ["Sinkhole"] = new(false),
            ["Sinter Rift"] = new(false),
            ["Site of the Chosen"] = new(false),
            ["Slash"] = new(false),
            ["Slick"] = new(false),
            ["Sloughed Gully"] = new(false),
            ["Snowfall"] = new(false),
            ["Spider Woods"] = new(false),
            ["Spring"] = new(false),
            ["Stagnant Basin"] = new(false),
            ["Steaming Springs"] = new(false),
            ["Steppe"] = new(false),
            ["Stronghold"] = new(false),
            ["Sulphuric Caverns"] = new(false),
            ["Sump"] = new(false),
            ["Sun Temple"] = new(false),
            ["Sunken Pyramid"] = new(false),
            ["Swarm"] = new(false),
            ["The Assembly"] = new(false),
            ["The Ezomyte Megaliths"] = new(false),
            ["The Well of Souls"] = new(false),
            ["Trenches"] = new(false),
            ["Vaal City"] = new(false),
            ["Vaal Village"] = new(false),
            ["Wayward Isle"] = new(false),
            ["Wetlands"] = new(false),
            ["Willow"] = new(false),
            ["Woodland"] = new(false),
            ["Crux of Nothingness"] = new(false),
            ["Derelict Mansion"] = new(false),
            ["Sacred Reservoir"] = new(false),
            ["Sealed Vault"] = new(false),
            ["Sprawling Jungle"] = new(false),
            ["The Copper Citadel"] = new(false),
            ["The Iron Citadel"] = new(false),
            ["The Jade Isles"] = new(false),
            ["The Matriarch Halls"] = new(false),
            ["The Patriarch Halls"] = new(false),
            ["The Stone Citadel"] = new(false),
            ["Eastern Enigma Chamber"] = new(false),
            ["Western Enigma Chamber"] = new(false),
            ["Corrupted Nexus - Corruption"] = new(false),
            ["Cleansed - Sanctification"] = new(false),
            ["Alpine Ridge"] = new(false),
            ["Bluff"] = new(false),
            ["Lost Towers"] = new(false),
            ["Mesa"] = new(false),
            ["Precursor Tower"] = new(false),
            ["Sinking Spire"] = new(false),
            ["The Burning Monolith"] = new(false),
            ["Eastern Gateway"] = new(false),
            ["Western Gateway"] = new(false),
            ["The Ziggurat Refuge"] = new(false),
            ["The Reliquary Vault"] = new(false),
            ["Monastery of the Keepers"] = new(false),
            ["Ruins of Kingsmarch"] = new(false),
            ["The Withered Willow"] = new(false),
            ["The Monument"] = new(false),
            ["Simulacrum of Delusion"] = new(false),
            ["Ancient Gateway"] = new(false),
            ["Merchant's Campsite"] = new(false),
            ["Jado's Campsite"] = new(false),
            ["Hilda's Campsite"] = new(false),
            ["Outlands"] = new(false),
            ["Vaal Ruins"] = new(false),
            ["The Chained Beast"] = new(false),
            ["The Fallen Star"] = new(false),
            ["Frigid Bluffs"] = new(false),
            ["Canal Hideout"] = new(false),
            ["Farmlands Hideout"] = new(false),
            ["Felled Hideout"] = new(false),
            ["Limestone Hideout"] = new(false),
            ["Prison Hideout"] = new(false),
            ["Shrine Hideout"] = new(false),
            ["Castaway"] = new(false),
            ["Moment of Zen"] = new(false),
            ["The Fractured Lake"] = new(false),
            ["The Silent Cave"] = new(false),
            ["The Viridian Wildwood"] = new(false),
            ["Untainted Paradise"] = new(false),
            ["Vaults of Kamasa"] = new(false),
        };
        public RangeNode<int> SpecialRingThickness { get; set; } = new(4, 1, 12);
        public RangeNode<float> SpecialAlphaMultiplier { get; set; } = new(0.85f, 0.1f, 2.0f);
        public ToggleNode ShowUniqueNameOnLabel { get; set; } = new(true);
        public ToggleNode ShowSpecialTag { get; set; } = new(true);
        public ToggleNode HighlightMomentofZen { get; set; } = new(true);
        public ExileCore2.Shared.Nodes.ColorNode MomentofZenRingColor { get; set; } = new(System.Drawing.Color.FromArgb(255, 208, 207));
        public ToggleNode PreferMapNameForDeadly { get; set; } = new(true);

        // Minimal but readable labels
        public RangeNode<int> LabelOffset { get; set; } = new(20, -60, 60);
        public ToggleNode LabelUseBiomeColor { get; set; } = new(true);
        public ColorNode LabelTextColor { get; set; } = new(Color.White);
        public ToggleNode LabelOutline { get; set; } = new(true);
        public RangeNode<int> LabelOutlineThickness { get; set; } = new(2, 1, 6);
        public ToggleNode LabelBold { get; set; } = new(true);

        // ===== Requested features =====
        // Map name labels (independent from biome/special labels).
        public ToggleNode ShowMapNames { get; set; } = new(true);
        public RangeNode<int> MapNameOffsetY { get; set; } = new(34, -10, 120);
        // Map connections overlay.
        public ToggleNode DrawMapConnections { get; set; } = new(false);
        public ToggleNode DrawVisitedConnections { get; set; } = new(false);
        public ColorNode ConnectionColor { get; set; } = new(Color.FromArgb(230, 230, 230));
        public ColorNode ConnectionColorLocked { get; set; } = new(Color.FromArgb(120, 120, 120));
        public RangeNode<int> ConnectionThickness { get; set; } = new(1, 1, 6);

        // Waypoints.
        public ToggleNode WaypointsEnabled { get; set; } = new(false);
        public ToggleNode ShowWaypointsOnAtlas { get; set; } = new(true);
        public ToggleNode ShowWaypointArrowsOnAtlas { get; set; } = new(true);
        public List<AtlasWaypoint> Waypoints { get; set; } = new();
        public ColorNode DefaultWaypointColor { get; set; } = new(Color.FromArgb(255, 80, 80));
        public RangeNode<int> WaypointRingRadius { get; set; } = new(32, 8, 64);
        public RangeNode<int> WaypointRingThickness { get; set; } = new(3, 1, 12);
        public RangeNode<int> WaypointAtlasMaxItems { get; set; } = new(30, 5, 250);
        public ToggleNode WaypointAtlasUnlockedOnly { get; set; } = new(false);

        // Shortest path.
        // Optional: draw a full shortest-path polyline to the selected waypoint.
        // Users requested that adding a waypoint should NOT automatically draw "roads" to it.
        public ToggleNode DrawShortestPath { get; set; } = new(false);
        public ColorNode ShortestPathColor { get; set; } = new(Color.FromArgb(255, 140, 0));
        public RangeNode<int> ShortestPathThickness { get; set; } = new(3, 1, 10);

        // Tower range.
        public ToggleNode DrawTowerRange { get; set; } = new(false);
        public RangeNode<int> TowerRange { get; set; } = new(11, 1, 30);
        public ColorNode TowerRangeColor { get; set; } = new(Color.FromArgb(255, 255, 255));

        // Hotkeys.
        public HotkeyNode AddWaypointHotkey { get; set; } = new(Keys.Insert);
        public HotkeyNode DeleteWaypointHotkey { get; set; } = new(Keys.Delete);
        public HotkeyNode ToggleWaypointPanelHotkey { get; set; } = new(Keys.End);
        public HotkeyNode ShowTowerRangeHotkey { get; set; } = new(Keys.PageUp);

        public Dictionary<Biome, ToggleNode> Visible { get; } = new()
        {
            [Biome.Water] = new(true),
            [Biome.Ocean] = new(true),
            [Biome.BreachCity] = new(true),
            [Biome.OriathCity] = new(true),
            [Biome.Swamp] = new(true),
            [Biome.Mountain] = new(true),
            [Biome.Forest] = new(true),
            [Biome.Desert] = new(true),
            [Biome.Grass] = new(true),
            [Biome.Citadel_Stone] = new(false),
            [Biome.Citadel_Iron] = new(false),
            [Biome.Citadel_Copper] = new(false)
        };

        public Dictionary<Biome, ColorNode> Colors { get; } = new()
        {
            [Biome.Water] = new(Color.FromArgb(30, 144, 255)),
            [Biome.Ocean] = new(Color.FromArgb(0, 200, 255)),
            [Biome.BreachCity] = new(Color.FromArgb(180, 0, 255)),
            [Biome.OriathCity] = new(Color.FromArgb(255, 215, 0)),
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
