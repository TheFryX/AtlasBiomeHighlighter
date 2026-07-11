using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Text;
using System.Reflection;
using System.Reflection.Emit;
using ExileCore2;
using ExileCore2.PoEMemory.Elements.AtlasElements;
using ExileCore2.Shared.Nodes;
using ImGuiNET;

namespace AtlasBiomeHighlighter
{
    public partial class AtlasBiomeHighlighter : BaseSettingsPlugin<AtlasBiomeSettings>
    {
        private ExileCore2.PoEMemory.Elements.AtlasElements.AtlasPanel? _atlasPanel;
        private AtlasNodeDescription[] _atlasNodes = Array.Empty<AtlasNodeDescription>();
        private System.Collections.Generic.List<AtlasNodeDescription> _visibleNodes = new();
        private readonly HashSet<string> _preferredDebugLogged = new(StringComparer.OrdinalIgnoreCase);

        private readonly Stopwatch _atlasRefreshSw = new();
        private readonly Stopwatch _screenRefreshSw = new();

        
        
        private int _viewportWidth;
        private int _viewportHeight;

        private void UpdateViewportSize()
        {
            
            int w = Settings.BorderX?.Value ?? 0;
            int h = Settings.BorderY?.Value ?? 0;

            if (w <= 0 || h <= 0)
            {
                
                
                var ds = ImGui.GetIO().DisplaySize;
                int autoW = (int)ds.X;
                int autoH = (int)ds.Y;

                if (w <= 0) w = autoW;
                if (h <= 0) h = autoH;
            }

            
            if (w <= 0) w = 1920;
            if (h <= 0) h = 1080;

            _viewportWidth = w;
            _viewportHeight = h;
        }

        private int BorderX => _viewportWidth > 0 ? _viewportWidth : 1920;
        private int BorderY => _viewportHeight > 0 ? _viewportHeight : 1080;

        
        
        private int _preferredCacheHash;
        private string[] _preferredTokensList = Array.Empty<string>();
        private HashSet<string> _preferredTokensExact = new(StringComparer.Ordinal);

        
        
        private string[] _preferredMechanicTokensList = Array.Empty<string>();
        private HashSet<string> _preferredMechanicTokensExact = new(StringComparer.Ordinal);

        private string[] _preferredRumourTokensList = Array.Empty<string>();
        private HashSet<string> _preferredRumourTokensExact = new(StringComparer.Ordinal);

        
        private readonly Dictionary<string, string> _preferredTokenToTag = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _preferredTokenToDisplayName = new(StringComparer.Ordinal);

        
        private readonly Dictionary<long, NodeTokenCache> _nodeTokenCache = new(512);
        private int _nodeTokenCacheFrame;
        private const int NodeTokenCacheMaxEntries = 4096;

        private readonly struct NodeTokenCache
        {
            public NodeTokenCache(string nameToken, string idToken, int lastSeenFrame)
            {
                NameToken = nameToken;
                IdToken = idToken;
                LastSeenFrame = lastSeenFrame;
            }

            public string NameToken { get; }
            public string IdToken { get; }
            public int LastSeenFrame { get; }
        }


        private void DebugPreferredMapHit(AtlasNodeDescription nd, string matchedToken, string? preferredTag, string? cachedMapName, Biome biome, Utility.SpecialFlags flags)
        {
            if (!Settings.DebugPreferredMaps.Value)
                return;

            try
            {
                string mapName = cachedMapName ?? string.Empty;
                Utility.TryGetAnyMapName(nd, out var anyMapName);
                string areaId = SafeMemberPath(nd.Element, "Area.Id");
                string areaName = SafeMemberPath(nd.Element, "Area.Name");
                string elementId = SafeMemberPath(nd.Element, "Id");
                string biomeRaw = SafeMemberPath(nd.Element, "Biome");
                string coord = $"{nd.Coordinate.X},{nd.Coordinate.Y}";
                string key = $"{matchedToken}|{coord}|{areaId}|{areaName}|{elementId}";
                if (!_preferredDebugLogged.Add(key))
                    return;

                var sb = new StringBuilder(4096);
                sb.AppendLine("============================================================");
                sb.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                sb.AppendLine($"PreferredToken: {matchedToken}");
                sb.AppendLine($"PreferredTag: {preferredTag ?? string.Empty}");
                sb.AppendLine($"CachedMapName: {mapName}");
                sb.AppendLine($"TryGetAnyMapName: {anyMapName ?? string.Empty}");
                sb.AppendLine($"Coordinate: {coord}");
                sb.AppendLine($"BiomeParsed: {biome}");
                sb.AppendLine($"SpecialFlags: {flags}");
                sb.AppendLine($"ElementType: {nd.Element?.GetType().FullName ?? "null"}");
                sb.AppendLine($"Element.Id: {elementId}");
                sb.AppendLine($"Element.Biome: {biomeRaw}");
                sb.AppendLine($"Area.Id: {areaId}");
                sb.AppendLine($"Area.Name: {areaName}");
                sb.AppendLine($"Text: {SafeMemberPath(nd.Element, "Text")}");
                sb.AppendLine($"TextNoTags: {SafeMemberPath(nd.Element, "TextNoTags")}");
                sb.AppendLine($"TextureName: {SafeMemberPath(nd.Element, "TextureName")}");
                sb.AppendLine($"PathFromRoot: {SafeMemberPath(nd.Element, "PathFromRoot")}");

                if (Settings.DebugPreferredDetails.Value)
                {
                    sb.AppendLine();
                    sb.AppendLine("[Element members]");
                    DumpObjectMembers(nd.Element, sb, 0, 2, new HashSet<object>(ReferenceEqualityComparer.Instance));
                }

                var path = Path.Combine(DirectoryFullName, "AtlasBiomeHighlighter.PreferredDebug.log");
                File.AppendAllText(path, sb.ToString());
            }
            catch
            {
                
            }
        }

