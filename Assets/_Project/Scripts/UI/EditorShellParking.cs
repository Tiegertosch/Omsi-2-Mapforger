using System;
using System.Collections.Generic;
using System.IO;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine;

namespace OmsiMapEditor.UI
{
    /// <summary>Parking rows along roads: two clicks on a road put car park objects beside it (ADR 087).</summary>
    public sealed partial class EditorShellBootstrap
    {
        /// <summary>The first click of a parking row: the road it lies on, how far along it and on which side.</summary>
        private OmsiSplineChain parkingChain;

        private double parkingFrom;
        private bool parkingRight;

        /// <summary>Forgets a half-made parking row, as when the tool or its mode changes.</summary>
        private void ResetParkingRow()
        {
            parkingChain = null;
        }

        /// <summary>
        /// A click with the road tool's parking switch on. The first click picks the start and the side of the road (the
        /// side the pointer is on), the second the end along the same road; spaces then stand beside the outermost lane.
        /// </summary>
        private void ClickParkingRow(OmsiMapPoint point)
        {
            if (EditingVariant)
            {
                SetStatusT("roadbuild.parking.variant");
                return;
            }
            var profiles = RoadProfiles();
            var road = OmsiRoadJunctions.RoadAt(currentSummary, profiles, point.X, point.Y);
            if (parkingChain == null)
            {
                if (road == null)
                {
                    SetStatusT("roadbuild.parking.no_road");
                    return;
                }
                var chain = OmsiSplineChain.Around(currentSummary, road.Tile, road.Spline, 400.0);
                var along = chain.Project(point.X, point.Y);
                chain.FrameAt(along, out var centre, out var heading, out _);
                var radians = heading * Math.PI / 180.0;
                parkingRight = (point.X - centre.X) * Math.Cos(radians) - (point.Y - centre.Y) * Math.Sin(radians) >= 0.0;
                parkingChain = chain;
                parkingFrom = along;
                SetStatusT(parkingRight ? "roadbuild.parking.first_right" : "roadbuild.parking.first_left"); // i18n:keys
                return;
            }

            var chainNow = parkingChain;
            var to = chainNow.Project(point.X, point.Y);
            chainNow.FrameAt(to, out var end, out _, out _);
            // The second click must lie on the same road, not on another one that happens to be near the chain.
            if (Math.Sqrt((end.X - point.X) * (end.X - point.X) + (end.Y - point.Y) * (end.Y - point.Y)) > 12.0)
            {
                SetStatusT("roadbuild.parking.other_road");
                return;
            }
            var plan = OmsiParkingRow.Plan(chainNow, profiles, parkingFrom, to, parkingRight);
            if (plan.Problem != null)
            {
                SetStatusT("roadbuild.parking.problem", ("reason", Loc.T("roadbuild.parking.problem." + plan.Problem))); // i18n:keys
                return;
            }
            PlaceParkingRow(plan);
        }

        private void PlaceParkingRow(OmsiParkingPlan plan)
        {
            var root = OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath);
            if (string.IsNullOrEmpty(root) || !File.Exists(Path.Combine(root, "Sceneryobjects", "Generic", "car_park.sco")))
            {
                SetStatusT("roadbuild.parking.no_object");
                return;
            }
            var size = currentSummary.TileSizeMeters;
            var steps = new List<object>();
            var skipped = 0;
            var parkIn = 0;
            try
            {
                foreach (var space in plan.Spaces)
                {
                    var tileX = (int)Math.Floor(space.Position.X / size);
                    var tileY = (int)Math.Floor(space.Position.Y / size);
                    var tile = System.Linq.Enumerable.FirstOrDefault(currentSummary.Tiles, each => each.X == tileX && each.Y == tileY);
                    if (tile == null || !tile.IsAvailable)
                    {
                        skipped++;
                        continue;
                    }
                    // The first label of a car park names its parking list.
                    var labels = roadPanel.ParkingList > 0 ? new List<string> { roadPanel.ParkingList.ToString(System.Globalization.CultureInfo.InvariantCulture) } : null;
                    steps.Add(session.PlaceObject(currentSummary, tile, OmsiParkingRow.SpaceObject, space.Position.X - tile.X * size, space.Position.Y - tile.Y * size,
                        0.0, space.HeadingDegrees, 0.0, 0.0, labels));
                    if (space.ParkInPossible)
                        parkIn++;
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                SetStatusT("roadbuild.parking.failed", ("reason", exception.Message));
                return;
            }
            if (steps.Count == 0)
            {
                SetStatusT("roadbuild.parking.no_tile");
                return;
            }
            // Without a parking list the spaces show no cars in openOMSI, so a map that has none gets one.
            var listed = roadPanel.ParkingList == 0 && session.EnsureParkList(OmsiParkingRow.DefaultParkList(root));
            PushSteps(steps, Loc.T("roadbuild.parking.step", ("n", steps.Count)));
            ResetParkingRow();
            SetStatusT(listed ? "roadbuild.parking.done_list" : "roadbuild.parking.done", ("n", steps.Count), ("in", parkIn), ("skipped", skipped)); // i18n:keys
        }
    }
}
