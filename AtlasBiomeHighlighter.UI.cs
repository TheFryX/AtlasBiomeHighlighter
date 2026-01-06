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
            // Hide completed maps toggle
            {
        }


            var s = Settings;

            ImGui.TextDisabled("Core");
            { bool v = s.Enable.Value; if (ImGui.Checkbox("Enable", ref v)) s.Enable.Value = v; }
            { int v = s.AtlasRefreshMs.Value; if (ImGui.SliderInt("Atlas refresh (ms)", ref v, s.AtlasRefreshMs.Min, s.AtlasRefreshMs.Max)) s.AtlasRefreshMs.Value = v; }
            { int v = s.ScreenRefreshMs.Value; if (ImGui.SliderInt("Screen refresh (ms)", ref v, s.ScreenRefreshMs.Min, s.ScreenRefreshMs.Max)) s.ScreenRefreshMs.Value = v; }

            if (ImGui.CollapsingHeader("Screen / Ultrawide", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();
                ImGui.TextDisabled("Override viewport size used for on-screen detection and off-screen guide clamping. 0 = auto.");
                { int v = s.BorderX.Value; if (ImGui.SliderInt("BorderX (width)", ref v, s.BorderX.Min, s.BorderX.Max)) s.BorderX.Value = v; }
                { int v = s.BorderY.Value; if (ImGui.SliderInt("BorderY (height)", ref v, s.BorderY.Min, s.BorderY.Max)) s.BorderY.Value = v; }
                ImGui.Unindent();
            }
            { int v = s.NodeRadius.Value; if (ImGui.SliderInt("Node radius", ref v, s.NodeRadius.Min, s.NodeRadius.Max)) s.NodeRadius.Value = v; }
            { int v = s.RingThickness.Value; if (ImGui.SliderInt("Ring thickness", ref v, s.RingThickness.Min, s.RingThickness.Max)) s.RingThickness.Value = v; }
            { float v = s.Opacity.Value; if (ImGui.SliderFloat("Opacity", ref v, s.Opacity.Min, s.Opacity.Max)) s.Opacity.Value = v; }
            { bool v = s.ShowLabels.Value; if (ImGui.Checkbox("Show labels", ref v)) s.ShowLabels.Value = v; }
            { bool v = s.DebugMode.Value; if (ImGui.Checkbox("Debug mode", ref v)) s.DebugMode.Value = v; }

            if (ImGui.CollapsingHeader("Hide completed / Attempted / Locked"))
            {
                ImGui.Indent();
                {
                    bool hideCompleted = Settings.HideCompletedMaps.Value;
                    if (ImGui.Checkbox("Hide completed maps", ref hideCompleted))
                        Settings.HideCompletedMaps.Value = hideCompleted;

                    bool hideAttempted = Settings.HideAttemptedMaps.Value;
                    if (ImGui.Checkbox("Hide attempted maps", ref hideAttempted))
                        Settings.HideAttemptedMaps.Value = hideAttempted;
                                    bool hideLocked = Settings.HideLockedMaps.Value;
                    if (ImGui.Checkbox("Hide locked maps", ref hideLocked))
                        Settings.HideLockedMaps.Value = hideLocked;
            }
                ImGui.Unindent();
            }


            if (ImGui.CollapsingHeader("Special highlights (strict)", ImGuiTreeNodeFlags.DefaultOpen))
            {
                {
    bool v = s.HighlightDeadlyBoss.Value;
    if (ImGui.Checkbox("Highlight Deadly Map Boss", ref v))
        s.HighlightDeadlyBoss.Value = v;
    ImGui.SameLine();
    if (DrawColorSquare("DeadlyBoss", s.DeadlyBossRingColor.Value, out var c))
        s.DeadlyBossRingColor.Value = c;
}
                {
    bool v = s.HighlightAbyssOverrun.Value;
    if (ImGui.Checkbox("Highlight Abyss Overrun", ref v))
        s.HighlightAbyssOverrun.Value = v;
    ImGui.SameLine();
    if (DrawColorSquare("AbyssOverrun", s.AbyssOverrunRingColor.Value, out var c))
        s.AbyssOverrunRingColor.Value = c;
}
                {
    bool v = s.HighlightMomentofZen.Value;
    if (ImGui.Checkbox("Highlight Moment of Zen", ref v))
        s.HighlightMomentofZen.Value = v;
    ImGui.SameLine();
    if (DrawColorSquare("MomentofZen", s.MomentofZenRingColor.Value, out var c))
        s.MomentofZenRingColor.Value = c;
}
                {
    bool v = s.HighlightCorruptedNexus.Value;
    if (ImGui.Checkbox("Highlight Corrupted Nexus", ref v))
        s.HighlightCorruptedNexus.Value = v;
    ImGui.SameLine();
    if (DrawColorSquare("CorruptedNexus", s.CorruptedNexusRingColor.Value, out var c))
        s.CorruptedNexusRingColor.Value = c;
}
                {
    bool v = s.HighlightCleansed.Value;
    if (ImGui.Checkbox("Highlight Cleansed", ref v))
        s.HighlightCleansed.Value = v;
    ImGui.SameLine();
    if (DrawColorSquare("Cleansed", s.CleansedRingColor.Value, out var c))
        s.CleansedRingColor.Value = c;
}
                {
    bool v = s.HighlightUniqueMaps.Value;
    if (ImGui.Checkbox("Highlight Unique maps", ref v))
        s.HighlightUniqueMaps.Value = v;
    ImGui.SameLine();
    if (DrawColorSquare("UniqueMap", s.UniqueMapRingColor.Value, out var c))
        s.UniqueMapRingColor.Value = c;
}

                { int v = s.SpecialRingThickness.Value; if (ImGui.SliderInt("Special ring thickness", ref v, s.SpecialRingThickness.Min, s.SpecialRingThickness.Max)) s.SpecialRingThickness.Value = v; }
                { float v = s.SpecialAlphaMultiplier.Value; if (ImGui.SliderFloat("Special alpha multiplier", ref v, s.SpecialAlphaMultiplier.Min, s.SpecialAlphaMultiplier.Max)) s.SpecialAlphaMultiplier.Value = v; }

                { bool v = s.ShowUniqueNameOnLabel.Value; if (ImGui.Checkbox("Show Unique map name instead of biome", ref v)) s.ShowUniqueNameOnLabel.Value = v; }
                { bool v = s.PreferMapNameForDeadly.Value; if (ImGui.Checkbox("Prefer map name on Deadly", ref v)) s.PreferMapNameForDeadly.Value = v; }
                { bool v = s.ShowSpecialTag.Value; if (ImGui.Checkbox("Show special tag on label", ref v)) s.ShowSpecialTag.Value = v; }
            }


	                static bool DrawColorSquare(string id, System.Drawing.Color color, out System.Drawing.Color newColor)
                {
                    float size = ImGui.GetFrameHeight();
                    if (size < 18f) size = 18f;

                    ImGui.PushID(id);
                    ImGui.SetNextItemWidth(size);

                    var vec = new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
                    bool changed = ImGui.ColorEdit4("##c", ref vec, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel);
                    if (changed)
                    {
                        int r = (int)(vec.X * 255f);
                        int g = (int)(vec.Y * 255f);
                        int b = (int)(vec.Z * 255f);
                        int a = (int)(vec.W * 255f);
                        if (r < 0) r = 0; else if (r > 255) r = 255;
                        if (g < 0) g = 0; else if (g > 255) g = 255;
                        if (b < 0) b = 0; else if (b > 255) b = 255;
                        if (a < 0) a = 0; else if (a > 255) a = 255;
                        newColor = System.Drawing.Color.FromArgb(a, r, g, b);
                    }
                    else
                    {
                        newColor = color;
                    }

                    ImGui.PopID();
                    return changed;
                }

            if (ImGui.CollapsingHeader("Label settings", ImGuiTreeNodeFlags.DefaultOpen))
            {
                { int v = s.LabelOffset.Value; if (ImGui.SliderInt("Label vertical offset", ref v, s.LabelOffset.Min, s.LabelOffset.Max)) s.LabelOffset.Value = v; }
                { bool v = s.LabelUseBiomeColor.Value; if (ImGui.Checkbox("Use biome color for text", ref v)) s.LabelUseBiomeColor.Value = v; }
                var vecText = new Vector4(s.LabelTextColor.Value.R/255f, s.LabelTextColor.Value.G/255f, s.LabelTextColor.Value.B/255f, 1f);
				if (ImGui.ColorEdit4("Label text color", ref vecText, ImGuiColorEditFlags.NoInputs)) s.LabelTextColor.Value = System.Drawing.Color.FromArgb((int)(vecText.X*255),(int)(vecText.Y*255),(int)(vecText.Z*255));
                { bool v = s.LabelOutline.Value; if (ImGui.Checkbox("Label outline", ref v)) s.LabelOutline.Value = v; }
                { int v = s.LabelOutlineThickness.Value; if (ImGui.SliderInt("Outline thickness", ref v, s.LabelOutlineThickness.Min, s.LabelOutlineThickness.Max)) s.LabelOutlineThickness.Value = v; }
                { bool v = s.LabelBold.Value; if (ImGui.Checkbox("Label bold (thicker)", ref v)) s.LabelBold.Value = v; }
            }

            if (ImGui.CollapsingHeader("QoL Features", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();

                bool showNames = s.ShowMapNames.Value;
                if (ImGui.Checkbox("Show map names", ref showNames)) s.ShowMapNames.Value = showNames;
                int my = s.MapNameOffsetY.Value;
                if (ImGui.SliderInt("Map name Y offset", ref my, s.MapNameOffsetY.Min, s.MapNameOffsetY.Max)) s.MapNameOffsetY.Value = my;
                ImGui.Separator();
                bool con = s.DrawMapConnections.Value;
                if (ImGui.Checkbox("Draw map connections", ref con)) s.DrawMapConnections.Value = con;
                bool hideVisited = !s.DrawVisitedConnections.Value;
                if (ImGui.Checkbox("Hide connections involving visited nodes", ref hideVisited)) s.DrawVisitedConnections.Value = !hideVisited;
                int ct = s.ConnectionThickness.Value;
                if (ImGui.SliderInt("Connection thickness", ref ct, s.ConnectionThickness.Min, s.ConnectionThickness.Max)) s.ConnectionThickness.Value = ct;
                var cc = new Vector4(s.ConnectionColor.Value.R / 255f, s.ConnectionColor.Value.G / 255f, s.ConnectionColor.Value.B / 255f, 1f);
				if (ImGui.ColorEdit4("Connection color (unlocked)", ref cc, ImGuiColorEditFlags.NoInputs))
                    s.ConnectionColor.Value = System.Drawing.Color.FromArgb((int)(cc.X * 255), (int)(cc.Y * 255), (int)(cc.Z * 255));
                var ccl = new Vector4(s.ConnectionColorLocked.Value.R / 255f, s.ConnectionColorLocked.Value.G / 255f, s.ConnectionColorLocked.Value.B / 255f, 1f);
				if (ImGui.ColorEdit4("Connection color (locked)", ref ccl, ImGuiColorEditFlags.NoInputs))
                    s.ConnectionColorLocked.Value = System.Drawing.Color.FromArgb((int)(ccl.X * 255), (int)(ccl.Y * 255), (int)(ccl.Z * 255));

                ImGui.Separator();

                bool wp = s.WaypointsEnabled.Value;
                if (ImGui.Checkbox("Waypoints enabled", ref wp)) s.WaypointsEnabled.Value = wp;
                int wr = s.WaypointRingRadius.Value;
                if (ImGui.SliderInt("Waypoint ring radius", ref wr, s.WaypointRingRadius.Min, s.WaypointRingRadius.Max)) s.WaypointRingRadius.Value = wr;
                int wt = s.WaypointRingThickness.Value;
                if (ImGui.SliderInt("Waypoint ring thickness", ref wt, s.WaypointRingThickness.Min, s.WaypointRingThickness.Max)) s.WaypointRingThickness.Value = wt;
                var dwc = new Vector4(s.DefaultWaypointColor.Value.R / 255f, s.DefaultWaypointColor.Value.G / 255f, s.DefaultWaypointColor.Value.B / 255f, 1f);
				if (ImGui.ColorEdit4("Default waypoint color", ref dwc, ImGuiColorEditFlags.NoInputs))
                    s.DefaultWaypointColor.Value = System.Drawing.Color.FromArgb((int)(dwc.X * 255), (int)(dwc.Y * 255), (int)(dwc.Z * 255));

                ImGui.Separator();

                bool sp = s.DrawShortestPath.Value;
                if (ImGui.Checkbox("Draw shortest path to selected waypoint", ref sp)) s.DrawShortestPath.Value = sp;
                int st = s.ShortestPathThickness.Value;
                if (ImGui.SliderInt("Shortest path thickness", ref st, s.ShortestPathThickness.Min, s.ShortestPathThickness.Max)) s.ShortestPathThickness.Value = st;
                var spc = new Vector4(s.ShortestPathColor.Value.R / 255f, s.ShortestPathColor.Value.G / 255f, s.ShortestPathColor.Value.B / 255f, 1f);
				if (ImGui.ColorEdit4("Shortest path color", ref spc, ImGuiColorEditFlags.NoInputs))
                    s.ShortestPathColor.Value = System.Drawing.Color.FromArgb((int)(spc.X * 255), (int)(spc.Y * 255), (int)(spc.Z * 255));

                ImGui.Separator();

                bool tr = s.DrawTowerRange.Value;
                if (ImGui.Checkbox("Tower range (toggle hotkey)", ref tr)) s.DrawTowerRange.Value = tr;
                int r = s.TowerRange.Value;
                if (ImGui.SliderInt("Tower range (coord)", ref r, s.TowerRange.Min, s.TowerRange.Max)) s.TowerRange.Value = r;
                var trc = new Vector4(s.TowerRangeColor.Value.R / 255f, s.TowerRangeColor.Value.G / 255f, s.TowerRangeColor.Value.B / 255f, 1f);
				if (ImGui.ColorEdit4("Tower range color", ref trc, ImGuiColorEditFlags.NoInputs))
                    s.TowerRangeColor.Value = System.Drawing.Color.FromArgb((int)(trc.X * 255), (int)(trc.Y * 255), (int)(trc.Z * 255));

                ImGui.TextDisabled("Hotkeys: Insert add waypoint, Delete remove, End waypoint window, PageUp tower range toggle");

                ImGui.Unindent();
            }

            if (ImGui.CollapsingHeader("Biomes", ImGuiTreeNodeFlags.DefaultOpen))
            {
                // Compact one-row-per-biome layout: checkbox + name + color square right next to it.
                // The table is used only to get alternating row backgrounds / separators for readability.
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

                        ImGui.PushID((int)biome);
                        ImGui.TableNextRow();
                        ImGui.TableSetColumnIndex(0);

                        bool vis = kvp.Value.Value;
                        if (ImGui.Checkbox("##enabled", ref vis))
                            kvp.Value.Value = vis;

                        ImGui.SameLine(0, 8);
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextUnformatted(biome.ToString());

                        // Color square (no RGBA inputs) right next to the biome name.
                        ImGui.SameLine(0, 10);
                        // MathF may be unavailable depending on the plugin host target framework.
                        // Keep this simple, allocation-free, and framework-agnostic.
                        float colorWidth = ImGui.GetFrameHeight() * 1.25f;
                        if (colorWidth < 22f) colorWidth = 22f;
                        ImGui.PushItemWidth(colorWidth);

                        var c = s.Colors[biome].Value;
                        var vec = new Vector4(c.R / 255f, c.G / 255f, c.B / 255f, 1f);
                        if (ImGui.ColorEdit4(
                                "##color",
                                ref vec,
                                ImGuiColorEditFlags.NoInputs |
                                ImGuiColorEditFlags.NoAlpha))
                        {
                            s.Colors[biome].Value = System.Drawing.Color.FromArgb(
                                (int)(vec.X * 255f),
                                (int)(vec.Y * 255f),
                                (int)(vec.Z * 255f));
                        }

                        ImGui.PopItemWidth();
                        ImGui.PopID();
                    }

                    ImGui.EndTable();
                }

                ImGui.Separator();
                ImGui.TextDisabled("Alpha jest sterowana globalnie przez \"Opacity\".");
            }
        
            if (ImGui.CollapsingHeader("Preferred maps", ImGuiTreeNodeFlags.DefaultOpen))
            {
                // Ensure groups exist (back-compat if user opens settings before Initialise()).
                MigratePreferredGroupsIfNeeded();

                var groups = Settings.PreferredMapGroups;
                if (groups == null)
                {
                    Settings.PreferredMapGroups = groups = new System.Collections.Generic.List<PreferredMapGroup>();
                }
                if (groups.Count == 0)
                {
                    groups.Add(new PreferredMapGroup { Name = "Default", Enabled = true });
                }

                if (_selectedPreferredGroup < 0) _selectedPreferredGroup = 0;
                if (_selectedPreferredGroup >= groups.Count) _selectedPreferredGroup = groups.Count - 1;


                bool highlight = s.HighlightPreferredMaps.Value;
                if (ImGui.Checkbox("Highlight Preferred maps", ref highlight))
                    s.HighlightPreferredMaps.Value = highlight;

                Vector4 pref = new Vector4(
                    s.PreferredMapRingColor.Value.R / 255f,
                    s.PreferredMapRingColor.Value.G / 255f,
                    s.PreferredMapRingColor.Value.B / 255f,
                    1f);
				if (ImGui.ColorEdit4("Preferred ring", ref pref, ImGuiColorEditFlags.NoInputs))
                    s.PreferredMapRingColor.Value = System.Drawing.Color.FromArgb(
                        (int)(pref.X * 255),
                        (int)(pref.Y * 255),
                        (int)(pref.Z * 255));

                ImGui.Separator();
                ImGui.TextDisabled("Map Groups:");

                // Create group
                ImGui.SetNextItemWidth(220);
                ImGui.InputText("##preferred_new_group", ref _newPreferredGroupName, 64);
                ImGui.SameLine();
                if (ImGui.Button("Add Group"))
                {
                    var name = string.IsNullOrWhiteSpace(_newPreferredGroupName) ? "New Group" : _newPreferredGroupName.Trim();
                    groups.Add(new PreferredMapGroup { Name = name, Enabled = true });
                    _selectedPreferredGroup = groups.Count - 1;
                }

                // Group tabs/buttons (avoid overlap: Selectable width must be explicit; also keep stable unique IDs)
                ImGui.SameLine();
                const float tabBarHeight = 26f;
                // IMPORTANT: Dear ImGui requires EndChild() to be called whenever BeginChild() is called,
                // even if BeginChild() returns false. Not doing so can corrupt the ImGui stack and crash the loader.
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
                        float w = ImGui.CalcTextSize(tabText).X + 16f; // padding
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

                // Group actions
                ImGui.Indent();
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
                        var nn = string.IsNullOrWhiteSpace(_renamePreferredGroupName) ? activeGroup.Name : _renamePreferredGroupName.Trim();
                        activeGroup.Name = nn;
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
                ImGui.Unindent();

                ImGui.TextDisabled("Select maps for this group:");
                ImGui.InputText("Filter##preferred", ref _preferredFilter, 128);
                ImGui.BeginChild("##preferred_maps_child", new Vector2(0, 220), ImGuiChildFlags.Border, ImGuiWindowFlags.None);

                ImGui.Separator();
                bool pg = s.PreferredGuideLines.Value;
                if (ImGui.Checkbox("Draw Preferred guide lines", ref pg)) s.PreferredGuideLines.Value = pg;
                bool po = s.PreferredGuideOnlyOffscreen.Value;
                if (ImGui.Checkbox("Only when off-screen", ref po)) s.PreferredGuideOnlyOffscreen.Value = po;
                bool pc = s.PreferredGuideFromScreenCenter.Value;
                if (ImGui.Checkbox("Origin at screen center", ref pc)) s.PreferredGuideFromScreenCenter.Value = pc;
                int th = s.PreferredGuideThickness.Value;
                if (ImGui.SliderInt("Guide thickness", ref th, 1, 8)) s.PreferredGuideThickness.Value = th;
                int ar = s.PreferredArrowSize.Value;
                if (ImGui.SliderInt("Arrow size", ref ar, 6, 28)) s.PreferredArrowSize.Value = ar;
                int gl = s.PreferredGuideLimit.Value;
                if (ImGui.SliderInt("Max guide count", ref gl, 5, 200)) s.PreferredGuideLimit.Value = gl;

                foreach (var key in s.PreferredMaps.Keys.OrderBy(k => k, System.StringComparer.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(_preferredFilter) && key.IndexOf(_preferredFilter, System.StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    bool on = activeGroup.Maps.Contains(key);
                    if (ImGui.Checkbox(key, ref on))
                    {
                        if (on) activeGroup.Maps.Add(key);
                        else activeGroup.Maps.Remove(key);
                    }
                }
                ImGui.EndChild();
            }
        }
    }
}
