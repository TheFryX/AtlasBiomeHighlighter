using System.Linq;
using System.Numerics;
using ImGuiNET;

namespace AtlasBiomeHighlighter
{
    public partial class AtlasBiomeHighlighter
    {
        private string _preferredFilter = string.Empty;
        private string _newPreferredGroupName = "New Group";
        private string _renamePreferredGroupName = string.Empty;
        private int _selectedPreferredGroup;
        private bool _renamePreferredGroupPopupOpen;

        public override void DrawSettings()
        {
            var s = Settings;

            DrawGeneralSettings(s);
            DrawRenderingSettings(s);
            DrawLabelSettings(s);
            DrawBiomeSettings(s);
            DrawPreferredMapSettings(s);
            DrawWaypointAndRoutingSettings(s);
            DrawTowerRangeSettings(s);
            DrawSpecialHighlightSettings(s);
            DrawDebugSettings(s);
        }

        private void DrawGeneralSettings(AtlasBiomeSettings s)
        {
            if (!ImGui.CollapsingHeader("General"))
                return;

            ImGui.Indent();
            { bool v = s.Enable.Value; if (ImGui.Checkbox("Enable", ref v)) s.Enable.Value = v; }
            { bool v = s.ShowLabels.Value; if (ImGui.Checkbox("Show labels", ref v)) s.ShowLabels.Value = v; }
            { bool v = s.ShowMapNames.Value; if (ImGui.Checkbox("Show map names", ref v)) s.ShowMapNames.Value = v; }
            { int v = s.MapNameOffsetY.Value; if (ImGui.SliderInt("Map name Y offset", ref v, s.MapNameOffsetY.Min, s.MapNameOffsetY.Max)) s.MapNameOffsetY.Value = v; }

            ImGui.Separator();
            ImGui.TextDisabled("Visibility filters");
            { bool v = s.HideCompletedMaps.Value; if (ImGui.Checkbox("Hide completed maps", ref v)) s.HideCompletedMaps.Value = v; }
            { bool v = s.HideAttemptedMaps.Value; if (ImGui.Checkbox("Hide attempted maps", ref v)) s.HideAttemptedMaps.Value = v; }
            { bool v = s.HideLockedMaps.Value; if (ImGui.Checkbox("Hide locked maps", ref v)) s.HideLockedMaps.Value = v; }
            ImGui.Unindent();
        }

        private void DrawRenderingSettings(AtlasBiomeSettings s)
        {
            if (!ImGui.CollapsingHeader("Rendering"))
                return;

            ImGui.Indent();
            { int v = s.NodeRadius.Value; if (ImGui.SliderInt("Node radius", ref v, s.NodeRadius.Min, s.NodeRadius.Max)) s.NodeRadius.Value = v; }
            { int v = s.RingThickness.Value; if (ImGui.SliderInt("Ring thickness", ref v, s.RingThickness.Min, s.RingThickness.Max)) s.RingThickness.Value = v; }
            { float v = s.Opacity.Value; if (ImGui.SliderFloat("Opacity", ref v, s.Opacity.Min, s.Opacity.Max)) s.Opacity.Value = v; }

            ImGui.Separator();
            ImGui.TextDisabled("Connections");
            { int v = s.ConnectionThickness.Value; if (ImGui.SliderInt("Connection thickness", ref v, s.ConnectionThickness.Min, s.ConnectionThickness.Max)) s.ConnectionThickness.Value = v; }
            DrawColorEdit("Connection color (unlocked)", s.ConnectionColor.Value, c => s.ConnectionColor.Value = c);
            DrawColorEdit("Connection color (locked)", s.ConnectionColorLocked.Value, c => s.ConnectionColorLocked.Value = c);

            ImGui.Separator();
            ImGui.TextDisabled("Waypoints / routing");
            { int v = s.WaypointRingRadius.Value; if (ImGui.SliderInt("Waypoint ring radius", ref v, s.WaypointRingRadius.Min, s.WaypointRingRadius.Max)) s.WaypointRingRadius.Value = v; }
            { int v = s.WaypointRingThickness.Value; if (ImGui.SliderInt("Waypoint ring thickness", ref v, s.WaypointRingThickness.Min, s.WaypointRingThickness.Max)) s.WaypointRingThickness.Value = v; }
            { int v = s.ShortestPathThickness.Value; if (ImGui.SliderInt("Shortest path thickness", ref v, s.ShortestPathThickness.Min, s.ShortestPathThickness.Max)) s.ShortestPathThickness.Value = v; }
            ImGui.Unindent();
        }

