using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// Making a new trip (ADR 093): choose the stops in order, the editor finds the way between them on the map's vehicle lanes, shows it, and writes
    /// the trip file, the station links the timetable lacks and the stops it adds. Opened from the timetable dialog.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        private VisualElement newTripDialog;
        private ScrollView newTripBody;
        private readonly List<OmsiTripStopChoice> newTripStops = new List<OmsiTripStopChoice>();
        private List<OmsiTripStopChoice> newTripKnown = new List<OmsiTripStopChoice>();
        private List<OmsiTripStopChoice> newTripObjects = new List<OmsiTripStopChoice>();
        private string newTripObjectChosen;
        private OmsiTripPlan newTripPlan;
        private OmsiPathNetwork newTripNetwork;
        private string newTripName = string.Empty;
        private string newTripLine = string.Empty;
        private string newTripDestination = string.Empty;
        private string newTripProfile = "standard";
        private string newTripNewStopName = string.Empty;
        private double newTripMinutes = 10.0;
        private bool newTripMinutesEdited;
        private string newTripChosen;

        private void SetUpNewTrip(VisualElement root)
        {
            newTripDialog = new VisualElement { name = "new-trip-dialog" };
            newTripDialog.AddToClassList("dialog-overlay");
            newTripDialog.style.display = DisplayStyle.None;
            var dialog = new VisualElement();
            dialog.AddToClassList("dialog");
            dialog.AddToClassList("content-folders");
            dialog.style.minWidth = 560;
            var title = LocBindings.NewLabel("newtrip.title");
            title.AddToClassList("popup-title");
            dialog.Add(title);
            var description = LocBindings.NewLabel("newtrip.description");
            description.AddToClassList("popup-description");
            dialog.Add(description);
            newTripBody = new ScrollView(ScrollViewMode.Vertical);
            newTripBody.AddToClassList("content-folders-list");
            newTripBody.style.maxHeight = 520;
            dialog.Add(newTripBody);
            var buttons = new VisualElement();
            buttons.AddToClassList("dialog-buttons");
            var cancel = LocBindings.NewButton("newtrip.cancel", CloseNewTrip);
            cancel.AddToClassList("map-control-button");
            buttons.Add(cancel);
            var create = LocBindings.NewButton("newtrip.create", CreateNewTrip);
            create.AddToClassList("map-control-button");
            create.AddToClassList("dialog-primary");
            buttons.Add(create);
            dialog.Add(buttons);
            newTripDialog.Add(dialog);
            root.Add(newTripDialog);
        }

        private void OpenNewTrip()
        {
            if (newTripDialog == null || currentSummary == null || session == null || !session.CanEdit || EditingVariant)
                return;
            try
            {
                newTripKnown = session.ReadStopChoices(currentSummary);
                newTripObjects = session.FindStopObjects(currentSummary);
                newTripObjectChosen = null;
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException)
            {
                Debug.LogException(exception);
                SetStatusT("newtrip.status.failed", ("reason", exception.Message));
                return;
            }
            newTripStops.Clear();
            newTripPlan = null;
            newTripNetwork = null;
            newTripName = string.Empty;
            newTripDestination = string.Empty;
            newTripLine = timetableLine ?? string.Empty;
            newTripMinutesEdited = false;
            newTripChosen = null;
            RefreshNewTrip();
            newTripDialog.style.display = DisplayStyle.Flex;
        }

        private void CloseNewTrip()
        {
            mapCanvas?.ClearRoadPreview();
            HidePopup(newTripDialog);
        }

        private static string StopLabel(OmsiTripStopChoice stop)
        {
            return stop.Name + " (" + stop.ObjectId.ToString(CultureInfo.InvariantCulture) + ")";
        }

        private void RefreshNewTrip()
        {
            var scroll = newTripBody.scrollOffset;
            newTripBody.Clear();

            var heading = new Label(Loc.T("newtrip.stops", ("count", newTripStops.Count)));
            heading.AddToClassList("inspector-group-title");
            newTripBody.Add(heading);
            for (var index = 0; index < newTripStops.Count; index++)
            {
                var position = index;
                var stop = newTripStops[index];
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.marginBottom = 2;
                var label = new Label((index + 1).ToString(CultureInfo.InvariantCulture) + ". " + StopLabel(stop) + (stop.IsNew ? " *" : string.Empty)) { tooltip = Loc.T("newtrip.stop.new.tooltip") };
                label.AddToClassList("popup-description");
                label.style.flexGrow = 1;
                row.Add(label);
                row.Add(RowButton("↑", "newtrip.stop.up", () => MoveStop(position, -1)));
                row.Add(RowButton("↓", "newtrip.stop.down", () => MoveStop(position, 1)));
                row.Add(RowButton("×", "newtrip.stop.remove", () =>
                {
                    newTripStops.RemoveAt(position);
                    StopsChanged();
                }));
                newTripBody.Add(row);
            }

            if (newTripKnown.Count > 0)
            {
                var labels = newTripKnown.Select(StopLabel).ToList();
                if (newTripChosen == null || !labels.Contains(newTripChosen))
                    newTripChosen = labels[0];
                var choice = new DropdownField(labels, labels.IndexOf(newTripChosen));
                choice.AddToClassList("new-map-dropdown");
                choice.RegisterValueChangedCallback(change => newTripChosen = change.newValue);
                var addRow = new VisualElement();
                addRow.style.flexDirection = FlexDirection.Row;
                choice.style.flexGrow = 1;
                addRow.Add(choice);
                addRow.Add(SmallButton("newtrip.stop.add", "newtrip.stop.add.tooltip", () =>
                {
                    var found = newTripKnown.Find(stop => StopLabel(stop) == newTripChosen);
                    if (found != null)
                    {
                        newTripStops.Add(found);
                        StopsChanged();
                    }
                }));
                AddNewTripRow("newtrip.stop.choose", addRow);
            }

            var newStopName = new TextField { value = newTripNewStopName, isDelayed = true };
            newStopName.AddToClassList("picker-field");
            newStopName.style.width = StyleKeyword.Auto;
            newStopName.RegisterValueChangedCallback(change => newTripNewStopName = change.newValue);
            AddNewTripRow("newtrip.stop.name", newStopName);
            if (newTripObjects.Count > 0)
            {
                var objectLabels = newTripObjects.Select(stop => stop.Name + " (" + Mathf.RoundToInt((float)stop.World.X) + ", " + Mathf.RoundToInt((float)stop.World.Y) + ")").ToList();
                var chosenIndex = Math.Max(0, objectLabels.IndexOf(newTripObjectChosen));
                newTripObjectChosen = objectLabels[chosenIndex];
                var objectChoice = new DropdownField(objectLabels, chosenIndex);
                objectChoice.AddToClassList("new-map-dropdown");
                objectChoice.style.flexGrow = 1;
                objectChoice.RegisterValueChangedCallback(change => newTripObjectChosen = change.newValue);
                var objectRow = new VisualElement();
                objectRow.style.flexDirection = FlexDirection.Row;
                objectRow.Add(objectChoice);
                objectRow.Add(SmallButton("newtrip.stop.selected.button", "newtrip.stop.object.tooltip", () =>
                {
                    var index = objectLabels.IndexOf(newTripObjectChosen);
                    if (index < 0)
                        return;
                    var found = newTripObjects[index];
                    var named = new OmsiTripStopChoice
                    {
                        Name = string.IsNullOrWhiteSpace(newTripNewStopName) ? found.Name : newTripNewStopName.Trim(), TileIndex = found.TileIndex, ObjectId = found.ObjectId,
                        World = found.World, IsNew = true
                    };
                    newTripNewStopName = string.Empty;
                    newTripStops.Add(named);
                    StopsChanged();
                }));
                AddNewTripRow("newtrip.stop.object", objectRow);
            }
            var selectedRow = new VisualElement();
            selectedRow.style.flexDirection = FlexDirection.Row;
            selectedRow.Add(SmallButton("newtrip.stop.selected.button", "newtrip.stop.selected.tooltip", AddSelectedObjectAsStop));
            selectedRow.style.marginLeft = -4;
            AddNewTripRow("newtrip.stop.selected", selectedRow);
            if (newTripObjects.Count == 0 && newTripKnown.Count == 0)
            {
                var none = new Label(Loc.T("newtrip.stop.none"));
                none.AddToClassList("panel-info");
                none.style.whiteSpace = WhiteSpace.Normal;
                newTripBody.Add(none);
            }

            var find = SmallButton("newtrip.find", "newtrip.find.tooltip", FindTripRoute);
            find.style.marginLeft = 0;
            find.style.marginTop = 6;
            newTripBody.Add(find);
            if (newTripPlan != null)
            {
                var result = new Label(Loc.T("newtrip.result"));
                result.AddToClassList("inspector-group-title");
                result.style.marginTop = 6;
                newTripBody.Add(result);
                foreach (var leg in newTripPlan.Legs)
                {
                    var key = leg.Existing ? "newtrip.leg.existing" : leg.Entries.Count == 0 ? "newtrip.leg.none" : "newtrip.leg";
                    var text = new Label(Loc.T(key, ("from", leg.From.Name), ("to", leg.To.Name), ("length", Math.Round(leg.LengthMeters)))); // i18n:keys
                    text.AddToClassList("popup-description");
                    text.style.whiteSpace = WhiteSpace.Normal;
                    newTripBody.Add(text);
                }
                foreach (var problem in newTripPlan.Problems)
                {
                    var warning = new Label(problem);
                    warning.AddToClassList("panel-info");
                    warning.AddToClassList("panel-info-warning");
                    warning.style.whiteSpace = WhiteSpace.Normal;
                    newTripBody.Add(warning);
                }
                if (newTripPlan.IsValid)
                {
                    var total = new Label(Loc.T("newtrip.total", ("count", newTripStops.Count), ("length", Math.Round(newTripPlan.LengthMeters)), ("minutes", newTripMinutes)));
                    total.AddToClassList("popup-description");
                    total.style.whiteSpace = WhiteSpace.Normal;
                    newTripBody.Add(total);
                }
            }

            var name = new TextField { value = newTripName, isDelayed = true };
            name.AddToClassList("picker-field");
            name.style.width = StyleKeyword.Auto;
            name.RegisterValueChangedCallback(change => newTripName = change.newValue);
            AddNewTripRow("newtrip.field.name", name);
            var line = new TextField { value = newTripLine, isDelayed = true };
            line.AddToClassList("picker-field");
            line.style.width = StyleKeyword.Auto;
            line.RegisterValueChangedCallback(change => newTripLine = change.newValue);
            AddNewTripRow("newtrip.field.line", line);
            var destination = new TextField { value = newTripDestination, isDelayed = true };
            destination.AddToClassList("picker-field");
            destination.style.width = StyleKeyword.Auto;
            destination.RegisterValueChangedCallback(change => newTripDestination = change.newValue);
            AddNewTripRow("newtrip.field.destination", destination);
            var profile = new TextField { value = newTripProfile, isDelayed = true };
            profile.AddToClassList("picker-field");
            profile.style.width = StyleKeyword.Auto;
            profile.RegisterValueChangedCallback(change => newTripProfile = change.newValue);
            AddNewTripRow("newtrip.field.profile", profile);
            var minutes = new FloatField { value = (float)newTripMinutes, isDelayed = true };
            minutes.AddToClassList("picker-field");
            minutes.RegisterValueChangedCallback(change =>
            {
                newTripMinutes = Math.Max(0.5, change.newValue);
                newTripMinutesEdited = true;
                RefreshNewTrip();
            });
            AddNewTripRow("newtrip.field.minutes", minutes);
            newTripBody.schedule.Execute(() => newTripBody.scrollOffset = scroll);
        }

        private void StopsChanged()
        {
            newTripPlan = null;
            mapCanvas?.ClearRoadPreview();
            if (newTripStops.Count >= 2)
            {
                if (string.IsNullOrWhiteSpace(newTripDestination) || newTripDestination == string.Empty)
                    newTripDestination = newTripStops[newTripStops.Count - 1].Name;
            }
            RefreshNewTrip();
        }

        private void MoveStop(int index, int delta)
        {
            var target = index + delta;
            if (target < 0 || target >= newTripStops.Count)
                return;
            var stop = newTripStops[index];
            newTripStops.RemoveAt(index);
            newTripStops.Insert(target, stop);
            StopsChanged();
        }

        private void AddSelectedObjectAsStop()
        {
            if (selectedObject == null || selectedTile == null || currentSummary == null)
            {
                SetStatusT("newtrip.status.no_selection");
                return;
            }
            var known = newTripKnown.Find(stop => stop.ObjectId == selectedObject.Id);
            var choice = known ?? session.NewStopChoice(currentSummary, selectedTile, selectedObject, newTripNewStopName);
            if (choice == null)
            {
                SetStatusT("newtrip.status.no_selection");
                return;
            }
            newTripStops.Add(choice);
            StopsChanged();
        }

        private void FindTripRoute()
        {
            if (newTripStops.Count < 2)
            {
                SetStatusT("newtrip.status.need_stops");
                return;
            }
            try
            {
                var installation = OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath);
                newTripNetwork = newTripNetwork ?? OmsiPathNetwork.ForMap(currentSummary, installation);
                newTripPlan = session.PlanTrip(currentSummary, installation, newTripStops, newTripNetwork);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is ArgumentException)
            {
                Debug.LogException(exception);
                SetStatusT("newtrip.status.failed", ("reason", exception.Message));
                return;
            }
            if (!newTripMinutesEdited && newTripPlan.IsValid)
                // 25 km/h on average and half a minute at each stop, to the half minute.
                newTripMinutes = Math.Max(1.0, Math.Round((newTripPlan.LengthMeters / (25.0 / 3.6) / 60.0 + 0.5 * (newTripStops.Count - 1)) * 2.0) / 2.0);
            mapCanvas?.SetRoadPreview(newTripPlan.Legs.SelectMany(leg => leg.Lines).ToList(), newTripPlan.IsValid, null, null, null);
            RefreshNewTrip();
            Debug.Log("New trip route: " + newTripPlan.Legs.Count + " legs, " + Math.Round(newTripPlan.LengthMeters) + " m" +
                      (newTripPlan.Problems.Count > 0 ? ", problems: " + string.Join(" ", newTripPlan.Problems) : string.Empty));
        }

        private void CreateNewTrip()
        {
            if (newTripPlan == null || !newTripPlan.IsValid)
            {
                if (newTripStops.Count < 2)
                    SetStatusT("newtrip.status.need_stops");
                else
                    SetStatusT("newtrip.status.need_route");
                return;
            }
            if (string.IsNullOrWhiteSpace(newTripName))
            {
                SetStatusT("newtrip.status.need_name");
                return;
            }
            List<string> files;
            try
            {
                files = session.CreateTrip(newTripPlan, newTripName.Trim(), string.IsNullOrWhiteSpace(newTripDestination) ? newTripStops[newTripStops.Count - 1].Name : newTripDestination,
                    newTripLine, string.IsNullOrWhiteSpace(newTripProfile) ? "standard" : newTripProfile, newTripMinutes);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is ArgumentException ||
                                              exception is UnauthorizedAccessException)
            {
                Debug.LogException(exception);
                SetStatusT("newtrip.status.failed", ("reason", exception.Message));
                return;
            }
            var created = newTripName.Trim();
            CloseNewTrip();
            if (timetableDialog != null && timetableDialog.resolvedStyle.display != DisplayStyle.None)
            {
                timetableTripNames = session.Workspace.EnumerateFiles("TTData", "*.ttp", false).Select(path => Path.GetFileNameWithoutExtension(path))
                    .OrderBy(each => each, StringComparer.OrdinalIgnoreCase).ToList();
                seriesTrip = created;
                RefreshTimetable();
            }
            StartRoutesBuild(currentSummary);
            SetStatusT("newtrip.status.created", ("name", created), ("count", files.Count));
            Debug.Log("New trip: " + statusLabel?.text);
        }

        private void AddNewTripRow(string labelKey, VisualElement control)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 3;
            var label = LocBindings.NewLabel(labelKey); // i18n:keys
            label.AddToClassList("popup-description");
            label.style.width = 130;
            label.style.flexShrink = 0;
            row.Add(label);
            control.style.flexGrow = 1;
            row.Add(control);
            newTripBody.Add(row);
        }

        private static Button RowButton(string text, string tooltipKey, Action clicked)
        {
            var button = new Button(clicked) { text = text, tooltip = Loc.T(tooltipKey) }; // i18n:keys
            button.AddToClassList("map-control-button");
            button.style.marginLeft = 4;
            return button;
        }

        /// <summary>For checks: -newTrip id,id,... name [line] [-newTripCreate] makes a trip over the stops of Busstops.cfg with these object IDs.</summary>
        private void RunNewTripArguments(Func<string, int, string> argument, string[] args)
        {
            var ids = argument("-newTrip", 1);
            if (Array.IndexOf(args, "-newTrip") < 0 || ids == null || session == null || !session.CanEdit)
                return;
            OpenNewTrip();
            foreach (var text in ids.Split(','))
                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                {
                    var stop = newTripKnown.Find(each => each.ObjectId == id);
                    if (stop != null)
                        newTripStops.Add(stop);
                }
            newTripName = argument("-newTrip", 2) ?? string.Empty;
            newTripLine = argument("-newTrip", 3) ?? string.Empty;
            StopsChanged();
            FindTripRoute();
            if (Array.IndexOf(args, "-newTripCreate") >= 0)
                CreateNewTrip();
        }
    }
}
