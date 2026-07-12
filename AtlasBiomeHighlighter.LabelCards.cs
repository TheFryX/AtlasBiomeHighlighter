using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using ImGuiNET;

namespace AtlasBiomeHighlighter
{
    public partial class AtlasBiomeHighlighter
    {
        private const int ModernLabelMeasureCacheMaxEntries = 4096;
        private const int ModernLabelFittedCacheMaxEntries = 2048;
        private const float ModernLabelViewportMargin = 8f;
        private const float ModernLabelPlacementGap = 7f;
        private const int ModernLabelRevealResetMs = 260;
        private const float ModernLabelRevealDurationMs = 120f;

        private const int ModernLabelPlacementCacheMaxEntries = 4096;

        private readonly Dictionary<(string text, int size), Vector2> _modernLabelMeasureCache = new();
        private readonly Dictionary<(string text, int size, int width), string> _modernLabelFittedCache = new();
        private readonly Dictionary<(int x, int y), ModernLabelPlacement> _modernLabelPlacementByCoord = new();
        private readonly Dictionary<(int x, int y), ModernLabelRevealState> _modernLabelRevealByCoord = new();
        private readonly List<ModernLabelCandidate> _modernLabelCandidates = new(256);
        private readonly List<ModernLabelRect> _modernLabelOccupiedRects = new(256);
        private long _modernLabelFrameTimeMs;

        private struct ModernLabelRevealState
        {
            public long LastSeenMs;
            public float Progress;
        }

        private readonly struct ModernLabelCandidate
        {
            public ModernLabelCandidate(
                NodeRenderInfo info,
                Vector2 center,
                float radius,
                Color biomeColor,
                bool biomeVisible,
                bool preferredWanted,
                string? preferredMatchedToken,
                string preferredDisplayName,
                bool mechanicWanted,
                bool towerWanted,
                string? highlightedTowerName,
                int priority,
                bool important)
            {
                Info = info;
                Center = center;
                Radius = radius;
                BiomeColor = biomeColor;
                BiomeVisible = biomeVisible;
                PreferredWanted = preferredWanted;
                PreferredMatchedToken = preferredMatchedToken;
                PreferredDisplayName = preferredDisplayName;
                MechanicWanted = mechanicWanted;
                TowerWanted = towerWanted;
                HighlightedTowerName = highlightedTowerName;
                Priority = priority;
                Important = important;
            }

            public NodeRenderInfo Info { get; }
            public Vector2 Center { get; }
            public float Radius { get; }
            public Color BiomeColor { get; }
            public bool BiomeVisible { get; }
            public bool PreferredWanted { get; }
            public string? PreferredMatchedToken { get; }
            public string PreferredDisplayName { get; }
            public bool MechanicWanted { get; }
            public bool TowerWanted { get; }
            public string? HighlightedTowerName { get; }
            public int Priority { get; }
            public bool Important { get; }
        }

        private readonly struct ModernLabelRect
        {
            public ModernLabelRect(Vector2 min, Vector2 max)
            {
                Min = min;
                Max = max;
            }

            public Vector2 Min { get; }
            public Vector2 Max { get; }
        }

        private enum ModernLabelPlacement
        {
            Above,
            Below,
            Right,
            Left
        }

        private static readonly ModernLabelPlacement[] ModernLabelPlacementsStable =
        {
            ModernLabelPlacement.Above,
            ModernLabelPlacement.Below,
            ModernLabelPlacement.Right,
            ModernLabelPlacement.Left
        };

        private void BeginModernNodeLabels()
        {
            _modernLabelCandidates.Clear();
            _modernLabelOccupiedRects.Clear();
            _modernLabelFrameTimeMs = Environment.TickCount64;
            BeginAtlasSignalPositionDebugFrame();
        }

        private void QueueModernNodeLabel(
            NodeRenderInfo info,
            Vector2 center,
            float radius,
            Color biomeColor,
            bool biomeVisible,
            bool preferredWanted,
            string? preferredMatchedToken,
            string preferredDisplayName,
            bool mechanicWanted,
            bool towerWanted,
            string? highlightedTowerName)
        {
            var sflags = info.SpecialFlags;
            bool delirium = Settings.ShowDeliriumStatus.Value && info.HasDelirium;
            bool special =
                delirium ||
                mechanicWanted ||
                towerWanted ||
                (sflags & (Utility.SpecialFlags.UniqueMap |
                           Utility.SpecialFlags.DeadlyBoss |
                           Utility.SpecialFlags.MomentofZen |
                           Utility.SpecialFlags.CorruptedNexus |
                           Utility.SpecialFlags.Cleansed |
                           Utility.SpecialFlags.AreaContainsAbyss |
                           Utility.SpecialFlags.AreaContainsExpedition)) != 0;

            bool important = preferredWanted || special || (info.Unlocked && !info.Completed);
            int priority = 0;
            if (preferredWanted) priority += 1000;
            if (delirium) priority += 850;
            if (special) priority += 700;
            if (info.Unlocked && !info.Completed) priority += 420;
            if (info.Attempted) priority += 260;
            if (info.Completed) priority += 180;
            if (info.Visited) priority += 120;
            if (biomeVisible) priority += 70;
            if (info.Locked) priority -= 20;

            ObserveAtlasSignalPosition(info, center);

            _modernLabelCandidates.Add(new ModernLabelCandidate(
                info,
                center,
                radius,
                biomeColor,
                biomeVisible,
                preferredWanted,
                preferredMatchedToken,
                preferredDisplayName,
                mechanicWanted,
                towerWanted,
                highlightedTowerName,
                priority,
                important));
        }

        private void RenderQueuedModernNodeLabels(ImDrawListPtr drawList)
        {
            CompleteAtlasSignalPositionDebugFrame();
            if (_modernLabelCandidates.Count == 0)
                return;

            bool compact = IsModernLabelCompactMode();
            _modernLabelCandidates.Sort(static (a, b) =>
            {
                int priority = b.Priority.CompareTo(a.Priority);
                if (priority != 0)
                    return priority;

                string aName = a.Info.MapName ?? a.Info.BiomeDisplay;
                string bName = b.Info.MapName ?? b.Info.BiomeDisplay;
                int name = string.Compare(aName, bName, StringComparison.OrdinalIgnoreCase);
                if (name != 0)
                    return name;

                var aCoord = a.Info.Node.Coordinate;
                var bCoord = b.Info.Node.Coordinate;
                int x = aCoord.X.CompareTo(bCoord.X);
                return x != 0 ? x : aCoord.Y.CompareTo(bCoord.Y);
            });

            for (int i = 0; i < _modernLabelCandidates.Count; i++)
            {
                var candidate = _modernLabelCandidates[i];
                if (compact && Settings.ModernLabelHideOrdinaryWhenZoomedOut.Value && !candidate.Important)
                    continue;

                DrawModernNodeLabelCard(drawList, candidate, compact);
            }
        }