        private static string SafeMemberPath(object? obj, string path)
        {
            try
            {
                object? cur = obj;
                foreach (var part in path.Split('.'))
                {
                    cur = GetDebugMember(cur, part);
                    if (cur == null)
                        return string.Empty;
                }
                return cur.ToString() ?? string.Empty;
            }
            catch { return string.Empty; }
        }

        private static readonly ConcurrentDictionary<Type, ConcurrentDictionary<string, Func<object, object?>>> DebugMemberAccessors = new();
        private static readonly Func<object, object?> MissingDebugMemberAccessor = static _ => null;

        private static object? GetDebugMember(object? obj, string name)
        {
            if (obj == null || string.IsNullOrEmpty(name))
                return null;

            var accessorsByName = DebugMemberAccessors.GetOrAdd(
                obj.GetType(),
                static _ => new ConcurrentDictionary<string, Func<object, object?>>(StringComparer.OrdinalIgnoreCase));
            var accessor = accessorsByName.GetOrAdd(name, memberName => CreateDebugMemberAccessor(obj.GetType(), memberName));

            try
            {
                return accessor(obj);
            }
            catch
            {
                return null;
            }
        }

        private static Func<object, object?> CreateDebugMemberAccessor(Type declaringType, string name)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;

            var property = declaringType.GetProperty(name, flags);
            if (property != null && property.GetIndexParameters().Length == 0 && property.GetGetMethod(true) != null)
                return CreateDebugPropertyAccessor(declaringType, property);

            var field = declaringType.GetField(name, flags);
            if (field != null)
                return CreateDebugFieldAccessor(declaringType, field);

            return MissingDebugMemberAccessor;
        }

        private static Func<object, object?> CreateDebugPropertyAccessor(Type declaringType, PropertyInfo property)
        {
            var getter = property.GetGetMethod(true)!;
            if (!getter.IsPublic ||
                property.PropertyType.IsByRef ||
                property.PropertyType.IsPointer ||
                property.PropertyType.IsByRefLike)
            {
                return obj => property.GetValue(obj);
            }

            try
            {
                var method = new DynamicMethod(
                    $"AtlasBiomeHighlighter_get_{declaringType.Name}_{property.Name}",
                    typeof(object),
                    new[] { typeof(object) },
                    typeof(AtlasBiomeHighlighter),
                    true);
                var il = method.GetILGenerator();
                EmitDebugAccessorInstance(il, declaringType);
                il.Emit(declaringType.IsValueType || !getter.IsVirtual ? OpCodes.Call : OpCodes.Callvirt, getter);
                EmitDebugAccessorBox(il, property.PropertyType);
                il.Emit(OpCodes.Ret);
                return (Func<object, object?>)method.CreateDelegate(typeof(Func<object, object?>));
            }
            catch
            {
                return obj => property.GetValue(obj);
            }
        }

        private static Func<object, object?> CreateDebugFieldAccessor(Type declaringType, FieldInfo field)
        {
            if (!field.IsPublic ||
                field.FieldType.IsByRef ||
                field.FieldType.IsPointer ||
                field.FieldType.IsByRefLike)
            {
                return obj => field.GetValue(obj);
            }

            try
            {
                var method = new DynamicMethod(
                    $"AtlasBiomeHighlighter_get_{declaringType.Name}_{field.Name}",
                    typeof(object),
                    new[] { typeof(object) },
                    typeof(AtlasBiomeHighlighter),
                    true);
                var il = method.GetILGenerator();
                EmitDebugAccessorInstance(il, declaringType);
                il.Emit(OpCodes.Ldfld, field);
                EmitDebugAccessorBox(il, field.FieldType);
                il.Emit(OpCodes.Ret);
                return (Func<object, object?>)method.CreateDelegate(typeof(Func<object, object?>));
            }
            catch
            {
                return obj => field.GetValue(obj);
            }
        }

