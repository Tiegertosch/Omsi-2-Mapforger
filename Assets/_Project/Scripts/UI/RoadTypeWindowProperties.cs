using System;
using System.Collections.Generic;
using System.IO;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The properties of the road type window (briefing of 18 November 2026): what is selected in the cross-section, changed through the commands of
    /// the draft. The controls are built once and changed in place by <see cref="SyncInspector"/>, so nothing is lost to a rebuild: a field being typed
    /// in keeps its text and its focus, the scroll position stays, and a change commits on Enter or when the field is left and makes one undo step.
    /// The surface picker is a page of its own that takes the place of the work area, like the templates.
    /// </summary>
    internal sealed partial class RoadTypeWindow
    {
        private Label propHeading;
        private Label propHint;
        private VisualElement elementBox;
        private VisualElement lineBox;
        private DropdownField kindDropdown;
        private TextField widthField;
        private RepeatButton widthMinus;
        private RepeatButton widthPlus;
        private Label widthRange;
        private VisualElement directionBox;
        private Label directionNone;
        private Button againstButton;
        private Button withButton;
        private VisualElement tramBox;
        private Toggle carsToggle;
        private VisualElement surfaceSwatch;
        private Label surfaceName;
        private Button surfaceReset;
        private Label kerbNote;
        private Button equipmentHeader;
        private Label equipmentHeaderText;
        private IconElement equipmentChevron;
        private VisualElement equipmentBody;
        private Label equipmentHint;
        private Button objectButton;
        private Label objectButtonText;
        private Button objectClear;
        private VisualElement rowFields;
        private TextField intervalField;
        private TextField turnField;
        private readonly List<(Button Button, OmsiRoadRowPlace Place)> placeButtons = new List<(Button, OmsiRoadRowPlace)>();
        private readonly List<(Button Button, OmsiRoadMarking? Marking)> lineButtons = new List<(Button, OmsiRoadMarking?)>();
        private bool equipmentOpen;
        private IVisualElementScheduledItem stepIdle;

        // The surface picker page.
        private VisualElement surfacesView;
        private Label surfaceTitle;
        private TextField surfaceSearch;
        private ScrollView surfaceTiles;
        private Label surfaceEmpty;
        private readonly Dictionary<string, VisualElement> tilePreviews = new Dictionary<string, VisualElement>(StringComparer.OrdinalIgnoreCase);
        private IVisualElementScheduledItem surfacePoll;

        // ----- small building blocks -----

        private static VisualElement PropRow(VisualElement parent, string labelKey)
        {
            var row = new VisualElement();
            row.AddToClassList("road-prop");
            var label = LocBindings.NewLabel(labelKey); // i18n:keys
            label.AddToClassList("road-prop-label");
            row.Add(label);
            parent.Add(row);
            return row;
        }

        /// <summary>A segment of a segmented control: an optional icon and a text, both inside the button so the active colour reaches them.</summary>
        private static Button SegmentButton(string icon, string textKey, string tooltipKey, Action clicked)
        {
            var button = new Button(clicked);
            button.AddToClassList("panel-tab");
            button.AddToClassList("road-window-segment");
            if (icon != null)
            {
                var image = new IconElement(icon);
                image.AddToClassList("road-window-segment-icon");
                button.Add(image);
            }
            var label = new Label { pickingMode = PickingMode.Ignore };
            LocBindings.Text(label, textKey); // i18n:keys
            button.Add(label);
            LocBindings.Tooltip(button, tooltipKey); // i18n:keys
            return button;
        }

        private static Button TextIconButton(string icon, string textKey, string tooltipKey, Action clicked)
        {
            var button = new Button(clicked);
            button.AddToClassList("map-control-button");
            button.AddToClassList("road-window-text-icon-button");
            button.Add(new IconElement(icon));
            var label = new Label { pickingMode = PickingMode.Ignore };
            LocBindings.Text(label, textKey); // i18n:keys
            button.Add(label);
            LocBindings.Tooltip(button, tooltipKey); // i18n:keys
            return button;
        }

        private static bool HasFocus(VisualElement element)
        {
            var focused = element.panel?.focusController?.focusedElement as VisualElement;
            return focused != null && (focused == element || element.Contains(focused));
        }

        /// <summary>
        /// Makes a text field commit a typed value on Enter and when it is left, never while it is typed in; the text it shows afterwards is the
        /// value as the draft keeps it. <paramref name="commit"/> returns false for text it cannot read, which puts the old value back.
        /// </summary>
        private void WireNumberField(TextField field, Func<string, bool> commit, Func<string> current)
        {
            void Commit()
            {
                var typed = field.value;
                if (!commit(typed))
                {
                    field.SetValueWithoutNotify(current());
                    return;
                }
                field.SetValueWithoutNotify(current());
            }

            field.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter)
                    return;
                Commit();
                evt.StopPropagation();
            }, TrickleDown.TrickleDown);
            field.RegisterCallback<FocusOutEvent>(_ => Commit());
        }

        // ----- building -----

        private void BuildInspector(VisualElement root)
        {
            propHeading = new Label();
            propHeading.AddToClassList("inspector-section-heading");
            propHeading.AddToClassList("road-prop-heading");
            root.Add(propHeading);
            propHint = new Label();
            propHint.AddToClassList("panel-info");
            root.Add(propHint);

            // An element: its kind, width, direction, surface and equipment.
            elementBox = new VisualElement();
            root.Add(elementBox);

            var kindRow = PropRow(elementBox, "roadbuilder.window.prop.kind");
            kindDropdown = new DropdownField(KindNames(), 0);
            kindDropdown.AddToClassList("road-prop-dropdown");
            LocBindings.Tooltip(kindDropdown, "roadbuilder.window.prop.kind.tooltip");
            kindDropdown.RegisterValueChangedCallback(_ => ChangeKind(kindDropdown.index));
            kindRow.Add(kindDropdown);

            var widthRow = PropRow(elementBox, "roadbuilder.panel.width");
            var widthLine = new VisualElement();
            widthLine.AddToClassList("road-prop-line");
            widthMinus = new RepeatButton(() => StepWidth(-1), 400, 90);
            widthPlus = new RepeatButton(() => StepWidth(1), 400, 90);
            foreach (var step in new[] { (widthMinus, "−", "roadbuilder.window.width.minus.tooltip"), (widthPlus, "+", "roadbuilder.window.width.plus.tooltip") })
            {
                step.Item1.text = step.Item2;
                step.Item1.AddToClassList("map-control-button");
                step.Item1.AddToClassList("road-prop-step");
                LocBindings.Tooltip(step.Item1, step.Item3); // i18n:keys
            }
            widthField = new TextField { maxLength = 8 };
            widthField.AddToClassList("picker-field");
            widthField.AddToClassList("road-prop-number");
            LocBindings.Tooltip(widthField, "roadbuilder.window.width.tooltip");
            WireNumberField(widthField, CommitWidth, () => CurrentLane() != null ? RoadTypeWindowRules.FormatWidth(CurrentLane().Width) : string.Empty);
            widthLine.Add(widthMinus);
            widthLine.Add(widthField);
            widthLine.Add(widthPlus);
            widthRow.Add(widthLine);
            widthRange = new Label();
            widthRange.AddToClassList("road-prop-note");
            widthRow.Add(widthRange);

            directionBox = PropRow(elementBox, "roadbuilder.panel.direction");
            var directions = new VisualElement();
            directions.AddToClassList("panel-tabs");
            directions.AddToClassList("road-segmented");
            againstButton = SegmentButton("arrow-down", "roadbuilder.window.direction.against", "roadbuilder.panel.backward.tooltip", () => ChangeDirection(OmsiRoadLaneDirection.Backward));
            withButton = SegmentButton("arrow-up", "roadbuilder.window.direction.with", "roadbuilder.panel.forward.tooltip", () => ChangeDirection(OmsiRoadLaneDirection.Forward));
            directions.Add(againstButton);
            directions.Add(withButton);
            directionBox.Add(directions);
            directionNone = LocBindings.NewLabel("roadbuilder.window.direction.none");
            directionNone.AddToClassList("road-prop-note");
            directionBox.Add(directionNone);

            tramBox = new VisualElement();
            tramBox.AddToClassList("road-prop");
            carsToggle = new Toggle();
            LocBindings.Text(carsToggle, "roadbuilder.panel.cars_too");
            LocBindings.Tooltip(carsToggle, "roadbuilder.panel.cars_too.tooltip");
            carsToggle.RegisterValueChangedCallback(evt => ChangeShared(evt.newValue));
            tramBox.Add(carsToggle);
            elementBox.Add(tramBox);

            var surfaceRow = PropRow(elementBox, "roadbuilder.panel.surface");
            var surfaceLine = new VisualElement();
            surfaceLine.AddToClassList("road-prop-line");
            var surfaceButton = new Button(ShowSurfaces);
            surfaceButton.AddToClassList("map-control-button");
            surfaceButton.AddToClassList("road-prop-surface");
            LocBindings.Tooltip(surfaceButton, "roadbuilder.panel.surface.tooltip");
            surfaceSwatch = new VisualElement { pickingMode = PickingMode.Ignore };
            surfaceSwatch.AddToClassList("road-prop-swatch");
            surfaceButton.Add(surfaceSwatch);
            surfaceName = new Label { pickingMode = PickingMode.Ignore };
            surfaceName.AddToClassList("road-prop-surface-name");
            surfaceButton.Add(surfaceName);
            surfaceLine.Add(surfaceButton);
            surfaceReset = new Button(() => ChangeTexture(null));
            surfaceReset.AddToClassList("map-control-button");
            surfaceReset.AddToClassList("road-window-icon-button");
            surfaceReset.Add(new IconElement("rotate-ccw"));
            LocBindings.Tooltip(surfaceReset, "roadbuilder.window.surface.reset");
            surfaceLine.Add(surfaceReset);
            surfaceRow.Add(surfaceLine);
            kerbNote = LocBindings.NewLabel("roadbuilder.window.surface.kerb_note");
            kerbNote.AddToClassList("road-prop-note");
            surfaceRow.Add(kerbNote);

            // Equipment: the row of objects along the element.
            equipmentHeader = new Button(() =>
            {
                equipmentOpen = !equipmentOpen;
                if (draft != null)
                    SyncInspector();
            });
            equipmentHeader.AddToClassList("road-prop-fold");
            equipmentChevron = new IconElement("chevron-right");
            equipmentChevron.AddToClassList("road-prop-fold-icon");
            equipmentHeader.Add(equipmentChevron);
            equipmentHeaderText = new Label { pickingMode = PickingMode.Ignore };
            equipmentHeaderText.AddToClassList("road-prop-fold-text");
            equipmentHeader.Add(equipmentHeaderText);
            LocBindings.Tooltip(equipmentHeader, "roadbuilder.window.equipment.tooltip");
            elementBox.Add(equipmentHeader);
            equipmentBody = new VisualElement();
            equipmentBody.AddToClassList("road-prop-fold-body");
            elementBox.Add(equipmentBody);
            equipmentHint = new Label();
            equipmentHint.AddToClassList("road-prop-note");
            equipmentBody.Add(equipmentHint);
            var objectLine = new VisualElement();
            objectLine.AddToClassList("road-prop-line");
            objectButton = new Button(() => RowObjectRequested?.Invoke());
            objectButton.AddToClassList("map-control-button");
            objectButton.AddToClassList("road-prop-surface");
            LocBindings.Tooltip(objectButton, "roadbuilder.panel.objects.tooltip");
            objectButton.Add(new IconElement("tree-deciduous"));
            objectButtonText = new Label { pickingMode = PickingMode.Ignore };
            objectButtonText.AddToClassList("road-prop-surface-name");
            objectButton.Add(objectButtonText);
            objectLine.Add(objectButton);
            objectClear = LocBindings.NewButton("roadbuilder.panel.none", () => ChangeRowObject(null));
            objectClear.AddToClassList("map-control-button");
            objectClear.AddToClassList("road-prop-reset");
            LocBindings.Tooltip(objectClear, "roadbuilder.panel.none.tooltip");
            objectLine.Add(objectClear);
            equipmentBody.Add(objectLine);
            rowFields = new VisualElement();
            equipmentBody.Add(rowFields);
            var intervalRow = PropRow(rowFields, "roadbuilder.panel.every");
            intervalField = new TextField { maxLength = 8 };
            intervalField.AddToClassList("picker-field");
            intervalField.AddToClassList("road-prop-number");
            LocBindings.Tooltip(intervalField, "roadbuilder.panel.every.tooltip");
            WireNumberField(intervalField, CommitInterval, () => CurrentLane() != null ? FormatNumber(CurrentLane().RowInterval) : string.Empty);
            intervalRow.Add(intervalField);
            var placeRow = PropRow(rowFields, "roadbuilder.window.place");
            var places = new VisualElement();
            places.AddToClassList("panel-tabs");
            places.AddToClassList("road-segmented");
            foreach (OmsiRoadRowPlace place in Enum.GetValues(typeof(OmsiRoadRowPlace)))
            {
                var chosen = place;
                var key = PlaceKey(place);
                var button = SegmentButton(null, key, key + ".tooltip", () => ChangePlace(chosen));
                places.Add(button);
                placeButtons.Add((button, place));
            }
            placeRow.Add(places);
            var turnRow = PropRow(rowFields, "roadbuilder.panel.turn");
            turnField = new TextField { maxLength = 8 };
            turnField.AddToClassList("picker-field");
            turnField.AddToClassList("road-prop-number");
            LocBindings.Tooltip(turnField, "roadbuilder.panel.turn.tooltip");
            WireNumberField(turnField, CommitTurn, () => CurrentLane() != null ? FormatNumber(CurrentLane().RowTurn) : string.Empty);
            turnRow.Add(turnField);

            // A line between two elements: Auto and the nine styles.
            lineBox = new VisualElement();
            root.Add(lineBox);
            var styles = new VisualElement();
            styles.AddToClassList("road-prop-styles");
            lineBox.Add(styles);
            AddLineButton(styles, null, "roadbuilder.panel.auto", "roadbuilder.panel.auto.tooltip");
            foreach (OmsiRoadMarking marking in Enum.GetValues(typeof(OmsiRoadMarking)))
                AddLineButton(styles, marking, MarkingKey(marking), MarkingKey(marking) + ".tooltip"); // i18n:keys
            var lineHint = LocBindings.NewLabel("roadbuilder.panel.line_hint");
            lineHint.AddToClassList("road-prop-note");
            lineBox.Add(lineHint);
        }

        private void AddLineButton(VisualElement parent, OmsiRoadMarking? marking, string textKey, string tooltipKey)
        {
            var button = new Button(() => ChangeLine(marking));
            button.AddToClassList("map-control-button");
            button.AddToClassList("road-prop-style");
            var label = new Label { pickingMode = PickingMode.Ignore };
            LocBindings.Text(label, textKey); // i18n:keys
            button.Add(label);
            LocBindings.Tooltip(button, tooltipKey); // i18n:keys
            parent.Add(button);
            lineButtons.Add((button, marking));
        }

        // ----- keys of the kinds, lines and places -----

        private static readonly string[] MarkingKeys =
        {
            "roadbuilder.panel.marking.none", "roadbuilder.panel.marking.dashed", "roadbuilder.panel.marking.solid", "roadbuilder.panel.marking.longdashed",
            "roadbuilder.panel.marking.double", "roadbuilder.panel.marking.soliddashed", "roadbuilder.panel.marking.dashedsolid", "roadbuilder.panel.marking.broad",
            "roadbuilder.panel.marking.broaddashed"
        };

        private static readonly string[] PlaceKeys = { "roadbuilder.panel.place.kerb", "roadbuilder.panel.place.middle", "roadbuilder.panel.place.outer" };

        private static string MarkingKey(OmsiRoadMarking marking)
        {
            return MarkingKeys[(int)marking];
        }

        private static string PlaceKey(OmsiRoadRowPlace place)
        {
            return PlaceKeys[(int)place];
        }

        private static List<string> KindNames()
        {
            var names = new List<string>();
            foreach (OmsiRoadLaneKind kind in Enum.GetValues(typeof(OmsiRoadLaneKind)))
                names.Add(Loc.T(RoadTypeWindowRules.LaneKey(kind))); // i18n:keys
            return names;
        }

        private static string FormatNumber(double value)
        {
            return value.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture);
        }

        private OmsiRoadLane CurrentLane()
        {
            return draft != null && draft.SelectionKind == OmsiRoadDraftSelectionKind.Element ? draft.LaneOf(draft.SelectedId) : null;
        }

        // ----- changes -----

        private void ChangeKind(int index)
        {
            if (draft == null || draft.SelectionKind != OmsiRoadDraftSelectionKind.Element || index < 0)
                return;
            if (draft.SetKind(draft.SelectedId, (OmsiRoadLaneKind)index))
                AfterCommand();
            else
                SyncInspector();
        }

        private bool CommitWidth(string text)
        {
            if (draft == null || draft.SelectionKind != OmsiRoadDraftSelectionKind.Element || !RoadTypeWindowRules.TryParseWidth(text, out var metres))
                return false;
            if (draft.SetWidth(draft.SelectedId, metres))
                AfterCommand();
            return true;
        }

        /// <summary>The - and + next to the width: steps of 0.05 m within the kind's range. Steps close together are one undo step.</summary>
        private void StepWidth(int steps)
        {
            if (draft == null || draft.SelectionKind != OmsiRoadDraftSelectionKind.Element)
                return;
            // A width still being typed is taken first, so the step goes from what the field shows.
            if (HasFocus(widthField) && RoadTypeWindowRules.TryParseWidth(widthField.value, out var typed))
                draft.SetWidth(draft.SelectedId, typed, "step");
            draft.StepWidth(draft.SelectedId, steps, "step");
            var owner = draft;
            stepIdle?.Pause();
            stepIdle = schedule.Execute(owner.EndGesture);
            stepIdle.ExecuteLater(700);
            Render();
            widthField.SetValueWithoutNotify(CurrentLane() != null ? RoadTypeWindowRules.FormatWidth(CurrentLane().Width) : string.Empty);
            Changed?.Invoke();
        }

        private void ChangeDirection(OmsiRoadLaneDirection direction)
        {
            if (draft != null && draft.SelectionKind == OmsiRoadDraftSelectionKind.Element && draft.SetDirection(draft.SelectedId, direction))
                AfterCommand();
        }

        private void ChangeShared(bool shared)
        {
            if (draft != null && draft.SelectionKind == OmsiRoadDraftSelectionKind.Element && draft.SetShared(draft.SelectedId, shared))
                AfterCommand();
        }

        private void ChangeTexture(string texture)
        {
            if (draft != null && draft.SelectionKind == OmsiRoadDraftSelectionKind.Element && draft.SetTexture(draft.SelectedId, texture))
                AfterCommand();
        }

        private void ChangeRowObject(string assetPath)
        {
            if (draft != null && draft.SelectionKind == OmsiRoadDraftSelectionKind.Element && draft.SetRowObject(draft.SelectedId, assetPath))
                AfterCommand();
        }

        private bool CommitInterval(string text)
        {
            if (draft == null || draft.SelectionKind != OmsiRoadDraftSelectionKind.Element || !RoadTypeWindowRules.TryParseNumber(text, out var metres) || metres <= 0.0)
                return false;
            if (draft.SetRowInterval(draft.SelectedId, metres))
                AfterCommand();
            return true;
        }

        private bool CommitTurn(string text)
        {
            if (draft == null || draft.SelectionKind != OmsiRoadDraftSelectionKind.Element || !RoadTypeWindowRules.TryParseNumber(text, out var degrees))
                return false;
            if (draft.SetRowTurn(draft.SelectedId, degrees))
                AfterCommand();
            return true;
        }

        private void ChangePlace(OmsiRoadRowPlace place)
        {
            if (draft != null && draft.SelectionKind == OmsiRoadDraftSelectionKind.Element && draft.SetRowPlace(draft.SelectedId, place))
                AfterCommand();
        }

        private void ChangeLine(OmsiRoadMarking? marking)
        {
            if (draft != null && draft.SelectionKind == OmsiRoadDraftSelectionKind.Boundary && draft.SetMarking(draft.SelectedId, marking))
                AfterCommand();
        }

        // ----- drawing what is selected into the controls -----

        /// <summary>
        /// Writes the selection's values into the controls that exist already. A field that has the focus keeps what is typed in it; only the
        /// parts that depend on the selection come and go (display), nothing is rebuilt.
        /// </summary>
        private void SyncInspector()
        {
            var kind = draft.SelectionKind;
            elementBox.style.display = kind == OmsiRoadDraftSelectionKind.Element ? DisplayStyle.Flex : DisplayStyle.None;
            lineBox.style.display = kind == OmsiRoadDraftSelectionKind.Boundary ? DisplayStyle.Flex : DisplayStyle.None;
            propHint.style.display = kind == OmsiRoadDraftSelectionKind.None ? DisplayStyle.Flex : DisplayStyle.None;
            if (kind == OmsiRoadDraftSelectionKind.None)
            {
                LocBindings.Text(propHeading, "roadbuilder.window.inspector.title"); // i18n:keys
                LocBindings.Text(propHint, draft.Count == 0 ? "roadbuilder.window.inspector.empty" : "roadbuilder.window.inspector.none"); // i18n:keys
                return;
            }
            if (kind == OmsiRoadDraftSelectionKind.Boundary)
            {
                SyncLine();
                return;
            }

            var id = draft.SelectedId;
            var lane = draft.LaneOf(id);
            LocBindings.Text(propHeading, "roadbuilder.window.inspector.element", ("number", draft.SelectedIndex + 1)); // i18n:keys
            kindDropdown.SetValueWithoutNotify(kindDropdown.choices[(int)lane.Kind]);
            if (!HasFocus(widthField))
                widthField.SetValueWithoutNotify(RoadTypeWindowRules.FormatWidth(lane.Width));
            draft.WidthRange(id, out var minimum, out var maximum);
            widthRange.text = Loc.T("roadbuilder.window.width.range", ("min", minimum), ("max", maximum));
            widthField.EnableInClassList("road-prop-invalid", lane.Width < minimum - 1e-9 || lane.Width > maximum + 1e-9);
            widthMinus.SetEnabled(lane.Width > minimum + 1e-9);
            widthPlus.SetEnabled(lane.Width < maximum - 1e-9);

            directionBox.style.display = DisplayStyle.Flex;
            var directed = lane.HasDirection;
            directionBox.Q<VisualElement>(className: "road-segmented").style.display = directed ? DisplayStyle.Flex : DisplayStyle.None;
            directionNone.style.display = directed ? DisplayStyle.None : DisplayStyle.Flex;
            againstButton.EnableInClassList("panel-tab-active", directed && lane.Direction == OmsiRoadLaneDirection.Backward);
            withButton.EnableInClassList("panel-tab-active", directed && lane.Direction == OmsiRoadLaneDirection.Forward);

            var tram = lane.Kind == OmsiRoadLaneKind.Tram;
            tramBox.style.display = tram ? DisplayStyle.Flex : DisplayStyle.None;
            carsToggle.SetValueWithoutNotify(lane.Shared);

            SyncSurfaceSwatch();
            surfaceReset.SetEnabled(lane.Texture != null);
            kerbNote.style.display = lane.Raised ? DisplayStyle.Flex : DisplayStyle.None;
            SyncEquipment(lane);
        }

        private void SyncSurfaceSwatch()
        {
            var lane = CurrentLane();
            if (lane == null)
                return;
            surfaceName.text = lane.Texture != null ? Path.GetFileNameWithoutExtension(lane.Texture) : Loc.T("roadbuilder.panel.standard");
            var preview = lane.Texture != null ? surfaces.Preview(lane.Texture) : null;
            surfaceSwatch.style.backgroundImage = preview != null ? new StyleBackground(preview) : new StyleBackground(StyleKeyword.Null);
            foreach (OmsiRoadLaneKind each in Enum.GetValues(typeof(OmsiRoadLaneKind)))
                surfaceSwatch.EnableInClassList("road-swatch-" + OmsiRoadLane.Word(each), preview == null && each == lane.Kind);
        }

        private void SyncEquipment(OmsiRoadLane lane)
        {
            var canHold = OmsiRoadLane.CanHoldRow(lane.Kind);
            var hasRow = lane.RowObject != null;
            equipmentChevron.SetIcon(equipmentOpen ? "chevron-down" : "chevron-right");
            equipmentHeaderText.text = hasRow ? Loc.T("roadbuilder.window.equipment.title_row", ("name", Path.GetFileNameWithoutExtension(lane.RowObject))) : Loc.T("roadbuilder.window.equipment.title_none");
            equipmentBody.style.display = equipmentOpen ? DisplayStyle.Flex : DisplayStyle.None;
            if (!equipmentOpen)
                return;
            // Objects stand only beside the traffic; a row given to another kind before shows, so it can be taken away.
            equipmentHint.style.display = hasRow && canHold ? DisplayStyle.None : DisplayStyle.Flex;
            LocBindings.Text(equipmentHint, !canHold ? hasRow ? "roadbuilder.window.equipment.not_allowed_row" : "roadbuilder.window.equipment.unavailable" : "roadbuilder.window.equipment.empty"); // i18n:keys
            objectButton.style.display = canHold || hasRow ? DisplayStyle.Flex : DisplayStyle.None;
            objectButton.SetEnabled(canHold);
            objectButtonText.text = hasRow ? Path.GetFileNameWithoutExtension(lane.RowObject) : Loc.T("roadbuilder.window.equipment.choose");
            objectButton.tooltip = hasRow ? lane.RowObject : Loc.T("roadbuilder.panel.objects.tooltip");
            objectClear.style.display = hasRow ? DisplayStyle.Flex : DisplayStyle.None;
            rowFields.style.display = hasRow && canHold ? DisplayStyle.Flex : DisplayStyle.None;
            if (!HasFocus(intervalField))
                intervalField.SetValueWithoutNotify(FormatNumber(lane.RowInterval));
            if (!HasFocus(turnField))
                turnField.SetValueWithoutNotify(FormatNumber(lane.RowTurn));
            intervalField.EnableInClassList("road-prop-invalid", lane.RowInterval < OmsiRoadLane.MinimumRowInterval);
            foreach (var pair in placeButtons)
                pair.Button.EnableInClassList("panel-tab-active", pair.Place == lane.RowPlace);
        }

        private void SyncLine()
        {
            var id = draft.SelectedId;
            LocBindings.Text(propHeading, "roadbuilder.window.inspector.line", ("number", draft.SelectedIndex + 1)); // i18n:keys
            var chosen = draft.LaneOf(id).RightMarking;
            var automatic = draft.ToRoadType().AutoMarking(draft.SelectedIndex);
            foreach (var pair in lineButtons)
            {
                pair.Button.EnableInClassList("panel-tab-active", pair.Marking == chosen);
                if (!pair.Marking.HasValue)
                    pair.Button.Q<Label>().text = Loc.T("roadbuilder.panel.auto_named", ("marking", Loc.T(MarkingKey(automatic)))); // i18n:keys
            }
        }

        /// <summary>The language changed: what the properties wrote from data is written again.</summary>
        private void RefreshPropertyTexts()
        {
            var index = kindDropdown.index;
            kindDropdown.choices = KindNames();
            kindDropdown.SetValueWithoutNotify(kindDropdown.choices[Mathf.Clamp(index, 0, kindDropdown.choices.Count - 1)]);
            if (surfacesView.style.display == DisplayStyle.Flex)
                RenderSurfacesHeader();
        }

        // ----- the surface picker page -----

        private void BuildSurfacesView()
        {
            surfacesView = new VisualElement();
            surfacesView.AddToClassList("road-window-surfaces");
            surfacesView.style.display = DisplayStyle.None;
            var header = new VisualElement();
            header.AddToClassList("road-window-surfaces-header");
            surfaceTitle = new Label();
            surfaceTitle.AddToClassList("inspector-section-heading");
            surfaceTitle.AddToClassList("road-window-surfaces-title");
            header.Add(surfaceTitle);
            surfaceSearch = new TextField();
            surfaceSearch.AddToClassList("picker-field");
            surfaceSearch.AddToClassList("road-window-surfaces-search");
            surfaceSearch.textEdition.placeholder = Loc.T("roadbuilder.panel.search");
            surfaceSearch.RegisterValueChangedCallback(_ => FillSurfaces());
            header.Add(surfaceSearch);
            var back = LocBindings.WithTooltip(LocBindings.NewButton("roadbuilder.panel.back", HideSurfaces), "roadbuilder.panel.back.tooltip");
            back.AddToClassList("map-control-button");
            header.Add(back);
            surfacesView.Add(header);
            surfaceTiles = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            surfaceTiles.AddToClassList("road-window-surface-tiles");
            surfaceTiles.contentContainer.AddToClassList("picker-tiles-content");
            surfacesView.Add(surfaceTiles);
            surfaceEmpty = new Label();
            surfaceEmpty.AddToClassList("panel-info");
            surfacesView.Add(surfaceEmpty);
            Content.Add(surfacesView);
        }

        /// <summary>Opens the surface picker for the selected element, as the Surface button does.</summary>
        public void ShowSurfaces()
        {
            if (draft == null || draft.SelectionKind != OmsiRoadDraftSelectionKind.Element)
                return;
            ShowPage(RoadWindowPage.Surfaces);
            RenderSurfacesHeader();
            if (!surfaces.Started && surfaces.InstallationRoot != null)
            {
                surfaces.Start();
                surfacePoll?.Pause();
                surfacePoll = schedule.Execute(() =>
                {
                    if (!surfaces.Ready)
                        return;
                    surfacePoll.Pause();
                    FillSurfaces();
                }).Every(100);
            }
            FillSurfaces();
        }

        /// <summary>Opens the palette under the cross-section, as Add element does; for command-line checks.</summary>
        public void ShowPalette()
        {
            paletteOpen = true;
            RenderAdding();
        }

        /// <summary>Opens the Equipment part of the properties; for command-line checks.</summary>
        public void ShowEquipment()
        {
            equipmentOpen = true;
            if (draft != null)
                SyncInspector();
        }

        /// <summary>
        /// Does what a person would do in the window, one step after the other, for command-line checks: <c>add:bus</c>, <c>side:left|right</c>,
        /// <c>dup</c>, <c>remove</c>, <c>left</c>, <c>right</c>, <c>sel:n</c>, <c>line:n</c>, <c>kind:tram</c>, <c>width:3,5</c> (typed), <c>plus</c>,
        /// <c>minus</c>, <c>dir:with|against</c>, <c>cars:on|off</c>, <c>marking:auto|solid|...</c>, <c>interval:n</c>, <c>turn:n</c>,
        /// <c>place:kerb|middle|outer</c>, <c>object:Sceneryobjects/x.sco</c>, <c>templates</c>, <c>template:tram-in-the-street</c> (chooses a card), <c>fromroad</c> (chooses From the chosen road), <c>use</c> (Use template), <c>undo</c>, <c>redo</c>, <c>save</c> (the main button of the footer), <c>savenew</c> (Save as new type), <c>delete</c>, <c>barname:text</c> (the name field of the bar) and <c>bar:1|2|3</c> (a button of the bar under the work area). The commands go through the same handlers as the controls.
        /// </summary>
        public void RunCommands(string list)
        {
            if (draft == null || string.IsNullOrWhiteSpace(list))
                return;
            foreach (var token in list.Split(','))
            {
                var parts = token.Split(new[] { ':' }, 2);
                var name = parts[0].Trim().ToLowerInvariant();
                var value = parts.Length > 1 ? parts[1].Trim().Replace(';', ',') : string.Empty;
                var number = int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : -1;
                switch (name)
                {
                    case "add":
                        foreach (OmsiRoadLaneKind kind in Enum.GetValues(typeof(OmsiRoadLaneKind)))
                            if (OmsiRoadLane.Word(kind) == value)
                                AddElement(kind);
                        break;
                    case "side": addSide = value == "left" ? OmsiRoadDraftSide.Before : OmsiRoadDraftSide.After; break;
                    case "dup": DuplicateSelected(); break;
                    case "remove": RemoveSelected(); break;
                    case "left": MoveSelected(-1); break;
                    case "right": MoveSelected(1); break;
                    case "sel": SelectLane(number - 1); break;
                    case "line": SelectLine(number - 1); break;
                    case "kind":
                        foreach (OmsiRoadLaneKind kind in Enum.GetValues(typeof(OmsiRoadLaneKind)))
                            if (OmsiRoadLane.Word(kind) == value)
                                ChangeKind((int)kind);
                        break;
                    case "width": CommitWidth(value); break;
                    case "plus": StepWidth(1); break;
                    case "minus": StepWidth(-1); break;
                    case "dir": ChangeDirection(value == "with" ? OmsiRoadLaneDirection.Forward : OmsiRoadLaneDirection.Backward); break;
                    case "cars": ChangeShared(value != "off"); break;
                    case "marking":
                        if (value == "auto")
                            ChangeLine(null);
                        else if (Enum.TryParse<OmsiRoadMarking>(value, true, out var marking))
                            ChangeLine(marking);
                        break;
                    case "interval": CommitInterval(value); break;
                    case "turn": CommitTurn(value); break;
                    case "place":
                        if (Enum.TryParse<OmsiRoadRowPlace>(value, true, out var place))
                            ChangePlace(place);
                        break;
                    case "object": SetRowObject(value.Replace('/', '\\'), 0d); break;
                    case "pickobject": RowObjectRequested?.Invoke(); break;
                    case "templates": ShowTemplates(true); break;
                    case "template": ChooseTemplate(value); break;
                    case "fromroad": ChooseTemplate(ChosenCardId); break;
                    case "use": UseTemplate(); break;
                    case "undo": UndoDraft(); break;
                    case "redo": RedoDraft(); break;
                    case "save": Save(); break;
                    case "savenew": SaveAsNewClicked(); break;
                    case "delete": AskDelete(); break;
                    case "barname":
                        noticeName.SetValueWithoutNotify(value);
                        RenderNotice();
                        break;
                    case "bar":
                        // The button of the bar a person would click: 1 first, 2 second, 3 third.
                        (number == 1 ? noticeFirstAction : number == 2 ? noticeSecondAction : noticeThirdAction)?.Invoke();
                        break;
                    default:
                        Debug.LogWarning("Road builder: unknown command \"" + token + "\".");
                        break;
                }
            }
        }

        /// <summary>Whether the surface list is read (or none is being read), for command-line checks that wait for it.</summary>
        public bool SurfacesReady => !surfaces.Started || surfaces.Ready;

        private void HideSurfaces()
        {
            ShowPage(RoadWindowPage.Work);
            Render();
        }

        private void RenderSurfacesHeader()
        {
            var lane = CurrentLane();
            if (lane == null)
                return;
            surfaceTitle.text = Loc.T("roadbuilder.window.surface.title", ("number", draft.SelectedIndex + 1), ("kind", Loc.T(RoadTypeWindowRules.LaneKey(lane.Kind)))); // i18n:keys
        }

        private void FillSurfaces()
        {
            surfaceTiles.Clear();
            tilePreviews.Clear();
            if (!surfaces.Ready)
            {
                surfaceEmpty.text = Loc.T(surfaces.InstallationRoot == null ? "roadbuilder.panel.surface.open_map" : "roadbuilder.panel.surface.reading"); // i18n:keys
                return;
            }
            var text = surfaceSearch.value?.Trim() ?? string.Empty;
            var lane = CurrentLane();
            var shown = 0;
            foreach (var path in surfaces.Paths)
            {
                if (text.Length > 0 && path.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (++shown > 240)
                    break;
                surfaceTiles.Add(SurfaceTile(path, lane != null && string.Equals(lane.Texture, path, StringComparison.OrdinalIgnoreCase)));
            }
            surfaceEmpty.text = shown == 0 ? text.Length > 0 ? Loc.T("roadbuilder.panel.surface.no_match", ("text", text)) : Loc.T("roadbuilder.panel.surface.none") : string.Empty;
        }

        private VisualElement SurfaceTile(string path, bool selected)
        {
            var tile = new VisualElement { tooltip = path, focusable = true };
            tile.AddToClassList("picker-tile");
            tile.EnableInClassList("picker-tile-selected", selected);
            var preview = new VisualElement();
            preview.AddToClassList("picker-tile-preview");
            if (surfaces.Preview(path) is Texture2D image)
                preview.style.backgroundImage = new StyleBackground(image);
            tilePreviews[path] = preview;
            tile.Add(preview);
            var name = new Label(Path.GetFileNameWithoutExtension(path));
            name.AddToClassList("picker-tile-name");
            tile.Add(name);
            tile.RegisterCallback<ClickEvent>(_ =>
            {
                HideSurfaces();
                ChangeTexture(path);
            });
            return tile;
        }

        /// <summary>Previews that arrived are put into the tiles that wait for them, without building the tiles anew.</summary>
        private void UpdateSurfaceTiles()
        {
            if (surfacesView.style.display != DisplayStyle.Flex)
                return;
            foreach (var pair in tilePreviews)
                if (surfaces.Preview(pair.Key) is Texture2D image)
                    pair.Value.style.backgroundImage = new StyleBackground(image);
        }
    }

    /// <summary>A button with a dashed border in its text colour (USS has no dashed border), for "Add element".</summary>
    internal sealed class DashedButton : Button
    {
        public DashedButton(Action clicked)
            : base(clicked)
        {
            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(_ => MarkDirtyRepaint());
        }

        private void Draw(MeshGenerationContext context)
        {
            var width = layout.width;
            var height = layout.height;
            if (width < 4f || height < 4f)
                return;
            var color = resolvedStyle.color;
            color.a *= 0.75f;
            var painter = context.painter2D;
            painter.strokeColor = color;
            painter.lineWidth = 1.5f;
            painter.lineCap = LineCap.Butt;
            const float Inset = 1f;
            const float Dash = 5f;
            const float Gap = 4f;
            painter.BeginPath();
            for (var x = Inset; x < width - Inset; x += Dash + Gap)
            {
                var end = Mathf.Min(width - Inset, x + Dash);
                painter.MoveTo(new Vector2(x, Inset));
                painter.LineTo(new Vector2(end, Inset));
                painter.MoveTo(new Vector2(x, height - Inset));
                painter.LineTo(new Vector2(end, height - Inset));
            }
            for (var y = Inset; y < height - Inset; y += Dash + Gap)
            {
                var end = Mathf.Min(height - Inset, y + Dash);
                painter.MoveTo(new Vector2(Inset, y));
                painter.LineTo(new Vector2(Inset, end));
                painter.MoveTo(new Vector2(width - Inset, y));
                painter.LineTo(new Vector2(width - Inset, end));
            }
            painter.Stroke();
        }
    }
}