        private void DrawLabelSettings(AtlasBiomeSettings s)
        {
            if (!ImGui.CollapsingHeader("Labels"))
                return;

            ImGui.Indent();
            { int v = s.LabelOffset.Value; if (ImGui.SliderInt("Label vertical offset", ref v, s.LabelOffset.Min, s.LabelOffset.Max)) s.LabelOffset.Value = v; }
            { bool v = s.LabelUseBiomeColor.Value; if (ImGui.Checkbox("Use biome color for text", ref v)) s.LabelUseBiomeColor.Value = v; }
            DrawColorEdit("Label text color", s.LabelTextColor.Value, c => s.LabelTextColor.Value = c, false);
            { bool v = s.LabelOutline.Value; if (ImGui.Checkbox("Label outline", ref v)) s.LabelOutline.Value = v; }
            { int v = s.LabelOutlineThickness.Value; if (ImGui.SliderInt("Outline thickness", ref v, s.LabelOutlineThickness.Min, s.LabelOutlineThickness.Max)) s.LabelOutlineThickness.Value = v; }
            { bool v = s.LabelBold.Value; if (ImGui.Checkbox("Label bold (thicker)", ref v)) s.LabelBold.Value = v; }
            { bool v = s.ShowSpecialTag.Value; if (ImGui.Checkbox("Show special tag on label", ref v)) s.ShowSpecialTag.Value = v; }
            { bool v = s.ShowUniqueNameOnLabel.Value; if (ImGui.Checkbox("Show Unique map name instead of biome", ref v)) s.ShowUniqueNameOnLabel.Value = v; }
            { bool v = s.PreferMapNameForDeadly.Value; if (ImGui.Checkbox("Prefer map name on Deadly", ref v)) s.PreferMapNameForDeadly.Value = v; }
            ImGui.Unindent();
        }

        private void DrawBiomeSettings(AtlasBiomeSettings s)
        {
            if (!ImGui.CollapsingHeader("Biomes"))
                return;

            ImGui.Indent();
            if (ImGui.BeginTable(
                    "##biomes_table",
                    1,
                    ImGuiTableFlags.RowBg |
                    ImGuiTableFlags.BordersInnerH |
                    ImGuiTableFlags.SizingStretchSame))
            {
                ImGui.TableSetupColumn("Biome", ImGuiTableColumnFlags.WidthStretch);

                foreach (var kvp in s.Visible.ToArray())
                {
                    var biome = kvp.Key;
                    if (biome == Biome.Unknown)
                        continue;

                    ImGui.PushID((int)biome);
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);

                    bool vis = kvp.Value.Value;
                    if (ImGui.Checkbox("##enabled", ref vis))
                        kvp.Value.Value = vis;

                    ImGui.SameLine(0, 8);
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted(biome.ToString());

                    ImGui.SameLine(0, 10);
                    float colorWidth = ImGui.GetFrameHeight() * 1.25f;
                    if (colorWidth < 22f) colorWidth = 22f;
                    ImGui.PushItemWidth(colorWidth);

                    var c = s.Colors[biome].Value;
                    var vec = new Vector4(c.R / 255f, c.G / 255f, c.B / 255f, 1f);
                    if (ImGui.ColorEdit4("##color", ref vec, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoAlpha))
                    {
                        s.Colors[biome].Value = System.Drawing.Color.FromArgb((int)(vec.X * 255f), (int)(vec.Y * 255f), (int)(vec.Z * 255f));
                    }

                    ImGui.PopItemWidth();
                    ImGui.PopID();
                }

                ImGui.EndTable();
            }
            ImGui.TextDisabled("Alpha is controlled globally by Opacity.");
            ImGui.Unindent();
        }

