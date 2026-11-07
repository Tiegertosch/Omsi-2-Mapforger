using System;
using System.Collections.Generic;
using System.Globalization;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The exact place of a map without world coordinates on the Earth (ADR 061): a reference point picked in the map, the
    /// position typed or pasted for it, a scale, moving by metres and a fit by two points. The pictures already loaded follow
    /// every change at once (each is laid where the ground it shows belongs under the new place); loading them again cuts them anew.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        // The stamp of the placement each tile's picture from the air was cut for, by tile.
        private readonly Dictionary<long, string> imageryStamps = new Dictionary<long, string>();

        // Whether the window has chosen its first tab for the map that is open (see OpenSatelliteWindow).
        private bool satelliteTabChosen;

        // Which pick waits for a click in the map: 0 the reference point, 1 and 2 the points of a fit; -1 none.
        private int placementPickFor = -1;
        private readonly OmsiMapPoint?[] fitPoints = new OmsiMapPoint?[2];

        private void InitializePlacement()
        {
            if (satelliteWindow == null)
                return;
            satelliteWindow.PickRequested += BeginPlacementPick;
            satelliteWindow.ReferenceCentred += CentreReference;
            satelliteWindow.PositionEntered += EnterReferencePosition;
            satelliteWindow.ScaleChanged += scale =>
            {
                if (!HasPlacement(out var anchor) || Math.Abs(anchor.Scale - scale) < 1e-6)
                    return;
                anchor.Scale = scale;
                PlacementChanged(Loc.T("placement.scaled", ("scale", scale.ToString("0.######", CultureInfo.InvariantCulture))));
            };
            satelliteWindow.MoveRequested += MoveMapOnEarth;
            satelliteWindow.FitRequested += FitMapToPoints;
            if (mapCanvas != null)
                mapCanvas.PointPicked += PlacementPointPicked;
        }

        /// <summary>The map's anchor when it is placed by hand; says what to do first otherwise.</summary>
        private bool HasPlacement(out OmsiGeoAnchor anchor)
        {
            anchor = geoSettings?.Anchor;
            if (currentSummary == null || currentSummary.UsesWorldCoordinates || anchor == null)
            {
                SetStatusT(currentSummary != null && currentSummary.UsesWorldCoordinates ? "placement.own" : "placement.first"); // i18n:keys
                return false;
            }
            return true;
        }

        /// <summary>Saves a changed place, shows it, lets the pictures follow it and says so.</summary>
        private void PlacementChanged(string message)
        {
            SaveGeo();
            ShowGeo();
            var loaded = groundRenderer != null && groundRenderer.ImageryTileCount > 0;
            SetStatus(message + (loaded ? " " + Loc.T("placement.follow") : string.Empty));
        }

        /// <summary>Marks the reference point and the points picked for a fit on the ground while the window is open.</summary>
        private void UpdatePlacementMarks()
        {
            if (mapCanvas == null)
                return;
            var points = new List<OmsiMapPoint>();
            if (satelliteWindow != null && IsShown(satelliteWindow) && currentSummary != null && !currentSummary.UsesWorldCoordinates)
            {
                var anchor = geoSettings?.Anchor;
                if (anchor != null)
                {
                    var size = currentSummary.TileSizeMeters;
                    points.Add(new OmsiMapPoint((anchor.TileX + 0.5) * size + anchor.OffsetX, (anchor.TileY + 0.5) * size + anchor.OffsetY));
                }
                foreach (var point in fitPoints)
                    if (point.HasValue)
                        points.Add(point.Value);
            }
            mapCanvas.SetPlacementPoints(points);
        }

        private void EndPlacementPick()
        {
            placementPickFor = -1;
            mapCanvas?.EndPointPick();
            satelliteWindow?.ShowPicking(-1);
        }

        private void BeginPlacementPick(int which)
        {
            if (mapCanvas == null || currentSummary == null)
                return;
            if (placementPickFor == which)
            {
                EndPlacementPick();
                SetStatusT("placement.pick_stopped");
                return;
            }
            if (currentSummary.UsesWorldCoordinates)
            {
                SetStatusT("placement.own");
                return;
            }
            if (which == 0 && !HasPlacement(out _))
                return;
            placementPickFor = which;
            mapCanvas.BeginPointPick();
            satelliteWindow?.ShowPicking(which);
            if (which == 0)
                SetStatusT("placement.pick_reference");
            else
                SetStatusT("placement.pick_point", ("n", which));
        }

        private void PlacementPointPicked(double x, double y)
        {
            var which = placementPickFor;
            EndPlacementPick();
            if (which < 0 || currentSummary == null)
                return;
            if (which == 0)
            {
                var geo = Georeference();
                if (geo == null || geo.IsWorldCoordinates)
                    return;
                geoSettings.Anchor = geo.AnchorAt(x, y);
                PlacementChanged(Loc.T("placement.reference_at", ("where", Describe(x, y))));
                return;
            }
            fitPoints[which - 1] = new OmsiMapPoint(x, y);
            satelliteWindow?.ShowFitPoint(which - 1, Describe(x, y));
            UpdatePlacementMarks();
            SetStatus(Loc.T("placement.point", ("n", which), ("where", Describe(x, y))));
        }

        private string Describe(double x, double y)
        {
            var size = currentSummary.TileSizeMeters;
            var tileX = (int)Math.Floor(x / size);
            var tileY = (int)Math.Floor(y / size);
            return Loc.T("placement.describe", ("x", tileX), ("y", tileY), ("east", x - tileX * size), ("north", y - tileY * size));
        }

        private void CentreReference()
        {
            if (!HasPlacement(out var anchor))
                return;
            var size = currentSummary.TileSizeMeters;
            geoSettings.Anchor = Georeference().AnchorAt((anchor.TileX + 0.5) * size, (anchor.TileY + 0.5) * size);
            PlacementChanged(Loc.T("placement.reference_middle", ("x", anchor.TileX), ("y", anchor.TileY)));
        }

        private void EnterReferencePosition(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || currentSummary == null || currentSummary.UsesWorldCoordinates)
                return;
            if (!OmsiLatLon.TryParse(text, out var position))
            {
                SetStatusT("placement.not_position");
                return;
            }
            var anchor = geoSettings?.Anchor;
            if (anchor == null)
            {
                PlaceMapAt(OmsiGeoreference.MercatorOf(position.Latitude, position.Longitude));
                return;
            }
            // The field shows six decimals: leaving it unchanged must not nudge the map by what the rounding cut off.
            if (Math.Abs(anchor.Latitude - position.Latitude) < 5e-7 && Math.Abs(anchor.Longitude - position.Longitude) < 5e-7)
                return;
            anchor.Latitude = position.Latitude;
            anchor.Longitude = position.Longitude;
            PlacementChanged(Loc.T("placement.reference_lies", ("latitude", position.Latitude.ToString("0.000000", CultureInfo.InvariantCulture)),
                ("longitude", position.Longitude.ToString("0.000000", CultureInfo.InvariantCulture))));
        }

        private void MoveMapOnEarth(double eastMeters, double northMeters)
        {
            if (!HasPlacement(out var anchor))
                return;
            var moved = OmsiGeoreference.Moved(new OmsiLatLon(anchor.Latitude, anchor.Longitude), eastMeters, northMeters);
            anchor.Latitude = moved.Latitude;
            anchor.Longitude = moved.Longitude;
            PlacementChanged(Loc.T("placement.moved", ("meters", Math.Abs(eastMeters + northMeters)),
                ("direction", eastMeters > 0 ? "east" : eastMeters < 0 ? "west" : northMeters > 0 ? "north" : "south")));
        }

        private void FitMapToPoints(string firstText, string secondText, bool fitScale)
        {
            if (!HasPlacement(out _))
                return;
            if (!fitPoints[0].HasValue || !fitPoints[1].HasValue)
            {
                SetStatusT("placement.pick_both");
                return;
            }
            if (!OmsiLatLon.TryParse(firstText, out var first) || !OmsiLatLon.TryParse(secondText, out var second))
            {
                SetStatusT("placement.paste_both");
                return;
            }
            var fitted = OmsiGeoreference.FitTwoPoints(currentSummary.TileSizeMeters, fitPoints[0].Value, first, fitPoints[1].Value, second, fitScale);
            if (fitted == null)
            {
                SetStatusT("placement.too_close");
                return;
            }
            geoSettings.Anchor = fitted;
            PlacementChanged(Loc.T("placement.fitted", ("degrees", fitted.RotationDegrees), ("scaled", fitScale ? "yes" : "no"), ("scale", fitted.Scale)));
        }

        /// <summary>
        /// Lays a tile's picture where its ground belongs under the map's present place: unmoved when it was cut for this place,
        /// moved, turned and scaled by the difference when the place changed since (or when the project was saved that way).
        /// </summary>
        private void RefreshImageryPlacement(int tileX, int tileY)
        {
            if (groundRenderer == null || currentSummary == null)
                return;
            var key = ((long)tileX << 32) | (uint)tileY;
            var current = Georeference();
            if (current == null || current.IsWorldCoordinates || !imageryStamps.TryGetValue(key, out var stamp) || stamp == current.Stamp)
            {
                groundRenderer.ClearImageryPlacement(tileX, tileY);
                return;
            }
            var made = OmsiGeoreference.FromStamp(stamp, currentSummary.TileSizeMeters);
            if (made != null && OmsiGeoreference.Transition(made, current, out var scale, out var turn, out var moveX, out var moveY))
                groundRenderer.SetImageryPlacement(tileX, tileY, scale, turn, moveX, moveY);
            else
                groundRenderer.ClearImageryPlacement(tileX, tileY);
        }

        private void RefreshImageryPlacements()
        {
            foreach (var key in new List<long>(imageryStamps.Keys))
                RefreshImageryPlacement((int)(key >> 32), (int)(key & 0xffffffffL));
        }

        /// <summary>
        /// -satelliteRef &lt;x&gt; &lt;y&gt; picks the map point (world metres) as the reference point; -satelliteAt &lt;lat&gt; &lt;lon&gt; puts the
        /// reference point there; -satelliteScale &lt;s&gt;; -satelliteTurn &lt;degrees&gt;; -satelliteMove &lt;east&gt; &lt;north&gt; metres; -satelliteFit &lt;x1&gt; &lt;y1&gt; &lt;lat1&gt;
        /// &lt;lon1&gt; &lt;x2&gt; &lt;y2&gt; &lt;lat2&gt; &lt;lon2&gt; [-satelliteFitScale]; -satelliteTab place|pictures|terrain|more shows a tab of the window, -satelliteUnfold [exact|fit|keys|server] opens its folded parts; -satelliteClose then closes the window, so a screenshot shows the map.
        /// Statuses go to the log as "Satellite:".
        /// </summary>
        private void ApplyPlacementArguments(string[] args)
        {
            double? Number(string name, int offset)
            {
                var index = Array.IndexOf(args, name);
                return index >= 0 && index + offset < args.Length &&
                       double.TryParse(args[index + offset], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                    ? value
                    : (double?)null;
            }
            var tabIndex = Array.IndexOf(args, "-satelliteTab");
            if (tabIndex >= 0 && tabIndex + 1 < args.Length)
                satelliteWindow?.ShowTab(args[tabIndex + 1]);
            var unfoldIndex = Array.IndexOf(args, "-satelliteUnfold");
            if (unfoldIndex >= 0)
                satelliteWindow?.Unfold(unfoldIndex + 1 < args.Length && !args[unfoldIndex + 1].StartsWith("-", StringComparison.Ordinal) ? args[unfoldIndex + 1] : null);
            var refX = Number("-satelliteRef", 1);
            var refY = Number("-satelliteRef", 2);
            if (refX.HasValue && refY.HasValue && HasPlacement(out _))
            {
                PlacementPointPickedAs(0, refX.Value, refY.Value);
                LogSatellite();
            }
            var atLatitude = Number("-satelliteAt", 1);
            var atLongitude = Number("-satelliteAt", 2);
            if (atLatitude.HasValue && atLongitude.HasValue)
            {
                EnterReferencePosition(atLatitude.Value.ToString("R", CultureInfo.InvariantCulture) + ", " + atLongitude.Value.ToString("R", CultureInfo.InvariantCulture));
                LogSatellite();
            }
            var scale = Number("-satelliteScale", 1);
            if (scale.HasValue && HasPlacement(out var scaled))
            {
                scaled.Scale = scale.Value;
                PlacementChanged(Loc.T("placement.scaled", ("scale", scale.Value.ToString("0.######", CultureInfo.InvariantCulture))));
                LogSatellite();
            }
            var turned = Number("-satelliteTurn", 1);
            if (turned.HasValue && HasPlacement(out var turning))
            {
                turning.RotationDegrees = turned.Value;
                PlacementChanged(Loc.T("placement.north_turned", ("degrees", turned.Value)));
                LogSatellite();
            }
            var east = Number("-satelliteMove", 1);
            var north = Number("-satelliteMove", 2);
            if (east.HasValue && north.HasValue)
            {
                MoveMapOnEarth(east.Value, north.Value);
                LogSatellite();
            }
            var values = new double?[8];
            for (var index = 0; index < values.Length; index++)
                values[index] = Number("-satelliteFit", 1 + index);
            if (Array.IndexOf(args, "-satelliteFit") >= 0 && Array.TrueForAll(values, value => value.HasValue) && HasPlacement(out _))
            {
                fitPoints[0] = new OmsiMapPoint(values[0].Value, values[1].Value);
                fitPoints[1] = new OmsiMapPoint(values[4].Value, values[5].Value);
                // As after picking them with a click: described, with the positions pasted into the fields.
                for (var point = 0; point < 2; point++)
                {
                    satelliteWindow?.ShowFitPoint(point, Describe(fitPoints[point].Value.X, fitPoints[point].Value.Y));
                    satelliteWindow?.SetFitPositionText(point, values[2 + 4 * point].Value.ToString("0.000000", CultureInfo.InvariantCulture) + ", " +
                                                                values[3 + 4 * point].Value.ToString("0.000000", CultureInfo.InvariantCulture));
                }
                var fitScale = Array.IndexOf(args, "-satelliteFitScale") >= 0;
                satelliteWindow?.SetFitScale(fitScale);
                FitMapToPoints(
                    values[2].Value.ToString("R", CultureInfo.InvariantCulture) + ", " + values[3].Value.ToString("R", CultureInfo.InvariantCulture),
                    values[6].Value.ToString("R", CultureInfo.InvariantCulture) + ", " + values[7].Value.ToString("R", CultureInfo.InvariantCulture), fitScale);
                LogSatellite();
            }
            if (Array.IndexOf(args, "-satelliteClose") >= 0)
            {
                HidePopup(satelliteWindow);
                EndPlacementPick();
                UpdatePlacementMarks();
            }
        }

        private void PlacementPointPickedAs(int which, double x, double y)
        {
            placementPickFor = which;
            PlacementPointPicked(x, y);
        }
    }
}