        private static void EmitDebugAccessorInstance(ILGenerator il, Type declaringType)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(declaringType.IsValueType ? OpCodes.Unbox : OpCodes.Castclass, declaringType);
        }

        private static void EmitDebugAccessorBox(ILGenerator il, Type valueType)
        {
            if (valueType.IsValueType)
                il.Emit(OpCodes.Box, valueType);
        }

        private static void DumpObjectMembers(object? obj, StringBuilder sb, int depth, int maxDepth, HashSet<object> seen)
        {
            if (obj == null || depth > maxDepth) return;
            var t = obj.GetType();

            if (!t.IsValueType && !seen.Add(obj))
                return;

            string indent = new string(' ', depth * 2);
            sb.AppendLine($"{indent}{t.FullName}");

            foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (p.GetIndexParameters().Length > 0) continue;
                object? val = null;
                try { val = p.GetValue(obj); } catch { }
                sb.AppendLine($"{indent}  P:{p.Name} = {FormatDebugValue(val)}");

                if (depth < maxDepth && ShouldExpandDebugMember(p.Name, val))
                    DumpObjectMembers(val, sb, depth + 1, maxDepth, seen);
            }

            foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                object? val = null;
                try { val = f.GetValue(obj); } catch { }
                sb.AppendLine($"{indent}  F:{f.Name} = {FormatDebugValue(val)}");

                if (depth < maxDepth && ShouldExpandDebugMember(f.Name, val))
                    DumpObjectMembers(val, sb, depth + 1, maxDepth, seen);
            }
        }

        private static bool ShouldExpandDebugMember(string name, object? val)
        {
            if (val == null) return false;
            if (val is string) return false;
            var t = val.GetType();
            if (t.IsPrimitive || t.IsEnum || t == typeof(decimal)) return false;
            var u = name.ToUpperInvariant();
            return u == "AREA" || u == "ATLASPANELNODE" || u == "NODE" || u == "ENTITY" || u.Contains("AREA") || u.Contains("BIOME");
        }

        private static string FormatDebugValue(object? val)
        {
            if (val == null) return "null";
            if (val is string s) return s;
            try { return val.ToString() ?? string.Empty; } catch { return "<ToString failed>"; }
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new();

            public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

            public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }

        public override bool Initialise()
        {
            _atlasRefreshSw.Start();
            _screenRefreshSw.Start();
            MigratePreferredGroupsIfNeeded();
            NormalizePreferredMapCatalogForCurrentAtlas();
            ResetAtlasCache();
            RegisterRequestedHotkeys();
            return true;
        }

        private void MigratePreferredGroupsIfNeeded()
        {
            Settings.PreferredMaps ??= new Dictionary<string, ToggleNode>(StringComparer.OrdinalIgnoreCase);
            
            if (Settings.PreferredMapGroups != null && Settings.PreferredMapGroups.Count > 0)
                return;

            var g = new PreferredMapGroup { Name = "Default", Enabled = true };
            foreach (var kv in Settings.PreferredMaps)
            {
                if (kv.Value?.Value == true)
                    g.Maps.Add(kv.Key);
            }

            Settings.PreferredMapGroups = new List<PreferredMapGroup> { g };
            
        }

        private void NormalizePreferredMapCatalogForCurrentAtlas()
        {
            NormalizeModernLabelSettings();
            NormalizeIslandRumourSettings();
            NormalizePreferredMapDictionary();
            NormalizePreferredMapGroups();
            NormalizeFavoriteWaypointMaps();
            NormalizeTowerHighlightDictionaries();
            _preferredCacheHash = 0;
        }

        private void NormalizeModernLabelSettings()
        {
            Settings.ModernLabelCards ??= new ToggleNode(true);
            Settings.ModernLabelScale ??= new RangeNode<float>(1.00f, 0.80f, 1.45f);
            Settings.ModernLabelBackgroundOpacity ??= new RangeNode<float>(0.88f, 0.20f, 1.0f);
            Settings.ModernLabelMaxWidth ??= new RangeNode<int>(360, 130, 420);
            Settings.ModernLabelPreloadViewportScale ??= new RangeNode<float>(1.5f, 1.0f, 3.0f);
            Settings.ModernLabelConnector ??= new ToggleNode(true);
            Settings.ModernLabelAutoCompact ??= new ToggleNode(true);
            Settings.ModernLabelCompactScaleThreshold ??= new RangeNode<float>(0.72f, 0.40f, 1.10f);
            Settings.ModernLabelHideOrdinaryWhenZoomedOut ??= new ToggleNode(true);
            Settings.ModernLabelShowBiomeText ??= new ToggleNode(false);
            Settings.ModernLabelReadableBiomeBadge ??= new ToggleNode(true);
            Settings.ModernLabelUseBiomeTitleColor ??= new ToggleNode(true);
            Settings.ModernLabelDeclutter ??= new ToggleNode(true);
            Settings.ModernLabelSpacing ??= new RangeNode<int>(0, 0, 16);
            Settings.ModernLabelBackgroundColor ??= new ColorNode(System.Drawing.Color.FromArgb(15, 19, 25));
            Settings.ModernLabelBorderColor ??= new ColorNode(System.Drawing.Color.FromArgb(164, 175, 194));
            Settings.ModernLabelSmoothReveal ??= new ToggleNode(true);
            Settings.ModernLabelPrioritySignalColors ??= new ToggleNode(true);
            Settings.ModernLabelAdaptiveTextContrast ??= new ToggleNode(true);
            Settings.PreferredGuideTargetPulse ??= new ToggleNode(true);
            Settings.DebugSignalPositions ??= new ToggleNode(false);
            Settings.PerformanceProfiling ??= new ToggleNode(false);
            if (Settings.DiagnosticsSettingsVersion < 1)
            {
                // Position diagnostics and the spike profiler are intentionally opt-in.
                // Older diagnostic packages enabled position sampling by default, which
                // sorted every visible label each frame and could itself cause pan jitter.
                Settings.DebugSignalPositions.Value = false;
                Settings.PerformanceProfiling.Value = false;
                Settings.DiagnosticsSettingsVersion = 1;
            }
            Settings.LabelOffset ??= new RangeNode<int>(20, -60, 60);
            Settings.LabelUseBiomeColor ??= new ToggleNode(true);
            Settings.LabelTextColor ??= new ColorNode(System.Drawing.Color.White);
            Settings.LabelOutline ??= new ToggleNode(true);
            Settings.LabelOutlineThickness ??= new RangeNode<int>(2, 1, 6);
            Settings.LabelBold ??= new ToggleNode(true);
            Settings.ShowSpecialTag ??= new ToggleNode(true);
            Settings.ShowUniqueNameOnLabel ??= new ToggleNode(true);
            Settings.PreferMapNameForDeadly ??= new ToggleNode(true);

            if (Settings.ModernLabelSettingsVersion < 2)
            {
                // Apply the v2 screenshot preset once, then preserve user tuning.
                Settings.ModernLabelCards.Value = true;
                Settings.ModernLabelScale.Value = 1.00f;
                Settings.ModernLabelBackgroundOpacity.Value = 0.88f;
                Settings.ModernLabelMaxWidth.Value = 360;
                Settings.ModernLabelPreloadViewportScale.Value = 1.5f;
                Settings.ModernLabelReadableBiomeBadge.Value = true;
                Settings.ModernLabelUseBiomeTitleColor.Value = true;
                Settings.ModernLabelDeclutter.Value = true;
                Settings.ModernLabelSpacing.Value = 0;
                Settings.ModernLabelConnector.Value = true;
                Settings.ModernLabelAutoCompact.Value = true;
                Settings.ModernLabelCompactScaleThreshold.Value = 0.72f;
                Settings.ModernLabelHideOrdinaryWhenZoomedOut.Value = true;
                Settings.ModernLabelShowBiomeText.Value = false;
                Settings.ModernLabelBackgroundColor.Value = System.Drawing.Color.FromArgb(15, 19, 25);
                Settings.ModernLabelBorderColor.Value = System.Drawing.Color.FromArgb(164, 175, 194);

                Settings.LabelOffset.Value = 20;
                Settings.LabelUseBiomeColor.Value = true;
                Settings.LabelTextColor.Value = System.Drawing.Color.White;
                Settings.LabelOutline.Value = true;
                Settings.LabelOutlineThickness.Value = 2;
                Settings.LabelBold.Value = true;
                Settings.ShowSpecialTag.Value = true;
                Settings.ShowUniqueNameOnLabel.Value = true;
                Settings.PreferMapNameForDeadly.Value = true;
                Settings.ModernLabelSettingsVersion = 2;
            }

            if (Settings.ModernLabelSettingsVersion < 3)
            {
                Settings.ModernLabelSmoothReveal.Value = true;
                Settings.ModernLabelPrioritySignalColors.Value = true;
                Settings.ModernLabelAdaptiveTextContrast.Value = true;
                Settings.PreferredGuideTargetPulse.Value = true;
                Settings.ModernLabelSettingsVersion = 3;
            }
        }

        private void NormalizeIslandRumourSettings()
        {
            if (Settings.ShowIslandRumourRegionStats == null)
                Settings.ShowIslandRumourRegionStats = new ToggleNode(true);

            if (Settings.IslandRumourRegionStatsColor == null)
                Settings.IslandRumourRegionStatsColor = new ColorNode(System.Drawing.Color.FromArgb(120, 220, 255));

            Settings.IslandRumourRowAccents ??= new ToggleNode(true);

            if (Settings.IslandRumourLabelFontSize == null)
                Settings.IslandRumourLabelFontSize = new RangeNode<int>(16, 13, 22);

            if (Settings.IslandRumourLabelMaxWidth == null)
                Settings.IslandRumourLabelMaxWidth = new RangeNode<int>(540, 420, 720);

            if (Settings.IslandRumourLabelSpacing == null)
                Settings.IslandRumourLabelSpacing = new RangeNode<int>(28, 22, 40);

            if (Settings.IslandRumourLabelBackgroundOpacity == null)
                Settings.IslandRumourLabelBackgroundOpacity = new RangeNode<float>(0.92f, 0.15f, 1.0f);

            if (Settings.IslandRumourLiveTooltipScanEnabled == null)
                Settings.IslandRumourLiveTooltipScanEnabled = new ToggleNode(false);

            if (Settings.IslandRumourSettingsVersion >= 7)
                return;

            // The direct dictionary path is inexpensive, so reduce the old one-second default
            // without overriding users who selected a different custom interval.
            if (Settings.IslandRumourRefreshMs.Value == 1000)
                Settings.IslandRumourRefreshMs.Value = 500;

            if (Settings.IslandRumourSettingsVersion < 6)
            {
                if (Settings.IslandRumourMaxLabels.Value > 3)
                    Settings.IslandRumourMaxLabels.Value = 3;

                if (Settings.IslandRumourRefreshMs.Value >= 3000)
                    Settings.IslandRumourRefreshMs.Value = 500;

                // Version 6 replaces the tiny compact card with a readable four-column table.
                // Recreate the range nodes so existing installations also receive the new bounds.
                int migratedFontSize = Settings.IslandRumourLabelFontSize.Value <= 13
                    ? 16
                    : Math.Clamp(Settings.IslandRumourLabelFontSize.Value, 13, 22);
                int migratedWidth = Settings.IslandRumourLabelMaxWidth.Value <= 420
                    ? 540
                    : Math.Clamp(Settings.IslandRumourLabelMaxWidth.Value, 420, 720);
                int migratedRowHeight = Settings.IslandRumourLabelSpacing.Value <= 18
                    ? 28
                    : Math.Clamp(Settings.IslandRumourLabelSpacing.Value, 22, 40);
                float migratedBackgroundOpacity = Math.Max(0.88f, Settings.IslandRumourLabelBackgroundOpacity.Value);

                Settings.IslandRumourLabelFontSize = new RangeNode<int>(migratedFontSize, 13, 22);
                Settings.IslandRumourLabelMaxWidth = new RangeNode<int>(migratedWidth, 420, 720);
                Settings.IslandRumourLabelSpacing = new RangeNode<int>(migratedRowHeight, 22, 40);
                Settings.IslandRumourLabelBackgroundOpacity = new RangeNode<float>(migratedBackgroundOpacity, 0.15f, 1.0f);
            }

            // Version 7 makes the inexpensive AtlasButtonNode.Rumors path the default and moves
            // the expensive child/tooltip walker behind an explicit opt-in switch.
            EnsureIslandRumourColorSettings();
            Settings.IslandRumourSettingsVersion = 7;
        }

        private void NormalizePreferredMapDictionary()
        {
            Settings.PreferredMaps ??= new Dictionary<string, ToggleNode>(StringComparer.OrdinalIgnoreCase);

            RenameDictionaryKey(Settings.PreferredMaps, Utility.LegacySinkingSpireName, Utility.CurrentSwampTowerName, MergeToggleNodes);
            RemoveDictionaryKey(Settings.PreferredMaps, Utility.RemovedPrecursorTowerName);

            if (!ContainsKeyOrdinalIgnoreCase(Settings.PreferredMaps, Utility.CurrentSwampTowerName))
                Settings.PreferredMaps[Utility.CurrentSwampTowerName] = new ToggleNode(false);
        }

        private void NormalizePreferredMapGroups()
        {
            if (Settings.PreferredMapGroups == null)
            {
                Settings.PreferredMapGroups = new List<PreferredMapGroup>();
                return;
            }

            for (int i = 0; i < Settings.PreferredMapGroups.Count; i++)
            {
                var group = Settings.PreferredMapGroups[i];
                if (group == null)
                    continue;

                group.Maps ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                RenameHashSetValue(group.Maps, Utility.LegacySinkingSpireName, Utility.CurrentSwampTowerName);
                RemoveHashSetValue(group.Maps, Utility.RemovedPrecursorTowerName);

                group.Mechanics ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                group.Rumours ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private void NormalizeFavoriteWaypointMaps()
        {
            Settings.FavoriteWaypointMaps ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            RenameHashSetValue(Settings.FavoriteWaypointMaps, Utility.LegacySinkingSpireName, Utility.CurrentSwampTowerName);
            RemoveHashSetValue(Settings.FavoriteWaypointMaps, Utility.RemovedPrecursorTowerName);
        }

        private void NormalizeTowerHighlightDictionaries()
        {
            Settings.TowerHighlights ??= new Dictionary<string, ToggleNode>(StringComparer.OrdinalIgnoreCase);
            Settings.TowerHighlightColors ??= new Dictionary<string, ColorNode>(StringComparer.OrdinalIgnoreCase);

            RenameDictionaryKey(Settings.TowerHighlights, Utility.LegacySinkingSpireName, Utility.CurrentSwampTowerName, MergeToggleNodes);
            RemoveDictionaryKey(Settings.TowerHighlights, Utility.RemovedPrecursorTowerName);
            if (!ContainsKeyOrdinalIgnoreCase(Settings.TowerHighlights, Utility.CurrentSwampTowerName))
                Settings.TowerHighlights[Utility.CurrentSwampTowerName] = new ToggleNode(false);

            RenameDictionaryKey(Settings.TowerHighlightColors, Utility.LegacySinkingSpireName, Utility.CurrentSwampTowerName, MergeColorNodes);
            RemoveDictionaryKey(Settings.TowerHighlightColors, Utility.RemovedPrecursorTowerName);
            if (!ContainsKeyOrdinalIgnoreCase(Settings.TowerHighlightColors, Utility.CurrentSwampTowerName))
                Settings.TowerHighlightColors[Utility.CurrentSwampTowerName] = new ColorNode(Settings.TowerHighlightRingColor.Value);
        }

        private static void MergeToggleNodes(ToggleNode? target, ToggleNode? source)
        {
            if (target != null && source?.Value == true)
                target.Value = true;
        }

        private static void MergeColorNodes(ColorNode? target, ColorNode? source)
        {
            if (target != null && source != null)
                target.Value = source.Value;
        }

        private static bool ContainsKeyOrdinalIgnoreCase<T>(Dictionary<string, T> dictionary, string key)
        {
            return TryGetActualKey(dictionary, key, out _);
        }

        private static void RemoveDictionaryKey<T>(Dictionary<string, T> dictionary, string key)
        {
            if (TryGetActualKey(dictionary, key, out var actualKey))
                dictionary.Remove(actualKey);
        }

        private static void RenameDictionaryKey<T>(Dictionary<string, T> dictionary, string oldKey, string newKey, Action<T?, T?> merge)
        {
            if (!TryGetActualKey(dictionary, oldKey, out var actualOldKey))
                return;

            var oldValue = dictionary[actualOldKey];
            dictionary.Remove(actualOldKey);

            if (TryGetActualKey(dictionary, newKey, out var actualNewKey))
            {
                merge(dictionary[actualNewKey], oldValue);
                return;
            }

            dictionary[newKey] = oldValue;
        }

        private static bool TryGetActualKey<T>(Dictionary<string, T> dictionary, string key, out string actualKey)
        {
            foreach (var existingKey in dictionary.Keys)
            {
                if (existingKey.Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    actualKey = existingKey;
                    return true;
                }
            }

            actualKey = string.Empty;
            return false;
        }

        private static void RemoveHashSetValue(HashSet<string> set, string value)
        {
            if (TryGetActualValue(set, value, out var actualValue))
                set.Remove(actualValue);
        }

        private static void RenameHashSetValue(HashSet<string> set, string oldValue, string newValue)
        {
            if (!TryGetActualValue(set, oldValue, out var actualOldValue))
                return;

            set.Remove(actualOldValue);
            set.Add(newValue);
        }

        private static bool TryGetActualValue(HashSet<string> set, string value, out string actualValue)
        {
            foreach (var existingValue in set)
            {
                if (existingValue.Equals(value, StringComparison.OrdinalIgnoreCase))
                {
                    actualValue = existingValue;
                    return true;
                }
            }

            actualValue = string.Empty;
            return false;
        }

        private void ResetAtlasCache()
        {
            _atlasNodes = Array.Empty<AtlasNodeDescription>();
            _visibleNodes.Clear();
            ClearIslandRumourCache();
            ResetPreferredGuideDiscovery();
            ResetNavigationTargetAnchors();
            _atlasRefreshSw.Restart();
            _screenRefreshSw.Restart();
        }

        private void EnsurePreferredCacheUpToDate()
        {
            
            int h = 17;
            int enabledCount = 0;
            var groups = Settings.PreferredMapGroups;
            if (groups != null)
            {
                for (int gi = 0; gi < groups.Count; gi++)
                {
                    var g = groups[gi];
                    if (g == null || !g.Enabled) continue;
                    foreach (var key in g.Maps)
                    {
                        enabledCount++;
                        h = unchecked(h * 31 + StringComparer.OrdinalIgnoreCase.GetHashCode(key));
                    }
                    if (g.Mechanics != null)
                    {
                        foreach (var key in g.Mechanics)
                        {
                            enabledCount++;
                            h = unchecked(h * 31 + StringComparer.OrdinalIgnoreCase.GetHashCode("mechanic:" + key));
                        }
                    }
                    if (g.Rumours != null)
                    {
                        foreach (var key in g.Rumours)
                        {
                            enabledCount++;
                            h = unchecked(h * 31 + StringComparer.OrdinalIgnoreCase.GetHashCode("rumour:" + key));
                        }
                    }
                }
            }
            h = unchecked(h * 31 + enabledCount);

            if (h == _preferredCacheHash) return;
            _preferredCacheHash = h;

            
            _preferredTokensExact.Clear();
            _preferredMechanicTokensExact.Clear();
            _preferredRumourTokensExact.Clear();
            _preferredTokenToTag.Clear();
            _preferredTokenToDisplayName.Clear();
            var list = new List<string>(enabledCount);
            var mechanicList = new List<string>(enabledCount);
            var rumourList = new List<string>(enabledCount);
            if (groups != null)
            {
                for (int gi = 0; gi < groups.Count; gi++)
                {
                    var g = groups[gi];
                    if (g == null || !g.Enabled) continue;
                    foreach (var key in g.Maps)
                    {
                        var display = Utility.PreferredKeyToDisplayName(key);
                        foreach (var token in Utility.PreferredKeyToTokens(key))
                        {
                            if (token.Length == 0) continue;

                            if (_preferredTokensExact.Add(token))
                                list.Add(token);

                            SetPreferredTokenDisplay(token, display);
                        }
                    }

                    if (g.Mechanics != null)
                    {
                        foreach (var key in g.Mechanics)
                        {
                            var token = Utility.NormalizeToken(key);
                            if (token.Length == 0) continue;

                            if (_preferredMechanicTokensExact.Add(token))
                                mechanicList.Add(token);

                            SetPreferredTokenDisplay(token, key);
                        }
                    }

                    if (g.Rumours != null)
                    {
                        foreach (var key in g.Rumours)
                        {
                            var canonical = GetIslandRumourCanonicalName(key);
                            var token = Utility.NormalizeToken(canonical);
                            if (token.Length == 0) continue;

                            if (_preferredRumourTokensExact.Add(token))
                                rumourList.Add(token);

                            SetPreferredTokenDisplay(token, canonical);
                        }
                    }
                }
            }
            _preferredTokensList = list.Count == 0 ? Array.Empty<string>() : list.ToArray();
            _preferredMechanicTokensList = mechanicList.Count == 0 ? Array.Empty<string>() : mechanicList.ToArray();
            _preferredRumourTokensList = rumourList.Count == 0 ? Array.Empty<string>() : rumourList.ToArray();
        }

        private void SetPreferredTokenDisplay(string token, string? displayName)
        {
            if (string.IsNullOrWhiteSpace(token) || _preferredTokenToDisplayName.ContainsKey(token))
                return;

            var display = displayName?.Trim() ?? string.Empty;
            _preferredTokenToDisplayName[token] = display;
            _preferredTokenToTag[token] = display.Length == 0 ? "[Preferred]" : $"[Preferred {display}]";
        }

        private string GetPreferredTag(string? matchedToken)
        {
            if (matchedToken != null && matchedToken.Length != 0 && _preferredTokenToTag.TryGetValue(matchedToken, out var tag))
                return tag;
            return "[Preferred]";
        }

        private string GetPreferredDisplayName(string? matchedToken)
        {
            if (matchedToken != null && matchedToken.Length != 0 && _preferredTokenToDisplayName.TryGetValue(matchedToken, out var display))
                return display;
            return string.Empty;
        }

        private bool TryGetCachedNodeTokens(AtlasNodeDescription nd, out string nameToken, out string idToken)
        {
            nameToken = string.Empty;
            idToken = string.Empty;

            var elem = nd.Element;
            if (elem is null) return false;

            
            long addr = elem.Address;
            _nodeTokenCacheFrame++;

            if (_nodeTokenCache.TryGetValue(addr, out var cached))
            {
                nameToken = cached.NameToken;
                idToken = cached.IdToken;
                
                _nodeTokenCache[addr] = new NodeTokenCache(nameToken, idToken, _nodeTokenCacheFrame);
                return nameToken.Length != 0 || idToken.Length != 0;
            }

            
            if (Utility.TryGetAnyMapName(nd, out var anyName) && !string.IsNullOrWhiteSpace(anyName))
                nameToken = Utility.NormalizeToken(anyName);

            if (Utility.TryGetNodeId(nd, out var nid) && !string.IsNullOrWhiteSpace(nid))
                idToken = Utility.NormalizeToken(nid);

            if (_nodeTokenCache.Count >= NodeTokenCacheMaxEntries)
            {
                
                
                var cutoff = _nodeTokenCacheFrame - 1024;
                var toRemove = new List<long>(64);
                foreach (var kv in _nodeTokenCache)
                {
                    if (kv.Value.LastSeenFrame < cutoff) toRemove.Add(kv.Key);
                }
                for (int i = 0; i < toRemove.Count; i++) _nodeTokenCache.Remove(toRemove[i]);
                if (_nodeTokenCache.Count >= NodeTokenCacheMaxEntries)
                    _nodeTokenCache.Clear();
            }

            _nodeTokenCache[addr] = new NodeTokenCache(nameToken, idToken, _nodeTokenCacheFrame);
            return nameToken.Length != 0 || idToken.Length != 0;
        }

        public override void Tick()
        {
            using var tickProfile = ProfileScope("Tick total");
            _atlasPanel = GameController?.IngameState?.IngameUi?.WorldMap?.AtlasPanel;
            if (_atlasPanel == null || !_atlasPanel.IsVisible)
            {
                if (_islandRumourSnapshots.Count != 0 ||
                    _islandRumourCameraAnchorNode != null)
                {
                    ClearIslandRumourCache();
                }
                return;
            }

            
            
            HandleHotkeys();

            if (Settings.HighlightPreferredMaps.Value)
                EnsurePreferredCacheUpToDate();

            
            UpdateViewportSize();

            using (ProfileScope("Island Rumours cache"))
            {
                UpdateIslandRumourCache();
            }

            
            using (ProfileScope("Preferred guide discovery"))
            {
                UpdatePreferredGuideDiscovery();
            }

            
            TickNavigationCoordStabilityDebug();

            if (_atlasRefreshSw.ElapsedMilliseconds > Settings.AtlasRefreshMs.Value)
            {
                _atlasNodes = _atlasPanel.Descriptions?.ToArray() ?? Array.Empty<AtlasNodeDescription>();
                ResetVisibleCacheBuild();
                _atlasRefreshSw.Restart();

                
                using (ProfileScope("Refresh graph caches"))
                {
                    RefreshGraphCaches();
                    SyncSelectedWaypoint();
                }
            }

            
            
            int effectiveScreenRefreshMs = IsLowLatencyVisibleCacheEnabled()
                ? Math.Min(Settings.ScreenRefreshMs.Value, 50)
                : Settings.ShowLabels.Value
                    ? Math.Min(Settings.ScreenRefreshMs.Value, 250)
                    : Settings.ScreenRefreshMs.Value;

            if (_visibleCacheBuildInProgress || _screenRefreshSw.ElapsedMilliseconds > effectiveScreenRefreshMs)
            {
                
                
                if (_nodeTokenCacheFrame > 10_000)
                {
                    _nodeTokenCacheFrame = 0;
                    _nodeTokenCache.Clear();
                }

                
                
                bool visibleCachesReady;
                using (ProfileScope("Rebuild visible caches"))
                {
                    visibleCachesReady = RebuildVisibleCaches();
                }

                if (visibleCachesReady)
                {
                    
                    using (ProfileScope("Recompute shortest path"))
                    {
                        RecomputeShortestPathIfNeeded();
                    }
                    _screenRefreshSw.Restart();
                }
            }
        }
    }
}
