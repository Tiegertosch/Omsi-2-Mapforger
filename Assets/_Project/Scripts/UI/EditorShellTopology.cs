using System;
using System.Collections.Generic;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The topology view (contour lines) in the shell: a button in the right group of the bottom bar that switches the lines on and
    /// off (a click) and opens their settings (a right click, or the Menu key), the same switch in the road tool, the height under
    /// the pointer in the status line while the lines are on, and the legend. The lines themselves are drawn by the ground renderer
    /// (<c>MapGroundRendererContours</c>); the state lives in <see cref="OmsiContourSettings"/>, for the session only: it starts off
    /// (decision 7A) and stays through a change of the map, as the textures switch does.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        // The fixed spacings of the popup, in metres; the range the settings allow is 0.25 to 10 (OmsiContourRules).
        private static readonly double[] TopologySpacings = { 0.25, 0.5, 1.0, 2.0, 5.0, 10.0 };

        private Button topologyButton;
        private VisualElement topologyPopup;
        private Label topologyHeightLabel;
        private Button topologyAutoChoice;
        private readonly List<KeyValuePair<double, Button>> topologySpacingChoices = new List<KeyValuePair<double, Button>>();
        private Toggle topologyBandSwitch;
        private Label topologySpacingLegend;
        private Label topologyMajorLegend;
        private Label topologyRangeLegend;
        private VisualElement topologyBandScale;
        private Label topologyBandLow;
        private Label topologyBandHigh;
        private bool topologyHooked;
        private bool topologyFlatPending;
        private int topologyFlatFrame;
        private int topologyShownTenths = int.MinValue;
        private static Texture2D topologyBandTexture;

        private OmsiContourSettings TopologySettings => groundRenderer != null ? groundRenderer.ContourSettings : null;

        private bool TopologyOn => TopologySettings != null && TopologySettings.Enabled;

        private bool TopologyPopupOpen => topologyPopup != null && topologyPopup.style.display == DisplayStyle.Flex;

        /// <summary>
        /// Makes the round button for the bottom bar. A click switches the lines; a right click or the Menu key (Shift+F10) opens the
        /// settings, as a right click opens the menu of an element elsewhere in the program. It does not belong to the lists of the
        /// bar (<see cref="barButtons"/>), so the popup logic never lights or darkens it.
        /// </summary>
        private Button CreateTopologyButton()
        {
            topologyButton = new Button(() => SetTopology(!TopologyOn, true));
            topologyButton.AddToClassList("bar-round-button");
            LocBindings.Tooltip(topologyButton, "bar.topology.tooltip");
            topologyButton.Add(new IconElement("mountain"));
            topologyButton.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != (int)MouseButton.RightMouse)
                    return;
                ShowTopologyPopup(!TopologyPopupOpen);
                evt.StopPropagation();
            });
            topologyButton.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Menu && !(evt.shiftKey && evt.keyCode == KeyCode.F10))
                    return;
                ShowTopologyPopup(!TopologyPopupOpen);
                evt.StopPropagation();
            });
            return topologyButton;
        }

        /// <summary>Enabled with a map open, orange while the lines are on; the road tool's switch follows.</summary>
        private void UpdateTopologyButton()
        {
            if (topologyButton == null)
                return;
            var loaded = currentSummary != null;
            topologyButton.SetEnabled(loaded);
            topologyButton.EnableInClassList("bar-button-active", loaded && TopologyOn);
            roadPanel?.ShowTopography(TopologyOn);
            if (!loaded)
                ShowTopologyPopup(false);
        }

        /// <summary>
        /// Switches the lines on or off. <paramref name="announce"/> says so in the status line and, once the terrain is read, that a
        /// flat map has nothing to show (it is not known before: tiles load in the background).
        /// </summary>
        private void SetTopology(bool on, bool announce)
        {
            var settings = TopologySettings;
            if (settings == null)
                return;
            settings.Enabled = on;
            UpdateTopologyButton();
            RefreshTopologyPopup();
            if (!on)
                ShowTopologyHeight(null);
            topologyFlatPending = on && announce;
            topologyFlatFrame = Time.frameCount + 2;
            if (announce)
            {
                if (on)
                    SetStatusT("topology.status.on");
                else
                    SetStatusT("topology.status.off");
            }
        }

        /// <summary>
        /// Every frame, cheap: the canvas draws its tiles unfilled while lines show below it, tells the shell where the pointer is
        /// only while the lines are on, and a flat map is announced once the terrain is read.
        /// </summary>
        private void TickTopology()
        {
            if (groundRenderer == null || mapCanvas == null)
                return;
            EnsureTopologyHooks();
            var shown = groundRenderer.ContourShown;
            if (mapCanvas.ContourLinesBelow != shown)
            {
                mapCanvas.ContourLinesBelow = shown;
                mapCanvas.Refresh();
            }
            var report = groundRenderer.ContourSettings.Enabled && currentSummary != null;
            if (mapCanvas.ReportGroundPointer != report)
            {
                mapCanvas.ReportGroundPointer = report;
                if (!report)
                    ShowTopologyHeight(null);
            }
            if (topologyFlatPending && Time.frameCount >= topologyFlatFrame && !groundRenderer.IsLoading)
            {
                topologyFlatPending = false;
                if (TopologyOn && groundRenderer.ContourFlat)
                    SetStatusT("topology.status.flat");
            }
        }

        private void EnsureTopologyHooks()
        {
            if (topologyHooked || statusLabel?.parent == null || toolHintLabel == null)
                return;
            topologyHooked = true;
            mapCanvas.GroundPointerMoved += OnTopologyPointer;
            // The height stands left of the hint of the tool, with the same divider; it is gone while the lines are off.
            topologyHeightLabel = new Label { pickingMode = PickingMode.Position };
            topologyHeightLabel.AddToClassList("status-hint");
            LocBindings.Tooltip(topologyHeightLabel, "topology.height.tooltip");
            statusLabel.parent.Insert(statusLabel.parent.IndexOf(toolHintLabel), topologyHeightLabel);
        }

        private void OnTopologyPointer(Vector2? point)
        {
            if (!point.HasValue || groundRenderer == null || !groundRenderer.TryHeightAt(point.Value, out var height))
            {
                ShowTopologyHeight(null);
                return;
            }
            ShowTopologyHeight((int)Math.Round(height * 10.0));
        }

        /// <summary>
        /// Writes the height under the pointer, in tenths of a metre, or hides it. The text changes only when the tenth does, so a
        /// moving pointer costs a comparison per move, not a formatted text.
        /// </summary>
        private void ShowTopologyHeight(int? tenths)
        {
            if (topologyHeightLabel == null)
                return;
            var value = tenths ?? int.MinValue;
            if (value == topologyShownTenths)
                return;
            topologyShownTenths = value;
            if (tenths.HasValue)
                topologyHeightLabel.text = Loc.T("topology.height", ("height", tenths.Value / 10.0));
            topologyHeightLabel.EnableInClassList("status-hint-shown", tenths.HasValue);
        }

        /// <summary>Opens or closes the settings of the topology view; it needs a map and replaces the list popup of the bar.</summary>
        private void ShowTopologyPopup(bool show)
        {
            if (show && (currentSummary == null || groundRenderer == null))
                show = false;
            if (show)
                EnsureTopologyPopup();
            if (topologyPopup == null)
                return;
            topologyPopup.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show)
                return;
            if (barPopupOpen)
                ShowBarPopup(false);
            RefreshTopologyPopup();
        }

        private void EnsureTopologyPopup()
        {
            if (topologyPopup != null || barPopup?.parent == null)
                return;
            topologyPopup = new VisualElement();
            topologyPopup.AddToClassList("floating-panel");
            topologyPopup.AddToClassList("bar-popup");
            topologyPopup.AddToClassList("bar-popup-right");
            topologyPopup.AddToClassList("bar-popup-wide");
            topologyPopup.style.display = DisplayStyle.None;
            topologyPopup.style.bottom = BottomBarLayout.DistanceAbove(bottomBarHeight, BottomBarLayout.PopupGap);

            var header = new VisualElement();
            header.AddToClassList("floating-panel-header");
            var title = LocBindings.NewLabel("topology.title");
            title.AddToClassList("floating-panel-title");
            header.Add(title);
            var close = new Button(() => ShowTopologyPopup(false));
            close.AddToClassList("panel-collapse-button");
            LocBindings.Tooltip(close, "bar.popup.close");
            DecorateButton(close, "x", null);
            header.Add(close);
            topologyPopup.Add(header);

            var content = new VisualElement();
            content.AddToClassList("floating-panel-content");
            topologyPopup.Add(content);

            // How far apart the lines are: automatic from the zoom and the terrain, or one fixed spacing.
            var spacingRow = OptionControls.Row(content, "topology.spacing", false, true);
            var spacings = OptionControls.Group(spacingRow);
            spacings.AddToClassList("option-icons-wrap");
            topologyAutoChoice = OptionControls.TextChoice(spacings, "topology.auto", "topology.auto.tooltip", () =>
            {
                TopologySettings.Automatic = true;
                SpacingChosen();
            });
            topologySpacingChoices.Clear();
            foreach (var spacing in TopologySpacings)
            {
                var value = spacing;
                var choice = OptionControls.TextChoice(spacings, null, null, () =>
                {
                    TopologySettings.Automatic = false;
                    TopologySettings.ManualInterval = value;
                    SpacingChosen();
                });
                LocBindings.Text(choice, "topology.spacing_value", ("value", value));
                LocBindings.Tooltip(choice, "topology.spacing_value.tooltip", ("value", value));
                topologySpacingChoices.Add(new KeyValuePair<double, Button>(value, choice));
            }

            var bandRow = OptionControls.Row(content, "topology.band", false, false);
            topologyBandSwitch = OptionControls.Switch(bandRow, "layers", "topology.band", "topology.band.tooltip", false);
            topologyBandSwitch.RegisterValueChangedCallback(change =>
            {
                TopologySettings.ShowBand = change.newValue;
                if (change.newValue && !TopologyOn)
                    SetTopology(true, false);
                RefreshTopologyPopup();
            });

            OptionControls.Heading(content, "topology.legend");
            topologySpacingLegend = LegendLine(content);
            topologyMajorLegend = LegendLine(content);
            topologyRangeLegend = LegendLine(content);
            topologyBandScale = new VisualElement();
            topologyBandScale.AddToClassList("topology-scale");
            topologyBandLow = new Label();
            topologyBandLow.AddToClassList("topology-scale-value");
            topologyBandScale.Add(topologyBandLow);
            var bar = new VisualElement { pickingMode = PickingMode.Ignore };
            bar.AddToClassList("topology-scale-bar");
            bar.style.backgroundImage = new StyleBackground(BandTexture());
            topologyBandScale.Add(bar);
            topologyBandHigh = new Label();
            topologyBandHigh.AddToClassList("topology-scale-value");
            topologyBandScale.Add(topologyBandHigh);
            content.Add(topologyBandScale);

            barPopup.parent.Insert(barPopup.parent.IndexOf(barPopup) + 1, topologyPopup);
            RegisterOverlay(topologyPopup, false);
            // The spacing of "automatic" follows the zoom, so the legend is read again while the popup is open.
            topologyPopup.schedule.Execute(() =>
            {
                if (TopologyPopupOpen)
                    RefreshTopologyPopup();
            }).Every(250);
        }

        private static Label LegendLine(VisualElement parent)
        {
            var line = new Label();
            line.AddToClassList("panel-info");
            parent.Add(line);
            return line;
        }

        /// <summary>A choice of spacing shows its effect: it switches the lines on, as someone who sets the spacing wants to see it.</summary>
        private void SpacingChosen()
        {
            if (!TopologyOn)
                SetTopology(true, false);
            RefreshTopologyPopup();
        }

        /// <summary>Marks the chosen spacing and the band switch and writes the legend: spacing, strong line, heights of the map.</summary>
        private void RefreshTopologyPopup()
        {
            var settings = TopologySettings;
            if (settings == null || !TopologyPopupOpen)
                return;
            groundRenderer.UpdateContourRange();
            topologyAutoChoice.EnableInClassList("panel-tab-active", settings.Automatic);
            foreach (var pair in topologySpacingChoices)
                pair.Value.EnableInClassList("panel-tab-active", !settings.Automatic && Math.Abs(pair.Key - settings.ManualInterval) < 1e-9);
            topologyBandSwitch.SetValueWithoutNotify(settings.ShowBand);

            var known = groundRenderer.ContourRangeKnown;
            var flat = groundRenderer.ContourFlat;
            double? interval = null;
            if (!settings.Automatic)
                interval = settings.ManualInterval;
            else if (groundRenderer.ContourShown)
                interval = groundRenderer.ContourIntervalMeters;

            string spacingText;
            if (flat)
            {
                if (known)
                    spacingText = Loc.T("topology.legend.flat");
                else
                    spacingText = Loc.T("topology.legend.unknown");
            }
            else if (!interval.HasValue)
                spacingText = Loc.T("topology.legend.spacing_auto_idle");
            else if (settings.Automatic)
                spacingText = Loc.T("topology.legend.spacing_auto", ("spacing", interval.Value));
            else
                spacingText = Loc.T("topology.legend.spacing_fixed", ("spacing", interval.Value));
            SetLegend(topologySpacingLegend, spacingText);

            SetLegend(topologyMajorLegend, flat || !interval.HasValue ? null
                : Loc.T("topology.legend.major", ("count", OmsiContourRules.MajorEvery), ("interval", interval.Value * OmsiContourRules.MajorEvery)));
            SetLegend(topologyRangeLegend, flat || !known ? null
                : Loc.T("topology.legend.range", ("low", groundRenderer.ContourMinimum), ("high", groundRenderer.ContourMaximum)));

            var scale = settings.ShowBand && !flat && known;
            topologyBandScale.style.display = scale ? DisplayStyle.Flex : DisplayStyle.None;
            if (scale)
            {
                topologyBandLow.text = Loc.T("terrain.here_height", ("height", groundRenderer.ContourMinimum));
                topologyBandHigh.text = Loc.T("terrain.here_height", ("height", groundRenderer.ContourMaximum));
            }
        }

        private static void SetLegend(Label label, string text)
        {
            var shown = !string.IsNullOrEmpty(text);
            label.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            if (shown && label.text != text)
                label.text = text;
        }

        /// <summary>The colours of the colour band, low to high, as the shader draws them (MapContourOverlay.shader, BandColour).</summary>
        private static Texture2D BandTexture()
        {
            if (topologyBandTexture != null)
                return topologyBandTexture;
            const int width = 64;
            var texture = new Texture2D(width, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "Topology colour band" };
            var low = new Color(0.30f, 0.62f, 0.34f);
            var middle = new Color(0.90f, 0.82f, 0.40f);
            var high = new Color(0.62f, 0.42f, 0.26f);
            var top = new Color(0.96f, 0.95f, 0.93f);
            for (var x = 0; x < width; x++)
            {
                var t = x / (float)(width - 1);
                var colour = Color.Lerp(low, middle, Mathf.Clamp01(t * 3f));
                colour = Color.Lerp(colour, high, Mathf.Clamp01(t * 3f - 1f));
                colour = Color.Lerp(colour, top, Mathf.Clamp01(t * 3f - 2f));
                colour.a = 1f;
                texture.SetPixel(x, 0, colour);
            }
            texture.Apply(false, true);
            topologyBandTexture = texture;
            return texture;
        }
    }
}