        private bool IsModernLabelCompactMode()
        {
            if (!Settings.ModernLabelAutoCompact.Value)
                return false;

            try
            {
                return _atlasPanel != null &&
                       _atlasPanel.Scale < Settings.ModernLabelCompactScaleThreshold.Value;
            }
            catch
            {
                return false;
            }
        }

        private void DrawModernNodeLabelCard(
            ImDrawListPtr drawList,
            ModernLabelCandidate candidate,
            bool compact)
        {
            NodeRenderInfo info = candidate.Info;
            string title = BuildModernLabelTitle(
                info,
                candidate.BiomeVisible,
                candidate.PreferredWanted,
                candidate.PreferredDisplayName,
                out bool labelContainsMapName);
            if (string.IsNullOrWhiteSpace(title))
                return;

            string detail = BuildModernLabelDetail(candidate, labelContainsMapName, compact);
            bool detailIsBiome = IsModernLabelBiomeDetail(candidate, detail, labelContainsMapName);
            var font = ImGui.GetFont();
            float uiFontSize = Math.Max(12f, ImGui.GetFontSize());
            float configuredScale = Math.Clamp(
                Settings.ModernLabelScale.Value,
                Settings.ModernLabelScale.Min,
                Settings.ModernLabelScale.Max);
            // ImGui text rendered at fractional font sizes is visibly softer because the
            // baked glyph atlas must be scaled. Keep both lines on whole pixel sizes so
            // map names, biome names and special tags stay crisp at every Atlas scale.
            float titleFontSize = MathF.Round(Math.Clamp(uiFontSize * configuredScale, 15f, 24f));
            float detailFontSize = MathF.Round(Math.Clamp(titleFontSize * 0.92f, 13f, 19f));
            int titleSizeKey = Math.Max(1, (int)titleFontSize);
            int detailSizeKey = Math.Max(1, (int)detailFontSize);
            bool showDelirium = Settings.ShowDeliriumStatus.Value && info.HasDelirium;
            string deliriumText = showDelirium
                ? info.DeliriumPercent > 0 ? $"DELI {info.DeliriumPercent}%" : "DELI"
                : string.Empty;
            float deliriumPaddingX = showDelirium ? 6f : 0f;
            float deliriumGap = showDelirium ? 7f : 0f;
            Vector2 deliriumTextSize = showDelirium
                ? MeasureModernLabelText(font, detailSizeKey, deliriumText)
                : Vector2.Zero;
            float deliriumLayoutWidth = deliriumTextSize.X + deliriumPaddingX * 2f;

            float maxPanelWidth = Math.Max(140f, Settings.ModernLabelMaxWidth.Value);
            bool showStatusGlyph = Settings.ShowMapStatus.Value;
            float leadingSpace = showStatusGlyph
                ? compact ? 17f : 19f
                : compact ? 6f : 7f;
            float paddingRight = compact ? 7f : 9f;
            float paddingY = compact ? 3f : 4f;
            float detailGap = string.IsNullOrEmpty(detail) ? 0f : 9f;
            float detailPaddingX = string.IsNullOrEmpty(detail) ? 0f : (detailIsBiome ? 7f : 6f);

            if (!string.IsNullOrEmpty(detail))
            {
                float maxDetailWidth = detailIsBiome
                    ? Math.Clamp(maxPanelWidth * 0.46f, 72f, 136f)
                    : Math.Clamp(maxPanelWidth * 0.40f, 48f, 112f);
                detail = FitModernLabelText(font, detail, detailSizeKey, maxDetailWidth);
            }

            float detailWidth = string.IsNullOrEmpty(detail)
                ? 0f
                : MeasureModernLabelText(font, detailSizeKey, detail).X;
            float detailLayoutWidth = detailWidth + detailPaddingX * 2f;
            float maxTitleWidth = Math.Max(
                54f,
                maxPanelWidth - leadingSpace - paddingRight -
                deliriumLayoutWidth - deliriumGap - detailGap - detailLayoutWidth);

            title = FitModernLabelText(font, title, titleSizeKey, maxTitleWidth);
            Vector2 titleSize = MeasureModernLabelText(font, titleSizeKey, title);
            float titleLineHeight = Math.Max(titleFontSize, MeasureModernLabelText(font, titleSizeKey, "Ag").Y);
            float panelHeight = MathF.Ceiling(titleLineHeight + paddingY * 2f);
            float contentWidth = leadingSpace + titleSize.X + paddingRight;
            if (showDelirium)
                contentWidth += deliriumLayoutWidth + deliriumGap;
            if (!string.IsNullOrEmpty(detail))
                contentWidth += detailGap + detailLayoutWidth;
            float panelWidth = Math.Clamp(MathF.Ceiling(contentWidth), 76f, maxPanelWidth);

            if (!TryPlaceModernLabel(
                    info,
                    candidate.Center,
                    candidate.Radius,
                    panelWidth,
                    panelHeight,
                    candidate.Important,
                    out var panelMin,
                    out var panelMax,
                    out var placement))
                return;

            float revealOpacity = GetModernLabelRevealOpacity(info);
            float baseGlobalOpacity = Math.Clamp(Settings.Opacity.Value, 0f, 1f);
            float globalOpacity = baseGlobalOpacity * revealOpacity;
            // Plate transparency is user-controlled, but text must not become washed out.
            // A minimum text opacity keeps small tags legible without making the panel heavy.
            float textOpacity = Math.Max(baseGlobalOpacity, candidate.Important ? 0.97f : 0.91f) * revealOpacity;
            float configuredBackgroundOpacity = Math.Clamp(
                Settings.ModernLabelBackgroundOpacity.Value,
                Settings.ModernLabelBackgroundOpacity.Min,
                Settings.ModernLabelBackgroundOpacity.Max);
            float plateOpacity = configuredBackgroundOpacity * (candidate.Important ? 0.90f : 0.76f);

            Color plateColor = Utility.WithOpacity(
                Settings.ModernLabelBackgroundColor.Value,
                globalOpacity * plateOpacity);
            Color mutedRailColor = Utility.WithOpacity(
                Settings.ModernLabelBorderColor.Value,
                globalOpacity * (candidate.Important ? 0.34f : 0.20f));
            Color accentColor = Utility.WithOpacity(
                GetModernLabelSignalAccent(candidate),
                globalOpacity * (candidate.Important ? 1f : 0.88f));
            Color shadowColor = Utility.WithOpacity(
                Color.Black,
                globalOpacity * 0.62f);

            GetAtlasSignalGeometry(
                candidate.Center,
                candidate.Radius,
                panelMin,
                panelMax,
                placement,
                out Vector2 nodeAnchor,
                out Vector2 signalTip,
                out Vector2 plateEdgeCenter,
                out bool horizontalSignal);

            DrawAtlasSignalRail(
                drawList,
                nodeAnchor,
                signalTip,
                horizontalSignal,
                accentColor,
                mutedRailColor,
                shadowColor,
                candidate.Important);

            DrawAtlasSignalPlate(
                drawList,
                panelMin,
                panelMax,
                plateEdgeCenter,
                signalTip,
                placement,
                plateColor,
                accentColor,
                shadowColor,
                candidate.Important);

            DrawAtlasSignalNodeGlyph(
                drawList,
                nodeAnchor,
                accentColor,
                mutedRailColor,
                candidate.Important);

            if (candidate.Important)
                DrawAtlasSignalFocusMarks(drawList, candidate.Center, candidate.Radius, accentColor, mutedRailColor);

            if (showStatusGlyph)
            {
                Color statusColor = Utility.WithOpacity(
                    GetModernLabelStatusColor(info),
                    globalOpacity);
                Vector2 glyphCenter = new(
                    panelMin.X + 9f,
                    panelMin.Y + panelHeight * 0.5f);
                DrawAtlasSignalStatusGlyph(drawList, glyphCenter, info, statusColor);
            }

            if (showDelirium)
            {
                Color rawDeliriumAccent = Settings.DeliriumStatusColor.Value;
                Color deliriumAccent = Settings.ModernLabelAdaptiveTextContrast.Value
                    ? MakeModernLabelAccentReadable(rawDeliriumAccent)
                    : rawDeliriumAccent;
                Vector2 deliriumChipMin = SnapTextPos(new Vector2(
                    panelMin.X + leadingSpace,
                    panelMin.Y + 2f));
                Vector2 deliriumChipMax = SnapTextPos(new Vector2(
                    deliriumChipMin.X + deliriumLayoutWidth,
                    panelMax.Y - 2f));
                Vector2 deliriumTextPos = SnapTextPos(new Vector2(
                    deliriumChipMin.X + deliriumPaddingX,
                    panelMin.Y + (panelHeight - deliriumTextSize.Y) * 0.5f));
                Color deliriumFill = Utility.WithOpacity(
                    BlendModernLabelColor(Settings.ModernLabelBackgroundColor.Value, rawDeliriumAccent, 0.42f),
                    Math.Max(globalOpacity, 0.90f));
                Color deliriumBorder = Utility.WithOpacity(
                    deliriumAccent,
                    Math.Max(globalOpacity, 0.96f));
                Color deliriumTextColor = Utility.WithOpacity(
                    Color.FromArgb(252, 250, 255),
                    textOpacity);

                drawList.AddRectFilled(
                    deliriumChipMin,
                    deliriumChipMax,
                    GetCachedImGuiColor(deliriumFill),
                    3f);
                drawList.AddRect(
                    deliriumChipMin,
                    deliriumChipMax,
                    GetCachedImGuiColor(deliriumBorder),
                    3f,
                    0,
                    1.4f);
                drawList.AddRectFilled(
                    deliriumChipMin + new Vector2(1f, 2f),
                    new Vector2(deliriumChipMin.X + 3f, deliriumChipMax.Y - 2f),
                    GetCachedImGuiColor(deliriumBorder),
                    1f);
                DrawModernLabelText(
                    drawList,
                    font,
                    detailFontSize,
                    deliriumTextPos,
                    deliriumTextColor,
                    deliriumText,
                    adaptiveOutline: false);
            }

            // The modern style must honour biome-coloured map names for every node,
            // including Preferred, Expedition, Deadly and other important targets.
            // Previously the Important branch forced those titles to neutral white,
            // which made Classic react to biome colours while Atlas Signal appeared stale.
            bool useBiomeTitleColor =
                Settings.ModernLabelUseBiomeTitleColor.Value ||
                Settings.LabelUseBiomeColor.Value;
            Color titleColor = useBiomeTitleColor
                ? Settings.ModernLabelAdaptiveTextContrast.Value
                    ? MakeModernLabelAccentReadable(candidate.BiomeColor)
                    : candidate.BiomeColor
                : candidate.Important
                    ? Color.FromArgb(250, 252, 255)
                    : Settings.LabelTextColor.Value;
            if (labelContainsMapName && !(info.Visited || info.Unlocked))
            {
                titleColor = Color.FromArgb(
                    titleColor.A,
                    Math.Max(150, (int)(titleColor.R * 0.90f)),
                    Math.Max(150, (int)(titleColor.G * 0.90f)),
                    Math.Max(150, (int)(titleColor.B * 0.90f)));
            }
            titleColor = Utility.WithOpacity(titleColor, textOpacity);
            bool needsAdaptiveOutline =
                Settings.ModernLabelAdaptiveTextContrast.Value &&
                (candidate.Important || GetModernLabelLuminance(titleColor) < 0.62f);

            Vector2 titlePos = SnapTextPos(new Vector2(
                panelMin.X + leadingSpace + deliriumLayoutWidth + deliriumGap,
                panelMin.Y + (panelHeight - titleSize.Y) * 0.5f));
            DrawModernLabelText(
                drawList,
                font,
                titleFontSize,
                titlePos,
                titleColor,
                title,
                adaptiveOutline: needsAdaptiveOutline);

            float underlineStart = titlePos.X;
            float underlineEnd = Math.Min(
                panelMax.X - paddingRight,
                titlePos.X + Math.Max(18f, titleSize.X * (candidate.Important ? 0.92f : 0.62f)));
            float underlineY = panelMax.Y - 1f;
            drawList.AddLine(
                new Vector2(underlineStart, underlineY),
                new Vector2(underlineEnd, underlineY),
                GetCachedImGuiColor(accentColor),
                candidate.Important ? 1.6f : 1.1f);

            if (!string.IsNullOrEmpty(detail))
            {
                Vector2 detailSize = MeasureModernLabelText(font, detailSizeKey, detail);
                Vector2 detailPos = SnapTextPos(new Vector2(
                    panelMax.X - paddingRight - detailPaddingX - detailSize.X,
                    panelMin.Y + (panelHeight - detailSize.Y) * 0.5f));

                Color rawTagAccent = detailIsBiome
                    ? candidate.BiomeColor
                    : GetModernLabelDetailColor(candidate, detail);
                Color tagAccent = Settings.ModernLabelAdaptiveTextContrast.Value
                    ? MakeModernLabelAccentReadable(rawTagAccent)
                    : rawTagAccent;
                Vector2 chipMin = SnapTextPos(new Vector2(
                    detailPos.X - detailPaddingX,
                    panelMin.Y + 2f));
                Vector2 chipMax = SnapTextPos(new Vector2(
                    detailPos.X + detailSize.X + detailPaddingX,
                    panelMax.Y - 2f));

                Color chipFill = Utility.WithOpacity(
                    BlendModernLabelColor(Settings.ModernLabelBackgroundColor.Value, tagAccent, detailIsBiome ? 0.30f : 0.24f),
                    Math.Max(globalOpacity, 0.88f));
                Color chipBorder = Utility.WithOpacity(
                    tagAccent,
                    Math.Max(globalOpacity, 0.94f));
                Color chipText = Utility.WithOpacity(
                    Color.FromArgb(252, 253, 255),
                    textOpacity);

                float dividerX = chipMin.X - detailGap * 0.5f;
                drawList.AddLine(
                    SnapTextPos(new Vector2(dividerX, panelMin.Y + 4f)),
                    SnapTextPos(new Vector2(dividerX, panelMax.Y - 4f)),
                    GetCachedImGuiColor(mutedRailColor),
                    1f);
                drawList.AddRectFilled(
                    chipMin,
                    chipMax,
                    GetCachedImGuiColor(chipFill),
                    3f);
                drawList.AddRect(
                    chipMin,
                    chipMax,
                    GetCachedImGuiColor(chipBorder),
                    3f,
                    0,
                    candidate.Important ? 1.3f : 1f);

                // A short solid accent bar makes the tag readable even for dark biomes
                // such as Forest or Swamp, without tinting the glyphs themselves.
                drawList.AddRectFilled(
                    chipMin + new Vector2(1f, 2f),
                    new Vector2(chipMin.X + 3f, chipMax.Y - 2f),
                    GetCachedImGuiColor(chipBorder),
                    1f);
                DrawModernLabelText(drawList, font, detailFontSize, detailPos, chipText, detail, false);
            }
        }

