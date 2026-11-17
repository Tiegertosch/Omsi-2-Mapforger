using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The name of a bus stop in the inspector (ADR 094): an object that is a bus stop (<c>bus_stop.sco</c>, or listed in Busstops.cfg) gets a field
    /// for the name the lines and the line tool show, as OMSI's editor has it on the object. The name is kept in <c>TTData\Busstops.cfg</c>.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        private void AddBusStopGroup(VisualElement editor, OmsiTileSummary tile, object element)
        {
            var record = element as OmsiObjectRecord;
            if (record == null || session == null || currentSummary == null)
                return;
            try
            {
                if (!session.IsBusStop(currentSummary, tile, record))
                    return;
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException)
            {
                // A stop list that cannot be read must not stop the rest of the inspector.
                Debug.LogException(exception);
                return;
            }
            string listedName;
            try
            {
                listedName = session.BusStopName(currentSummary, tile, record);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException)
            {
                Debug.LogException(exception);
                listedName = null;
            }
            var listed = listedName != null;
            // The name is the object's first label ("Name"); renaming happens only there.
            var firstLabel = record.Labels.Count > 0 ? record.Labels[0] : string.Empty;
            AddInspectorGroup(editor, "bus-stop", Loc.T("inspector.busstop.group"), true, body =>
            {
                var status = new Label(listed ? Loc.T("inspector.busstop.listed", ("name", listedName)) : Loc.T("inspector.busstop.not_listed"));
                status.AddToClassList("inspector-edit-hint");
                status.style.whiteSpace = WhiteSpace.Normal;
                body.Add(status);
                if (listed)
                {
                    // Cheap per render: the session keeps what it read from the trip files until one changes. A failure leaves the state
                    // unknown (null): the button stays disabled, and the click would refuse again (RemoveBusStopFromInspector).
                    IReadOnlyList<string> users;
                    try
                    {
                        users = session.StopUsers(record.Id);
                    }
                    catch (Exception exception) when (exception is InvalidOperationException || exception is IOException ||
                                                      exception is UnauthorizedAccessException || exception is InvalidDataException)
                    {
                        Debug.LogException(exception);
                        users = null;
                    }
                    var free = LabelEditRules.CanRemoveFromStopList(true, users);
                    var remove = new Button(() => RemoveBusStopFromInspector(tile, record)) { text = Loc.T("busstop.remove") };
                    remove.AddToClassList("map-control-button");
                    remove.SetEnabled(CanEdit && free);
                    body.Add(remove);
                    if (free)
                    {
                        remove.tooltip = Loc.T("busstop.remove.tooltip");
                        return;
                    }
                    string why;
                    if (users == null)
                    {
                        why = Loc.T("busstop.remove.unknown");
                    }
                    else
                    {
                        var shown = LabelEditRules.TripsToShow(users, 3, out var more);
                        var list = string.Join(", ", shown);
                        if (more > 0)
                            list = Loc.T("busstop.remove.used_more", ("list", list), ("count", more));
                        why = Loc.T("busstop.remove.used", ("trips", list));
                    }
                    remove.tooltip = why;
                    // A disabled button may not show its tooltip, so the reason is also written under it.
                    var reason = new Label(why);
                    reason.AddToClassList("inspector-edit-hint");
                    reason.style.whiteSpace = WhiteSpace.Normal;
                    body.Add(reason);
                    return;
                }
                // The hint reflects the name at draw time only. The button stays enabled while editing is allowed: a name typed in the Name line
                // is written one tick after the click's focus loss, so the click reads the name anew (AddBusStopFromInspector).
                var hasName = LabelEditRules.CanAddToStopList(firstLabel, false);
                var add = new Button(() => AddBusStopFromInspector(tile, record))
                {
                    text = Loc.T("inspector.busstop.add"),
                    tooltip = Loc.T("inspector.busstop.add.tooltip")
                };
                add.AddToClassList("map-control-button");
                add.SetEnabled(CanEdit);
                body.Add(add);
                if (!hasName)
                {
                    var need = new Label(Loc.T("inspector.busstop.need_name"));
                    need.AddToClassList("inspector-edit-hint");
                    need.style.whiteSpace = WhiteSpace.Normal;
                    body.Add(need);
                }
            });
        }

        /// <summary>
        /// "Add to stop list": writes a typed but not yet written Name line first (its own undo step), then reads the tile, the object and its
        /// first label anew, as the map is read again after that write, and lists the stop under that name.
        /// </summary>
        private void AddBusStopFromInspector(OmsiTileSummary tile, OmsiObjectRecord record)
        {
            if (!CanEdit || session == null || currentSummary == null)
                return;
            FlushLabelEdits();
            var tileNow = currentSummary.Tiles.Find(each => each.X == tile.X && each.Y == tile.Y) ?? tile;
            var recordNow = tileNow.Objects.Find(each => each.Id == record.Id) ?? record;
            var name = recordNow.Labels.Count > 0 ? recordNow.Labels[0] : string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                SetStatusT("busstop.status.need_name");
                return;
            }
            SetBusStopNameFromInspector(tileNow, recordNow, name);
        }

        private void SetBusStopNameFromInspector(OmsiTileSummary tile, OmsiObjectRecord record, string name)
        {
            if (!CanEdit || session == null)
                return;
            if (string.IsNullOrWhiteSpace(name))
            {
                SetStatusT("busstop.status.need_name");
                return;
            }
            OmsiTerrainChange change = null;
            OmsiObjectChange labelChange = null;
            try
            {
                var labels = OmsiMapSession.StopLabels(record.Labels, name);
                if (!labels.SequenceEqual(record.Labels))
                    labelChange = session.SetElementLabels(tile, record, labels);
                change = SetListedBusStopName(tile, record, name);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is ArgumentException ||
                                              exception is UnauthorizedAccessException)
            {
                Debug.LogException(exception);
                // A label change already written must stay undoable.
                PushBusStopStep(tile, labelChange, null, name);
                SetStatusT("busstop.status.failed", ("reason", exception.Message));
                return;
            }
            PushBusStopStep(tile, labelChange, change, name);
            if (change == null)
                return;
            AfterBusStopRenamed();
            SetStatusT("busstop.status.named", ("id", record.Id), ("name", name.Trim()));
            Debug.Log("Bus stop: " + statusLabel?.text);
        }

        /// <summary>Renames the stop of an object in Busstops.cfg; the map was read again after a label change, so its tile and object are looked up anew. No history step.</summary>
        private OmsiTerrainChange SetListedBusStopName(OmsiTileSummary tile, OmsiObjectRecord record, string name)
        {
            var tileNow = currentSummary.Tiles.Find(each => each.X == tile.X && each.Y == tile.Y) ?? tile;
            var recordNow = tileNow.Objects.Find(each => each.Id == record.Id) ?? record;
            return session.SetBusStopName(currentSummary, tileNow, recordNow, name);
        }

        /// <summary>The label change and the Busstops.cfg change of one rename as ONE undo step, so a single undo or redo takes both.</summary>
        private ObjectEditStep BusStopStep(OmsiTileSummary tile, OmsiObjectChange labelChange, OmsiTerrainChange change, string name)
        {
            var description = Loc.T("busstop.step", ("name", name.Trim()));
            var parts = new List<ObjectEditStep>();
            if (labelChange != null)
                parts.Add(new ObjectEditStep { TileX = tile.X, TileY = tile.Y, ObjectId = labelChange.ElementId, Change = labelChange, Description = description });
            if (change != null)
                parts.Add(new ObjectEditStep { TerrainChange = change, Description = description });
            return StepGroup.Join(parts, group => new ObjectEditStep { Group = group, Description = description });
        }

        private void PushBusStopStep(OmsiTileSummary tile, OmsiObjectChange labelChange, OmsiTerrainChange change, string name)
        {
            var step = BusStopStep(tile, labelChange, change, name);
            if (step == null)
                return;
            PushHistory(step);
            RefreshAfterChange(labelChange?.CurrentRecord != null ? new[] { new MapSelectionItem { Tile = tile, Record = labelChange.CurrentRecord } } : null);
        }

        /// <summary>The routes and the line tool's stops read the new name.</summary>
        private void AfterBusStopRenamed()
        {
            StartRoutesBuild(currentSummary);
            if (activeTool == EditorTool.Line)
            {
                lineObjects = session.ListStopObjects(currentSummary);
                lineTimetable = OmsiTimetable.Read(session.Workspace);
                ReplaceDraftStops();
                LineChanged(true);
            }
        }

        /// <summary>For checks: -busStopName id Name (the object of that ID in a selected tile's map) names its stop.</summary>
        private void RunBusStopArguments(Func<string, int, string> argument, string[] args)
        {
            if (Array.IndexOf(args, "-busStopName") < 0 || session == null || !session.CanEdit)
                return;
            if (!int.TryParse(argument("-busStopName", 1), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var id))
                return;
            foreach (var tile in currentSummary.Tiles)
            {
                var record = tile.Objects.Find(item => item.Id == id);
                if (record != null)
                {
                    SetBusStopNameFromInspector(tile, record, argument("-busStopName", 2) ?? string.Empty);
                    return;
                }
            }
        }
    }
}
