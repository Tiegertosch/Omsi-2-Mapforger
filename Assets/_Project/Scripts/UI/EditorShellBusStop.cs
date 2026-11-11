using System;
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
    /// for the name the timetable and the line tool show, as OMSI's editor has it on the object. The name is kept in <c>TTData\Busstops.cfg</c>.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        private void AddBusStopGroup(VisualElement editor, OmsiTileSummary tile, object element)
        {
            var record = element as OmsiObjectRecord;
            if (record == null || session == null || currentSummary == null || !session.IsBusStop(currentSummary, tile, record))
                return;
            string current;
            try
            {
                // The name is the object's first label, as in OMSI's editor; Busstops.cfg takes it from there.
                current = record.Labels.Count > 0 && !string.IsNullOrWhiteSpace(record.Labels[0]) ? record.Labels[0] : session.BusStopName(currentSummary, tile, record);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException)
            {
                Debug.LogException(exception);
                return;
            }
            AddInspectorGroup(editor, "bus-stop", Loc.T("inspector.busstop.group"), true, body =>
            {
                var hint = new Label(current == null ? Loc.T("inspector.busstop.unnamed") : Loc.T("inspector.busstop.hint"));
                hint.AddToClassList("inspector-edit-hint");
                hint.style.whiteSpace = WhiteSpace.Normal;
                body.Add(hint);
                var field = new TextField { value = current ?? string.Empty };
                LocBindings.Text(field, "inspector.busstop.name");
                field.AddToClassList("inspector-edit-field");
                field.SetEnabled(CanEdit);
                body.Add(field);
                var set = new Button(() => SetBusStopNameFromInspector(tile, record, field.value)) { text = Loc.T("inspector.busstop.set") };
                set.AddToClassList("map-control-button");
                set.SetEnabled(CanEdit);
                body.Add(set);
            });
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
            OmsiTerrainChange change;
            try
            {
                var labels = OmsiMapSession.StopLabels(record.Labels, name);
                if (!labels.SequenceEqual(record.Labels))
                {
                    var labelChange = session.SetElementLabels(tile, record, labels);
                    if (labelChange != null)
                    {
                        PushHistory(new ObjectEditStep { TileX = tile.X, TileY = tile.Y, ObjectId = labelChange.ElementId, Change = labelChange, Description = Loc.T("busstop.step", ("name", name.Trim())) });
                        RefreshAfterChange(labelChange.CurrentRecord != null ? new[] { new MapSelectionItem { Tile = tile, Record = labelChange.CurrentRecord } } : null);
                    }
                }
                // The map was read again after the label change: the stop's tile and object are looked up anew.
                var tileNow = currentSummary.Tiles.Find(each => each.X == tile.X && each.Y == tile.Y) ?? tile;
                var recordNow = tileNow.Objects.Find(each => each.Id == record.Id) ?? record;
                change = session.SetBusStopName(currentSummary, tileNow, recordNow, name);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is ArgumentException ||
                                              exception is UnauthorizedAccessException)
            {
                Debug.LogException(exception);
                SetStatusT("busstop.status.failed", ("reason", exception.Message));
                return;
            }
            if (change == null)
                return;
            PushHistory(new ObjectEditStep { TerrainChange = change, Description = Loc.T("busstop.step", ("name", name.Trim())) });
            StartRoutesBuild(currentSummary);
            if (activeTool == EditorTool.Line)
            {
                lineObjects = session.ListStopObjects(currentSummary);
                lineTimetable = OmsiTimetable.Read(session.Workspace);
                ReplaceDraftStops();
                LineChanged(true);
            }
            SetStatusT("busstop.status.named", ("id", record.Id), ("name", name.Trim()));
            Debug.Log("Bus stop: " + statusLabel?.text);
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