        private void DrawPreferredMapSettings(AtlasBiomeSettings s)
        {
            if (!ImGui.CollapsingHeader("Preferred Maps"))
                return;

            ImGui.Indent();
            MigratePreferredGroupsIfNeeded();

            var groups = Settings.PreferredMapGroups;
            if (groups == null)
                Settings.PreferredMapGroups = groups = new System.Collections.Generic.List<PreferredMapGroup>();
            if (groups.Count == 0)
                groups.Add(new PreferredMapGroup { Name = "Default", Enabled = true });

            if (_selectedPreferredGroup < 0) _selectedPreferredGroup = 0;
            if (_selectedPreferredGroup >= groups.Count) _selectedPreferredGroup = groups.Count - 1;

            bool highlight = s.HighlightPreferredMaps.Value;
            if (ImGui.Checkbox("Highlight Preferred maps", ref highlight))
                s.HighlightPreferredMaps.Value = highlight;
            DrawColorEdit("Preferred ring", s.PreferredMapRingColor.Value, c => s.PreferredMapRingColor.Value = c, false);

            ImGui.Separator();
            ImGui.TextDisabled("Map Groups");
            ImGui.SetNextItemWidth(220);
            ImGui.InputText("##preferred_new_group", ref _newPreferredGroupName, 64);
            ImGui.SameLine();
            if (ImGui.Button("Add Group"))
            {
                var name = string.IsNullOrWhiteSpace(_newPreferredGroupName) ? "New Group" : _newPreferredGroupName.Trim();
                groups.Add(new PreferredMapGroup { Name = name, Enabled = true });
                _selectedPreferredGroup = groups.Count - 1;
            }

            const float tabBarHeight = 26f;
            bool tabsOpen = ImGui.BeginChild("##preferred_group_tabs", new Vector2(0, tabBarHeight), ImGuiChildFlags.Border, ImGuiWindowFlags.HorizontalScrollbar);
            if (tabsOpen)
            {
                float avail = ImGui.GetContentRegionAvail().X;
                float startX = ImGui.GetCursorPosX();
                for (int i = 0; i < groups.Count; i++)
                {
                    var g = groups[i];
                    ImGui.PushID(i);
                    string tabText = $"{g.Name} [{(g.Enabled ? "ON" : "OFF")}]";
                    float w = ImGui.CalcTextSize(tabText).X + 16f;
                    float x = ImGui.GetCursorPosX() - startX;
                    if (x + w > avail && x > 0)
                        ImGui.NewLine();

                    if (ImGui.Selectable(tabText, _selectedPreferredGroup == i, ImGuiSelectableFlags.None, new Vector2(w, 0)))
                        _selectedPreferredGroup = i;

                    ImGui.SameLine(0, 6f);
                    ImGui.PopID();
                }
            }
            ImGui.EndChild();

            var activeGroup = groups[_selectedPreferredGroup];
            bool enabled = activeGroup.Enabled;
            if (ImGui.Checkbox("Enable this group", ref enabled)) activeGroup.Enabled = enabled;
            ImGui.SameLine();
            if (ImGui.Button("Rename"))
            {
                _renamePreferredGroupName = activeGroup.Name;
                _renamePreferredGroupPopupOpen = true;
                ImGui.OpenPopup("RenamePreferredGroupPopup");
            }
            ImGui.SameLine();
            if (ImGui.Button("Delete Group") && groups.Count > 1)
            {
                groups.RemoveAt(_selectedPreferredGroup);
                if (_selectedPreferredGroup >= groups.Count) _selectedPreferredGroup = groups.Count - 1;
            }

            if (_renamePreferredGroupPopupOpen && ImGui.BeginPopupModal("RenamePreferredGroupPopup", ref _renamePreferredGroupPopupOpen, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.TextDisabled("New name:");
                ImGui.SetNextItemWidth(280);
                ImGui.InputText("##rename_pref_group", ref _renamePreferredGroupName, 64);
                if (ImGui.Button("OK"))
                {
                    activeGroup.Name = string.IsNullOrWhiteSpace(_renamePreferredGroupName) ? activeGroup.Name : _renamePreferredGroupName.Trim();
                    _renamePreferredGroupPopupOpen = false;
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel"))
                {
                    _renamePreferredGroupPopupOpen = false;
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }

            ImGui.Separator();
            if (ImGui.CollapsingHeader("Preferred Guide Lines"))
            {
                ImGui.Indent();
                { bool v = s.PreferredGuideLines.Value; if (ImGui.Checkbox("Draw Preferred guide lines", ref v)) s.PreferredGuideLines.Value = v; }
                { bool v = s.PreferredGuideOnlyOffscreen.Value; if (ImGui.Checkbox("Only when off-screen", ref v)) s.PreferredGuideOnlyOffscreen.Value = v; }
                { bool v = s.PreferredGuideFromScreenCenter.Value; if (ImGui.Checkbox("Origin at screen center", ref v)) s.PreferredGuideFromScreenCenter.Value = v; }
                { int v = s.PreferredGuideThickness.Value; if (ImGui.SliderInt("Guide thickness", ref v, 1, 8)) s.PreferredGuideThickness.Value = v; }
                { int v = s.PreferredArrowSize.Value; if (ImGui.SliderInt("Arrow size", ref v, 6, 28)) s.PreferredArrowSize.Value = v; }
                { int v = s.PreferredGuideLimit.Value; if (ImGui.SliderInt("Max guide count", ref v, 5, 200)) s.PreferredGuideLimit.Value = v; }
                ImGui.Unindent();
            }

            ImGui.TextDisabled("Select maps for this group:");
            ImGui.InputText("Filter##preferred", ref _preferredFilter, 128);
            ImGui.BeginChild("##preferred_maps_child", new Vector2(0, 300), ImGuiChildFlags.Border, ImGuiWindowFlags.None);

            DrawPreferredCategory("Normal Maps", activeGroup, key => IsPreferredInCategory(key, PreferredNormalMaps));
            DrawPreferredCategory("Bosses", activeGroup, key => IsPreferredInCategory(key, PreferredBosses));
            DrawPreferredCategory("Towers", activeGroup, key => IsPreferredInCategory(key, PreferredTowers));
            DrawPreferredCategory("Atlas Objects", activeGroup, key => IsPreferredInCategory(key, PreferredAtlasObjects));
            DrawPreferredCategory("Hideout", activeGroup, key => IsPreferredInCategory(key, PreferredHideouts));
            DrawPreferredCategory("Unique Maps", activeGroup, key => IsPreferredInCategory(key, PreferredUniqueMaps));

            ImGui.EndChild();
            ImGui.Unindent();
        }

        private void DrawWaypointAndRoutingSettings(AtlasBiomeSettings s)
        {
            if (!ImGui.CollapsingHeader("Waypoints & Routing"))
                return;

            ImGui.Indent();
            { bool v = s.DrawMapConnections.Value; if (ImGui.Checkbox("Draw map connections", ref v)) s.DrawMapConnections.Value = v; }
            bool hideVisited = !s.DrawVisitedConnections.Value;
            if (ImGui.Checkbox("Hide connections involving visited nodes", ref hideVisited)) s.DrawVisitedConnections.Value = !hideVisited;

            ImGui.Separator();
            { bool v = s.WaypointsEnabled.Value; if (ImGui.Checkbox("Waypoints enabled", ref v)) s.WaypointsEnabled.Value = v; }
            DrawColorEdit("Default waypoint color", s.DefaultWaypointColor.Value, c => s.DefaultWaypointColor.Value = c, false);

            ImGui.Separator();
            { bool v = s.DrawShortestPath.Value; if (ImGui.Checkbox("Draw shortest path to selected waypoint", ref v)) s.DrawShortestPath.Value = v; }
            DrawColorEdit("Shortest path color", s.ShortestPathColor.Value, c => s.ShortestPathColor.Value = c, false);
            ImGui.TextDisabled("Hotkeys: Insert add waypoint, Delete remove, End waypoint window.");
            ImGui.Unindent();
        }

        private void DrawTowerRangeSettings(AtlasBiomeSettings s)
        {
            if (!ImGui.CollapsingHeader("Tower Range"))
                return;

            ImGui.Indent();
            { bool v = s.DrawTowerRange.Value; if (ImGui.Checkbox("Tower range (toggle hotkey)", ref v)) s.DrawTowerRange.Value = v; }
            { int v = s.TowerRange.Value; if (ImGui.SliderInt("Tower range (coord)", ref v, s.TowerRange.Min, s.TowerRange.Max)) s.TowerRange.Value = v; }
            DrawColorEdit("Tower range color", s.TowerRangeColor.Value, c => s.TowerRangeColor.Value = c, false);
            ImGui.TextDisabled("Hotkey: PageUp tower range toggle.");
            ImGui.Unindent();
        }

        private void DrawSpecialHighlightSettings(AtlasBiomeSettings s)
        {
            if (!ImGui.CollapsingHeader("Special Highlights"))
                return;

            ImGui.Indent();
            DrawHighlightRow("Deadly Map Boss", s.HighlightDeadlyBoss.Value, v => s.HighlightDeadlyBoss.Value = v, "DeadlyBoss", s.DeadlyBossRingColor.Value, c => s.DeadlyBossRingColor.Value = c);
            DrawHighlightRow("Moment of Zen / Merchant", s.HighlightMomentofZen.Value, v => s.HighlightMomentofZen.Value = v, "MomentofZen", s.MomentofZenRingColor.Value, c => s.MomentofZenRingColor.Value = c);
            DrawHighlightRow("Corrupted Nexus", s.HighlightCorruptedNexus.Value, v => s.HighlightCorruptedNexus.Value = v, "CorruptedNexus", s.CorruptedNexusRingColor.Value, c => s.CorruptedNexusRingColor.Value = c);
            DrawHighlightRow("Cleansed", s.HighlightCleansed.Value, v => s.HighlightCleansed.Value = v, "Cleansed", s.CleansedRingColor.Value, c => s.CleansedRingColor.Value = c);
            DrawHighlightRow("Unique maps", s.HighlightUniqueMaps.Value, v => s.HighlightUniqueMaps.Value = v, "UniqueMap", s.UniqueMapRingColor.Value, c => s.UniqueMapRingColor.Value = c);

            ImGui.Separator();
            { int v = s.SpecialRingThickness.Value; if (ImGui.SliderInt("Special ring thickness", ref v, s.SpecialRingThickness.Min, s.SpecialRingThickness.Max)) s.SpecialRingThickness.Value = v; }
            { float v = s.SpecialAlphaMultiplier.Value; if (ImGui.SliderFloat("Special alpha multiplier", ref v, s.SpecialAlphaMultiplier.Min, s.SpecialAlphaMultiplier.Max)) s.SpecialAlphaMultiplier.Value = v; }
            ImGui.Unindent();
        }

        private void DrawDebugSettings(AtlasBiomeSettings s)
        {
            if (!ImGui.CollapsingHeader("Debug / Advanced"))
                return;

            ImGui.Indent();
            { bool v = s.DebugMode.Value; if (ImGui.Checkbox("Debug mode", ref v)) s.DebugMode.Value = v; }
            { bool v = s.DebugPreferredMaps.Value; if (ImGui.Checkbox("Debug Preferred map hits to file", ref v)) s.DebugPreferredMaps.Value = v; }
            { bool v = s.DebugPreferredDetails.Value; if (ImGui.Checkbox("Debug include reflected node details", ref v)) s.DebugPreferredDetails.Value = v; }
            ImGui.TextDisabled("Debug log: plugin folder / AtlasBiomeHighlighter.PreferredDebug.log");

            ImGui.Separator();
            ImGui.TextDisabled("Refresh / cache timings");
            { int v = s.AtlasRefreshMs.Value; if (ImGui.SliderInt("Atlas refresh (ms)", ref v, s.AtlasRefreshMs.Min, s.AtlasRefreshMs.Max)) s.AtlasRefreshMs.Value = v; }
            { int v = s.ScreenRefreshMs.Value; if (ImGui.SliderInt("Screen refresh (ms)", ref v, s.ScreenRefreshMs.Min, s.ScreenRefreshMs.Max)) s.ScreenRefreshMs.Value = v; }

            ImGui.Separator();
            if (ImGui.CollapsingHeader("Screen / Ultrawide"))
            {
                ImGui.Indent();
                ImGui.TextDisabled("Override viewport size used for on-screen detection and off-screen guide clamping. 0 = auto.");
                { int v = s.BorderX.Value; if (ImGui.SliderInt("BorderX (width)", ref v, s.BorderX.Min, s.BorderX.Max)) s.BorderX.Value = v; }
                { int v = s.BorderY.Value; if (ImGui.SliderInt("BorderY (height)", ref v, s.BorderY.Min, s.BorderY.Max)) s.BorderY.Value = v; }
                ImGui.Unindent();
            }
            ImGui.Unindent();
        }

        private void DrawPreferredCategory(string label, PreferredMapGroup activeGroup, System.Func<string, bool> predicate)
        {
            var keys = Settings.PreferredMaps.Keys
                .Where(k => predicate(k))
                .Where(k => string.IsNullOrEmpty(_preferredFilter) || k.IndexOf(_preferredFilter, System.StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(k => k, System.StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (keys.Length == 0)
                return;

            if (!ImGui.CollapsingHeader($"{label} ({keys.Length})"))
                return;

            ImGui.Indent();
            foreach (var key in keys)
            {
                bool on = activeGroup.Maps.Contains(key);
                if (ImGui.Checkbox(key, ref on))
                {
                    if (on) activeGroup.Maps.Add(key);
                    else activeGroup.Maps.Remove(key);
                }
            }
            ImGui.Unindent();
        }

        private static readonly string[] PreferredNormalMaps = { "Arid Plains", "Augury", "Azmerian Ranges", "Backwash", "Barren Atoll", "Bastille", "Bazaar", "Bleached Shoals", "Bloodwood", "Blooming Field", "Burial Bog", "Caer Tarth", "Caldera", "Canyon", "Cenotes", "Channel", "Chasm", "Cliffside", "Confluence", "Craggy Peninsula", "Creek", "Crimson Shores", "Crypt", "Decay", "Deforestation", "Deserted", "Digsite", "Epitaph", "Exhumed Ruins", "Flotsam", "Forge", "Fortress", "Frozen Falls", "Garukhan's Tomb", "Grazed Prairie", "Greenhouse", "Grimhaven", "Headland", "Hidden Grotto", "Hive", "Hive Colony", "Hive Fortress", "Ice Cave", "Inferno", "Lofty Summit", "Lush Isle", "Marrow", "Mineshaft", "Mire", "Molten Vault", "Moor of Fallen Skies", "Mortuary", "Mournful Cliffside", "Necropolis", "Oasis", "Obscure Island", "Orbala's Crossing", "Ornate Chambers", "Overgrown", "Penitentiary", "Pit", "Plantation", "Port", "Precipice", "Ravine", "Razed Fields", "Reservoir", "Riverhold", "Riverside", "Rockpools", "Rugosa", "Rupture", "Rustbowl", "Sanctuary", "Sandspit", "Savannah", "Scorched Cay", "Secluded Temple", "Seepage", "Sinkhole", "Sinter Rift", "Site of the Chosen", "Slash", "Slick", "Sloughed Gully", "Snowfall", "Spider Woods", "Spring", "Stagnant Basin", "Steaming Springs", "Steppe", "Stronghold", "Sulphuric Caverns", "Sump", "Sun Temple", "Sunken Pyramid", "Swarm", "The Assembly", "The Ezomyte Megaliths", "The Well of Souls", "Trenches", "Vaal City", "Vaal Village", "Wayward Isle", "Wetlands", "Willow", "Woodland" };
        private static readonly string[] PreferredBosses = { "Crux of Nothingness", "Derelict Mansion", "Sacred Reservoir", "Sealed Vault", "Sprawling Jungle", "The Copper Citadel", "The Iron Citadel", "The Jade Isles", "The Matriarch Halls", "The Patriarch Halls", "The Stone Citadel", "Eastern Enigma Chamber", "Western Enigma Chamber", "Corrupted Nexus - Corruption", "Cleansed - Sanctification" };
        private static readonly string[] PreferredTowers = { "Alpine Ridge", "Bluff", "Lost Towers", "Mesa", "Precursor Tower", "Sinking Spire" };
        private static readonly string[] PreferredAtlasObjects = { "The Burning Monolith", "Eastern Gateway", "Western Gateway", "Site of the Chosen", "The Ziggurat Refuge", "The Reliquary Vault", "Monastery of the Keepers", "Ruins of Kingsmarch", "The Withered Willow", "The Monument", "Simulacrum of Delusion", "Ancient Gateway", "Merchant's Campsite", "Jado's Campsite", "Hilda's Campsite", "Outlands", "Vaal Ruins", "The Chained Beast", "The Fallen Star", "Frigid Bluffs" };
        private static readonly string[] PreferredHideouts = { "Canal Hideout", "Farmlands Hideout", "Felled Hideout", "Limestone Hideout", "Prison Hideout", "Shrine Hideout" };
        private static readonly string[] PreferredUniqueMaps = { "Castaway", "Moment of Zen", "The Fractured Lake", "The Silent Cave", "The Viridian Wildwood", "Untainted Paradise", "Vaults of Kamasa" };

        private static bool IsPreferredInCategory(string key, string[] category)
        {
            for (int i = 0; i < category.Length; i++)
            {
                if (key.Equals(category[i], System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static void DrawHighlightRow(string label, bool value, System.Action<bool> setValue, string colorId, System.Drawing.Color color, System.Action<System.Drawing.Color> setColor)
        {
            bool v = value;
            if (ImGui.Checkbox(label, ref v))
                setValue(v);
            ImGui.SameLine();
            if (DrawColorSquare(colorId, color, out var c))
                setColor(c);
        }

        private static void DrawColorEdit(string label, System.Drawing.Color color, System.Action<System.Drawing.Color> setColor, bool includeAlpha = true)
        {
            var vec = new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, includeAlpha ? color.A / 255f : 1f);
            var flags = ImGuiColorEditFlags.NoInputs;
            if (!includeAlpha)
                flags |= ImGuiColorEditFlags.NoAlpha;

            if (ImGui.ColorEdit4(label, ref vec, flags))
            {
                int r = ClampToByte(vec.X * 255f);
                int g = ClampToByte(vec.Y * 255f);
                int b = ClampToByte(vec.Z * 255f);
                int a = includeAlpha ? ClampToByte(vec.W * 255f) : 255;
                setColor(System.Drawing.Color.FromArgb(a, r, g, b));
            }
        }

        private static bool DrawColorSquare(string id, System.Drawing.Color color, out System.Drawing.Color newColor)
        {
            float size = ImGui.GetFrameHeight();
            if (size < 18f) size = 18f;

            ImGui.PushID(id);
            ImGui.SetNextItemWidth(size);

            var vec = new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
            bool changed = ImGui.ColorEdit4("##c", ref vec, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel);
            if (changed)
            {
                newColor = System.Drawing.Color.FromArgb(ClampToByte(vec.W * 255f), ClampToByte(vec.X * 255f), ClampToByte(vec.Y * 255f), ClampToByte(vec.Z * 255f));
            }
            else
            {
                newColor = color;
            }

            ImGui.PopID();
            return changed;
        }

        private static int ClampToByte(float value)
        {
            if (value < 0f) return 0;
            if (value > 255f) return 255;
            return (int)value;
        }
    }
}
