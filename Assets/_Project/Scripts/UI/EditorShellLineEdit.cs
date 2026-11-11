using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The line tool for the stops of a trip that exists (ADR 096): the trip's stops are loaded as the draft, stops are clicked in, taken out or moved, and
    /// saving puts them into the trip file (with the station links and stops the timetable lacks) while its times, tours and line stay.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        private string lineEditTrip;

        /// <summary>Opens the line tool on the stops of a trip; refused for a trip that runs on a track of its own.</summary>
        private void BeginEditTripStops(string trip)
        {
            if (!CanEdit || session == null || string.IsNullOrEmpty(trip))
                return;
            OmsiTripFile file;
            try
            {
                file = session.ReadTripFile(trip);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException)
            {
                Debug.LogException(exception);
                SetStatusT("line.status.failed", ("reason", exception.Message));
                return;
            }
            if (file == null || file.HasTrack || file.StopIds.Count == 0)
            {
                SetStatusT("line.status.trip_track", ("name", trip));
                return;
            }
            lineEditTrip = trip;
            if (activeTool == EditorTool.Line)
            {
                ApplyTripEditDraft();
                LineChanged(true);
            }
            else
            {
                SetTool(EditorTool.Line);
            }
        }

        /// <summary>Loads the stops of the trip being edited as the draft, or shows the tool for new lines when there is none.</summary>
        private void ApplyTripEditDraft()
        {
            if (lineEditTrip == null)
            {
                lineOptions.SetEditing(null);
                return;
            }
            var ids = session.ReadTripFile(lineEditTrip)?.StopIds ?? new List<int>();
            var items = new List<OmsiTripStopChoice>();
            foreach (var id in ids)
            {
                var stop = lineObjects.Find(each => each.ObjectId == id);
                if (stop == null)
                {
                    SetStatusT("line.status.trip_missing", ("name", lineEditTrip), ("id", id));
                    lineEditTrip = null;
                    lineOptions.SetEditing(null);
                    return;
                }
                items.Add(stop);
            }
            lineOut.Clear();
            lineBack.Clear();
            lineRing = false;
            lineBackMode = false;
            lineOut.AddRange(items);
            lineOptions.SetEditing(Loc.T("line.edit.banner", ("trip", lineEditTrip)));
        }

        /// <summary>The tool was left: a trip being edited is let go, with its draft.</summary>
        private void LeaveTripEdit()
        {
            if (lineEditTrip == null)
                return;
            lineEditTrip = null;
            lineOut.Clear();
            lineBack.Clear();
            lineOptions?.SetEditing(null);
        }

        private void SaveTripStops()
        {
            if (!CanEdit || session == null || lineEditTrip == null)
                return;
            if (linePlanOut == null || !linePlanOut.IsValid)
            {
                SetStatusT("line.status.need_route");
                return;
            }
            var trip = lineEditTrip;
            OmsiTerrainChange change;
            try
            {
                var old = session.ReadTripFile(trip)?.StopIds ?? new List<int>();
                var last = linePlanOut.Legs[linePlanOut.Legs.Count - 1].To;
                // A trip that now ends elsewhere shows the new end as its destination.
                var destination = old.Count > 0 && old[old.Count - 1] == last.ObjectId ? null : last.Name;
                change = session.ReplaceTripStops(trip, linePlanOut, destination);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is ArgumentException ||
                                              exception is UnauthorizedAccessException)
            {
                Debug.LogException(exception);
                SetStatusT("line.status.failed", ("reason", exception.Message));
                return;
            }
            if (change != null)
            {
                PushHistory(new ObjectEditStep { TerrainChange = change, Description = Loc.T("line.trip.step", ("name", trip)) });
                StartRoutesBuild(currentSummary);
            }
            SetStatusT("line.status.trip_saved", ("name", trip));
            Debug.Log("Line: " + statusLabel?.text);
            lineOut.Clear();
            lineEditTrip = null;
            lineOptions.SetEditing(null);
            lineTimetable = OmsiTimetable.Read(session.Workspace);
            LineChanged(true);
            OpenTimetable(null);
        }

        /// <summary>Saves a line on rails: a track and a trip on it for the way out and, unless it is a ring, for the way back (ADR 097).</summary>
        private void SaveRailLine(string line)
        {
            if (lineTrackOut == null || !lineTrackOut.IsValid)
            {
                SetStatusT("line.status.need_route");
                return;
            }
            var specs = new List<OmsiRailTripSpec>();
            var outFirst = lineTrackOut.Stations[0].Stop;
            var outLast = lineTrackOut.Stations[lineTrackOut.Stations.Count - 1].Stop;
            if (lineRing)
            {
                specs.Add(RailSpec(line + "_ring", lineTrackOut, outFirst.Name));
            }
            else
            {
                specs.Add(RailSpec(line + "_" + FileName(outFirst.Name) + "-" + FileName(outLast.Name), lineTrackOut, outLast.Name));
                if (lineTrackBack != null)
                {
                    if (!lineTrackBack.IsValid)
                    {
                        SetStatusT("line.status.need_route");
                        return;
                    }
                    var backFirst = lineTrackBack.Stations[0].Stop;
                    var backLast = lineTrackBack.Stations[lineTrackBack.Stations.Count - 1].Stop;
                    specs.Add(RailSpec(line + "_" + FileName(backFirst.Name) + "-" + FileName(backLast.Name), lineTrackBack, backLast.Name));
                }
            }
            OmsiTerrainChange change;
            try
            {
                change = session.CreateRailLine(currentSummary, line, lineOptions.Group, specs);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is ArgumentException ||
                                              exception is UnauthorizedAccessException)
            {
                Debug.LogException(exception);
                SetStatusT("line.status.failed", ("reason", exception.Message));
                return;
            }
            if (change == null)
                return;
            PushHistory(new ObjectEditStep { TerrainChange = change, Description = Loc.T("line.step", ("name", line)) });
            StartRoutesBuild(currentSummary);
            SetStatusT("line.status.saved", ("name", line), ("count", specs.Count));
            Debug.Log("Line: " + statusLabel?.text);
            lineOut.Clear();
            lineBack.Clear();
            lineRing = false;
            lineBackMode = false;
            lineName = string.Empty;
            lineOptions.SetName(string.Empty);
            lineTimetable = OmsiTimetable.Read(session.Workspace);
            LineChanged(true);
            OpenTimetable(line);
        }

        /// <summary>A trip on rails with a running time suggested from the length of the way (50 km/h and half a minute at each station).</summary>
        private static OmsiRailTripSpec RailSpec(string name, OmsiTrackPlan plan, string destination)
        {
            var minutes = Math.Max(1.0, Math.Round((plan.LengthMeters / (50.0 / 3.6) / 60.0 + 0.5 * plan.Stations.Count) * 2.0) / 2.0);
            return new OmsiRailTripSpec { Name = name, Destination = destination, Plan = plan, RunningMinutes = minutes };
        }

        /// <summary>For checks: -editTrip Name [-lineStops id,id,...] [-lineSave] loads a trip's stops into the line tool; with stops they replace the draft.</summary>
        private void RunEditTripArguments(Func<string, int, string> argument, string[] args)
        {
            if (Array.IndexOf(args, "-editTrip") < 0 || session == null || !session.CanEdit)
                return;
            BeginEditTripStops(argument("-editTrip", 1));
            if (lineEditTrip == null)
                return;
            if (lineNetworkTask != null)
                lineNetworkTask.Wait();
            lineNetwork = lineNetworkTask?.Result ?? lineNetwork ?? OmsiPathNetwork.ForMap(currentSummary, OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath));
            var stops = argument("-lineStops", 1);
            if (!string.IsNullOrEmpty(stops))
            {
                lineOut.Clear();
                foreach (var text in stops.Split(','))
                    if (int.TryParse(text.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var id))
                    {
                        var stop = lineObjects.Find(each => each.ObjectId == id);
                        if (stop != null)
                            lineOut.Add(stop);
                    }
            }
            LineChanged(true);
            Debug.Log("Line plan: out " + (linePlanOut == null ? "none" : linePlanOut.IsValid ? Math.Round(linePlanOut.LengthMeters) + " m" : string.Join(" ", linePlanOut.Problems)));
            if (Array.IndexOf(args, "-lineSave") >= 0)
                SaveLine();
        }
    }
}