        private void GetAtlasSignalGeometry(
            Vector2 center,
            float radius,
            Vector2 panelMin,
            Vector2 panelMax,
            ModernLabelPlacement placement,
            out Vector2 nodeAnchor,
            out Vector2 signalTip,
            out Vector2 plateEdgeCenter,
            out bool horizontalSignal)
        {
            float middleX = (panelMin.X + panelMax.X) * 0.5f;
            float middleY = (panelMin.Y + panelMax.Y) * 0.5f;
            const float noseLength = 7f;

            switch (placement)
            {
                case ModernLabelPlacement.Below:
                    nodeAnchor = new Vector2(center.X, center.Y + radius + 1f);
                    plateEdgeCenter = new Vector2(middleX, panelMin.Y);
                    signalTip = plateEdgeCenter - new Vector2(0f, noseLength);
                    horizontalSignal = false;
                    break;
                case ModernLabelPlacement.Right:
                    nodeAnchor = new Vector2(center.X + radius + 1f, center.Y);
                    plateEdgeCenter = new Vector2(panelMin.X, middleY);
                    signalTip = plateEdgeCenter - new Vector2(noseLength, 0f);
                    horizontalSignal = true;
                    break;
                case ModernLabelPlacement.Left:
                    nodeAnchor = new Vector2(center.X - radius - 1f, center.Y);
                    plateEdgeCenter = new Vector2(panelMax.X, middleY);
                    signalTip = plateEdgeCenter + new Vector2(noseLength, 0f);
                    horizontalSignal = true;
                    break;
                default:
                    nodeAnchor = new Vector2(center.X, center.Y - radius - 1f);
                    plateEdgeCenter = new Vector2(middleX, panelMax.Y);
                    signalTip = plateEdgeCenter + new Vector2(0f, noseLength);
                    horizontalSignal = false;
                    break;
            }
        }

