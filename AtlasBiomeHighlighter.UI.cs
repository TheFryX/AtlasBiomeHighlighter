using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Windows.Forms;
using ExileCore2.Shared.Nodes;
using ImGuiNET;

namespace AtlasBiomeHighlighter
{
    public partial class AtlasBiomeHighlighter
    {
        private string _preferredFilter = string.Empty;
        private string _specialMechanicFilter = string.Empty;
        private string _newPreferredGroupName = "New Group";
        private string _renamePreferredGroupName = string.Empty;
        private int _selectedPreferredGroup;
        private bool _renamePreferredGroupPopupOpen;
        private bool _capturingPreferredGuideHotkey;
        private bool _capturingWaypointHotkey;
        private HotkeyNode? _capturingWaypointHotkeyNode;
        private string _capturingWaypointHotkeyLabel = string.Empty;

        public override void DrawSettings()
        {
            var s = Settings;

            DrawGeneralSettings(s);
            DrawRenderingSettings(s);
            DrawLabelSettings(s);
            DrawBiomeSettings(s);
            DrawIslandRumourSettings(s);
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
            { bool v = s.ShowMapStatus.Value; if (ImGui.Checkbox("Show map status", ref v)) s.ShowMapStatus.Value = v; }
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
            { bool v = s.FastRingRendering.Value; if (ImGui.Checkbox("Fast ring rendering", ref v)) s.FastRingRendering.Value = v; }
            { int v = s.FastRingMaxSegments.Value; if (ImGui.SliderInt("Fast ring max segments", ref v, s.FastRingMaxSegments.Min, s.FastRingMaxSegments.Max)) s.FastRingMaxSegments.Value = v; }

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

        private void DrawIslandRumourSettings(AtlasBiomeSettings s)
        {
            if (!ImGui.CollapsingHeader("Island Rumours"))
                return;

            ImGui.Indent();
            { bool v = s.IslandRumoursEnabled.Value; if (ImGui.Checkbox("Enable Island Rumours", ref v)) s.IslandRumoursEnabled.Value = v; }
            { bool v = s.ShowIslandRumourLabels.Value; if (ImGui.Checkbox("Show tables near atlas buttons", ref v)) s.ShowIslandRumourLabels.Value = v; }
            {
                bool v = s.IslandRumourLiveTooltipScanEnabled.Value;
                if (ImGui.Checkbox("Live tooltip scan (max 3 entries)", ref v))
                {
                    s.IslandRumourLiveTooltipScanEnabled.Value = v;
                    RequestIslandRumourRefresh();
                }
            }
            if (s.IslandRumourLiveTooltipScanEnabled.Value)
            {
                ImGui.TextDisabled("Live mode: scans Tooltip/Children only for AtlasButtonNode.IsVisible == true; maximum 3 rows.");
            }
            else
            {
                ImGui.TextDisabled("Fast mode: reads AtlasPanel.Buttons.Rumors and displays every discovered entry (including new ones).");
            }
            DrawColorEdit("Rumour text", s.IslandRumourTextColor.Value, c => s.IslandRumourTextColor.Value = c, false);
            { bool v = s.IslandRumourUseIndividualColors.Value; if (ImGui.Checkbox("Use per-rumour colors", ref v)) s.IslandRumourUseIndividualColors.Value = v; }
            { bool v = s.ShowIslandRumourRegionStats.Value; if (ImGui.Checkbox("Show region map / Grand Expedition counts", ref v)) s.ShowIslandRumourRegionStats.Value = v; }
            DrawColorEdit("Region stats text", s.IslandRumourRegionStatsColor.Value, c => s.IslandRumourRegionStatsColor.Value = c, false);
            { int v = s.IslandRumourRefreshMs.Value; if (ImGui.SliderInt("Button cache refresh (ms)", ref v, s.IslandRumourRefreshMs.Min, s.IslandRumourRefreshMs.Max)) s.IslandRumourRefreshMs.Value = v; }
            { int v = s.IslandRumourLabelOffsetY.Value; if (ImGui.SliderInt("Table distance from button", ref v, s.IslandRumourLabelOffsetY.Min, s.IslandRumourLabelOffsetY.Max)) s.IslandRumourLabelOffsetY.Value = v; }
            { int v = s.IslandRumourLabelFontSize.Value; if (ImGui.SliderInt("Table font size", ref v, s.IslandRumourLabelFontSize.Min, s.IslandRumourLabelFontSize.Max)) s.IslandRumourLabelFontSize.Value = v; }
            { int v = s.IslandRumourLabelMaxWidth.Value; if (ImGui.SliderInt("Table max width", ref v, s.IslandRumourLabelMaxWidth.Min, s.IslandRumourLabelMaxWidth.Max)) s.IslandRumourLabelMaxWidth.Value = v; }
            { int v = s.IslandRumourLabelSpacing.Value; if (ImGui.SliderInt("Table row height", ref v, s.IslandRumourLabelSpacing.Min, s.IslandRumourLabelSpacing.Max)) s.IslandRumourLabelSpacing.Value = v; }
            { float v = s.IslandRumourLabelBackgroundOpacity.Value; if (ImGui.SliderFloat("Table background opacity", ref v, s.IslandRumourLabelBackgroundOpacity.Min, s.IslandRumourLabelBackgroundOpacity.Max)) s.IslandRumourLabelBackgroundOpacity.Value = v; }
            ImGui.TextDisabled("Fast mode scans all Rumors. Live mode limits the expensive child scan to currently visible atlas buttons.");
            if (ImGui.SmallButton("Reset table style##island_rumours_reset_style"))
            {
                s.IslandRumourLabelFontSize.Value = 16;
                s.IslandRumourLabelMaxWidth.Value = 540;
                s.IslandRumourLabelSpacing.Value = 28;
                s.IslandRumourLabelOffsetY.Value = 44;
                s.IslandRumourLabelBackgroundOpacity.Value = 0.92f;
            }

            ImGui.Separator();
            if (ImGui.SmallButton("Refresh now##island_rumours_refresh_now"))
            {
                RequestIslandRumourRefresh();
                UpdateIslandRumourCache();
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("Clear cache##island_rumours_clear_cache"))
            {
                ClearIslandRumourCache();
            }

            ImGui.TextDisabled($"Cached buttons: {_islandRumourLastButtonCount}; with rumours: {_islandRumourLastNodeCount}; total rumours: {_islandRumourLastRumourCount}");
            var regionStatButtons = _islandRumourSnapshots.Count(snapshot => snapshot.RegionMapCount > 0);
            var regionStatMaps = _islandRumourSnapshots.Sum(snapshot => snapshot.RegionMapCount);
            var regionStatGrandExpeditions = _islandRumourSnapshots.Sum(snapshot => snapshot.RegionGrandExpeditionCount);
            ImGui.TextDisabled($"Region stats: buttons: {regionStatButtons}; maps: {regionStatMaps}; Grand Expedition: {regionStatGrandExpeditions}");
            ImGui.TextDisabled("Maps / GE are resolved once per atlas button and then kept in the session cache.");
            if (!string.IsNullOrWhiteSpace(_islandRumourLastSource))
                ImGui.TextDisabled("Current source: " + _islandRumourLastSource);
            if (!string.IsNullOrWhiteSpace(_islandRumourLastError))
                ImGui.TextDisabled("Last error: " + _islandRumourLastError);

            DrawIslandRumoursCacheTable();

            ImGui.Unindent();
        }

        private void DrawIslandRumoursCacheTable()
        {
            if (!ImGui.CollapsingHeader("Rumours Cache"))
                return;

            EnsureIslandRumourColorSettings();
            var activePreferredGroup = GetActivePreferredMapGroupForUi();
            var observed = BuildIslandRumourCatalog();
            var observedTokens = observed
                .Select(GetIslandRumourToken)
                .Where(token => token.Length != 0)
                .ToHashSet(StringComparer.Ordinal);

            ImGui.Indent();
            ImGui.TextDisabled("Known names are matched against in-game names with punctuation/ellipsis stripped.");
            ImGui.TextDisabled($"Preferred group: {activePreferredGroup.Name} [{(activePreferredGroup.Enabled ? "ON" : "OFF")}]");
            if (ImGui.SmallButton("Reset tier colors##island_rumour_reset_colors"))
            {
                foreach (var definition in BuildIslandRumourDefinitionList())
                    Settings.IslandRumourColors[definition.Name] = new ColorNode(GetDefaultIslandRumourColor(definition));
            }

            var flags =
                ImGuiTableFlags.RowBg |
                ImGuiTableFlags.BordersInnerH |
                ImGuiTableFlags.ScrollY |
                ImGuiTableFlags.SizingStretchProp |
                ImGuiTableFlags.Resizable;

            if (ImGui.BeginTable("##island_rumours_cache_table", 7, flags, new Vector2(0, 330)))
            {
                ImGui.TableSetupColumn("Seen", ImGuiTableColumnFlags.WidthFixed, 42f);
                ImGui.TableSetupColumn("Color", ImGuiTableColumnFlags.WidthFixed, 54f);
                ImGui.TableSetupColumn("Preferred", ImGuiTableColumnFlags.WidthFixed, 72f);
                ImGui.TableSetupColumn("Rumor", ImGuiTableColumnFlags.WidthStretch, 1.1f);
                ImGui.TableSetupColumn("Map Type", ImGuiTableColumnFlags.WidthStretch, 1.0f);
                ImGui.TableSetupColumn("Mods", ImGuiTableColumnFlags.WidthStretch, 1.2f);
                ImGui.TableSetupColumn("Rating", ImGuiTableColumnFlags.WidthFixed, 96f);
                ImGui.TableHeadersRow();

                string currentCategory = string.Empty;
                var definitions = BuildIslandRumourDefinitionList();
                for (int i = 0; i < definitions.Count; i++)
                {
                    var definition = definitions[i];
                    if (!string.Equals(currentCategory, definition.Category, StringComparison.Ordinal))
                    {
                        currentCategory = definition.Category;
                        ImGui.TableNextRow();
                        ImGui.TableSetColumnIndex(0);
                        ImGui.TextDisabled(currentCategory);
                        for (int col = 1; col < 7; col++)
                        {
                            ImGui.TableSetColumnIndex(col);
                            ImGui.TextDisabled("-");
                        }
                    }

                    ImGui.PushID("rumour_cache_" + definition.Name);
                    ImGui.TableNextRow();

                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextUnformatted(observedTokens.Contains(Utility.NormalizeToken(definition.Name)) ? "Yes" : "");

                    ImGui.TableSetColumnIndex(1);
                    var colorNode = Settings.IslandRumourColors[definition.Name];
                    if (DrawColorSquare("color", colorNode.Value, out var newColor))
                        colorNode.Value = newColor;

                    ImGui.TableSetColumnIndex(2);
                    bool preferred = activePreferredGroup.Rumours.Contains(definition.Name);
                    if (ImGui.Checkbox("##preferred", ref preferred))
                    {
                        if (preferred)
                            activePreferredGroup.Rumours.Add(definition.Name);
                        else
                            activePreferredGroup.Rumours.Remove(definition.Name);
                    }

                    ImGui.TableSetColumnIndex(3);
                    ImGui.TextUnformatted(definition.Name);

                    ImGui.TableSetColumnIndex(4);
                    ImGui.TextUnformatted(definition.MapType);

                    ImGui.TableSetColumnIndex(5);
                    ImGui.TextUnformatted(definition.Mods);

                    ImGui.TableSetColumnIndex(6);
                    ImGui.TextUnformatted(definition.Rating);

                    ImGui.PopID();
                }

                ImGui.EndTable();
            }

            var unknownObserved = observed
                .Where(name => !TryGetIslandRumourDefinition(name, out _))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (unknownObserved.Length != 0 && ImGui.CollapsingHeader($"Observed outside table ({unknownObserved.Length})"))
            {
                ImGui.Indent();
                for (int i = 0; i < Math.Min(unknownObserved.Length, 32); i++)
                    ImGui.BulletText(unknownObserved[i]);
                if (unknownObserved.Length > 32)
                    ImGui.TextDisabled($"+ {unknownObserved.Length - 32} more");
                ImGui.Unindent();
            }

            ImGui.Unindent();
        }

        private PreferredMapGroup GetActivePreferredMapGroupForUi()
        {
            MigratePreferredGroupsIfNeeded();

            var groups = Settings.PreferredMapGroups;
            if (groups == null)
                Settings.PreferredMapGroups = groups = new List<PreferredMapGroup>();
            if (groups.Count == 0)
                groups.Add(new PreferredMapGroup { Name = "Default", Enabled = true });

            if (_selectedPreferredGroup < 0)
                _selectedPreferredGroup = 0;
            if (_selectedPreferredGroup >= groups.Count)
                _selectedPreferredGroup = groups.Count - 1;

            var activeGroup = groups[_selectedPreferredGroup];
            activeGroup.Maps ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            activeGroup.Mechanics ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            activeGroup.Rumours ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return activeGroup;
        }

        private void DrawPreferredMapSettings(AtlasBiomeSettings s)
        {
            if (!ImGui.CollapsingHeader("Preferred Maps"))
                return;

            ImGui.Indent();
            var activeGroup = GetActivePreferredMapGroupForUi();
            var groups = Settings.PreferredMapGroups;

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
                activeGroup = groups[_selectedPreferredGroup];
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

            activeGroup = groups[_selectedPreferredGroup];
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
                activeGroup = groups[_selectedPreferredGroup];
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
            if (ImGui.CollapsingHeader("Guide & Key Settings"))
            {
                ImGui.Indent();
                
                
                
                s.PreferredGuideOnlyOffscreen.Value = false;
                s.PreferredGuideFromScreenCenter.Value = true;

                { bool v = s.PreferredGuideLines.Value; if (ImGui.Checkbox("Draw Preferred guide lines", ref v)) s.PreferredGuideLines.Value = v; }
                DrawPreferredGuideHotkeySelector(s);
                { int v = s.PreferredGuideThickness.Value; if (ImGui.SliderInt("Guide thickness", ref v, 1, 8)) s.PreferredGuideThickness.Value = v; }
                { int v = s.PreferredArrowSize.Value; if (ImGui.SliderInt("Arrow size", ref v, 6, 28)) s.PreferredArrowSize.Value = v; }
                { int v = s.PreferredGuideLimit.Value; if (ImGui.SliderInt("Max guide count", ref v, 5, 200)) s.PreferredGuideLimit.Value = v; }
                ImGui.Unindent();
            }

            ImGui.TextDisabled("Select maps / mechanics for this group:");
            ImGui.InputText("Filter##preferred", ref _preferredFilter, 128);
            ImGui.BeginChild("##preferred_maps_child", new Vector2(0, 300), ImGuiChildFlags.Border, ImGuiWindowFlags.None);

            DrawPreferredCategory("Normal Maps", activeGroup, key => IsPreferredInCategory(key, PreferredNormalMaps));
            DrawPreferredCategory("Bosses", activeGroup, key => IsPreferredInCategory(key, PreferredBosses));
            DrawPreferredCategory("Towers", activeGroup, key => IsPreferredInCategory(key, PreferredTowers));
            DrawPreferredCategory("Atlas Objects", activeGroup, key => IsPreferredInCategory(key, PreferredAtlasObjects));
            DrawPreferredCategory("Hideout", activeGroup, key => IsPreferredInCategory(key, PreferredHideouts));
            DrawPreferredCategory("Unique Maps", activeGroup, key => IsPreferredInCategory(key, PreferredUniqueMaps));
            DrawPreferredMechanicsCategory("Mechanics", activeGroup);
            DrawPreferredRumoursCategory("Island Rumours", activeGroup);

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
            if (ImGui.CollapsingHeader("Key Settings"))
            {
                ImGui.Indent();
                DrawWaypointHotkeySelector("Add waypoint", s.AddWaypointHotkey, "WaypointAddHotkeyCapturePopup", "default: Insert");
                DrawWaypointHotkeySelector("Remove hovered waypoint", s.DeleteWaypointHotkey, "WaypointDeleteHotkeyCapturePopup", "default: Delete");
                DrawWaypointHotkeySelector("Toggle Navigator window", s.ToggleWaypointPanelHotkey, "WaypointPanelHotkeyCapturePopup", "default: End");
                DrawWaypointHotkeySelector("Toggle shortest path", s.ToggleShortestPathHotkey, "WaypointShortestPathHotkeyCapturePopup", "default: PageDown");
                DrawWaypointHotkeyCapturePopup();
                ImGui.TextDisabled("Click a key button, then press a new key. Esc cancels, Clear disables.");
                ImGui.Unindent();
            }
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

            EnsureMechanicHighlightSettings(s);

            ImGui.Indent();
            DrawHighlightRow("Deadly Map Boss", s.HighlightDeadlyBoss.Value, v => s.HighlightDeadlyBoss.Value = v, "DeadlyBoss", s.DeadlyBossRingColor.Value, c => s.DeadlyBossRingColor.Value = c);
            DrawHighlightRow("Moment of Zen / Merchant", s.HighlightMomentofZen.Value, v => s.HighlightMomentofZen.Value = v, "MomentofZen", s.MomentofZenRingColor.Value, c => s.MomentofZenRingColor.Value = c);
            DrawHighlightRow("Corrupted Nexus", s.HighlightCorruptedNexus.Value, v => s.HighlightCorruptedNexus.Value = v, "CorruptedNexus", s.CorruptedNexusRingColor.Value, c => s.CorruptedNexusRingColor.Value = c);
            DrawHighlightRow("Cleansed", s.HighlightCleansed.Value, v => s.HighlightCleansed.Value = v, "Cleansed", s.CleansedRingColor.Value, c => s.CleansedRingColor.Value = c);
            DrawHighlightRow("Unique maps", s.HighlightUniqueMaps.Value, v => s.HighlightUniqueMaps.Value = v, "UniqueMap", s.UniqueMapRingColor.Value, c => s.UniqueMapRingColor.Value = c);
            DrawHighlightRow("Area contains Abysses", s.HighlightAreaContainsAbyss.Value, v => s.HighlightAreaContainsAbyss.Value = v, "AreaContainsAbyss", s.AreaContainsAbyssRingColor.Value, c => s.AreaContainsAbyssRingColor.Value = c);
            DrawHighlightRow("Area contains Expedition", s.HighlightAreaContainsExpedition.Value, v => s.HighlightAreaContainsExpedition.Value = v, "AreaContainsExpedition", s.AreaContainsExpeditionRingColor.Value, c => s.AreaContainsExpeditionRingColor.Value = c);


            ImGui.Separator();
            if (ImGui.CollapsingHeader("Towers", ImGuiTreeNodeFlags.DefaultOpen))
            {
                EnsureTowerHighlightSettings(s);
                ImGui.Indent();
                if (ImGui.SmallButton("On All##SpecialTowersOnAll"))
                    SetAllTowerHighlights(s, true);
                ImGui.SameLine();
                if (ImGui.SmallButton("Off All##SpecialTowersOffAll"))
                    SetAllTowerHighlights(s, false);

                ImGui.TextDisabled("Each tower has its own enable toggle and ring color.");

                foreach (var tower in Utility.PreferredTowerNames)
                {
                    if (!s.TowerHighlights.TryGetValue(tower, out var node))
                    {
                        node = new ToggleNode(false);
                        s.TowerHighlights[tower] = node;
                    }

                    if (s.TowerHighlightColors == null)
                        s.TowerHighlightColors = new System.Collections.Generic.Dictionary<string, ColorNode>(StringComparer.OrdinalIgnoreCase);
                    if (!s.TowerHighlightColors.TryGetValue(tower, out var colorNode))
                    {
                        colorNode = new ColorNode(s.TowerHighlightRingColor.Value);
                        s.TowerHighlightColors[tower] = colorNode;
                    }

                    bool enabled = node.Value;
                    if (ImGui.Checkbox($"{tower}##tower_{tower}", ref enabled))
                        node.Value = enabled;
                    ImGui.SameLine();
                    DrawColorEdit($"Color##tower_color_{tower}", colorNode.Value, c => colorNode.Value = c, false);
                }

                ImGui.TextDisabled("Detected from atlas node Area/Id; enabled towers draw their own Special Highlight ring.");
                ImGui.Unindent();
            }

            ImGui.Separator();
            if (ImGui.CollapsingHeader("Map Content / Mechanics", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();
                ImGui.SetNextItemWidth(260f);
                ImGui.InputText("Search##SpecialMechanicSearch", ref _specialMechanicFilter, 64);
                ImGui.SameLine();
                if (ImGui.SmallButton("Clear##SpecialMechanicSearchClear"))
                    _specialMechanicFilter = string.Empty;
                ImGui.SameLine();
                if (ImGui.SmallButton("On All##SpecialMechanicsOnAll"))
                    SetAllMechanicHighlights(s, true);
                ImGui.SameLine();
                if (ImGui.SmallButton("Off All##SpecialMechanicsOffAll"))
                    SetAllMechanicHighlights(s, false);

                DrawColorEdit("Mechanic highlight color", s.MechanicHighlightRingColor.Value, c => s.MechanicHighlightRingColor.Value = c, false);

                int visible = 0;
                foreach (var mechanic in Utility.MapContentMechanics)
                {
                    if (!string.IsNullOrWhiteSpace(_specialMechanicFilter) &&
                        mechanic.Name.IndexOf(_specialMechanicFilter, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    if (!s.MechanicHighlights.TryGetValue(mechanic.Name, out var node))
                    {
                        node = new ToggleNode(false);
                        s.MechanicHighlights[mechanic.Name] = node;
                    }

                    bool enabled = node.Value;
                    if (ImGui.Checkbox($"{mechanic.Name}##mechanic_{mechanic.Name}", ref enabled))
                        node.Value = enabled;
                    visible++;
                }

                if (visible == 0)
                    ImGui.TextDisabled("No mechanics match the current search.");
                ImGui.TextDisabled("Detected from ContentIdentity/PassiveArt before hover; enabled entries draw one clean mechanic ring.");
                ImGui.Unindent();
            }

            ImGui.Separator();
            { int v = s.SpecialRingThickness.Value; if (ImGui.SliderInt("Special ring thickness", ref v, s.SpecialRingThickness.Min, s.SpecialRingThickness.Max)) s.SpecialRingThickness.Value = v; }
            { float v = s.SpecialAlphaMultiplier.Value; if (ImGui.SliderFloat("Special alpha multiplier", ref v, s.SpecialAlphaMultiplier.Min, s.SpecialAlphaMultiplier.Max)) s.SpecialAlphaMultiplier.Value = v; }
            ImGui.Unindent();
        }

        private static void SetAllTowerHighlights(AtlasBiomeSettings settings, bool enabled)
        {
            EnsureTowerHighlightSettings(settings);
            foreach (var tower in Utility.PreferredTowerNames)
                settings.TowerHighlights[tower].Value = enabled;
        }

        private static void EnsureTowerHighlightSettings(AtlasBiomeSettings s)
        {
            if (s.TowerHighlights == null)
                s.TowerHighlights = new System.Collections.Generic.Dictionary<string, ToggleNode>(StringComparer.OrdinalIgnoreCase);
            if (s.TowerHighlightColors == null)
                s.TowerHighlightColors = new System.Collections.Generic.Dictionary<string, ColorNode>(StringComparer.OrdinalIgnoreCase);

            foreach (var tower in Utility.PreferredTowerNames)
            {
                if (!s.TowerHighlights.ContainsKey(tower))
                    s.TowerHighlights[tower] = new ToggleNode(false);
                if (!s.TowerHighlightColors.ContainsKey(tower))
                    s.TowerHighlightColors[tower] = new ColorNode(s.TowerHighlightRingColor.Value);
            }
        }

        private static void SetAllMechanicHighlights(AtlasBiomeSettings settings, bool enabled)
        {
            foreach (var mechanic in Utility.MapContentMechanics)
            {
                if (!settings.MechanicHighlights.TryGetValue(mechanic.Name, out var node))
                {
                    node = new ToggleNode(enabled);
                    settings.MechanicHighlights[mechanic.Name] = node;
                    continue;
                }

                node.Value = enabled;
            }
        }

        private static void EnsureMechanicHighlightSettings(AtlasBiomeSettings s)
        {
            if (s.MechanicHighlights == null)
                s.MechanicHighlights = new System.Collections.Generic.Dictionary<string, ToggleNode>(StringComparer.OrdinalIgnoreCase);

            foreach (var mechanic in Utility.MapContentMechanics)
            {
                if (!s.MechanicHighlights.ContainsKey(mechanic.Name))
                    s.MechanicHighlights[mechanic.Name] = new ToggleNode(false);
            }
        }

        private void DrawDebugSettings(AtlasBiomeSettings s)
        {
            if (!ImGui.CollapsingHeader("Debug / Advanced"))
                return;

            ImGui.Indent();
            { bool v = s.DebugMode.Value; if (ImGui.Checkbox("Debug mode", ref v)) s.DebugMode.Value = v; }
            { bool v = s.DebugPreferredMaps.Value; if (ImGui.Checkbox("Debug Preferred map hits to file", ref v)) s.DebugPreferredMaps.Value = v; }
            { bool v = s.DebugPreferredDetails.Value; if (ImGui.Checkbox("Debug include reflected node details", ref v)) s.DebugPreferredDetails.Value = v; }
            { bool v = s.DebugNavigationTargets.Value; if (ImGui.Checkbox("Debug navigation targets / arrows", ref v)) s.DebugNavigationTargets.Value = v; }
            ImGui.TextDisabled("Debug logs: AtlasBiomeHighlighter.PreferredDebug.log / NavigationDebug.log / JumpTrace.txt");
            ImGui.TextDisabled("Spike profiler log: AtlasBiomeHighlighter.PerformanceSpikes.txt");

            ImGui.Separator();
            ImGui.TextDisabled("Performance diagnostics");
            ImGui.TextDisabled("Profiler is active only when Debug mode is enabled.");
            { bool v = s.PerformanceProfiling.Value; if (ImGui.Checkbox("Spike profiler", ref v)) s.PerformanceProfiling.Value = v; }
            { int v = s.PerformanceSpikeThresholdMs.Value; if (ImGui.SliderInt("Spike threshold ms", ref v, s.PerformanceSpikeThresholdMs.Min, s.PerformanceSpikeThresholdMs.Max)) s.PerformanceSpikeThresholdMs.Value = v; }

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


        private static readonly Keys[] PreferredGuideCaptureKeys =
        {
            Keys.Back, Keys.Tab, Keys.Enter, Keys.Pause, Keys.CapsLock,
            Keys.Space, Keys.PageUp, Keys.PageDown, Keys.End, Keys.Home,
            Keys.Left, Keys.Up, Keys.Right, Keys.Down,
            Keys.Insert, Keys.Delete,
            Keys.D0, Keys.D1, Keys.D2, Keys.D3, Keys.D4, Keys.D5, Keys.D6, Keys.D7, Keys.D8, Keys.D9,
            Keys.A, Keys.B, Keys.C, Keys.D, Keys.E, Keys.F, Keys.G, Keys.H, Keys.I, Keys.J, Keys.K, Keys.L, Keys.M,
            Keys.N, Keys.O, Keys.P, Keys.Q, Keys.R, Keys.S, Keys.T, Keys.U, Keys.V, Keys.W, Keys.X, Keys.Y, Keys.Z,
            Keys.NumPad0, Keys.NumPad1, Keys.NumPad2, Keys.NumPad3, Keys.NumPad4,
            Keys.NumPad5, Keys.NumPad6, Keys.NumPad7, Keys.NumPad8, Keys.NumPad9,
            Keys.Multiply, Keys.Add, Keys.Subtract, Keys.Decimal, Keys.Divide,
            Keys.F1, Keys.F2, Keys.F3, Keys.F4, Keys.F5, Keys.F6, Keys.F7, Keys.F8, Keys.F9, Keys.F10, Keys.F11, Keys.F12,
            Keys.OemSemicolon, Keys.Oemplus, Keys.Oemcomma, Keys.OemMinus, Keys.OemPeriod, Keys.OemQuestion,
            Keys.Oemtilde, Keys.OemOpenBrackets, Keys.OemPipe, Keys.OemCloseBrackets, Keys.OemQuotes
        };


        private void DrawWaypointHotkeySelector(string label, HotkeyNode hotkey, string popupId, string hint)
        {
            var current = hotkey.Value;
            var preview = current == Keys.None ? "Disabled" : current.ToString();

            bool requestOpen = false;
            ImGui.PushID(popupId);

            
            if (ImGui.BeginTable("##WaypointHotkeyRow", 3, ImGuiTableFlags.SizingFixedFit))
            {
                ImGui.TableSetupColumn("label", ImGuiTableColumnFlags.WidthFixed, 270f);
                ImGui.TableSetupColumn("button", ImGuiTableColumnFlags.WidthFixed, 112f);
                ImGui.TableSetupColumn("hint", ImGuiTableColumnFlags.WidthStretch);

                ImGui.TableNextRow();

                ImGui.TableSetColumnIndex(0);
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(label);

                ImGui.TableSetColumnIndex(1);
                if (ImGui.Button($"{preview}##WaypointCaptureButton", new Vector2(104f, 0f)))
                {
                    _capturingWaypointHotkey = true;
                    _capturingWaypointHotkeyNode = hotkey;
                    _capturingWaypointHotkeyLabel = label;
                    requestOpen = true;
                }

                ImGui.TableSetColumnIndex(2);
                if (!string.IsNullOrWhiteSpace(hint))
                {
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextDisabled(hint);
                }

                ImGui.EndTable();
            }

            ImGui.PopID();

            
            if (requestOpen)
                ImGui.OpenPopup("WaypointHotkeyCapturePopup");
        }

        private void DrawWaypointHotkeyCapturePopup()
        {
            if (ImGui.BeginPopup("WaypointHotkeyCapturePopup"))
            {
                var label = string.IsNullOrWhiteSpace(_capturingWaypointHotkeyLabel)
                    ? "waypoint action"
                    : _capturingWaypointHotkeyLabel;

                ImGui.Text($"Press new key for {label}.");
                ImGui.TextDisabled("Esc = cancel");

                if (ImGui.Button("Clear"))
                {
                    if (_capturingWaypointHotkeyNode != null)
                        _capturingWaypointHotkeyNode.Value = Keys.None;

                    _capturingWaypointHotkey = false;
                    _capturingWaypointHotkeyNode = null;
                    _capturingWaypointHotkeyLabel = string.Empty;
                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();
                if (ImGui.Button("Cancel"))
                {
                    _capturingWaypointHotkey = false;
                    _capturingWaypointHotkeyNode = null;
                    _capturingWaypointHotkeyLabel = string.Empty;
                    ImGui.CloseCurrentPopup();
                }

                if (IsKeyPressedOnce(Keys.Escape))
                {
                    _capturingWaypointHotkey = false;
                    _capturingWaypointHotkeyNode = null;
                    _capturingWaypointHotkeyLabel = string.Empty;
                    ImGui.CloseCurrentPopup();
                }
                else if (_capturingWaypointHotkeyNode != null)
                {
                    foreach (var key in PreferredGuideCaptureKeys)
                    {
                        if (!IsKeyPressedOnce(key))
                            continue;

                        _capturingWaypointHotkeyNode.Value = key;
                        _capturingWaypointHotkey = false;
                        _capturingWaypointHotkeyNode = null;
                        _capturingWaypointHotkeyLabel = string.Empty;
                        ImGui.CloseCurrentPopup();
                        break;
                    }
                }

                ImGui.EndPopup();
            }
            else if (_capturingWaypointHotkey)
            {
                
                _capturingWaypointHotkey = false;
                _capturingWaypointHotkeyNode = null;
                _capturingWaypointHotkeyLabel = string.Empty;
            }
        }

        private void DrawPreferredGuideHotkeySelector(AtlasBiomeSettings s)
        {
            var current = s.PreferredGuideLinesToggleHotkey.Value;
            var preview = current == Keys.None ? "Disabled" : current.ToString();

            ImGui.SetNextItemWidth(180);
            if (ImGui.Button($"{preview}##PreferredGuideCaptureButton"))
            {
                _capturingPreferredGuideHotkey = true;
                ImGui.OpenPopup("PreferredGuideHotkeyCapturePopup");
            }

            ImGui.SameLine();
            ImGui.TextDisabled("click, then press a key to toggle only arrows/guide lines");

            if (ImGui.BeginPopup("PreferredGuideHotkeyCapturePopup"))
            {
                ImGui.Text("Press new key for Preferred guide arrows.");
                ImGui.TextDisabled("Esc = cancel");

                if (ImGui.Button("Clear"))
                {
                    s.PreferredGuideLinesToggleHotkey.Value = Keys.None;
                    _capturingPreferredGuideHotkey = false;
                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();
                if (ImGui.Button("Cancel"))
                {
                    _capturingPreferredGuideHotkey = false;
                    ImGui.CloseCurrentPopup();
                }

                if (IsKeyPressedOnce(Keys.Escape))
                {
                    _capturingPreferredGuideHotkey = false;
                    ImGui.CloseCurrentPopup();
                }
                else
                {
                    foreach (var key in PreferredGuideCaptureKeys)
                    {
                        if (!IsKeyPressedOnce(key))
                            continue;

                        s.PreferredGuideLinesToggleHotkey.Value = key;
                        _capturingPreferredGuideHotkey = false;
                        ImGui.CloseCurrentPopup();
                        break;
                    }
                }

                ImGui.EndPopup();
            }
            else
            {
                _capturingPreferredGuideHotkey = false;
            }
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

        private void DrawPreferredMechanicsCategory(string label, PreferredMapGroup activeGroup)
        {
            activeGroup.Mechanics ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var keys = Utility.MapContentMechanics
                .Select(m => m.Name)
                .Where(k => string.IsNullOrEmpty(_preferredFilter) || k.IndexOf(_preferredFilter, System.StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(k => k, System.StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (keys.Length == 0)
                return;

            if (!ImGui.CollapsingHeader($"{label} ({keys.Length})"))
                return;

            ImGui.Indent();
            ImGui.TextDisabled("Uses the same fast Map Content cache as Special Highlights.");
            foreach (var key in keys)
            {
                bool on = activeGroup.Mechanics.Contains(key);
                if (ImGui.Checkbox($"{key}##preferred_mechanic_{key}", ref on))
                {
                    if (on) activeGroup.Mechanics.Add(key);
                    else activeGroup.Mechanics.Remove(key);
                }
            }
            ImGui.Unindent();
        }

        private void DrawPreferredRumoursCategory(string label, PreferredMapGroup activeGroup)
        {
            activeGroup.Rumours ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var keys = BuildIslandRumourDefinitionList()
                .Where(d => string.IsNullOrEmpty(_preferredFilter) ||
                            d.Name.IndexOf(_preferredFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            d.MapType.IndexOf(_preferredFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            d.Mods.IndexOf(_preferredFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            d.Rating.IndexOf(_preferredFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToArray();

            if (keys.Length == 0)
                return;

            if (!ImGui.CollapsingHeader($"{label} ({keys.Length})"))
                return;

            ImGui.Indent();
            if (ImGui.BeginTable("##preferred_rumours_table", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("On", ImGuiTableColumnFlags.WidthFixed, 42f);
                ImGui.TableSetupColumn("Rumor", ImGuiTableColumnFlags.WidthStretch, 1.1f);
                ImGui.TableSetupColumn("Map Type", ImGuiTableColumnFlags.WidthStretch, 0.95f);
                ImGui.TableSetupColumn("Mods", ImGuiTableColumnFlags.WidthStretch, 1.1f);
                ImGui.TableSetupColumn("Rating", ImGuiTableColumnFlags.WidthFixed, 82f);
                ImGui.TableHeadersRow();

                for (int i = 0; i < keys.Length; i++)
                {
                    var definition = keys[i];
                    ImGui.PushID("preferred_rumour_" + definition.Name);
                    ImGui.TableNextRow();

                    ImGui.TableSetColumnIndex(0);
                    bool on = activeGroup.Rumours.Contains(definition.Name);
                    if (ImGui.Checkbox("##enabled", ref on))
                    {
                        if (on) activeGroup.Rumours.Add(definition.Name);
                        else activeGroup.Rumours.Remove(definition.Name);
                    }

                    ImGui.TableSetColumnIndex(1);
                    ImGui.TextUnformatted(definition.Name);

                    ImGui.TableSetColumnIndex(2);
                    ImGui.TextUnformatted(definition.MapType);

                    ImGui.TableSetColumnIndex(3);
                    ImGui.TextUnformatted(definition.Mods);

                    ImGui.TableSetColumnIndex(4);
                    ImGui.TextUnformatted(definition.Rating);

                    ImGui.PopID();
                }

                ImGui.EndTable();
            }

            ImGui.TextDisabled("Selected rumours use Preferred guide arrows when the active Island Rumours cache contains them.");
            ImGui.Unindent();
        }

        private static readonly string[] PreferredNormalMaps = { "Arid Plains", "Augury", "Azmerian Ranges", "Backwash", "Barren Atoll", "Bastille", "Bazaar", "Bleached Shoals", "Bloodwood", "Blooming Field", "Burial Bog", "Caer Tarth", "Caldera", "Canyon", "Cenotes", "Channel", "Chasm", "Cliffside", "Confluence", "Craggy Peninsula", "Creek", "Crimson Shores", "Crypt", "Decay", "Deforestation", "Deserted", "Digsite", "Epitaph", "Exhumed Ruins", "Flotsam", "Forge", "Fortress", "Frozen Falls", "Garukhan's Tomb", "Grazed Prairie", "Greenhouse", "Grimhaven", "Headland", "Hidden Grotto", "Hive", "Hive Colony", "Hive Fortress", "Ice Cave", "Inferno", "Lofty Summit", "Lush Isle", "Marrow", "Mineshaft", "Mire", "Molten Vault", "Moor of Fallen Skies", "Mortuary", "Mournful Cliffside", "Necropolis", "Oasis", "Obscure Island", "Orbala's Crossing", "Ornate Chambers", "Overgrown", "Penitentiary", "Pit", "Plantation", "Port", "Precipice", "Ravine", "Razed Fields", "Reservoir", "Riverhold", "Riverside", "Rockpools", "Rugosa", "Rupture", "Rustbowl", "Sanctuary", "Sandspit", "Savannah", "Scorched Cay", "Secluded Temple", "Seepage", "Sinkhole", "Sinter Rift", "Site of the Chosen", "Slash", "Slick", "Sloughed Gully", "Snowfall", "Spider Woods", "Spring", "Stagnant Basin", "Steaming Springs", "Steppe", "Stronghold", "Sulphuric Caverns", "Sump", "Sun Temple", "Sunken Pyramid", "Swarm", "The Assembly", "The Ezomyte Megaliths", "The Well of Souls", "Trenches", "Vaal City", "Vaal Village", "Wayward Isle", "Wetlands", "Willow", "Woodland" };
        private static readonly string[] PreferredBosses = { "Crux of Nothingness", "Derelict Mansion", "Sacred Reservoir", "Sealed Vault", "Sprawling Jungle", "The Copper Citadel", "The Iron Citadel", "The Jade Isles", "The Matriarch Halls", "The Patriarch Halls", "The Stone Citadel", "Eastern Enigma Chamber", "Western Enigma Chamber", "Corrupted Nexus - Corruption", "Cleansed - Sanctification" };
        private static readonly string[] PreferredTowers = { "Alpine Ridge", "Bluff", "Lost Towers", "Mesa", "Swamp Tower" };
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
