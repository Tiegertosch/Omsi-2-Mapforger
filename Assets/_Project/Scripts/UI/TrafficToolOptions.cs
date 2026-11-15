using System;
using System.Collections.Generic;
using System.Globalization;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>The tabs of the traffic rules window.</summary>
    internal enum TrafficTab
    {
        /// <summary>Signs for the arms of a junction (priority road, give way, normal) and the overtaking ban for the pieces of a road with its eraser.</summary>
        Rules,

        /// <summary>Signs for the pieces of a road: speeds, a custom speed and the eraser of the limit.</summary>
        Speed
    }

    internal enum TrafficItemKind
    {
        /// <summary>A sign for an arm of a junction; <see cref="TrafficItem.Sign"/> says which.</summary>
        RightOfWay,

        /// <summary>One of the speeds of the tiles; <see cref="TrafficItem.Kmh"/> says which.</summary>
        Speed,

        /// <summary>The speed typed in the options column.</summary>
        CustomSpeed,

        OvertakingBan,

        /// <summary>Takes the speed limit off a road piece; its overtaking ban stays.</summary>
        ClearSpeed,

        /// <summary>Takes the overtaking ban off a road piece; its speed limit stays.</summary>
        ClearOvertaking
    }

    /// <summary>What a tile of the window stands for, and so what a click or a drop on the map does.</summary>
    internal struct TrafficItem : IEquatable<TrafficItem>
    {
        public TrafficItemKind Kind;
        public OmsiRightOfWaySign Sign;
        public int Kmh;

        /// <summary>True for what goes on the pieces of a road (the speeds, the ban and their erasers), false for the signs of a junction's arms.</summary>
        public bool OnRoad => Kind != TrafficItemKind.RightOfWay;

        /// <summary>True for the erasers: the one of the arms (normal) and those of the road pieces.</summary>
        public bool IsEraser => Kind == TrafficItemKind.ClearSpeed || Kind == TrafficItemKind.ClearOvertaking || Kind == TrafficItemKind.RightOfWay && Sign == OmsiRightOfWaySign.Normal;

        /// <summary>The tab the tile stands in; the ban is a road rule but lies with the other traffic rules on the first tab.</summary>
        public TrafficTab Tab => Kind == TrafficItemKind.Speed || Kind == TrafficItemKind.CustomSpeed || Kind == TrafficItemKind.ClearSpeed ? TrafficTab.Speed : TrafficTab.Rules;

        public bool Equals(TrafficItem other)
        {
            return Kind == other.Kind && Sign == other.Sign && Kmh == other.Kmh;
        }

        public override bool Equals(object obj)
        {
            return obj is TrafficItem other && Equals(other);
        }

        public override int GetHashCode()
        {
            return ((int)Kind * 397 ^ (int)Sign) * 397 ^ Kmh;
        }
    }

    /// <summary>
    /// The window of the traffic rules tool (ADR 098, M4): the options column on the left and the catalog of signs on the right, in two tabs.
    /// A tile is armed by a click (it stays armed for further clicks in the map) or dragged to the map; the shell decides what a drop on an
    /// arm or a road piece does. The window only holds the settings.
    /// </summary>
    internal sealed class TrafficToolOptions : VisualElement
    {
        private const float DragStartPixels = 6f;
        private const int DefaultCustomKmh = 40;

        private readonly ToolCatalog catalog;
        private readonly Dictionary<Button, TrafficTab> tabButtons = new Dictionary<Button, TrafficTab>();
        private readonly Dictionary<VisualElement, TrafficItem> tiles = new Dictionary<VisualElement, TrafficItem>();

        private readonly VisualElement junctionRow;
        private readonly Label junctionLabel;
        private readonly VisualElement scopeRow;
        private readonly Button pieceButton;
        private readonly Button wholeRoadButton;
        private readonly VisualElement speedRow;
        private readonly TextField customField;

        private TrafficTab tab = TrafficTab.Rules;
        private TrafficItem? armed;
        private TrafficSignElement customSign;

        // The press on a tile: it becomes a click when the pointer is let go close by and a drag when it moved on.
        private VisualElement pressedTile;
        private TrafficItem pressedItem;
        private int pressedPointer;
        private Vector2 pressedAt;
        private bool dragging;

        public TrafficToolOptions()
        {
            AddToClassList("picker-tool");
            // The row is as tall as its tallest panel and the shorter ones stand at its bottom: the empty part above them must not catch clicks meant for the map.
            pickingMode = PickingMode.Ignore;

            var options = ToolCatalog.Options(scrolling: true, icons: true);
            junctionRow = OptionControls.Row(options, "traffic.row.junction");
            junctionLabel = junctionRow.Q<Label>();
            OptionControls.Choice(OptionControls.Group(junctionRow), "eraser", "traffic.clear_junction", "traffic.clear_junction.tooltip", () => ClearJunctionRequested?.Invoke());

            scopeRow = OptionControls.Row(options, "traffic.row.apply_to");
            var scope = OptionControls.Group(scopeRow);
            pieceButton = OptionControls.Choice(scope, "minus", "traffic.this_piece", "traffic.this_piece.tooltip", () => SetWholeRoad(false));
            wholeRoadButton = OptionControls.Choice(scope, "route", "traffic.whole_road", "traffic.whole_road.tooltip", () => SetWholeRoad(true));

            speedRow = OptionControls.Row(options, "traffic.row.speed");
            customField = OptionControls.Value(speedRow, "gauge", DefaultCustomKmh.ToString(CultureInfo.InvariantCulture), "km/h", "traffic.custom", "traffic.custom.tooltip");
            customField.RegisterValueChangedCallback(_ => ShowCustom());
            customField.RegisterCallback<KeyDownEvent>(evt =>
            {
                // Enter arms the custom speed, so typing a number and pressing Enter is enough to start laying it.
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    Arm(new TrafficItem { Kind = TrafficItemKind.CustomSpeed });
                    customField.Blur();
                }
            }, TrickleDown.TrickleDown);
            Add(options);

            catalog = new ToolCatalog("traffic.close.tooltip", () => CloseRequested?.Invoke());
            AddTab(TrafficTab.Rules, "diamond", "traffic.tab.rules", "traffic.tab.rules.tooltip", TrafficSignSvgs.TabRules);
            AddTab(TrafficTab.Speed, "speed-sign", "traffic.tab.speed", "traffic.tab.speed.tooltip", TrafficSignSvgs.TabSpeed);
            catalog.Empty.style.display = DisplayStyle.None;
            Add(catalog.Panel);

            SetWholeRoad(false);
            Refresh();
        }

        /// <summary>The X was pressed: the shell leaves the tool.</summary>
        public event Action CloseRequested;

        /// <summary>The eraser of the first tab's options was pressed: every arm of the junction goes back to normal.</summary>
        public event Action ClearJunctionRequested;

        /// <summary>The armed tile changed; the shell shows the hint and the pointer.</summary>
        public event Action ArmedChanged;

        /// <summary>The tab changed.</summary>
        public event Action TabChanged;

        /// <summary>The scope (this piece or the whole road) changed.</summary>
        public event Action ScopeChanged;

        /// <summary>The speed typed in the options column changed.</summary>
        public event Action CustomSpeedChanged;

        /// <summary>A tile began to be dragged, at this position in the window's panel.</summary>
        public event Action<TrafficItem, Vector2> DragStarted;

        public event Action<TrafficItem, Vector2> DragMoved;

        /// <summary>A tile was let go at this position; the shell decides whether it is over the map.</summary>
        public event Action<TrafficItem, Vector2> DragDropped;

        /// <summary>The drag ended without a drop, such as by Esc or because the pointer was taken from the tile.</summary>
        public event Action DragCancelled;

        /// <summary>What the next click does; the shell shows it in the status bar.</summary>
        public event Action<string, bool> HintChanged
        {
            add => catalog.HintChanged += value;
            remove => catalog.HintChanged -= value;
        }

        public TrafficTab Tab => tab;

        /// <summary>The tile a click in the map uses, or none.</summary>
        public TrafficItem? Armed => armed;

        public bool IsDragging => dragging;

        /// <summary>True when a speed or the ban (or an eraser of them) goes to every piece of the road, not only the one pointed at.</summary>
        public bool WholeRoad { get; private set; }

        /// <summary>The speed typed in the options column in km/h, or null when it is not a usable number.</summary>
        public double? CustomKmh => OmsiTrafficSigns.TryParseKmh(customField.value, out var kmh) ? kmh : (double?)null;

        /// <summary>Chooses whether a speed goes to the piece pointed at or to its whole road, as the two icons do.</summary>
        public void ChooseScope(bool whole)
        {
            SetWholeRoad(whole);
        }

        public void ShowHint(string text, bool warning)
        {
            catalog.ShowHint(text, warning);
        }

        /// <summary>The junction's line is the tooltip of the junction row.</summary>
        public void ShowJunction(string text)
        {
            junctionLabel.tooltip = text ?? string.Empty;
        }

        /// <summary>Opens a tab by name, "rules" ("priority" still works, it was the name of the first tab) or "speed"; false for any other name.</summary>
        public bool ShowTab(string name)
        {
            if (string.Equals(name, "rules", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "priority", StringComparison.OrdinalIgnoreCase))
                Open(TrafficTab.Rules);
            else if (string.Equals(name, "speed", StringComparison.OrdinalIgnoreCase))
                Open(TrafficTab.Speed);
            else
                return false;
            return true;
        }

        public void Open(TrafficTab opened)
        {
            if (opened == tab)
                return;
            tab = opened;
            // A sign of the other tab means nothing here.
            Arm(null);
            Refresh();
            TabChanged?.Invoke();
        }

        /// <summary>Arms a tile, or lets go with null; arming the armed one again lets go of it too, as a click does.</summary>
        public void Arm(TrafficItem? item)
        {
            if (item.HasValue && armed.HasValue && armed.Value.Equals(item.Value))
                item = null;
            if (item.HasValue == armed.HasValue && (!item.HasValue || item.Value.Equals(armed.Value)))
                return;
            armed = item;
            foreach (var pair in tiles)
                pair.Key.EnableInClassList("picker-tile-selected", armed.HasValue && pair.Value.Equals(armed.Value));
            ArmedChanged?.Invoke();
        }

        /// <summary>Takes the pointer from a tile that is being dragged; the drag is cancelled.</summary>
        public void CancelDrag()
        {
            if (pressedTile == null)
                return;
            var tile = pressedTile;
            var pointer = pressedPointer;
            var was = dragging;
            pressedTile = null;
            dragging = false;
            if (tile.HasPointerCapture(pointer))
                tile.ReleasePointer(pointer);
            if (was)
                DragCancelled?.Invoke();
        }

        private void SetWholeRoad(bool whole)
        {
            var changed = WholeRoad != whole;
            WholeRoad = whole;
            pieceButton.EnableInClassList("panel-tab-active", !whole);
            wholeRoadButton.EnableInClassList("panel-tab-active", whole);
            if (changed)
                ScopeChanged?.Invoke();
        }

        private void ShowCustom()
        {
            if (customSign != null)
                customSign.Set(TrafficSignKind.Speed, CustomKmh.HasValue ? (int)Math.Round(CustomKmh.Value) : 0);
            CustomSpeedChanged?.Invoke();
        }

        private void AddTab(TrafficTab kind, string icon, string nameKey, string tooltipKey, string svgName)
        {
            var button = catalog.AddTab(icon, nameKey, tooltipKey, () => Open(kind));
            // The owner's own icon, when its file is there, takes the place of the line icon.
            var picture = TrafficSignSvgs.Get(svgName);
            if (picture != null)
                button.Q<IconElement>()?.SetSvg(picture);
            tabButtons.Add(button, kind);
        }

        /// <summary>Builds the tiles of the open tab and the rows of the options that belong to it.</summary>
        private void Refresh()
        {
            foreach (var pair in tabButtons)
                pair.Key.EnableInClassList("panel-tab-active", pair.Value == tab);
            junctionRow.style.display = tab == TrafficTab.Rules ? DisplayStyle.Flex : DisplayStyle.None;
            // The scope belongs to both tabs: the speeds are on the second, the overtaking ban and its eraser on the first.
            scopeRow.style.display = DisplayStyle.Flex;
            speedRow.style.display = tab == TrafficTab.Speed ? DisplayStyle.Flex : DisplayStyle.None;
            catalog.Tiles.Clear();
            tiles.Clear();
            customSign = null;
            if (tab == TrafficTab.Rules)
            {
                AddSign(OmsiRightOfWaySign.PriorityRoad, "traffic.sign.priority_road", "traffic.sign.priority_road.tooltip");
                AddSign(OmsiRightOfWaySign.Yield, "traffic.sign.yield", "traffic.sign.yield.tooltip");
                AddTile(new TrafficItem { Kind = TrafficItemKind.OvertakingBan }, new TrafficSignElement(TrafficSignKind.OvertakingBan), "traffic.sign.overtaking", "traffic.sign.overtaking.tooltip");
                // Two erasers: the first for the arms of a junction, the second for the road pieces.
                AddEraser(new TrafficItem { Kind = TrafficItemKind.RightOfWay, Sign = OmsiRightOfWaySign.Normal }, "traffic.sign.normal", "traffic.sign.normal.tooltip");
                AddEraser(new TrafficItem { Kind = TrafficItemKind.ClearOvertaking }, "traffic.sign.clear_overtaking", "traffic.sign.clear_overtaking.tooltip", TrafficSignKind.OvertakingBan);
            }
            else
            {
                foreach (var kmh in OmsiTrafficSigns.SpeedTiles)
                    AddSpeed(new TrafficItem { Kind = TrafficItemKind.Speed, Kmh = kmh }, kmh);
                customSign = AddSpeed(new TrafficItem { Kind = TrafficItemKind.CustomSpeed }, CustomKmh.HasValue ? (int)Math.Round(CustomKmh.Value) : 0, true);
                AddEraser(new TrafficItem { Kind = TrafficItemKind.ClearSpeed }, "traffic.sign.clear_road", "traffic.sign.clear_road.tooltip");
            }
            foreach (var pair in tiles)
                pair.Key.EnableInClassList("picker-tile-selected", armed.HasValue && pair.Value.Equals(armed.Value));
        }

        private void AddSign(OmsiRightOfWaySign sign, string nameKey, string tooltipKey)
        {
            var kind = TrafficSignPainter.Of(sign);
            AddTile(new TrafficItem { Kind = TrafficItemKind.RightOfWay, Sign = sign }, new TrafficSignElement(kind), nameKey, tooltipKey);
        }

        private TrafficSignElement AddSpeed(TrafficItem item, int kmh, bool custom = false)
        {
            var sign = new TrafficSignElement(TrafficSignKind.Speed, kmh);
            var tile = custom ? AddTile(item, sign, "traffic.sign.custom", "traffic.sign.custom.tooltip") : AddTile(item, sign, null, null);
            if (!custom)
            {
                var name = tile.Q<Label>();
                LocBindings.Text(name, "traffic.sign.speed", ("kmh", kmh));
                LocBindings.Tooltip(tile, "traffic.sign.speed.tooltip", ("kmh", kmh));
            }
            return sign;
        }

        /// <summary>An eraser tile; with <paramref name="removes"/> the sign it takes away stands faintly behind the eraser, so two erasers on one tab can be told apart.</summary>
        private void AddEraser(TrafficItem item, string nameKey, string tooltipKey, TrafficSignKind? removes = null)
        {
            var tile = AddTile(item, null, nameKey, tooltipKey);
            var preview = tile.Q(className: "picker-tile-preview");
            preview.AddToClassList("picker-tile-icon");
            if (removes.HasValue)
            {
                var faint = new TrafficSignElement(removes.Value);
                faint.style.position = Position.Absolute;
                faint.style.left = 6f;
                faint.style.top = 6f;
                faint.style.right = 6f;
                faint.style.bottom = 6f;
                faint.style.opacity = 0.4f;
                preview.Add(faint);
            }
            preview.Add(new IconElement("eraser"));
        }

        private VisualElement AddTile(TrafficItem item, TrafficSignElement sign, string nameKey, string tooltipKey)
        {
            var tile = new VisualElement { focusable = true, tabIndex = 0 };
            tile.AddToClassList("picker-tile");
            tile.AddToClassList("traffic-tile");
            if (nameKey != null)
                LocBindings.TitledTooltip(tile, nameKey, tooltipKey); // i18n:keys
            var preview = new VisualElement { pickingMode = PickingMode.Ignore };
            preview.AddToClassList("picker-tile-preview");
            if (sign != null)
            {
                sign.AddToClassList("traffic-sign");
                preview.Add(sign);
            }
            tile.Add(preview);
            var name = nameKey != null ? LocBindings.NewLabel(nameKey) : new Label(); // i18n:keys
            name.pickingMode = PickingMode.Ignore;
            name.AddToClassList("picker-tile-name");
            tile.Add(name);
            WireTile(tile, item);
            catalog.Tiles.Add(tile);
            tiles[tile] = item;
            return tile;
        }

        /// <summary>
        /// A press on a tile is a click when it is let go close by (arm it) and a drag once the pointer moved on: the sign follows the pointer
        /// to the map and the shell decides what it hits. The tile holds the pointer meanwhile, so the events keep coming over the map.
        /// </summary>
        private void WireTile(VisualElement tile, TrafficItem item)
        {
            tile.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || pressedTile != null)
                    return;
                pressedTile = tile;
                pressedItem = item;
                pressedPointer = evt.pointerId;
                pressedAt = evt.position;
                dragging = false;
                tile.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });
            tile.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (pressedTile != tile || evt.pointerId != pressedPointer)
                    return;
                var position = (Vector2)evt.position;
                if (!dragging && (position - pressedAt).sqrMagnitude > DragStartPixels * DragStartPixels)
                {
                    dragging = true;
                    DragStarted?.Invoke(item, position);
                }
                if (dragging)
                    DragMoved?.Invoke(item, position);
            });
            tile.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (pressedTile != tile || evt.pointerId != pressedPointer || evt.button != 0)
                    return;
                var was = dragging;
                pressedTile = null;
                dragging = false;
                tile.ReleasePointer(evt.pointerId);
                if (was)
                    DragDropped?.Invoke(item, (Vector2)evt.position);
                else
                    Arm(item);
                evt.StopPropagation();
            });
            tile.RegisterCallback<PointerCaptureOutEvent>(evt =>
            {
                // The pointer was taken from the tile by something else (a window closing, Esc): the press is over.
                if (pressedTile != tile)
                    return;
                var was = dragging;
                pressedTile = null;
                dragging = false;
                if (was)
                    DragCancelled?.Invoke();
            });
            // The keyboard arms a tile too: Enter or Space on the tile in focus.
            tile.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter && evt.keyCode != KeyCode.Space)
                    return;
                Arm(item);
                evt.StopPropagation();
            });
        }
    }
}