        private void DrawAtlasSignalRail(
            ImDrawListPtr drawList,
            Vector2 start,
            Vector2 end,
            bool horizontal,
            Color accent,
            Color muted,
            Color shadow,
            bool important)
        {
            drawList.AddLine(start + Vector2.One, end + Vector2.One, GetCachedImGuiColor(shadow), important ? 3f : 2.3f);
            drawList.AddLine(start, end, GetCachedImGuiColor(muted), important ? 1.9f : 1.45f);

            // Keep the node end bright and let the signal dissolve into the muted rail.
            // This creates depth without animation or additional Atlas reads.
            Vector2 rail = end - start;
            float brightLength = important ? 0.74f : 0.56f;
            Vector2 brightEnd = start + rail * brightLength;
            drawList.AddLine(start, brightEnd, GetCachedImGuiColor(accent), important ? 1.45f : 1.05f);

            if (important)
            {
                Vector2 coreEnd = start + rail * 0.30f;
                Color core = ScaleModernLabelAlpha(MakeModernLabelAccentReadable(accent), 0.72f);
                drawList.AddLine(start, coreEnd, GetCachedImGuiColor(core), 2.1f);
                drawList.AddCircleFilled(start, 2.1f, GetCachedImGuiColor(core), 8);
            }
        }

        private void DrawAtlasSignalPlate(
            ImDrawListPtr drawList,
            Vector2 panelMin,
            Vector2 panelMax,
            Vector2 plateEdgeCenter,
            Vector2 signalTip,
            ModernLabelPlacement placement,
            Color background,
            Color accent,
            Color shadow,
            bool important)
        {
            const float rounding = 2.5f;
            drawList.AddRectFilled(
                panelMin + new Vector2(1f, 2f),
                panelMax + new Vector2(1f, 2f),
                GetCachedImGuiColor(shadow),
                rounding);
            drawList.AddRectFilled(panelMin, panelMax, GetCachedImGuiColor(background), rounding);

            float halfNose = Math.Max(3f, (panelMax.Y - panelMin.Y) * 0.18f);
            switch (placement)
            {
                case ModernLabelPlacement.Below:
                    drawList.AddTriangleFilled(
                        signalTip,
                        plateEdgeCenter + new Vector2(-halfNose, 0f),
                        plateEdgeCenter + new Vector2(halfNose, 0f),
                        GetCachedImGuiColor(background));
                    break;
                case ModernLabelPlacement.Right:
                    drawList.AddTriangleFilled(
                        signalTip,
                        plateEdgeCenter + new Vector2(0f, -halfNose),
                        plateEdgeCenter + new Vector2(0f, halfNose),
                        GetCachedImGuiColor(background));
                    break;
                case ModernLabelPlacement.Left:
                    drawList.AddTriangleFilled(
                        signalTip,
                        plateEdgeCenter + new Vector2(0f, halfNose),
                        plateEdgeCenter + new Vector2(0f, -halfNose),
                        GetCachedImGuiColor(background));
                    break;
                default:
                    drawList.AddTriangleFilled(
                        signalTip,
                        plateEdgeCenter + new Vector2(halfNose, 0f),
                        plateEdgeCenter + new Vector2(-halfNose, 0f),
                        GetCachedImGuiColor(background));
                    break;
            }

            float topAccentLength = Math.Min(
                panelMax.X - panelMin.X - 8f,
                important ? 64f : 32f);
            drawList.AddLine(
                panelMin + new Vector2(4f, 0.5f),
                panelMin + new Vector2(4f + topAccentLength, 0.5f),
                GetCachedImGuiColor(accent),
                important ? 1.7f : 1.1f);

            if (important)
            {
                Vector2 accentEnd = panelMin + new Vector2(4f + topAccentLength, 0.5f);
                drawList.AddCircleFilled(accentEnd, 1.8f, GetCachedImGuiColor(accent), 8);
            }
        }

