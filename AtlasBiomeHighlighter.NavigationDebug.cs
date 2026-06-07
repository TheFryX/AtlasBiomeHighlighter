using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using System.Diagnostics;
using ExileCore2.PoEMemory.Elements.AtlasElements;

namespace AtlasBiomeHighlighter
{
    public partial class AtlasBiomeHighlighter
    {
        private long _navigationDebugLastWriteMs;
        private long _navigationDebugSeq;

        private long _coordStabilityLastWriteMs;
        private long _coordStabilitySeq;

        private void TickNavigationCoordStabilityDebug()
        {
            if (!NavigationDebugEnabled())
                return;

            long now = Environment.TickCount64;
            if (now - _coordStabilityLastWriteMs < 900)
                return;
            _coordStabilityLastWriteMs = now;

            try
            {
                var sb = new StringBuilder(32 * 1024);
                sb.AppendLine("============================================================");
                sb.AppendLine($"Stage50 Coord Stability Snapshot #{++_coordStabilitySeq}");
                sb.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                sb.AppendLine($"Viewport: {BorderX}x{BorderY}");

                var atlasCenter = _atlasPanel != null
                    ? new Vector2(_atlasPanel.Center.X, _atlasPanel.Center.Y)
                    : new Vector2(BorderX / 2f, BorderY / 2f);
                var screenCenter = new Vector2(BorderX / 2f, BorderY / 2f);
                sb.AppendLine($"AtlasCenter: {atlasCenter.X:0.##},{atlasCenter.Y:0.##}");
                sb.AppendLine($"ScreenCenter: {screenCenter.X:0.##},{screenCenter.Y:0.##}");

                if (_atlasPanel != null)
                {
                    sb.AppendLine("[AtlasPanel]");
                    sb.AppendLine($"Address: 0x{_atlasPanel.Address:X}");
                    sb.AppendLine($"Center: {_atlasPanel.Center}");
                    sb.AppendLine($"Position: {_atlasPanel.Position}");
                    sb.AppendLine($"ScrollOffset: {_atlasPanel.ScrollOffset}");
                    sb.AppendLine($"Scale: {_atlasPanel.Scale}");
                    sb.AppendLine($"Visible: {_atlasPanel.IsVisible} local={_atlasPanel.IsVisibleLocal} active={_atlasPanel.IsActive}");
                }

                float camX = 0, camY = 0, camZ = 0, m41 = 0, m42 = 0, m43 = 0, m44 = 0;
                bool hasCamera = false;
                try
                {
                    var cam = _atlasPanel?.Camera;
                    if (cam != null)
                    {
                        hasCamera = true;
                        var pos = cam.Position;
                        camX = pos.X; camY = pos.Y; camZ = pos.Z;
                        var mat = cam.Snapshot.Matrix;
                        m41 = mat.M41; m42 = mat.M42; m43 = mat.M43; m44 = mat.M44;
                        sb.AppendLine("[Camera]");
                        sb.AppendLine($"Address: 0x{cam.Address:X}");
                        sb.AppendLine($"Position: {camX:0.###},{camY:0.###},{camZ:0.###}");
                        sb.AppendLine($"Matrix.M41/M42/M43/M44: {m41:0.###},{m42:0.###},{m43:0.###},{m44:0.###}");
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine("[Camera] ERROR: " + ex.Message);
                }

                try
                {
                    var helper = _atlasPanel?.CameraHelper;
                    if (helper != null)
                    {
                        int panOffset = GetStage37PanOffset(helper, new List<string>());
                        sb.AppendLine("[PanOffset]");
                        sb.AppendLine($"CameraHelper.Address: 0x{helper.Address:X}");
                        sb.AppendLine($"PanOffset: {panOffset} / 0x{panOffset:X}");
                        if (panOffset > 0)
                        {
                            bool close;
                            var h = GetStage35WritableProcessHandle(new List<string>(), out close);
                            try
                            {
                                if (h != IntPtr.Zero && ReadProcessBytesStage32(h, helper.Address + panOffset, 0x40, out var bytes, out var read, out var err) && bytes != null && bytes.Length >= 0x30)
                                {
                                    sb.AppendLine($"PanXY: {ReadFloatFromBytes(bytes, 0):0.###},{ReadFloatFromBytes(bytes, 4):0.###} active={ReadFloatFromBytes(bytes, 0x2C):0.###} read={read} err={err}");
                                }
                                else
                                {
                                    sb.AppendLine("PanXY: <read failed>");
                                }
                            }
                            finally
                            {
                                if (close && h != IntPtr.Zero) CloseHandle(h);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine("[PanOffset] ERROR: " + ex.Message);
                }

                sb.AppendLine("[Tracked nodes]");
                sb.AppendLine("Columns: idx coord name addr center pos center-screenCenter center-atlasCenter center-cameraM42/M43 visible suspicious source");

                var seen = new HashSet<(int x, int y)>();
                int written = 0;

                void AddNodeLine(AtlasNodeDescription? n, string source)
                {
                    if (n == null || n.Element == null || written >= 48)
                        return;
                    var c = n.Coordinate;
                    var key = (c.X, c.Y);
                    if (!seen.Add(key))
                        return;

                    var e = n.Element;
                    var center = new Vector2(e.Center.X, e.Center.Y);
                    var pos = e.Position;
                    var fromScreen = center - screenCenter;
                    var fromAtlas = center - atlasCenter;
                    var fromCam = hasCamera ? new Vector2(center.X - m42, center.Y - m43) : Vector2.Zero;
                    bool suspicious = IsNavigationTargetSuspicious(center);
                    sb.AppendLine($"{written:00} coord={c.X},{c.Y} name='{SafeNodeName(n)}' addr=0x{e.Address:X} center={center.X:0.##},{center.Y:0.##} pos={pos.X:0.##},{pos.Y:0.##} dScreen={fromScreen.X:0.##},{fromScreen.Y:0.##} dAtlas={fromAtlas.X:0.##},{fromAtlas.Y:0.##} dCamM42M43={fromCam.X:0.##},{fromCam.Y:0.##} vis={e.IsVisible}/{e.IsVisibleLocal}/{e.IsActive} suspicious={suspicious} source={source}");
                    written++;
                }

                for (int i = 0; i < _preferredGuideNodes.Count && written < 16; i++)
                    AddNodeLine(_preferredGuideNodes[i], "preferred");

                if (_selectedWaypointCoord.HasValue && _nodeByCoord != null && _nodeByCoord.TryGetValue(_selectedWaypointCoord.Value, out var selected))
                    AddNodeLine(selected, "selectedWaypoint");

                
                int total = _atlasNodes?.Length ?? 0;
                int step = total <= 0 ? 1 : Math.Max(1, total / 28);
                for (int i = 0; i < total && written < 48; i += step)
                    AddNodeLine(_atlasNodes[i], "atlasSample");

                if (written == 0)
                    sb.AppendLine("<no nodes sampled>");

                sb.AppendLine("[Interpretation]");
                sb.AppendLine("If the same coord/address changes center while PanXY/camera changes, Element.Center is screen-space, not stable atlas-space.");
                sb.AppendLine("Compare dCamM42M43 and dScreen across snapshots while panning to identify the stable transform basis.");

                var path = Path.Combine(DirectoryFullName, "AtlasBiomeHighlighter.CoordStabilityDebug.log");
                File.AppendAllText(path, sb.ToString());
            }
            catch
            {
                
            }
        }


        private bool NavigationDebugEnabled()
        {
            try { return Settings?.DebugNavigationTargets?.Value == true; }
            catch { return false; }
        }

        private void AppendNavigationDebug(string source, AtlasNodeDescription? node, Vector2 origin, Vector2 target, string note, bool force = false)
        {
            if (!NavigationDebugEnabled())
                return;

            long now = Environment.TickCount64;
            if (!force && now - _navigationDebugLastWriteMs < 350)
                return;
            _navigationDebugLastWriteMs = now;

            try
            {
                var sb = new StringBuilder(4096);
                sb.AppendLine("============================================================");
                sb.AppendLine($"Seq: {++_navigationDebugSeq}");
                sb.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                sb.AppendLine($"Source: {source}");
                sb.AppendLine($"Note: {note}");
                sb.AppendLine($"Viewport: {BorderX}x{BorderY}");
                sb.AppendLine($"Origin: {origin.X:0.##},{origin.Y:0.##}");
                sb.AppendLine($"Target: {target.X:0.##},{target.Y:0.##}");
                var delta = target - origin;
                sb.AppendLine($"Delta target-origin: {delta.X:0.##},{delta.Y:0.##} dist={delta.Length():0.##}");
                sb.AppendLine($"TargetOnScreen: {target.X >= 0 && target.X <= BorderX && target.Y >= 0 && target.Y <= BorderY}");
                sb.AppendLine($"TargetSuspicious: {IsNavigationTargetSuspicious(target)}");

                if (_atlasPanel != null)
                {
                    sb.AppendLine("[AtlasPanel]");
                    sb.AppendLine($"Center: {_atlasPanel.Center}");
                    sb.AppendLine($"Position: {_atlasPanel.Position}");
                    sb.AppendLine($"ScrollOffset: {_atlasPanel.ScrollOffset}");
                    sb.AppendLine($"Scale: {_atlasPanel.Scale}");
                    sb.AppendLine($"Visible: {_atlasPanel.IsVisible} local={_atlasPanel.IsVisibleLocal} active={_atlasPanel.IsActive}");
                }

                if (node != null)
                {
                    var c = node.Coordinate;
                    sb.AppendLine("[Node]");
                    sb.AppendLine($"Coord: {c.X},{c.Y}");
                    sb.AppendLine($"Name: {SafeNodeName(node)}");
                    sb.AppendLine($"Area.Id: {SafeText(() => node.Element?.Area?.Id)}");
                    sb.AppendLine($"Area.Name: {SafeText(() => node.Element?.Area?.Name)}");
                    if (node.Element != null)
                    {
                        sb.AppendLine($"Element.Address: 0x{node.Element.Address:X}");
                        sb.AppendLine($"Element.Center: {node.Element.Center}");
                        sb.AppendLine($"Element.Position: {node.Element.Position}");
                        sb.AppendLine($"Element.ScrollOffset: {node.Element.ScrollOffset}");
                        sb.AppendLine($"Element.Scale: {node.Element.Scale}");
                        sb.AppendLine($"Element.Size: {node.Element.Width}x{node.Element.Height}");
                        sb.AppendLine($"Element.Visible: {node.Element.IsVisible} local={node.Element.IsVisibleLocal} active={node.Element.IsActive}");
                        sb.AppendLine($"Element.Path: {SafeText(() => node.Element.PathFromRoot)}");
                    }

                    if (_nodeByCoord != null && _nodeByCoord.TryGetValue((c.X, c.Y), out var fresh) && fresh != null)
                    {
                        sb.AppendLine("[NodeByCoord fresh]");
                        sb.AppendLine($"SameRef: {object.ReferenceEquals(node, fresh)}");
                        sb.AppendLine($"FreshName: {SafeNodeName(fresh)}");
                        if (fresh.Element != null)
                        {
                            sb.AppendLine($"Fresh.Address: 0x{fresh.Element.Address:X}");
                            sb.AppendLine($"Fresh.Center: {fresh.Element.Center}");
                            sb.AppendLine($"Fresh.Visible: {fresh.Element.IsVisible} local={fresh.Element.IsVisibleLocal} active={fresh.Element.IsActive}");
                            sb.AppendLine($"Fresh.Path: {SafeText(() => fresh.Element.PathFromRoot)}");
                        }
                    }
                    else
                    {
                        sb.AppendLine("[NodeByCoord fresh] missing");
                    }
                }

                try
                {
                    var cam = _atlasPanel?.Camera;
                    if (cam != null)
                    {
                        sb.AppendLine("[Camera]");
                        sb.AppendLine($"Address: 0x{cam.Address:X}");
                        sb.AppendLine($"Position: {cam.Position}");
                        var snap = cam.Snapshot;
                        var m = snap.Matrix;
                        sb.AppendLine($"Snapshot.Matrix.M41/M42/M43/M44: {m.M41:0.###},{m.M42:0.###},{m.M43:0.###},{m.M44:0.###}");
                    }
                }
                catch { }

                var path = Path.Combine(DirectoryFullName, "AtlasBiomeHighlighter.NavigationDebug.log");
                File.AppendAllText(path, sb.ToString());
            }
            catch
            {
                
            }
        }

        private bool IsNavigationTargetSuspicious(Vector2 target)
        {
            if (!float.IsFinite(target.X) || !float.IsFinite(target.Y)) return true;
            float limitX = Math.Max(BorderX * 8f, 12000f);
            float limitY = Math.Max(BorderY * 8f, 12000f);
            if (Math.Abs(target.X) > limitX || Math.Abs(target.Y) > limitY) return true;
            return false;
        }
    }
}
