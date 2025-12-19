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
                { bool v = s.HighlightDeadlyBoss.Value; if (ImGui.Checkbox("Highlight Deadly Map Boss", ref v)) s.HighlightDeadlyBoss.Value = v; }
                { bool v = s.HighlightAbyssOverrun.Value; if (ImGui.Checkbox("Highlight Abyss Overrun", ref v)) s.HighlightAbyssOverrun.Value = v; }
                { bool v = s.HighlightMomentofZen.Value; if (ImGui.Checkbox("Highlight Moment of Zen", ref v)) s.HighlightMomentofZen.Value = v; }
                { bool v = s.HighlightCorruptedNexus.Value; if (ImGui.Checkbox("Highlight Corrupted Nexus", ref v)) s.HighlightCorruptedNexus.Value = v; }
                { bool v = s.HighlightCleansed.Value; if (ImGui.Checkbox("Highlight Cleansed", ref v)) s.HighlightCleansed.Value = v; }
                { bool v = s.HighlightUniqueMaps.Value; if (ImGui.Checkbox("Highlight Unique maps", ref v)) s.HighlightUniqueMaps.Value = v; }

                { int v = s.SpecialRingThickness.Value; if (ImGui.SliderInt("Special ring thickness", ref v, s.SpecialRingThickness.Min, s.SpecialRingThickness.Max)) s.SpecialRingThickness.Value = v; }
                { float v = s.SpecialAlphaMultiplier.Value; if (ImGui.SliderFloat("Special alpha multiplier", ref v, s.SpecialAlphaMultiplier.Min, s.SpecialAlphaMultiplier.Max)) s.SpecialAlphaMultiplier.Value = v; }

                Vector4 vec;
                vec = new Vector4(s.DeadlyBossRingColor.Value.R/255f, s.DeadlyBossRingColor.Value.G/255f, s.DeadlyBossRingColor.Value.B/255f, 1f);
                if (ImGui.ColorEdit4("Deadly boss ring", ref vec)) s.DeadlyBossRingColor.Value = System.Drawing.Color.FromArgb((int)(vec.X*255),(int)(vec.Y*255),(int)(vec.Z*255));
                vec = new Vector4(s.AbyssOverrunRingColor.Value.R/255f, s.AbyssOverrunRingColor.Value.G/255f, s.AbyssOverrunRingColor.Value.B/255f, 1f);
                if (ImGui.ColorEdit4("Abyss Overrun ring", ref vec)) s.AbyssOverrunRingColor.Value = System.Drawing.Color.FromArgb((int)(vec.X*255),(int)(vec.Y*255),(int)(vec.Z*255));
                vec = new Vector4(s.MomentofZenRingColor.Value.R/255f, s.MomentofZenRingColor.Value.G/255f, s.MomentofZenRingColor.Value.B/255f, 1f);
                if (ImGui.ColorEdit4("Moment of Zen ring", ref vec)) s.MomentofZenRingColor.Value = System.Drawing.Color.FromArgb((int)(vec.X*255),(int)(vec.Y*255),(int)(vec.Z*255));
                vec = new Vector4(s.CorruptedNexusRingColor.Value.R/255f, s.CorruptedNexusRingColor.Value.G/255f, s.CorruptedNexusRingColor.Value.B/255f, 1f);
                if (ImGui.ColorEdit4("Corrupted Nexus ring", ref vec)) s.CorruptedNexusRingColor.Value = System.Drawing.Color.FromArgb((int)(vec.X*255),(int)(vec.Y*255),(int)(vec.Z*255));
                vec = new Vector4(s.CleansedRingColor.Value.R/255f, s.CleansedRingColor.Value.G/255f, s.CleansedRingColor.Value.B/255f, 1f);
                if (ImGui.ColorEdit4("Cleansed ring", ref vec)) s.CleansedRingColor.Value = System.Drawing.Color.FromArgb((int)(vec.X*255),(int)(vec.Y*255),(int)(vec.Z*255));
                vec = new Vector4(s.UniqueMapRingColor.Value.R/255f, s.UniqueMapRingColor.Value.G/255f, s.UniqueMapRingColor.Value.B/255f, 1f);
                if (ImGui.ColorEdit4("Unique map ring", ref vec)) s.UniqueMapRingColor.Value = System.Drawing.Color.FromArgb((int)(vec.X*255),(int)(vec.Y*255),(int)(vec.Z*255));

                { bool v = s.ShowUniqueNameOnLabel.Value; if (ImGui.Checkbox("Show Unique map name instead of biome", ref v)) s.ShowUniqueNameOnLabel.Value = v; }
                { bool v = s.PreferMapNameForDeadly.Value; if (ImGui.Checkbox("Prefer map name on Deadly", ref v)) s.PreferMapNameForDeadly.Value = v; }
                { bool v = s.ShowSpecialTag.Value; if (ImGui.Checkbox("Show special tag on label", ref v)) s.ShowSpecialTag.Value = v; }
            }

            if (ImGui.CollapsingHeader("Label settings", ImGuiTreeNodeFlags.DefaultOpen))
            {
                { int v = s.LabelOffset.Value; if (ImGui.SliderInt("Label vertical offset", ref v, s.LabelOffset.Min, s.LabelOffset.Max)) s.LabelOffset.Value = v; }
                { bool v = s.LabelUseBiomeColor.Value; if (ImGui.Checkbox("Use biome color for text", ref v)) s.LabelUseBiomeColor.Value = v; }
                var vecText = new Vector4(s.LabelTextColor.Value.R/255f, s.LabelTextColor.Value.G/255f, s.LabelTextColor.Value.B/255f, 1f);
                if (ImGui.ColorEdit4("Label text color", ref vecText)) s.LabelTextColor.Value = System.Drawing.Color.FromArgb((int)(vecText.X*255),(int)(vecText.Y*255),(int)(vecText.Z*255));
                { bool v = s.LabelOutline.Value; if (ImGui.Checkbox("Label outline", ref v)) s.LabelOutline.Value = v; }
                { int v = s.LabelOutlineThickness.Value; if (ImGui.SliderInt("Outline thickness", ref v, s.LabelOutlineThickness.Min, s.LabelOutlineThickness.Max)) s.LabelOutlineThickness.Value = v; }
                { bool v = s.LabelBold.Value; if (ImGui.Checkbox("Label bold (thicker)", ref v)) s.LabelBold.Value = v; }
            }

            if (ImGui.CollapsingHeader("Biomes", ImGuiTreeNodeFlags.DefaultOpen))
            {
                foreach (var kvp in s.Visible.ToArray())
                {
                    var biome = kvp.Key;
                    bool vis = kvp.Value.Value;
                    if (ImGui.Checkbox(biome.ToString(), ref vis)) kvp.Value.Value = vis;

                    var vec = new Vector4(s.Colors[biome].Value.R/255f, s.Colors[biome].Value.G/255f, s.Colors[biome].Value.B/255f, 1f);
                    if (ImGui.ColorEdit4("color##" + biome, ref vec))
                    {
                        s.Colors[biome].Value = System.Drawing.Color.FromArgb((int)(vec.X*255),(int)(vec.Y*255),(int)(vec.Z*255));
                    }
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
                if (ImGui.ColorEdit4("Preferred ring", ref pref))
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