        private void DrawAtlasSignalNodeGlyph(
            ImDrawListPtr drawList,
            Vector2 center,
            Color accent,
            Color muted,
            bool important)
        {
            float size = important ? 4.2f : 3.3f;
            Vector2 top = center + new Vector2(0f, -size);
            Vector2 right = center + new Vector2(size, 0f);
            Vector2 bottom = center + new Vector2(0f, size);
            Vector2 left = center + new Vector2(-size, 0f);
            float thickness = important ? 1.6f : 1.1f;

            drawList.AddLine(top, right, GetCachedImGuiColor(accent), thickness);
            drawList.AddLine(right, bottom, GetCachedImGuiColor(accent), thickness);
            drawList.AddLine(bottom, left, GetCachedImGuiColor(muted), thickness);
            drawList.AddLine(left, top, GetCachedImGuiColor(muted), thickness);
        }

        private void DrawAtlasSignalFocusMarks(
            ImDrawListPtr drawList,
            Vector2 center,
            float radius,
            Color accent,
            Color muted)
        {
            float reach = radius + 6f;
            float shortLength = Math.Clamp(radius * 0.28f, 3.5f, 7f);
            float x = reach * 0.72f;
            float y = reach * 0.72f;

            Vector2 tl = center + new Vector2(-x, -y);
            Vector2 tr = center + new Vector2(x, -y);
            Vector2 bl = center + new Vector2(-x, y);
            Vector2 br = center + new Vector2(x, y);

            drawList.AddLine(tl, tl + new Vector2(shortLength, 0f), GetCachedImGuiColor(accent), 1.2f);
            drawList.AddLine(tl, tl + new Vector2(0f, shortLength), GetCachedImGuiColor(muted), 1.2f);
            drawList.AddLine(tr, tr + new Vector2(-shortLength, 0f), GetCachedImGuiColor(accent), 1.2f);
            drawList.AddLine(tr, tr + new Vector2(0f, shortLength), GetCachedImGuiColor(muted), 1.2f);
            drawList.AddLine(bl, bl + new Vector2(shortLength, 0f), GetCachedImGuiColor(accent), 1.2f);
            drawList.AddLine(bl, bl + new Vector2(0f, -shortLength), GetCachedImGuiColor(muted), 1.2f);
            drawList.AddLine(br, br + new Vector2(-shortLength, 0f), GetCachedImGuiColor(accent), 1.2f);
            drawList.AddLine(br, br + new Vector2(0f, -shortLength), GetCachedImGuiColor(muted), 1.2f);
        }

        private void DrawAtlasSignalStatusGlyph(
            ImDrawListPtr drawList,
            Vector2 center,
            NodeRenderInfo info,
            Color color)
        {
            uint packed = GetCachedImGuiColor(color);
            if (info.Completed)
            {
                drawList.AddLine(center + new Vector2(-3f, 0f), center + new Vector2(-1f, 2.5f), packed, 1.5f);
                drawList.AddLine(center + new Vector2(-1f, 2.5f), center + new Vector2(3.5f, -2.5f), packed, 1.5f);
                return;
            }

            if (info.Attempted)
            {
                drawList.AddLine(center + new Vector2(-2.5f, -2.5f), center + new Vector2(2.5f, 2.5f), packed, 1.3f);
                drawList.AddLine(center + new Vector2(2.5f, -2.5f), center + new Vector2(-2.5f, 2.5f), packed, 1.3f);
                return;
            }

            if (info.Unlocked || info.Visited)
            {
                drawList.AddLine(center + new Vector2(-2.5f, -2.5f), center + new Vector2(2f, 0f), packed, 1.4f);
                drawList.AddLine(center + new Vector2(2f, 0f), center + new Vector2(-2.5f, 2.5f), packed, 1.4f);
                return;
            }

            drawList.AddRect(
                center + new Vector2(-2.5f, -2.5f),
                center + new Vector2(2.5f, 2.5f),
                packed,
                0.5f,
                0,
                1.1f);
        }

        private string BuildModernLabelTitle(
            NodeRenderInfo info,
            bool biomeVisible,
            bool preferredWanted,
            string preferredDisplayName,
            out bool labelContainsMapName)
        {
            labelContainsMapName = false;
            bool hasMapName = !string.IsNullOrWhiteSpace(info.MapName);
            var sflags = info.SpecialFlags;

            if (biomeVisible && hasMapName)
            {
                labelContainsMapName = true;
                return info.MapName!;
            }

            if (Settings.PreferMapNameForDeadly.Value &&
                (sflags & Utility.SpecialFlags.DeadlyBoss) != 0 &&
                hasMapName)
            {
                labelContainsMapName = true;
                return info.MapName!;
            }

            if (preferredWanted && !string.IsNullOrWhiteSpace(preferredDisplayName) && !hasMapName)
            {
                labelContainsMapName = true;
                return preferredDisplayName;
            }

            if (Settings.ShowUniqueNameOnLabel.Value &&
                (sflags & Utility.SpecialFlags.UniqueMap) != 0 &&
                !string.IsNullOrWhiteSpace(info.UniqueName))
            {
                labelContainsMapName = true;
                return info.UniqueName!;
            }

            if (Settings.ShowMapNames.Value && hasMapName)
            {
                labelContainsMapName = true;
                return info.MapName!;
            }

            return info.BiomeDisplay;
        }

