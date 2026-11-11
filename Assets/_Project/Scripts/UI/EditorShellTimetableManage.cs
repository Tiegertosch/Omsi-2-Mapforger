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
    /// Renaming and deleting lines and trips from the timetable dialog (ADR 096). Each is one undoable step that keeps the references right; what a tour or a
    /// chrono variant still names is refused with its name. Edits waiting in the dialog are written first.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        private bool timetableManageOpen;
        private string manageLineName = string.Empty;
        private string manageTrip;
        private string manageTripName = string.Empty;

        private void AddManageRows()
        {
            var fold = new Foldout { text = Loc.T("timetable.manage"), value = timetableManageOpen };
            fold.RegisterValueChangedCallback(change =>
            {
                if (change.target == fold)
                    timetableManageOpen = change.newValue;
            });
            fold.style.marginBottom = 6;
            if (timetableLine != null)
            {
                var line = timetableLine;
                var name = new TextField { value = manageLineName.Length > 0 ? manageLineName : line, isDelayed = false };
                name.AddToClassList("picker-field");
                name.style.width = StyleKeyword.Auto;
                name.RegisterValueChangedCallback(change => manageLineName = change.newValue);
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                name.style.flexGrow = 1;
                row.Add(name);
                row.Add(SmallButton("timetable.manage.rename", "timetable.manage.rename.tooltip", () =>
                {
                    var target = (manageLineName.Length > 0 ? manageLineName : line).Trim();
                    ManageTimetable(() => session.RenameLine(line, target), target, "timetable.status.line_renamed", ("old", line), ("name", target));
                }));
                row.Add(SmallButton("timetable.manage.delete", "timetable.manage.delete.line.tooltip", () =>
                    ManageTimetable(() => session.DeleteLine(line), null, "timetable.status.line_deleted", ("name", line))));
                AddManageRow(fold, "timetable.manage.line", row);
            }
            if (timetableTripNames.Count > 0)
            {
                if (manageTrip == null || !timetableTripNames.Contains(manageTrip))
                {
                    manageTrip = timetableTripNames[0];
                    manageTripName = manageTrip;
                }
                var trips = new DropdownField(timetableTripNames, timetableTripNames.IndexOf(manageTrip));
                trips.AddToClassList("new-map-dropdown");
                trips.RegisterValueChangedCallback(change =>
                {
                    manageTrip = change.newValue;
                    manageTripName = change.newValue;
                    RefreshTimetable();
                });
                AddManageRow(fold, "timetable.manage.trip", trips);
                var trip = manageTrip;
                var tripName = new TextField { value = manageTripName, isDelayed = false };
                tripName.AddToClassList("picker-field");
                tripName.style.width = StyleKeyword.Auto;
                tripName.style.flexGrow = 1;
                tripName.RegisterValueChangedCallback(change => manageTripName = change.newValue);
                var tripRow = new VisualElement();
                tripRow.style.flexDirection = FlexDirection.Row;
                tripRow.Add(tripName);
                tripRow.Add(SmallButton("timetable.manage.rename", "timetable.manage.rename.trip.tooltip", () =>
                {
                    var target = manageTripName.Trim();
                    ManageTimetable(() => session.RenameTrip(trip, target), timetableLine, "timetable.status.trip_renamed", ("old", trip), ("name", target));
                }));
                tripRow.Add(SmallButton("timetable.manage.delete", "timetable.manage.delete.trip.tooltip", () =>
                    ManageTimetable(() => session.DeleteTrip(trip), timetableLine, "timetable.status.trip_deleted", ("name", trip))));
                AddManageRow(fold, "timetable.manage.name", tripRow);
                var stopsButton = SmallButton("timetable.manage.stops.button", "timetable.manage.stops.tooltip", () =>
                {
                    if (!FlushTimetableEdits())
                        return;
                    CloseTimetable();
                    BeginEditTripStops(trip);
                });
                stopsButton.style.marginLeft = 0;
                AddManageRow(fold, "timetable.manage.stops", stopsButton);
            }
            timetableBody.Add(fold);
        }

        private readonly HashSet<string> stopTimesOpen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<int, string> timetableStopNames = new Dictionary<int, string>();

        /// <summary>
        /// The times and stopping of the single stops of a trip, per profile: the minute after the trip began at which the bus arrives or leaves (a stop with a
        /// time is one the bus waits at; the others are timed by the running time and the lengths of the station links) and how it stops there.
        /// </summary>
        private void AddStopTimes(OmsiTripFile tripFile, string tripName)
        {
            var ids = tripFile.StopIds;
            if (ids.Count == 0)
                return;
            var profiles = tripFile.Profiles;
            for (var profileIndex = 0; profileIndex < profiles.Count; profileIndex++)
            {
                var profile = profileIndex;
                var key = tripName + "#" + profile;
                var fold = new Foldout { text = Loc.T("timetable.stoptimes", ("trip", tripName), ("name", profiles[profile].Key)), value = stopTimesOpen.Contains(key) };
                fold.RegisterValueChangedCallback(change =>
                {
                    if (change.target != fold)
                        return;
                    if (change.newValue)
                        stopTimesOpen.Add(key);
                    else
                        stopTimesOpen.Remove(key);
                });
                var hint = new Label(Loc.T("timetable.stoptimes.hint"));
                hint.AddToClassList("panel-info");
                hint.style.whiteSpace = WhiteSpace.Normal;
                fold.Add(hint);
                var settings = tripFile.GetStopSettings(profile);
                var header = new VisualElement();
                header.style.flexDirection = FlexDirection.Row;
                var spacer = new VisualElement();
                spacer.style.flexGrow = 1;
                spacer.style.flexBasis = 0;
                spacer.style.flexShrink = 1;
                header.Add(spacer);
                foreach (var labelKey in new[] { "timetable.stoptimes.arrival", "timetable.stoptimes.departure", "timetable.stoptimes.stopping" })
                {
                    var label = LocBindings.NewLabel(labelKey); // i18n:keys
                    label.AddToClassList("popup-description");
                    label.style.width = 84;
                    label.style.marginLeft = 4;
                    header.Add(label);
                }
                fold.Add(header);
                var kinds = new List<string>
                {
                    Loc.T("timetable.stopping.0"), Loc.T("timetable.stopping.1"), Loc.T("timetable.stopping.2"), Loc.T("timetable.stopping.3"), Loc.T("timetable.stopping.4")
                };
                for (var stopIndex = 0; stopIndex < ids.Count; stopIndex++)
                {
                    var stop = stopIndex;
                    var row = new VisualElement();
                    row.style.flexDirection = FlexDirection.Row;
                    row.style.alignItems = Align.Center;
                    row.style.marginBottom = 2;
                    var name = timetableStopNames.TryGetValue(ids[stop], out var known) ? known : ids[stop].ToString(CultureInfo.InvariantCulture);
                    var label = new Label(Loc.T("timetable.stoptimes.stop", ("number", stop + 1), ("name", name)));
                    label.AddToClassList("popup-description");
                    label.style.flexGrow = 1;
                    label.style.flexBasis = 0;
                    label.style.flexShrink = 1;
                    label.style.minWidth = 0;
                    row.Add(label);
                    row.Add(StopTimeField(settings.Arrivals, stop, minutes => tripFile.SetArrival(profile, stop, minutes), tripName));
                    row.Add(StopTimeField(settings.Departures, stop, minutes => tripFile.SetDeparture(profile, stop, minutes), tripName));
                    var kind = new DropdownField(kinds, settings.Stopping.TryGetValue(stop, out var current) ? Mathf.Clamp(current, 0, 4) : 0);
                    kind.AddToClassList("new-map-dropdown");
                    kind.style.width = 84;
                    kind.style.marginLeft = 4;
                    kind.RegisterValueChangedCallback(change =>
                    {
                        var chosen = kinds.IndexOf(change.newValue);
                        tripFile.SetStopping(profile, stop, chosen <= 0 ? (int?)null : chosen);
                        timetableDirtyTrips.Add(tripName);
                        RefreshTimetable();
                    });
                    row.Add(kind);
                    fold.Add(row);
                }
                timetableBody.Add(fold);
            }
        }

        private TextField StopTimeField(Dictionary<int, double> values, int stop, Action<double?> set, string tripName)
        {
            var field = new TextField { value = values.TryGetValue(stop, out var minutes) ? minutes.ToString("0.###", CultureInfo.InvariantCulture) : string.Empty, isDelayed = true };
            field.AddToClassList("picker-field");
            field.style.width = 84;
            field.style.marginLeft = 4;
            field.RegisterValueChangedCallback(change =>
            {
                var text = change.newValue.Trim().Replace(',', '.');
                if (text.Length == 0)
                    set(null);
                else if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value >= 0.0)
                    set(value);
                else
                    SetStatusT("timetable.stoptimes.invalid");
                timetableDirtyTrips.Add(tripName);
                RefreshTimetable();
            });
            return field;
        }

        private static void AddManageRow(VisualElement parent, string labelKey, VisualElement control)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 3;
            var label = LocBindings.NewLabel(labelKey); // i18n:keys
            label.AddToClassList("popup-description");
            label.style.width = 120;
            label.style.flexShrink = 0;
            row.Add(label);
            control.style.flexGrow = 1;
            row.Add(control);
            parent.Add(row);
        }

        /// <summary>Writes the edits waiting in the dialog, so a rename or delete works on the files as they are shown.</summary>
        private bool FlushTimetableEdits()
        {
            try
            {
                foreach (var name in timetableDirtyLines)
                    if (timetableLines.TryGetValue(name, out var file))
                        session.WriteLineFile(name, file);
                foreach (var name in timetableDirtyTrips)
                    if (timetableTrips.TryGetValue(name, out var file) && file != null)
                        session.WriteTripFile(name, file);
                timetableDirtyLines.Clear();
                timetableDirtyTrips.Clear();
                return true;
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogException(exception);
                SetStatusT("timetable.status.failed", ("reason", exception.Message));
                return false;
            }
        }

        private void ManageTimetable(Func<OmsiTerrainChange> operation, string showLine, string statusKey, params (string Name, object Value)[] arguments)
        {
            if (session == null || !session.CanEdit || !FlushTimetableEdits())
                return;
            OmsiTerrainChange change;
            try
            {
                change = operation();
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException || exception is IOException ||
                                              exception is UnauthorizedAccessException)
            {
                Debug.LogException(exception);
                SetStatusT("timetable.status.manage_failed", ("reason", exception.Message));
                return;
            }
            if (change != null)
            {
                PushHistory(new ObjectEditStep { TerrainChange = change, Description = Loc.T("timetable.manage.step") });
                if (currentSummary != null)
                    StartRoutesBuild(currentSummary);
                SetStatusT(statusKey, arguments); // i18n:keys
                Debug.Log("Timetable: " + statusLabel?.text);
            }
            manageLineName = string.Empty;
            manageTrip = null;
            OpenTimetable(showLine);
        }

        /// <summary>The "Remove from timetable" button of the bus stop group in the inspector.</summary>
        private void RemoveBusStopFromInspector(OmsiTileSummary tile, OmsiObjectRecord record)
        {
            if (!CanEdit || session == null)
                return;
            OmsiTerrainChange change;
            try
            {
                change = session.RemoveBusStop(currentSummary, tile, record);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException || exception is IOException ||
                                              exception is UnauthorizedAccessException)
            {
                Debug.LogException(exception);
                SetStatusT("busstop.status.remove_failed", ("reason", exception.Message));
                return;
            }
            if (change == null)
                return;
            PushHistory(new ObjectEditStep { TerrainChange = change, Description = Loc.T("busstop.remove.step", ("id", record.Id)) });
            StartRoutesBuild(currentSummary);
            SetStatusT("busstop.status.removed", ("id", record.Id));
            Debug.Log("Bus stop: " + statusLabel?.text);
        }
    }
}
