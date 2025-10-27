using System.Linq;
using System.Numerics;
using ImGuiNET;

namespace AtlasBiomeHighlighter
{
    public partial class AtlasBiomeHighlighter
    {
        private string _preferredFilter = string.Empty;

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

                ImGui.TextDisabled("Select preferred map names:");
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
    foreach (var kv in s.PreferredMaps.ToList())
                {
                    if (!string.IsNullOrEmpty(_preferredFilter) && kv.Key.IndexOf(_preferredFilter, System.StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    bool on = kv.Value.Value;
                    if (ImGui.Checkbox(kv.Key, ref on))
                        kv.Value.Value = on;
                }
                ImGui.EndChild();
            }
}
    }
}