        private string BuildModernLabelDetail(
            ModernLabelCandidate candidate,
            bool labelContainsMapName,
            bool compact)
        {
            var info = candidate.Info;
            var sflags = info.SpecialFlags;

            if (candidate.PreferredWanted)
                return "PREF";
            if ((sflags & Utility.SpecialFlags.DeadlyBoss) != 0)
                return "DEADLY";
            if ((sflags & Utility.SpecialFlags.AreaContainsExpedition) != 0)
                return "EXP";
            if ((sflags & Utility.SpecialFlags.AreaContainsAbyss) != 0)
                return "ABYSS";
            if ((sflags & Utility.SpecialFlags.UniqueMap) != 0)
                return "UNIQUE";
            if ((sflags & Utility.SpecialFlags.CorruptedNexus) != 0)
                return "CORRUPTED";
            if ((sflags & Utility.SpecialFlags.Cleansed) != 0)
                return "CLEANSED";
            if ((sflags & Utility.SpecialFlags.MomentofZen) != 0)
                return "ZEN";
            if (candidate.TowerWanted)
                return "TOWER";
            if (candidate.MechanicWanted)
                return "MOD";

            bool showReadableBiome = Settings.ModernLabelReadableBiomeBadge.Value ||
                                       (!compact && Settings.ModernLabelShowBiomeText.Value);
            if (showReadableBiome && candidate.BiomeVisible &&
                labelContainsMapName && info.Biome != Biome.Unknown)
                return info.BiomeDisplay.ToUpperInvariant();

            return string.Empty;
        }


        private static bool IsModernLabelBiomeDetail(
            ModernLabelCandidate candidate,
            string detail,
            bool labelContainsMapName)
        {
            return labelContainsMapName &&
                   candidate.BiomeVisible &&
                   candidate.Info.Biome != Biome.Unknown &&
                   !string.IsNullOrWhiteSpace(detail) &&
                   string.Equals(
                       detail,
                       candidate.Info.BiomeDisplay,
                       StringComparison.OrdinalIgnoreCase);
        }

        private Color GetModernLabelDetailColor(ModernLabelCandidate candidate, string detail)
        {
            if (detail == "PREF")
                return Settings.PreferredMapRingColor.Value;
            if (detail == "DEADLY")
                return Settings.DeadlyBossRingColor.Value;
            if (detail == "EXP")
                return Settings.AreaContainsExpeditionRingColor.Value;
            if (detail == "ABYSS")
                return Settings.AreaContainsAbyssRingColor.Value;
            if (detail == "UNIQUE")
                return Settings.UniqueMapRingColor.Value;
            if (detail == "CORRUPTED")
                return Settings.CorruptedNexusRingColor.Value;
            if (detail == "CLEANSED")
                return Settings.CleansedRingColor.Value;
            if (detail == "ZEN")
                return Settings.MomentofZenRingColor.Value;
            if (detail == "TOWER" && !string.IsNullOrWhiteSpace(candidate.HighlightedTowerName))
                return GetTowerHighlightColor(candidate.HighlightedTowerName);
            if (detail == "MOD")
                return Settings.MechanicHighlightRingColor.Value;
            return candidate.BiomeColor;
        }

        private Color GetModernLabelSignalAccent(ModernLabelCandidate candidate)
        {
            if (!Settings.ModernLabelPrioritySignalColors.Value)
                return candidate.BiomeColor;

            var flags = candidate.Info.SpecialFlags;
            if (candidate.PreferredWanted)
                return Settings.PreferredMapRingColor.Value;
            if (Settings.ShowDeliriumStatus.Value && candidate.Info.HasDelirium)
                return Settings.DeliriumStatusColor.Value;
            if ((flags & Utility.SpecialFlags.DeadlyBoss) != 0)
                return Settings.DeadlyBossRingColor.Value;
            if ((flags & Utility.SpecialFlags.AreaContainsExpedition) != 0)
                return Settings.AreaContainsExpeditionRingColor.Value;
            if ((flags & Utility.SpecialFlags.UniqueMap) != 0)
                return Settings.UniqueMapRingColor.Value;
            if ((flags & Utility.SpecialFlags.CorruptedNexus) != 0)
                return Settings.CorruptedNexusRingColor.Value;
            if ((flags & Utility.SpecialFlags.Cleansed) != 0)
                return Settings.CleansedRingColor.Value;
            if ((flags & Utility.SpecialFlags.AreaContainsAbyss) != 0)
                return Settings.AreaContainsAbyssRingColor.Value;
            if ((flags & Utility.SpecialFlags.MomentofZen) != 0)
                return Settings.MomentofZenRingColor.Value;
            if (candidate.TowerWanted && !string.IsNullOrWhiteSpace(candidate.HighlightedTowerName))
                return GetTowerHighlightColor(candidate.HighlightedTowerName);
            if (candidate.MechanicWanted)
                return Settings.MechanicHighlightRingColor.Value;
            return candidate.BiomeColor;
        }

        private float GetModernLabelRevealOpacity(NodeRenderInfo info)
        {
            if (!Settings.ModernLabelSmoothReveal.Value ||
                !TryGetModernLabelPlacementKey(info, out var key))
                return 1f;

            long now = _modernLabelFrameTimeMs != 0 ? _modernLabelFrameTimeMs : Environment.TickCount64;
            if (!_modernLabelRevealByCoord.TryGetValue(key, out var state) ||
                now - state.LastSeenMs > ModernLabelRevealResetMs)
            {
                state = new ModernLabelRevealState
                {
                    LastSeenMs = now,
                    Progress = 0.08f
                };
            }
            else
            {
                long elapsedMs = Math.Clamp(now - state.LastSeenMs, 0L, 50L);
                state.Progress = Math.Min(1f, state.Progress + elapsedMs / ModernLabelRevealDurationMs);
                state.LastSeenMs = now;
            }

            if (_modernLabelRevealByCoord.Count >= ModernLabelPlacementCacheMaxEntries &&
                !_modernLabelRevealByCoord.ContainsKey(key))
                _modernLabelRevealByCoord.Clear();
            _modernLabelRevealByCoord[key] = state;

            // Cubic ease-out reaches full clarity quickly while keeping the first frame soft.
            float remaining = 1f - state.Progress;
            return 1f - remaining * remaining * remaining;
        }


        private static Color MakeModernLabelAccentReadable(Color color)
        {
            // Keep the original hue, but lift very dark biome/mechanic colors enough
            // to survive the black Atlas background and low-opacity signal plate.
            float luminance = (0.2126f * color.R + 0.7152f * color.G + 0.0722f * color.B) / 255f;
            if (luminance >= 0.46f)
                return color;

            float mix = Math.Clamp((0.46f - luminance) * 1.35f, 0.16f, 0.48f);
            Color lifted = BlendModernLabelColor(color, Color.White, mix);
            return Color.FromArgb(color.A, lifted.R, lifted.G, lifted.B);
        }

        private static Color ScaleModernLabelAlpha(Color color, float scale)
        {
            int alpha = (int)MathF.Round(color.A * Math.Clamp(scale, 0f, 1f));
            return Color.FromArgb(alpha, color.R, color.G, color.B);
        }

