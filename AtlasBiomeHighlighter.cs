using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ExileCore2;
using ExileCore2.PoEMemory.Elements.AtlasElements;

namespace AtlasBiomeHighlighter
{
    public partial class AtlasBiomeHighlighter : BaseSettingsPlugin<AtlasBiomeSettings>
    {
        private ExileCore2.PoEMemory.Elements.AtlasElements.AtlasPanel? _atlasPanel;
        private AtlasNodeDescription[] _atlasNodes = Array.Empty<AtlasNodeDescription>();
        private readonly System.Collections.Generic.List<AtlasNodeDescription> _visibleNodes = new();

        private readonly Stopwatch _atlasRefreshSw = new();
        private readonly Stopwatch _screenRefreshSw = new();

        private const int BorderX = 1920;
        private const int BorderY = 1080;

        public override bool Initialise()
        {
            _atlasRefreshSw.Start();
            _screenRefreshSw.Start();
            ResetAtlasCache();
            return true;
        }

        private void ResetAtlasCache()
        {
            _atlasNodes = Array.Empty<AtlasNodeDescription>();
            _visibleNodes.Clear();
            _atlasRefreshSw.Restart();
            _screenRefreshSw.Restart();
        }

        public override void Tick()
        {
            _atlasPanel = GameController?.IngameState?.IngameUi?.WorldMap?.AtlasPanel;
            if (_atlasPanel == null || !_atlasPanel.IsVisible) return;

            if (_atlasRefreshSw.ElapsedMilliseconds > Settings.AtlasRefreshMs.Value)
            {
                _atlasNodes = _atlasPanel.Descriptions?.ToArray() ?? Array.Empty<AtlasNodeDescription>();
                _atlasRefreshSw.Restart();
            }

            if (_screenRefreshSw.ElapsedMilliseconds > Settings.ScreenRefreshMs.Value)
            {
                _visibleNodes.Clear();
                foreach (var nd in _atlasNodes)
                {
                    if (nd?.Element is null) continue;
                    if (Utility.IsInScreen(nd, BorderX, BorderY))
                        _visibleNodes.Add(nd);
                }
                _screenRefreshSw.Restart();
            }
        }
    }
}
