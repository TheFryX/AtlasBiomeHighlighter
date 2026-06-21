using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using ImGuiNET;

namespace AtlasBiomeHighlighter
{
    public partial class AtlasBiomeHighlighter
    {
        private const int FavoriteMapNameInputLength = 96;

        private void DrawFavoriteMapsPanel()
        {
            EnsureFavoriteMapStorage();

            // Favorite auto-waypoints must keep updating even when the Atlas Maps / Mechanic
            // navigator sections are collapsed. The actual cache builders use a small per-frame
            // budget, so calling this here advances the background caches without depending on
            // the visible tables being open.
            SyncFavoriteMapWaypointsFromCurrentAtlasRows(removeStale: true);

            int selectedCount = Settings.FavoriteWaypointMaps.Count;
            int autoCount = CountAutoFavoriteWaypoints();

            // Keep the tree node ID stable. The selected/auto counts change while the atlas cache updates;
            // if they are part of the ImGui ID, Dear ImGui treats the header as a different node and
            // can reopen/recollapse it unexpectedly. Only direct user interaction should change this state.
            const ImGuiTreeNodeFlags headerFlags = ImGuiTreeNodeFlags.None;
            bool isOpen = ImGui.TreeNodeEx("Favorite Maps###favorite_maps_panel", headerFlags);
            ImGui.SameLine();
            ImGui.TextDisabled($"{selectedCount} selected, {autoCount} auto");

            if (!isOpen)
                return;

            ImGui.TextDisabled("List comes from Preferred Maps, including Map Content / Mechanics. Auto-track uses the Atlas Navigator caches.");

            bool autoTrack = Settings.FavoriteMapsAutoTrack.Value;
            if (ImGui.Checkbox("Auto-track favorite maps", ref autoTrack))
            {
                Settings.FavoriteMapsAutoTrack.Value = autoTrack;
                if (autoTrack)
                    SyncFavoriteMapWaypointsFromCurrentAtlasRows(removeStale: false);
            }

            ImGui.SameLine();
            bool route = Settings.FavoriteMapsRoute.Value;
            if (ImGui.Checkbox("Route", ref route))
            {
                Settings.FavoriteMapsRoute.Value = route;
                SyncFavoriteMapWaypointsFromCurrentAtlasRows(removeStale: false);
            }

            ImGui.SameLine();
            DrawColorEdit("Favorite color##favorite_maps_waypoint_color", Settings.FavoriteMapWaypointColor.Value, c =>
            {
                Settings.FavoriteMapWaypointColor.Value = c;
                RecolorAutoFavoriteWaypoints(c);
            }, false);

            ImGui.AlignTextToFramePadding();
            ImGui.Text("Max Steps:");
            ImGui.SameLine();
            int maxSteps = Settings.FavoriteMapsMaxSteps.Value;
            ImGui.SetNextItemWidth(58);
            if (ImGui.InputInt("##favorite_max_steps", ref maxSteps, 0, 0))
            {
                Settings.FavoriteMapsMaxSteps.Value = Math.Clamp(maxSteps, 1, 999);
                SyncFavoriteMapWaypointsFromCurrentAtlasRows(removeStale: true);
            }

            ImGui.SameLine();
            int maxAuto = Settings.FavoriteMapsMaxAutoWaypoints.Value;
            ImGui.Text("Max Auto:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(58);
            if (ImGui.InputInt("##favorite_max_auto", ref maxAuto, 0, 0))
            {
                Settings.FavoriteMapsMaxAutoWaypoints.Value = Math.Clamp(maxAuto, 1, 250);
                SyncFavoriteMapWaypointsFromCurrentAtlasRows(removeStale: true);
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("Clear auto##favorite_maps_clear_auto"))
                RemoveAutoFavoriteWaypoints();

            ImGui.Text("Search:");
            ImGui.SameLine();
            var searchWidth = Math.Max(180f, ImGui.GetContentRegionAvail().X * 0.35f);
            ImGui.SetNextItemWidth(searchWidth);
            ImGui.InputText("##favorite_maps_search", ref _favoriteMapSearch, FavoriteMapNameInputLength);

            ImGui.SameLine();
            if (ImGui.SmallButton("Clear selected##favorite_maps_clear_selected"))
            {
                Settings.FavoriteWaypointMaps.Clear();
                RemoveAutoFavoriteWaypoints();
            }

            DrawFavoriteMapSelectionTable();
            ImGui.TreePop();
        }

        private void DrawFavoriteMapSelectionTable()
        {
            var sourceNames = BuildFavoriteMapSourceNames();
            var searchToken = Utility.NormalizeToken(_favoriteMapSearch);
            var tableFlags =
                ImGuiTableFlags.RowBg |
                ImGuiTableFlags.BordersInnerH |
                ImGuiTableFlags.ScrollY |
                ImGuiTableFlags.SizingStretchProp |
                ImGuiTableFlags.Resizable;

            var avail = ImGui.GetContentRegionAvail();
            var tableHeight = Math.Max(150f, avail.Y - ImGui.GetStyle().ItemSpacing.Y);

            if (!ImGui.BeginTable("##favorite_maps_table", 2, tableFlags, new Vector2(0, tableHeight)))
                return;

            ImGui.TableSetupColumn("Fav", ImGuiTableColumnFlags.WidthFixed, 42);
            ImGui.TableSetupColumn("Target", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();

            int shown = 0;
            for (int i = 0; i < sourceNames.Count; i++)
            {
                var mapName = sourceNames[i];
                if (searchToken.Length != 0 && !Utility.NormalizeToken(mapName).Contains(searchToken, StringComparison.Ordinal))
                    continue;

                shown++;
                ImGui.PushID(i);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();

                bool selected = Settings.FavoriteWaypointMaps.Contains(mapName);
                if (ImGui.Checkbox("##favorite_map_check", ref selected))
                {
                    if (selected)
                        Settings.FavoriteWaypointMaps.Add(mapName);
                    else
                    {
                        Settings.FavoriteWaypointMaps.Remove(mapName);
                        RemoveAutoFavoriteWaypointsForMap(mapName);
                    }

                    SyncFavoriteMapWaypointsFromCurrentAtlasRows(removeStale: true);
                }

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(mapName);
                ImGui.PopID();
            }

            if (shown == 0)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextDisabled("-");
                ImGui.TableNextColumn();
                ImGui.TextDisabled("No targets match the search.");
            }

            ImGui.EndTable();
        }

        private List<string> BuildFavoriteMapSourceNames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddPreferredMapName(string? key)
            {
                var display = Utility.PreferredKeyToDisplayName(key ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(display))
                    names.Add(display);
            }

            void AddRawName(string? name)
            {
                var display = name?.Trim();
                if (!string.IsNullOrWhiteSpace(display))
                    names.Add(display);
            }

            if (Settings.PreferredMaps != null)
            {
                foreach (var key in Settings.PreferredMaps.Keys)
                    AddPreferredMapName(key);
            }

            foreach (var mechanic in Utility.MapContentMechanics)
                AddRawName(mechanic.Name);

            var groups = Settings.PreferredMapGroups;
            if (groups != null)
            {
                for (int gi = 0; gi < groups.Count; gi++)
                {
                    var group = groups[gi];
                    if (group == null)
                        continue;

                    if (group.Maps != null)
                    {
                        foreach (var key in group.Maps)
                            AddPreferredMapName(key);
                    }

                    if (group.Mechanics != null)
                    {
                        foreach (var key in group.Mechanics)
                            AddRawName(key);
                    }
                }
            }

            var result = names.ToList();
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        private void SyncFavoriteMapWaypointsFromCurrentAtlasRows(bool removeStale)
        {
            EnsureFavoriteMapStorage();

            if (!Settings.FavoriteMapsAutoTrack.Value || Settings.FavoriteWaypointMaps.Count == 0)
            {
                if (removeStale)
                    RemoveAutoFavoriteWaypoints();
                return;
            }

            // Always advance the Atlas Maps cache here. Previously this cache was only
            // processed while the Atlas Maps table was expanded, which meant Favorite Maps
            // could stop auto-adding waypoints when that UI section was collapsed.
            ProcessWaypointAtlasCacheBudget();
            if (!_waypointAtlasBuildActive)
                SortWaypointAtlasRowsBySteps();

            bool hasSelectedMechanics = HasSelectedFavoriteMechanics();
            if (hasSelectedMechanics)
            {
                // Mechanics are also allowed to build in the background for Favorite Maps.
                // This keeps selected mechanic favorites working even when the Mechanic table
                // in Atlas Navigator is collapsed.
                ProcessWaypointMechanicCacheBudget();
                if (!_waypointMechanicBuildActive)
                    SortWaypointMechanicRowsBySteps();
            }

            if (_waypointAtlasRows.Count == 0 && (!hasSelectedMechanics || _waypointMechanicRows.Count == 0))
                return;

            int maxSteps = Math.Clamp(Settings.FavoriteMapsMaxSteps.Value, 1, 999);
            int maxAuto = Math.Clamp(Settings.FavoriteMapsMaxAutoWaypoints.Value, 1, 250);
            bool canRemoveStale = removeStale &&
                                  !_waypointAtlasBuildActive &&
                                  (!hasSelectedMechanics || !_waypointMechanicBuildActive) &&
                                  string.IsNullOrEmpty(_waypointAtlasCachedSearch) &&
                                  (!hasSelectedMechanics || string.IsNullOrEmpty(_waypointMechanicCachedSearch));
            var desiredCoords = canRemoveStale
                ? new HashSet<(int x, int y)>()
                : null;
            var handledCoords = new HashSet<(int x, int y)>();

            int autoCount = 0;
            bool changed = false;

            for (int i = 0; i < _waypointAtlasRows.Count; i++)
            {
                var row = _waypointAtlasRows[i];
                if (!TryGetFavoriteMapMatch(row.Name, out var favoriteName))
                    continue;

                if (!TryGetAtlasRouteSteps(row.X, row.Y, out var steps) || steps < 1 || steps > maxSteps)
                    continue;

                var coord = (row.X, row.Y);
                desiredCoords?.Add(coord);
                if (!handledCoords.Add(coord))
                    continue;

                if (CountAutoFavoriteWaypoints() >= maxAuto && !HasWaypointAt(coord))
                    continue;

                int before = Settings.Waypoints.Count;
                AddWaypoint(row.Node, row.Name, Settings.FavoriteMapsRoute.Value, true, favoriteName, Settings.FavoriteMapWaypointColor.Value);
                if (Settings.Waypoints.Count != before)
                    changed = true;

                autoCount++;
                if (autoCount >= maxAuto)
                    break;
            }

            if (hasSelectedMechanics && autoCount < maxAuto)
            {
                for (int i = 0; i < _waypointMechanicRows.Count; i++)
                {
                    var row = _waypointMechanicRows[i];
                    if (!TryGetFavoriteMechanicMatch(row.Mechanics, out var favoriteName))
                        continue;

                    if (!TryGetAtlasRouteSteps(row.X, row.Y, out var steps) || steps < 1 || steps > maxSteps)
                        continue;

                    var coord = (row.X, row.Y);
                    desiredCoords?.Add(coord);
                    if (!handledCoords.Add(coord))
                        continue;

                    if (CountAutoFavoriteWaypoints() >= maxAuto && !HasWaypointAt(coord))
                        continue;

                    var waypointName = string.IsNullOrWhiteSpace(row.MapName)
                        ? favoriteName
                        : $"{row.MapName} - {favoriteName}";

                    int before = Settings.Waypoints.Count;
                    AddWaypoint(row.Node, waypointName, Settings.FavoriteMapsRoute.Value, true, favoriteName, Settings.FavoriteMapWaypointColor.Value);
                    if (Settings.Waypoints.Count != before)
                        changed = true;

                    autoCount++;
                    if (autoCount >= maxAuto)
                        break;
                }
            }

            if (desiredCoords != null)
                changed |= RemoveStaleAutoFavoriteWaypoints(desiredCoords);

            if (changed)
                SyncSelectedWaypoint();
        }

        private bool TryGetFavoriteMapMatch(string detectedMapName, out string favoriteName)
        {
            favoriteName = string.Empty;
            if (string.IsNullOrWhiteSpace(detectedMapName) || Settings.FavoriteWaypointMaps == null || Settings.FavoriteWaypointMaps.Count == 0)
                return false;

            if (Settings.FavoriteWaypointMaps.Contains(detectedMapName))
            {
                favoriteName = detectedMapName;
                return true;
            }

            var detectedToken = Utility.PreferredKeyToToken(detectedMapName);
            if (detectedToken.Length == 0)
                detectedToken = Utility.NormalizeToken(detectedMapName);

            foreach (var selected in Settings.FavoriteWaypointMaps)
            {
                var selectedToken = Utility.PreferredKeyToToken(selected);
                if (selectedToken.Length == 0)
                    selectedToken = Utility.NormalizeToken(selected);

                if (selectedToken.Length != 0 && selectedToken.Equals(detectedToken, StringComparison.Ordinal))
                {
                    favoriteName = selected;
                    return true;
                }
            }

            return false;
        }

        private bool TryGetFavoriteMechanicMatch(string mechanicText, out string favoriteName)
        {
            favoriteName = string.Empty;
            if (string.IsNullOrWhiteSpace(mechanicText) || Settings.FavoriteWaypointMaps == null || Settings.FavoriteWaypointMaps.Count == 0)
                return false;

            var mechanicParts = mechanicText
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Trim())
                .Where(part => part.Length != 0)
                .ToArray();

            if (mechanicParts.Length == 0)
                return false;

            foreach (var selected in Settings.FavoriteWaypointMaps)
            {
                if (!IsKnownFavoriteMechanicName(selected))
                    continue;

                var selectedToken = Utility.NormalizeToken(selected);
                if (selectedToken.Length == 0)
                    continue;

                for (int i = 0; i < mechanicParts.Length; i++)
                {
                    var mechanicToken = Utility.NormalizeToken(mechanicParts[i]);
                    if (mechanicToken.Length != 0 && mechanicToken.Equals(selectedToken, StringComparison.Ordinal))
                    {
                        favoriteName = selected;
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsKnownFavoriteMechanicName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            for (int i = 0; i < Utility.MapContentMechanics.Length; i++)
            {
                if (string.Equals(Utility.MapContentMechanics[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private bool HasSelectedFavoriteMechanics()
        {
            if (Settings.FavoriteWaypointMaps == null || Settings.FavoriteWaypointMaps.Count == 0)
                return false;

            foreach (var selected in Settings.FavoriteWaypointMaps)
            {
                if (IsKnownFavoriteMechanicName(selected))
                    return true;
            }

            return false;
        }

        private static string GetFavoriteRemovalToken(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            if (IsKnownFavoriteMechanicName(name))
                return Utility.NormalizeToken(name);

            var token = Utility.PreferredKeyToToken(name);
            return token.Length == 0 ? Utility.NormalizeToken(name) : token;
        }

        private bool HasWaypointAt((int x, int y) coord)
        {
            var wps = Settings.Waypoints;
            for (int i = 0; i < wps.Count; i++)
            {
                if (wps[i].X == coord.x && wps[i].Y == coord.y)
                    return true;
            }

            return false;
        }

        private int CountAutoFavoriteWaypoints()
        {
            var wps = Settings.Waypoints;
            int count = 0;
            for (int i = 0; i < wps.Count; i++)
            {
                if (wps[i].AutoFavoriteMap)
                    count++;
            }

            return count;
        }

        private void RecolorAutoFavoriteWaypoints(Color color)
        {
            var wps = Settings.Waypoints;
            int argb = color.ToArgb();
            for (int i = 0; i < wps.Count; i++)
            {
                var wp = wps[i];
                if (!wp.AutoFavoriteMap || wp.ColorArgb == argb)
                    continue;

                wp.ColorArgb = argb;
                wps[i] = wp;
            }
        }

        private void RemoveAutoFavoriteWaypoints()
        {
            var wps = Settings.Waypoints;
            bool removedSelected = false;
            for (int i = wps.Count - 1; i >= 0; i--)
            {
                if (!wps[i].AutoFavoriteMap)
                    continue;

                removedSelected |= wps[i].Selected;
                wps.RemoveAt(i);
            }

            if (removedSelected)
                SyncSelectedWaypoint();
        }

        private void RemoveAutoFavoriteWaypointsForMap(string mapName)
        {
            var token = GetFavoriteRemovalToken(mapName);
            var wps = Settings.Waypoints;
            bool removedSelected = false;

            for (int i = wps.Count - 1; i >= 0; i--)
            {
                var wp = wps[i];
                if (!wp.AutoFavoriteMap)
                    continue;

                var wpToken = GetFavoriteRemovalToken(string.IsNullOrWhiteSpace(wp.FavoriteMapName) ? wp.Name : wp.FavoriteMapName);
                if (!string.Equals(token, wpToken, StringComparison.Ordinal))
                    continue;

                removedSelected |= wp.Selected;
                wps.RemoveAt(i);
            }

            if (removedSelected)
                SyncSelectedWaypoint();
        }

        private bool RemoveStaleAutoFavoriteWaypoints(HashSet<(int x, int y)> desiredCoords)
        {
            var wps = Settings.Waypoints;
            bool changed = false;
            bool removedSelected = false;

            for (int i = wps.Count - 1; i >= 0; i--)
            {
                var wp = wps[i];
                if (!wp.AutoFavoriteMap)
                    continue;

                if (desiredCoords.Contains((wp.X, wp.Y)) && TryGetFavoriteMapMatch(string.IsNullOrWhiteSpace(wp.FavoriteMapName) ? wp.Name : wp.FavoriteMapName, out _))
                    continue;

                removedSelected |= wp.Selected;
                wps.RemoveAt(i);
                changed = true;
            }

            if (removedSelected)
                SyncSelectedWaypoint();

            return changed;
        }

        private void EnsureFavoriteMapStorage()
        {
            if (Settings.FavoriteWaypointMaps == null)
                Settings.FavoriteWaypointMaps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
