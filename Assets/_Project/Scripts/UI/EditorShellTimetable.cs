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
    /// The timetable editor (ADR 092): a dialog from the Lines tab to change the tours of a line (departures, trips, shifting a whole tour, a trip every
    /// so many minutes) and the running times of the trips. Edits are made on the files in memory; Apply writes the changed <c>.ttl</c> and
    /// <c>.ttp</c> files of the project copy and shows the lines again.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        private VisualElement timetableDialog;
        private ScrollView timetableBody;
        private readonly Dictionary<string, OmsiLineFile> timetableLines = new Dictionary<string, OmsiLineFile>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, OmsiTripFile> timetableTrips = new Dictionary<string, OmsiTripFile>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> timetableDirtyLines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> timetableDirtyTrips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private List<string> timetableLineNames = new List<string>();
        private List<string> timetableTripNames = new List<string>();
        private string timetableLine;
        private int timetableTour;
        private string seriesTrip;
        private int seriesProfile;
        private string seriesFrom = "6:00";
        private string seriesTo = "20:00";
        private double seriesEvery = 15.0;
        private double shiftMinutes = 5.0;
        private bool timetableNewLineOpen;
        private string timetableNewLineName = string.Empty;
        private string timetableNewLineGroup;

        private void SetUpTimetable(VisualElement root)
        {
            timetableDialog = new VisualElement { name = "timetable-dialog" };
            timetableDialog.AddToClassList("dialog-overlay");
            timetableDialog.style.display = DisplayStyle.None;
            var dialog = new VisualElement();
            dialog.AddToClassList("dialog");
            dialog.AddToClassList("content-folders");
            dialog.style.minWidth = 560;
            var title = LocBindings.NewLabel("timetable.title");
            title.AddToClassList("popup-title");
            dialog.Add(title);
            var description = LocBindings.NewLabel("timetable.description");
            description.AddToClassList("popup-description");
            dialog.Add(description);
            timetableBody = new ScrollView(ScrollViewMode.Vertical);
            timetableBody.AddToClassList("content-folders-list");
            timetableBody.style.maxHeight = 520;
            dialog.Add(timetableBody);
            var buttons = new VisualElement();
            buttons.AddToClassList("dialog-buttons");
            var cancel = LocBindings.NewButton("timetable.cancel", CloseTimetable);
            cancel.AddToClassList("map-control-button");
            buttons.Add(cancel);
            var apply = LocBindings.NewButton("timetable.apply", ApplyTimetable);
            apply.AddToClassList("map-control-button");
            apply.AddToClassList("dialog-primary");
            buttons.Add(apply);
            dialog.Add(buttons);
            timetableDialog.Add(dialog);
            root.Add(timetableDialog);
        }

        /// <summary>The button in the Lines tab.</summary>
        private Button TimetableButton()
        {
            var button = new Button(() => OpenTimetable(highlightedLine)) { text = Loc.T("timetable.open"), tooltip = Loc.T("timetable.open.tooltip") };
            button.AddToClassList("map-control-button");
            button.style.marginBottom = 6;
            return button;
        }

        private void OpenTimetable(string line)
        {
            if (timetableDialog == null || currentSummary == null || session == null || !session.CanEdit || EditingVariant)
                return;
            timetableLines.Clear();
            timetableTrips.Clear();
            timetableDirtyLines.Clear();
            timetableDirtyTrips.Clear();
            try
            {
                timetableLineNames = session.TimetableLineNames();
                timetableTripNames = session.Workspace.EnumerateFiles("TTData", "*.ttp", false)
                    .Select(path => Path.GetFileNameWithoutExtension(path)).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException)
            {
                Debug.LogException(exception);
                SetStatusT("timetable.status.failed", ("reason", exception.Message));
                return;
            }
            try
            {
                timetableStopNames = OmsiTimetable.Read(session.Workspace).BusStops.GroupBy(stop => stop.ObjectId).ToDictionary(group => group.Key, group => group.First().Name);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException)
            {
                Debug.LogException(exception);
                timetableStopNames = new Dictionary<int, string>();
            }
            timetableLine = timetableLineNames.Find(name => string.Equals(name, line, StringComparison.OrdinalIgnoreCase)) ??
                            (timetableLineNames.Count > 0 ? timetableLineNames[0] : null);
            timetableTour = 0;
            seriesTrip = null;
            RefreshTimetable();
            timetableDialog.style.display = DisplayStyle.Flex;
        }

        private void CloseTimetable()
        {
            HidePopup(timetableDialog);
        }

        private OmsiLineFile TimetableLineFile()
        {
            if (timetableLine == null)
                return null;
            if (!timetableLines.TryGetValue(timetableLine, out var file))
            {
                file = session.ReadLineFile(timetableLine);
                timetableLines[timetableLine] = file;
            }
            return file;
        }

        private OmsiTripFile TimetableTripFile(string trip)
        {
            if (!timetableTrips.TryGetValue(trip, out var file))
            {
                file = session.ReadTripFile(trip);
                timetableTrips[trip] = file;
            }
            return file;
        }

        /// <summary>Marks the line file as changed and draws the dialog again.</summary>
        private void TimetableChanged()
        {
            timetableDirtyLines.Add(timetableLine);
            RefreshTimetable();
        }

        private void RefreshTimetable()
        {
            var scroll = timetableBody.scrollOffset;
            timetableBody.Clear();
            AddNewLineAndTripRow();
            AddManageRows();
            if (timetableLine == null)
            {
                var empty = new Label(Loc.T("timetable.empty"));
                empty.AddToClassList("panel-info");
                timetableBody.Add(empty);
                return;
            }
            OmsiLineFile file;
            List<OmsiLineTour> tours;
            try
            {
                file = TimetableLineFile();
                tours = file.Tours;
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException)
            {
                Debug.LogException(exception);
                SetStatusT("timetable.status.failed", ("reason", exception.Message));
                return;
            }
            timetableTour = Mathf.Clamp(timetableTour, 0, Math.Max(0, tours.Count - 1));

            var lineChoice = new DropdownField(timetableLineNames, Math.Max(0, timetableLineNames.IndexOf(timetableLine)));
            lineChoice.AddToClassList("new-map-dropdown");
            lineChoice.RegisterValueChangedCallback(change =>
            {
                timetableLine = change.newValue;
                timetableTour = 0;
                RefreshTimetable();
            });
            AddTimetableRow("timetable.line", lineChoice);

            var tourNames = tours.Select((tour, index) => Loc.T("timetable.tour_item", ("name", tour.Number), ("group", tour.VehicleGroup), ("count", tour.Trips.Count))).ToList();
            var tourRow = new VisualElement();
            tourRow.style.flexDirection = FlexDirection.Row;
            tourRow.style.flexGrow = 1;
            if (tourNames.Count > 0)
            {
                var tourChoice = new DropdownField(tourNames, timetableTour);
                tourChoice.AddToClassList("new-map-dropdown");
                tourChoice.style.flexGrow = 1;
                tourChoice.RegisterValueChangedCallback(change =>
                {
                    timetableTour = tourNames.IndexOf(change.newValue);
                    RefreshTimetable();
                });
                tourRow.Add(tourChoice);
            }
            tourRow.Add(SmallButton("timetable.tour.add", "timetable.tour.add.tooltip", () =>
            {
                var next = (tours.Count + 1).ToString(CultureInfo.InvariantCulture);
                timetableTour = file.AddTour(next, tours.Count > 0 ? tours[Math.Min(timetableTour, tours.Count - 1)].VehicleGroup : string.Empty);
                TimetableChanged();
            }));
            if (tours.Count > 0)
                tourRow.Add(SmallButton("timetable.tour.remove", "timetable.tour.remove.tooltip", () =>
                {
                    file.RemoveTour(timetableTour);
                    TimetableChanged();
                }));
            AddTimetableRow("timetable.tour", tourRow);
            if (tours.Count == 0)
                return;

            var current = tours[timetableTour];
            var name = new TextField { value = current.Number, isDelayed = true };
            name.AddToClassList("picker-field");
            name.style.width = StyleKeyword.Auto;
            name.style.flexGrow = 1;
            name.RegisterValueChangedCallback(change =>
            {
                file.SetTour(timetableTour, change.newValue, current.VehicleGroup);
                TimetableChanged();
            });
            AddTimetableRow("timetable.tour.name", name);
            var group = new TextField { value = current.VehicleGroup, isDelayed = true };
            group.AddToClassList("picker-field");
            group.style.width = StyleKeyword.Auto;
            group.style.flexGrow = 1;
            group.RegisterValueChangedCallback(change =>
            {
                file.SetTour(timetableTour, current.Number, change.newValue);
                TimetableChanged();
            });
            AddTimetableRow("timetable.tour.group", group);

            var days = new VisualElement();
            days.style.flexDirection = FlexDirection.Row;
            days.style.flexWrap = Wrap.Wrap;
            days.tooltip = Loc.T("timetable.days.tooltip");
            var dayKeys = new[] { "timetable.day.1", "timetable.day.2", "timetable.day.3", "timetable.day.4", "timetable.day.5", "timetable.day.6", "timetable.day.7",
                "timetable.day.holiday", "timetable.day.term", "timetable.day.vacation" };
            for (var bit = 0; bit < dayKeys.Length; bit++)
            {
                var mask = 1 << bit;
                var toggle = new Toggle(Loc.T(dayKeys[bit])) { value = current.DayMask >= 0 && (current.DayMask & mask) != 0 }; // i18n:keys
                toggle.style.marginRight = 8;
                toggle.RegisterValueChangedCallback(change =>
                {
                    var now = file.Tours[timetableTour].DayMask;
                    file.SetTourDays(timetableTour, change.newValue ? Math.Max(0, now) | mask : Math.Max(0, now) & ~mask);
                    TimetableChanged();
                });
                days.Add(toggle);
            }
            AddTimetableRow("timetable.days", days);

            var shiftRow = new VisualElement();
            shiftRow.style.flexDirection = FlexDirection.Row;
            var shiftField = new FloatField { value = (float)shiftMinutes, isDelayed = true };
            shiftField.AddToClassList("picker-field");
            shiftField.RegisterValueChangedCallback(change => shiftMinutes = change.newValue);
            shiftRow.Add(shiftField);
            shiftRow.Add(SmallButton("timetable.shift.button", "timetable.shift.tooltip", () =>
            {
                file.ShiftTour(timetableTour, shiftMinutes);
                TimetableChanged();
            }));
            AddTimetableRow("timetable.shift", shiftRow);

            AddSeriesRow(file);

            var heading = new Label(Loc.T("timetable.trips", ("count", current.Trips.Count)));
            heading.AddToClassList("inspector-group-title");
            heading.style.marginTop = 8;
            timetableBody.Add(heading);
            for (var index = 0; index < current.Trips.Count; index++)
                timetableBody.Add(TripRow(file, current.Trips[index], index));

            AddRunningTimes(current);
            timetableBody.schedule.Execute(() => timetableBody.scrollOffset = scroll);
        }

        /// <summary>The buttons for a new line and a new trip above the tours, and the small form of a new line.</summary>
        private void AddNewLineAndTripRow()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 6;
            var line = SmallButton("timetable.newline", "timetable.newline.tooltip", () =>
            {
                timetableNewLineOpen = !timetableNewLineOpen;
                RefreshTimetable();
            });
            line.style.marginLeft = 0;
            row.Add(line);
            row.Add(SmallButton("timetable.newtrip", "timetable.newtrip.tooltip", () =>
            {
                CloseTimetable();
                SetTool(EditorTool.Line);
            }));
            timetableBody.Add(row);
            if (!timetableNewLineOpen)
                return;
            var name = new TextField { value = timetableNewLineName, isDelayed = true };
            name.AddToClassList("picker-field");
            name.style.width = StyleKeyword.Auto;
            name.RegisterValueChangedCallback(change => timetableNewLineName = change.newValue);
            AddTimetableRow("timetable.newline.name", name);
            var groups = session.ReadAiGroups();
            if (groups.Count > 0)
            {
                if (timetableNewLineGroup == null || !groups.Contains(timetableNewLineGroup))
                    timetableNewLineGroup = groups.Find(each => each.IndexOf("bus", StringComparison.OrdinalIgnoreCase) >= 0) ?? groups[0];
                var group = new DropdownField(groups, groups.IndexOf(timetableNewLineGroup));
                group.AddToClassList("new-map-dropdown");
                group.RegisterValueChangedCallback(change => timetableNewLineGroup = change.newValue);
                AddTimetableRow("timetable.newline.group", group);
            }
            else
            {
                var group = new TextField { value = timetableNewLineGroup ?? string.Empty, isDelayed = true };
                group.AddToClassList("picker-field");
                group.style.width = StyleKeyword.Auto;
                group.RegisterValueChangedCallback(change => timetableNewLineGroup = change.newValue);
                AddTimetableRow("timetable.newline.group", group);
            }
            var create = SmallButton("timetable.newline.create", "timetable.newline.create.tooltip", () =>
            {
                var lineName = (timetableNewLineName ?? string.Empty).Trim();
                if (lineName.Length == 0)
                {
                    SetStatusT("timetable.status.need_line_name");
                    return;
                }
                try
                {
                    session.CreateLineFile(lineName, timetableNewLineGroup ?? string.Empty);
                    timetableLineNames = session.TimetableLineNames();
                }
                catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is ArgumentException)
                {
                    Debug.LogException(exception);
                    SetStatusT("timetable.status.failed", ("reason", exception.Message));
                    return;
                }
                timetableLine = timetableLineNames.Find(each => string.Equals(each, lineName, StringComparison.OrdinalIgnoreCase)) ?? timetableLine;
                timetableTour = 0;
                timetableNewLineOpen = false;
                timetableNewLineName = string.Empty;
                StartRoutesBuild(currentSummary);
                SetStatusT("timetable.status.line_created", ("name", lineName));
                RefreshTimetable();
            });
            create.style.marginLeft = 0;
            timetableBody.Add(create);
        }

        private void AddSeriesRow(OmsiLineFile file)
        {
            var heading = new Label(Loc.T("timetable.series"));
            heading.AddToClassList("inspector-group-title");
            heading.style.marginTop = 8;
            timetableBody.Add(heading);
            var tripChoices = timetableTripNames;
            if (seriesTrip == null || !tripChoices.Contains(seriesTrip))
                seriesTrip = tripChoices.Find(trip => trip.StartsWith(timetableLine + "_", StringComparison.OrdinalIgnoreCase)) ??
                             (tripChoices.Count > 0 ? tripChoices[0] : null);
            if (seriesTrip == null)
                return;
            var trip = new DropdownField(tripChoices, tripChoices.IndexOf(seriesTrip));
            trip.AddToClassList("new-map-dropdown");
            trip.RegisterValueChangedCallback(change => seriesTrip = change.newValue);
            AddTimetableRow("timetable.series.trip", trip);
            var profile = new IntegerField { value = seriesProfile, isDelayed = true };
            profile.AddToClassList("picker-field");
            profile.RegisterValueChangedCallback(change => seriesProfile = Math.Max(0, change.newValue));
            AddTimetableRow("timetable.series.profile", profile);
            var times = new VisualElement();
            times.style.flexDirection = FlexDirection.Row;
            var from = new TextField { value = seriesFrom, isDelayed = true };
            from.AddToClassList("picker-field");
            from.RegisterValueChangedCallback(change => seriesFrom = change.newValue);
            var to = new TextField { value = seriesTo, isDelayed = true };
            to.AddToClassList("picker-field");
            to.style.marginLeft = 6;
            to.RegisterValueChangedCallback(change => seriesTo = change.newValue);
            var every = new FloatField { value = (float)seriesEvery, isDelayed = true };
            every.AddToClassList("picker-field");
            every.style.marginLeft = 6;
            every.RegisterValueChangedCallback(change => seriesEvery = change.newValue);
            times.Add(from);
            times.Add(to);
            times.Add(every);
            AddTimetableRow("timetable.series.span", times);
            var add = SmallButton("timetable.series.add", "timetable.series.add.tooltip", () =>
            {
                if (!TryParseClock(seriesFrom, out var first) || !TryParseClock(seriesTo, out var last) || !(seriesEvery > 0.0) || last < first)
                {
                    SetStatusT("timetable.status.bad_series");
                    return;
                }
                var count = file.AddSeries(timetableTour, seriesTrip, seriesProfile, first, last, seriesEvery);
                SetStatusT("timetable.status.series", ("count", count));
                TimetableChanged();
            });
            timetableBody.Add(add);
        }

        private VisualElement TripRow(OmsiLineFile file, OmsiLineTrip trip, int index)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 2;
            var time = new TextField { value = FormatClock(trip.DepartureMinutes), isDelayed = true, tooltip = Loc.T("timetable.trip.time.tooltip") };
            time.AddToClassList("picker-field");
            time.RegisterValueChangedCallback(change =>
            {
                if (!TryParseClock(change.newValue, out var minutes))
                {
                    SetStatusT("timetable.status.bad_time", ("text", change.newValue));
                    time.SetValueWithoutNotify(FormatClock(trip.DepartureMinutes));
                    return;
                }
                file.SetDeparture(timetableTour, index, minutes);
                TimetableChanged();
            });
            row.Add(time);
            var label = new Label(trip.Trip) { tooltip = trip.Trip };
            label.AddToClassList("popup-description");
            label.style.flexGrow = 1;
            label.style.marginLeft = 8;
            row.Add(label);
            var profile = new IntegerField { value = trip.Profile, isDelayed = true, tooltip = Loc.T("timetable.trip.profile.tooltip") };
            profile.AddToClassList("picker-field");
            profile.style.width = 44;
            profile.RegisterValueChangedCallback(change =>
            {
                file.SetProfile(timetableTour, index, Math.Max(0, change.newValue));
                TimetableChanged();
            });
            row.Add(profile);
            var remove = new Button(() =>
            {
                file.RemoveTrip(timetableTour, index);
                TimetableChanged();
            }) { text = "×", tooltip = Loc.T("timetable.trip.remove.tooltip") };
            remove.AddToClassList("map-control-button");
            remove.style.marginLeft = 4;
            row.Add(remove);
            return row;
        }

        /// <summary>The running times of the profiles of the trips this tour runs, one trip file each.</summary>
        private void AddRunningTimes(OmsiLineTour tour)
        {
            var trips = tour.Trips.Select(trip => trip.Trip).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var added = false;
            foreach (var tripName in trips)
            {
                OmsiTripFile tripFile;
                try
                {
                    tripFile = TimetableTripFile(tripName);
                }
                catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is ArgumentException)
                {
                    Debug.LogException(exception);
                    continue;
                }
                if (tripFile == null)
                    continue;
                if (!added)
                {
                    var heading = new Label(Loc.T("timetable.running"));
                    heading.AddToClassList("inspector-group-title");
                    heading.style.marginTop = 8;
                    timetableBody.Add(heading);
                    var hint = new Label(Loc.T("timetable.running.hint"));
                    hint.AddToClassList("panel-info");
                    timetableBody.Add(hint);
                    added = true;
                }
                var profiles = tripFile.Profiles;
                for (var index = 0; index < profiles.Count; index++)
                {
                    var profileIndex = index;
                    var captured = tripName;
                    var row = new VisualElement();
                    row.style.flexDirection = FlexDirection.Row;
                    row.style.alignItems = Align.Center;
                    row.style.marginBottom = 2;
                    var minutes = new FloatField { value = (float)profiles[index].Value, isDelayed = true };
                    minutes.AddToClassList("picker-field");
                    minutes.RegisterValueChangedCallback(change =>
                    {
                        tripFile.SetRunningMinutes(profileIndex, Math.Max(0.0, change.newValue));
                        timetableDirtyTrips.Add(captured);
                        RefreshTimetable();
                    });
                    row.Add(minutes);
                    var label = new Label(Loc.T("timetable.running.profile", ("trip", tripName), ("index", index), ("name", profiles[index].Key)));
                    label.AddToClassList("popup-description");
                    label.style.marginLeft = 8;
                    row.Add(label);
                    timetableBody.Add(row);
                }
                AddStopTimes(tripFile, tripName);
            }
        }

        private void AddTimetableRow(string labelKey, VisualElement control)
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
            timetableBody.Add(row);
        }

        private static Button SmallButton(string textKey, string tooltipKey, Action clicked)
        {
            var button = new Button(clicked) { text = Loc.T(textKey), tooltip = Loc.T(tooltipKey) }; // i18n:keys
            button.AddToClassList("map-control-button");
            button.style.marginLeft = 4;
            return button;
        }

        private void ApplyTimetable()
        {
            if (session == null || !session.CanEdit)
                return;
            var written = 0;
            try
            {
                foreach (var name in timetableDirtyLines)
                    if (timetableLines.TryGetValue(name, out var file))
                    {
                        session.WriteLineFile(name, file);
                        written++;
                    }
                foreach (var name in timetableDirtyTrips)
                    if (timetableTrips.TryGetValue(name, out var file) && file != null)
                    {
                        session.WriteTripFile(name, file);
                        written++;
                    }
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogException(exception);
                SetStatusT("timetable.status.failed", ("reason", exception.Message));
                return;
            }
            CloseTimetable();
            if (written > 0 && currentSummary != null)
                StartRoutesBuild(currentSummary);
            SetStatusT("timetable.status.applied", ("count", written));
            Debug.Log("Timetable: " + statusLabel?.text);
        }

        /// <summary>Hours and minutes as "H:MM", with ":SS" when the time has seconds.</summary>
        internal static string FormatClock(double minutes)
        {
            var seconds = (long)Math.Round(minutes * 60.0);
            var text = (seconds / 3600).ToString(CultureInfo.InvariantCulture) + ":" + (seconds / 60 % 60).ToString("00", CultureInfo.InvariantCulture);
            return seconds % 60 == 0 ? text : text + ":" + (seconds % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>A time as "H:MM", "H:MM:SS" or minutes after midnight ("247"), to minutes.</summary>
        internal static bool TryParseClock(string text, out double minutes)
        {
            minutes = 0.0;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            var parts = text.Trim().Replace(',', '.').Split(':');
            if (parts.Length == 1)
                return double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out minutes) && minutes >= 0.0 && !double.IsInfinity(minutes);
            if (parts.Length > 3 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hours) ||
                !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var mins) || hours < 0 || mins < 0 || mins > 59)
                return false;
            var secs = 0;
            if (parts.Length == 3 && (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out secs) || secs < 0 || secs > 59))
                return false;
            minutes = hours * 60.0 + mins + secs / 60.0;
            return true;
        }

        /// <summary>Opens the timetable editor on a line, for checks: -timetable [line].</summary>
        private void RunTimetableArguments(Func<string, int, string> argument, string[] args)
        {
            if (Array.IndexOf(args, "-timetable") < 0 || session == null || !session.CanEdit)
                return;
            var line = argument("-timetable", 1);
            OpenTimetable(line != null && line.StartsWith("-", StringComparison.Ordinal) ? null : line);
            // -timetableShift minutes, -timetableSeries trip profile from to every, -timetableApply
            if (double.TryParse(argument("-timetableShift", 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var shift) && timetableLine != null)
            {
                TimetableLineFile().ShiftTour(timetableTour, shift);
                TimetableChanged();
            }
            if (Array.IndexOf(args, "-timetableSeries") >= 0 && timetableLine != null && TryParseClock(argument("-timetableSeries", 3), out var first) &&
                TryParseClock(argument("-timetableSeries", 4), out var last) &&
                double.TryParse(argument("-timetableSeries", 5), NumberStyles.Float, CultureInfo.InvariantCulture, out var every) &&
                int.TryParse(argument("-timetableSeries", 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var profile))
            {
                var count = TimetableLineFile().AddSeries(timetableTour, argument("-timetableSeries", 1), profile, first, last, every);
                Debug.Log("Timetable series: " + count);
                TimetableChanged();
            }
            if (Array.IndexOf(args, "-timetableApply") >= 0)
                ApplyTimetable();
        }
    }
}
