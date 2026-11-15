using System;
using System.Collections.Generic;
using System.Globalization;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>The tabs of the lanes window: what is laid on the arms of the junction.</summary>
    internal enum LaneTab
    {
        TurnLanes,
        Crossings
    }

    /// <summary>What the window needs to know of one arm of the selected junction to list it.</summary>
    internal struct LaneArmEntry
    {
        /// <summary>The arm, from 0.</summary>
        public int Arm;

        /// <summary>The way the arm points, such as "north-east", as the language writes it.</summary>
        public string Direction;

        /// <summary>True when the arm can have a left-turn lane (it has a median) or has one already.</summary>
        public bool TurnAvailable;

        /// <summary>The arm's left-turn lane, or null.</summary>
        public OmsiTurnLane Turn;

        /// <summary>The arm's pedestrian crossing, or null.</summary>
        public OmsiCrossing Crossing;
    }

    /// <summary>
    /// The window of the lanes tool (ADR 048, ADR 098 M4): the options column on the left (the ways through the junction as a whole, the
    /// lengths of a new left-turn lane, the values of a new crossing) and on the right the arms of the selected junction as tiles in two
    /// tabs. A tile gives its arm the lane or crossing, or takes it away; the tile next to it gives it the values of the column. The
    /// window only holds the settings and says what was asked; the shell lays the lanes out and makes the junction again.
    /// </summary>
    internal sealed class LaneToolOptions : VisualElement
    {
        private readonly ToolCatalog catalog;
        private readonly Dictionary<Button, LaneTab> tabButtons = new Dictionary<Button, LaneTab>();
        private readonly Label waysLabel;
        private LaneTab tab = LaneTab.TurnLanes;
        private LaneTab userTab = LaneTab.TurnLanes;
        private List<LaneArmEntry> arms;
        private string junctionText = string.Empty;
        private string planText = string.Empty;
        private bool planProblem;

        public LaneToolOptions()
        {
            AddToClassList("picker-tool");
            // The row is as tall as its tallest panel and the shorter ones stand at its bottom: the empty part above them must not catch clicks meant for the map.
            pickingMode = PickingMode.Ignore;

            var options = ToolCatalog.Options(scrolling: true, icons: true);
            var waysRow = OptionControls.Row(options, "lanes.row.ways");
            waysLabel = waysRow.Q<Label>();
            // The junction's line, what the dots and rings mean, and Esc are the tooltip of the label; ShowJunction sets it (and again after a language change).
            var ways = OptionControls.Group(waysRow);
            OptionControls.Choice(ways, "shuffle", "lanes.every", "lanes.every.tooltip", () => EveryRequested?.Invoke());
            // Taking all ways away is the destructive choice of the row, so it looks like the delete buttons (red).
            OptionControls.Choice(ways, "ban", "lanes.none", "lanes.none.tooltip", () => NoneRequested?.Invoke()).AddToClassList("option-icon-danger");

            var turnRow = OptionControls.Row(options, "lanes.row.turn");
            TaperField = OptionControls.Value(turnRow, "triangle-right", FormatMeters(OmsiTurnLane.DefaultTaperMeters), "m", "lanes.taper", "lanes.taper.tooltip");
            StorageField = OptionControls.Value(turnRow, "ruler", FormatMeters(OmsiTurnLane.DefaultStorageMeters), "m", "lanes.storage", "lanes.storage.tooltip");

            var crossingRow = OptionControls.Row(options, "lanes.row.crossing");
            CrossingDistanceField = OptionControls.Value(crossingRow, "arrow-right-to-line", FormatMeters(OmsiCrossing.DefaultDistanceMeters), "m", "lanes.crossing.distance",
                "lanes.crossing.distance.tooltip");
            CrossingWidthField = OptionControls.Value(crossingRow, "arrow-left-right", FormatMeters(OmsiCrossing.DefaultWidthMeters), "m", "lanes.crossing.width",
                "lanes.crossing.width.tooltip");
            Add(options);

            catalog = new ToolCatalog("lanes.close.tooltip", () => CloseRequested?.Invoke());
            AddTab(LaneTab.TurnLanes, "corner-up-left", "lanes.tab.turn", "lanes.tab.turn.tooltip");
            AddTab(LaneTab.Crossings, "footprints", "lanes.tab.crossing", "lanes.tab.crossing.tooltip");
            Add(catalog.Panel);
            ShowJunction(string.Empty);
            ShowTab(LaneTab.TurnLanes);
        }

        /// <summary>The taper of a new left-turn lane in metres, as typed.</summary>
        public TextField TaperField { get; }

        /// <summary>The storage length of a new left-turn lane in metres, as typed.</summary>
        public TextField StorageField { get; }

        /// <summary>The distance of a new crossing from the corner in metres, as typed.</summary>
        public TextField CrossingDistanceField { get; }

        /// <summary>The width of a new crossing in metres, as typed.</summary>
        public TextField CrossingWidthField { get; }

        public LaneTab Tab => tab;

        /// <summary>The tab the user chose (a click on it, or -lanesTab); the automatic switch to the crossings does not change it.</summary>
        public LaneTab UserTab => userTab;

        /// <summary>
        /// The tab to show for a junction that has just been chosen: the crossings when no arm can have a left-turn lane (the other tab would
        /// be empty), else the tab the user chose last (<see cref="UserTab"/>, not the one an earlier junction forced).
        /// </summary>
        public static LaneTab TabForJunction(bool anyTurnAvailable, LaneTab userChoice)
        {
            return anyTurnAvailable ? userChoice : LaneTab.Crossings;
        }

        /// <summary>Every lane in leads to every lane out again.</summary>
        public event Action EveryRequested;

        /// <summary>No ways through the junction.</summary>
        public event Action NoneRequested;

        /// <summary>An arm's left-turn lane is to be added (none now) or taken away.</summary>
        public event Action<int> TurnToggled;

        /// <summary>An arm's left-turn lane is to get the lengths of the fields.</summary>
        public event Action<int> TurnLengthsRequested;

        /// <summary>An arm's crossing is to be added (none now) or taken away.</summary>
        public event Action<int> CrossingToggled;

        /// <summary>An arm's crossing is to get the values of the fields.</summary>
        public event Action<int> CrossingValuesRequested;

        /// <summary>The pointer came to a tile of an arm (true) or left it (false); the shell marks the arm on the map.</summary>
        public event Action<int, bool> ArmHovered;

        /// <summary>The window wants to close the lanes tool.</summary>
        public event Action CloseRequested;

        /// <summary>What the next click does; the shell shows it in the status bar.</summary>
        public event Action<string, bool> HintChanged
        {
            add => catalog.HintChanged += value;
            remove => catalog.HintChanged -= value;
        }

        /// <summary>The junction's line is the tooltip of the ways row, and the hint while there is no plan.</summary>
        public void ShowJunction(string text)
        {
            junctionText = text ?? string.Empty;
            waysLabel.tooltip = junctionText.Length > 0
                ? junctionText + "\n" + Loc.T("lanes.legend") // i18n:keys
                : Loc.T("lanes.legend"); // i18n:keys
            ShowHint();
        }

        /// <summary>Shows what the next click does in the hint of the status bar.</summary>
        public void ShowPlan(string text, bool problem)
        {
            planText = text ?? string.Empty;
            planProblem = problem;
            ShowHint();
        }

        private void ShowHint()
        {
            catalog.ShowHint(planText.Length > 0 ? planText : junctionText, planText.Length > 0 && planProblem);
        }

        /// <summary>Lists the arms of the junction, or with null none (no junction is chosen).</summary>
        public void SetArms(List<LaneArmEntry> entries)
        {
            arms = entries;
            Rebuild();
        }

        /// <summary>The language changed: the tiles are written again.</summary>
        public void RefreshTexts()
        {
            Rebuild();
        }

        /// <summary>Opens a tab as the user's choice, as a click on it does.</summary>
        public void ChooseTab(LaneTab next)
        {
            userTab = next;
            ShowTab(next);
        }

        /// <summary>Opens a tab without making it the user's choice (the switch for a junction with no turn lanes).</summary>
        public void ShowTab(LaneTab next)
        {
            tab = next;
            foreach (var pair in tabButtons)
                pair.Key.EnableInClassList("panel-tab-active", pair.Value == tab);
            Rebuild();
        }

        private void AddTab(LaneTab value, string icon, string nameKey, string tooltipKey)
        {
            tabButtons[catalog.AddTab(icon, nameKey, tooltipKey, () => ChooseTab(value))] = value;
        }

        private void Rebuild()
        {
            var tiles = catalog.Tiles.contentContainer;
            tiles.Clear();
            var any = false;
            if (arms != null)
            {
                foreach (var entry in arms)
                {
                    if (tab == LaneTab.TurnLanes)
                    {
                        if (!entry.TurnAvailable)
                            continue;
                        any = true;
                        AddTurnTiles(tiles, entry);
                    }
                    else
                    {
                        any = true;
                        AddCrossingTiles(tiles, entry);
                    }
                }
            }
            catalog.Empty.style.display = any ? DisplayStyle.None : DisplayStyle.Flex;
            LocBindings.Text(catalog.Empty, arms == null ? "lanes.click_junction" : tab == LaneTab.TurnLanes ? "lanes.turn.none" : "lanes.crossing.none"); // i18n:keys
        }

        private void AddTurnTiles(VisualElement tiles, LaneArmEntry entry)
        {
            var arm = entry.Arm;
            var has = entry.Turn != null;
            var toggle = Tile("corner-up-left", Loc.T("lanes.tile.arm", ("n", arm + 1), ("direction", entry.Direction)),
                has ? Loc.T("lanes.turn.remove", ("n", arm + 1), ("direction", entry.Direction), ("taper", entry.Turn.TaperMeters), ("storage", entry.Turn.StorageMeters))
                    : Loc.T("lanes.turn.add", ("n", arm + 1), ("direction", entry.Direction)),
                Loc.T(has ? "lanes.turn.remove.tooltip" : "lanes.turn.add.tooltip"), () => TurnToggled?.Invoke(arm), arm, has); // i18n:keys
            tiles.Add(toggle);
            if (!has)
                return;
            tiles.Add(Tile("ruler", Loc.T("lanes.tile.turn_values", ("taper", entry.Turn.TaperMeters), ("storage", entry.Turn.StorageMeters)),
                Loc.T("lanes.turn.lengths", ("n", arm + 1), ("direction", entry.Direction)), Loc.T("lanes.turn.lengths.tooltip"), () => TurnLengthsRequested?.Invoke(arm), arm, false));
        }

        private void AddCrossingTiles(VisualElement tiles, LaneArmEntry entry)
        {
            var arm = entry.Arm;
            var has = entry.Crossing != null;
            var toggle = Tile("footprints", Loc.T("lanes.tile.arm", ("n", arm + 1), ("direction", entry.Direction)),
                has ? Loc.T("lanes.crossing.remove", ("n", arm + 1), ("direction", entry.Direction), ("distance", entry.Crossing.DistanceMeters), ("width", entry.Crossing.WidthMeters))
                    : Loc.T("lanes.crossing.add", ("n", arm + 1), ("direction", entry.Direction)),
                Loc.T(has ? "lanes.crossing.remove.tooltip" : "lanes.crossing.add.tooltip"), () => CrossingToggled?.Invoke(arm), arm, has); // i18n:keys
            tiles.Add(toggle);
            if (!has)
                return;
            tiles.Add(Tile("ruler", Loc.T("lanes.tile.crossing_values", ("distance", entry.Crossing.DistanceMeters), ("width", entry.Crossing.WidthMeters)),
                Loc.T("lanes.crossing.values", ("n", arm + 1), ("direction", entry.Direction)), Loc.T("lanes.crossing.values.tooltip"), () => CrossingValuesRequested?.Invoke(arm), arm, false));
        }

        /// <summary>A tile with an icon under its caption; the tooltip says what a click does (title) and what that means (text). Orange while the arm has the lane or crossing.</summary>
        private Button Tile(string icon, string caption, string title, string text, Action clicked, int arm, bool has)
        {
            var tile = ToolCatalog.ImageTile(null, icon, caption, string.Empty, clicked);
            tile.tooltip = LocBindings.CardTooltip(title, text);
            tile.EnableInClassList("picker-tile-selected", has);
            // The pointer over a tile marks its arm on the map.
            tile.RegisterCallback<PointerEnterEvent>(_ => ArmHovered?.Invoke(arm, true));
            tile.RegisterCallback<PointerLeaveEvent>(_ => ArmHovered?.Invoke(arm, false));
            return tile;
        }

        private static string FormatMeters(double meters)
        {
            return meters.ToString("0.#", CultureInfo.InvariantCulture);
        }
    }
}