        private static float GetModernLabelLuminance(Color color)
        {
            return (0.2126f * color.R + 0.7152f * color.G + 0.0722f * color.B) / 255f;
        }

        private static Color BlendModernLabelColor(Color from, Color to, float amount)
        {
            amount = Math.Clamp(amount, 0f, 1f);
            return Color.FromArgb(
                (int)MathF.Round(from.A + (to.A - from.A) * amount),
                (int)MathF.Round(from.R + (to.R - from.R) * amount),
                (int)MathF.Round(from.G + (to.G - from.G) * amount),
                (int)MathF.Round(from.B + (to.B - from.B) * amount));
        }

        private static Color GetModernLabelStatusColor(NodeRenderInfo info)
        {
            if (info.Completed)
                return Color.FromArgb(90, 225, 135);
            if (info.Attempted)
                return Color.FromArgb(255, 190, 75);
            if (info.Unlocked || info.Visited)
                return Color.FromArgb(80, 205, 245);
            return Color.FromArgb(225, 95, 95);
        }

        private bool TryPlaceModernLabel(
            NodeRenderInfo info,
            Vector2 center,
            float radius,
            float width,
            float height,
            bool important,
            out Vector2 panelMin,
            out Vector2 panelMax,
            out ModernLabelPlacement placement)
        {
            // Labels are annotations for a concrete Atlas node, not edge indicators.
            // Once the node center leaves the viewport, stop rendering its label instead
            // of clamping the plate to the screen edge and making it appear "stuck".
            if (!IsModernLabelAnchorInsideViewport(center))
            {
                panelMin = default;
                panelMax = default;
                placement = default;
                return false;
            }

            bool declutter = Settings.ModernLabelDeclutter.Value;
            bool hasStableKey = TryGetModernLabelPlacementKey(info, out var stableKey);

            if (hasStableKey && _modernLabelPlacementByCoord.TryGetValue(stableKey, out var cachedPlacement))
            {
                GetModernLabelRect(center, radius, width, height, cachedPlacement, out var cachedMin, out var cachedMax);
                if (IsModernLabelInsideViewport(cachedMin, cachedMax))
                {
                    bool overlaps = declutter && DoesModernLabelOverlap(cachedMin, cachedMax);
                    if (!overlaps || important)
                    {
                        panelMin = cachedMin;
                        panelMax = cachedMax;
                        placement = cachedPlacement;
                        RegisterModernLabelRect(panelMin, panelMax);
                        return true;
                    }

                    // Ordinary labels remain attached to their original side. Hiding one
                    // conflicting frame is less distracting than jumping around the node.
                    panelMin = default;
                    panelMax = default;
                    placement = cachedPlacement;
                    return false;
                }
            }

            for (int i = 0; i < ModernLabelPlacementsStable.Length; i++)
            {
                ModernLabelPlacement candidatePlacement = ModernLabelPlacementsStable[i];
                GetModernLabelRect(center, radius, width, height, candidatePlacement, out var min, out var max);
                if (!IsModernLabelInsideViewport(min, max))
                    continue;
                if (declutter && DoesModernLabelOverlap(min, max))
                    continue;

                panelMin = min;
                panelMax = max;
                placement = candidatePlacement;
                RememberModernLabelPlacement(stableKey, hasStableKey, placement);
                RegisterModernLabelRect(min, max);
                return true;
            }

            if (important)
            {
                // Important labels may overlap another label, but they must remain attached
                // to their own node. Try the remembered side first, then the remaining
                // stable sides. Never clamp a label to the viewport edge.
                ModernLabelPlacement preferredPlacement = hasStableKey &&
                                                          _modernLabelPlacementByCoord.TryGetValue(stableKey, out var remembered)
                    ? remembered
                    : ModernLabelPlacement.Above;

                GetModernLabelRect(center, radius, width, height, preferredPlacement, out var preferredMin, out var preferredMax);
                if (IsModernLabelInsideViewport(preferredMin, preferredMax))
                {
                    panelMin = preferredMin;
                    panelMax = preferredMax;
                    placement = preferredPlacement;
                    RememberModernLabelPlacement(stableKey, hasStableKey, placement);
                    RegisterModernLabelRect(panelMin, panelMax);
                    return true;
                }

                for (int i = 0; i < ModernLabelPlacementsStable.Length; i++)
                {
                    ModernLabelPlacement fallbackPlacement = ModernLabelPlacementsStable[i];
                    if (fallbackPlacement == preferredPlacement)
                        continue;

                    GetModernLabelRect(center, radius, width, height, fallbackPlacement, out var fallbackMin, out var fallbackMax);
                    if (!IsModernLabelInsideViewport(fallbackMin, fallbackMax))
                        continue;

                    panelMin = fallbackMin;
                    panelMax = fallbackMax;
                    placement = fallbackPlacement;
                    RememberModernLabelPlacement(stableKey, hasStableKey, placement);
                    RegisterModernLabelRect(panelMin, panelMax);
                    return true;
                }
            }

            panelMin = default;
            panelMax = default;
            placement = default;
            return false;
        }

        private static bool TryGetModernLabelPlacementKey(
            NodeRenderInfo info,
            out (int x, int y) key)
        {
            try
            {
                var coordinate = info.Node.Coordinate;
                key = (coordinate.X, coordinate.Y);
                return true;
            }
            catch
            {
                key = default;
                return false;
            }
        }

        private void RememberModernLabelPlacement(
            (int x, int y) key,
            bool hasKey,
            ModernLabelPlacement placement)
        {
            if (!hasKey)
                return;

            if (_modernLabelPlacementByCoord.Count >= ModernLabelPlacementCacheMaxEntries &&
                !_modernLabelPlacementByCoord.ContainsKey(key))
                _modernLabelPlacementByCoord.Clear();

            _modernLabelPlacementByCoord[key] = placement;
        }

        private static void GetModernLabelRect(
            Vector2 center,
            float radius,
            float width,
            float height,
            ModernLabelPlacement placement,
            out Vector2 min,
            out Vector2 max)
        {
            min = placement switch
            {
                ModernLabelPlacement.Above => new Vector2(
                    center.X - width * 0.5f,
                    center.Y - radius - ModernLabelPlacementGap - height),
                ModernLabelPlacement.Below => new Vector2(
                    center.X - width * 0.5f,
                    center.Y + radius + ModernLabelPlacementGap),
                ModernLabelPlacement.Right => new Vector2(
                    center.X + radius + ModernLabelPlacementGap,
                    center.Y - height * 0.5f),
                _ => new Vector2(
                    center.X - radius - ModernLabelPlacementGap - width,
                    center.Y - height * 0.5f)
            };
            max = min + new Vector2(width, height);
        }

        private bool IsModernLabelAnchorInsideViewport(Vector2 center)
        {
            if (!float.IsFinite(center.X) || !float.IsFinite(center.Y))
                return false;

            return center.X >= ModernLabelViewportMargin &&
                   center.Y >= ModernLabelViewportMargin &&
                   center.X <= _currentRenderDisplaySize.X - ModernLabelViewportMargin &&
                   center.Y <= _currentRenderDisplaySize.Y - ModernLabelViewportMargin;
        }

        private bool IsModernLabelInsideViewport(Vector2 min, Vector2 max)
        {
            return min.X >= ModernLabelViewportMargin &&
                   min.Y >= ModernLabelViewportMargin &&
                   max.X <= _currentRenderDisplaySize.X - ModernLabelViewportMargin &&
                   max.Y <= _currentRenderDisplaySize.Y - ModernLabelViewportMargin;
        }

        private Vector2 ClampModernLabelMin(Vector2 min, float width, float height)
        {
            float maxX = Math.Max(ModernLabelViewportMargin, _currentRenderDisplaySize.X - width - ModernLabelViewportMargin);
            float maxY = Math.Max(ModernLabelViewportMargin, _currentRenderDisplaySize.Y - height - ModernLabelViewportMargin);
            return new Vector2(
                Math.Clamp(min.X, ModernLabelViewportMargin, maxX),
                Math.Clamp(min.Y, ModernLabelViewportMargin, maxY));
        }

        private bool DoesModernLabelOverlap(Vector2 min, Vector2 max)
        {
            float spacing = Math.Clamp(
                Settings.ModernLabelSpacing.Value,
                Settings.ModernLabelSpacing.Min,
                Settings.ModernLabelSpacing.Max);
            var expandedMin = min - new Vector2(spacing);
            var expandedMax = max + new Vector2(spacing);

            for (int i = 0; i < _modernLabelOccupiedRects.Count; i++)
            {
                var existing = _modernLabelOccupiedRects[i];
                if (expandedMin.X < existing.Max.X &&
                    expandedMax.X > existing.Min.X &&
                    expandedMin.Y < existing.Max.Y &&
                    expandedMax.Y > existing.Min.Y)
                    return true;
            }

            return false;
        }

        private void RegisterModernLabelRect(Vector2 min, Vector2 max)
        {
            _modernLabelOccupiedRects.Add(new ModernLabelRect(min, max));
        }

        private void DrawModernLabelConnector(
            ImDrawListPtr drawList,
            Vector2 center,
            float radius,
            Vector2 panelMin,
            Vector2 panelMax,
            ModernLabelPlacement placement,
            Color color)
        {
            Vector2 start;
            Vector2 end;
            switch (placement)
            {
                case ModernLabelPlacement.Below:
                    start = new Vector2(Math.Clamp(center.X, panelMin.X + 8f, panelMax.X - 8f), panelMin.Y);
                    end = new Vector2(center.X, center.Y + radius);
                    break;
                case ModernLabelPlacement.Right:
                    start = new Vector2(panelMin.X, Math.Clamp(center.Y, panelMin.Y + 6f, panelMax.Y - 6f));
                    end = new Vector2(center.X + radius, center.Y);
                    break;
                case ModernLabelPlacement.Left:
                    start = new Vector2(panelMax.X, Math.Clamp(center.Y, panelMin.Y + 6f, panelMax.Y - 6f));
                    end = new Vector2(center.X - radius, center.Y);
                    break;
                default:
                    start = new Vector2(Math.Clamp(center.X, panelMin.X + 8f, panelMax.X - 8f), panelMax.Y);
                    end = new Vector2(center.X, center.Y - radius);
                    break;
            }

            drawList.AddLine(start, end, GetCachedImGuiColor(Utility.WithOpacity(color, 0.45f)), 1f);
        }

        private Vector2 MeasureModernLabelText(ImFontPtr font, int fontSize, string text)
        {
            if (string.IsNullOrEmpty(text))
                return Vector2.Zero;

            var key = (text, fontSize);
            if (_modernLabelMeasureCache.TryGetValue(key, out var size))
                return size;

            size = font.CalcTextSizeA(fontSize, float.MaxValue, 0f, text);
            if (_modernLabelMeasureCache.Count >= ModernLabelMeasureCacheMaxEntries)
                _modernLabelMeasureCache.Clear();
            _modernLabelMeasureCache[key] = size;
            return size;
        }

        private string FitModernLabelText(ImFontPtr font, string text, int fontSize, float maxWidth)
        {
            if (string.IsNullOrEmpty(text) || maxWidth <= 0f)
                return string.Empty;

            if (MeasureModernLabelText(font, fontSize, text).X <= maxWidth)
                return text;

            int widthKey = Math.Max(1, (int)MathF.Floor(maxWidth));
            var key = (text, fontSize, widthKey);
            if (_modernLabelFittedCache.TryGetValue(key, out var cached))
                return cached;

            const string ellipsis = "...";
            float ellipsisWidth = MeasureModernLabelText(font, fontSize, ellipsis).X;
            if (ellipsisWidth >= maxWidth)
                return ellipsis;

            int low = 0;
            int high = text.Length;
            while (low < high)
            {
                int mid = (low + high + 1) / 2;
                string candidate = text.Substring(0, mid).TrimEnd() + ellipsis;
                if (MeasureModernLabelText(font, fontSize, candidate).X <= maxWidth)
                    low = mid;
                else
                    high = mid - 1;
            }

            string fitted = text.Substring(0, low).TrimEnd() + ellipsis;
            if (_modernLabelFittedCache.Count >= ModernLabelFittedCacheMaxEntries)
                _modernLabelFittedCache.Clear();
            _modernLabelFittedCache[key] = fitted;
            return fitted;
        }

        private void DrawModernLabelText(
            ImDrawListPtr drawList,
            ImFontPtr font,
            float fontSize,
            Vector2 position,
            Color color,
            string text,
            bool drawShadow = true,
            bool adaptiveOutline = false)
        {
            if (string.IsNullOrEmpty(text))
                return;

            position = SnapTextPos(position);
            if (drawShadow)
            {
                Color shadow = Utility.WithOpacity(
                    Color.Black,
                    Math.Clamp(color.A / 255f, 0f, 1f) * 0.82f);
                if (adaptiveOutline)
                    drawList.AddText(font, fontSize, position + new Vector2(-1f, 0f), GetCachedImGuiColor(shadow), text, 0f);
                drawList.AddText(font, fontSize, position + Vector2.One, GetCachedImGuiColor(shadow), text, 0f);
            }
            drawList.AddText(font, fontSize, position, GetCachedImGuiColor(color), text, 0f);
        }
    }
}
