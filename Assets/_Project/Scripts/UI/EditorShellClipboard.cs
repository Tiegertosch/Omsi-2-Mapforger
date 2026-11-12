using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using OmsiMapEditor.World;
using UnityEngine;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// Copying objects and putting them down again (ADR 070): Ctrl+C takes the selection with its labels, pitch and bank, rules
    /// and the objects hung on it; Ctrl+V shows the copies as outlines at the pointer, R turns them, a click puts them down
    /// (Shift keeps the clipboard in hand for more); Ctrl+D copies and puts the copies down 5 m aside at once. Each paste is one
    /// undo step, and it happens all or not at all.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        /// <summary>How far Ctrl+D puts the copies from the originals, east and south, in metres.</summary>
        private const double DuplicateOffsetMeters = 5.0;

        private OmsiClipboardData clipboard;
        private bool clipboardPlacing;
        private readonly Dictionary<string, bool> absoluteHeightByAsset = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        private List<KeyValuePair<OmsiTileSummary, OmsiObjectRecord>> SelectedObjectPairs()
        {
            var pairs = new List<KeyValuePair<OmsiTileSummary, OmsiObjectRecord>>(selection.Count);
            foreach (var item in selection)
            {
                var tile = TileHolding(item) ?? item.Tile;
                if (tile != null && item.Record != null)
                    pairs.Add(new KeyValuePair<OmsiTileSummary, OmsiObjectRecord>(tile, item.Record));
            }
            return pairs;
        }

        private Func<double, double, double?> GroundAtWorld()
        {
            var sampler = new OmsiTerrainSampler(currentSummary, session.Source);
            return (x, y) => sampler.TryGetHeight(x, y, out var height) ? height : (double?)null;
        }

        /// <summary>Whether an object file takes its height as written (a junction, an [absheight] object) instead of above the ground.</summary>
        private bool HasAbsoluteHeight(string assetPath)
        {
            if (absoluteHeightByAsset.TryGetValue(assetPath, out var known))
                return known;
            var absolute = false;
            try
            {
                var root = OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath);
                var path = OmsiRelativePath.Resolve(root, assetPath);
                if (File.Exists(path))
                {
                    var sceneryObject = OmsiSceneryObject.Parse(File.ReadAllBytes(path));
                    absolute = sceneryObject.HasAbsoluteHeight || sceneryObject.HasSplineHelper;
                }
            }
            catch (Exception exception) when (exception is IOException || exception is InvalidDataException || exception is UnauthorizedAccessException ||
                                              exception is ArgumentException)
            {
                // An object file that cannot be read keeps its height as written.
            }
            absoluteHeightByAsset[assetPath] = absolute;
            return absolute;
        }

        /// <summary>Ctrl+C: keeps the selected objects, by value, to put down later.</summary>
        private bool CopySelection()
        {
            if (currentSummary == null || session == null || selection.Count == 0)
            {
                SetStatusT("copy.select_first");
                return false;
            }
            var data = OmsiClipboardData.Capture(currentSummary, SelectedObjectPairs(), GroundAtWorld());
            if (data.Items.Count == 0)
            {
                SetStatusT("copy.only_generated");
                return false;
            }
            clipboard = data;
            // Turns rolled before belong to the old copies. Copies still held at the pointer (Shift+click) become the new ones.
            if (clipboardPlacing && mapCanvas != null)
            {
                placementAsset = clipboard.Items[0].AssetPath;
                RestartPasteGroup(mapCanvas.PlacementHeading);
            }
            else
                pasteHeadingOffsets = null;
            SetStatusT("copy.done", ("count", data.Items.Count), ("attached", data.AttachedCount), ("left", data.GeneratedLeftOut));
            return true;
        }

        /// <summary>Ctrl+V: the copies follow the pointer as outlines until a click puts them down.</summary>
        private void BeginPaste()
        {
            if (currentSummary == null || session == null || mapCanvas == null)
                return;
            if (clipboard == null || clipboard.Items.Count == 0)
            {
                SetStatusT("paste.copy_first");
                return;
            }
            if (!CanEditObjects)
            {
                SetStatus(ReadOnlyMessage("readonly.action.place_objects"));
                return;
            }
            PreparePaste();
            placementAsset = clipboard.Items[0].AssetPath;
            clipboardPlacing = true;
            StartPasteGroup(0.0);
            objectsPanel?.ShowHeading(0.0);
            UpdateToolbar();
            mapCanvas.Focus();
            SetStatusT("paste.status.placing", ("count", clipboard.Items.Count), ("attached", clipboard.AttachedCount),
                ("variant", EditingVariant ? "yes" : "no"));
        }

        /// <summary>Hands the copies to the canvas to follow the pointer, each with its random turn if the option is on.</summary>
        private void StartPasteGroup(double turn)
        {
            var items = new List<PlacementGroupItem>(clipboard.Items.Count);
            for (var index = 0; index < clipboard.Items.Count; index++)
            {
                var item = clipboard.Items[index];
                items.Add(new PlacementGroupItem
                {
                    Asset = item.AssetPath, OffsetX = item.WorldX - clipboard.CentreX, OffsetY = item.WorldY - clipboard.CentreY,
                    HeadingDegrees = item.HeadingDegrees + (pasteHeadingOffsets != null ? pasteHeadingOffsets[index] : 0.0),
                    PitchDegrees = item.PitchDegrees, BankDegrees = item.BankDegrees, Height = item.Height, SourceGround = item.SourceGround
                });
            }
            mapCanvas.BeginGroupPlacement(items, turn);
        }

        /// <summary>Draws the copies held at the pointer in their models, each turned with the group.</summary>
        private void ShowGroupPreview(List<PlacementGroupItem> items, List<Vector2> positions, double turn)
        {
            if (objectRenderer == null)
                return;
            if (items == null)
            {
                objectRenderer.SetGroupPreview(null);
                return;
            }
            var objects = new List<GroupPreviewObject>(items.Count);
            for (var index = 0; index < items.Count; index++)
                objects.Add(new GroupPreviewObject
                {
                    Asset = items[index].Asset, MapPosition = positions[index], HeadingDegrees = items[index].HeadingDegrees + turn, PitchDegrees = items[index].PitchDegrees,
                    BankDegrees = items[index].BankDegrees, Height = items[index].Height, SourceGround = items[index].SourceGround
                });
            objectRenderer.SetGroupPreview(objects);
        }

        /// <summary>
        /// Gets the clipboard ready to be put down, the same way for Ctrl+V, "Paste here" and the command line: an object being placed is let
        /// go, and every copy gets a random turn of its own when the option is on (none otherwise).
        /// </summary>
        private void PreparePaste()
        {
            if (placementAsset != null)
                StopPlacement();
            RollPasteOffsets();
        }

        /// <summary>
        /// "Paste here" in the context menu: the middle of the clipboard goes to the point clicked, prepared as Ctrl+V prepares it and not
        /// turned. Copies already held at the pointer keep their turns.
        /// </summary>
        private void PasteHere(MapPlacement placement)
        {
            if (clipboard == null || clipboard.Items.Count == 0)
            {
                SetStatusT("paste.copy_first");
                return;
            }
            if (!CanEditObjects)
            {
                SetStatus(ReadOnlyMessage("readonly.action.place_objects"));
                return;
            }
            if (clipboardPlacing && mapCanvas != null)
            {
                PlaceClipboardAt(placement, mapCanvas.PlacementHeading);
                return;
            }
            PreparePaste();
            PlaceClipboardAt(placement, 0.0);
        }

        /// <summary>A click while the copies are held: the middle of the group goes where the pointer is.</summary>
        private void PlaceClipboardAt(MapPlacement placement)
        {
            PlaceClipboardAt(placement, mapCanvas.PlacementHeading);
        }

        private void PlaceClipboardAt(MapPlacement placement, double turn)
        {
            if (clipboard == null || !CanEditObjects)
            {
                StopPlacement();
                return;
            }
            if (placement.Tile == null)
            {
                SetStatusT("place.status.no_tile");
                return;
            }
            var size = currentSummary.TileSizeMeters;
            if (!RunPaste(clipboard, placement.Tile.X * size + placement.LocalX, placement.Tile.Y * size + placement.LocalY, turn, pasteHeadingOffsets))
                return;
            if (!placement.KeepPlacing)
                StopPlacement();
            else if (pasteHeadingOffsets != null)
                RestartPasteGroup(turn);
        }

        /// <summary>Ctrl+D: copies the selection and puts the copies a few metres aside, as one step; the clipboard stays as it was.</summary>
        private void DuplicateSelection()
        {
            if (currentSummary == null || session == null)
                return;
            if (!CanEditObjects)
            {
                SetStatus(ReadOnlyMessage("readonly.action.place_objects"));
                return;
            }
            if (selection.Count == 0)
            {
                SetStatusT("copy.select_first");
                return;
            }
            var data = OmsiClipboardData.Capture(currentSummary, SelectedObjectPairs(), GroundAtWorld());
            if (data.Items.Count == 0)
            {
                SetStatusT("copy.only_generated");
                return;
            }
            RunPaste(data, data.CentreX + DuplicateOffsetMeters, data.CentreY - DuplicateOffsetMeters, 0.0, null);
        }

        /// <summary>
        /// Puts copies down with the middle of the group at a world position, turned clockwise, as one undo step, and selects them; false,
        /// with the reason shown, when nothing could be written (also when the turns do not fit the copies).
        /// </summary>
        private bool RunPaste(OmsiClipboardData data, double centreX, double centreY, double turn, IReadOnlyList<double> headingOffsets)
        {
            OmsiPasteResult result;
            try
            {
                result = session.PasteObjects(currentSummary, data.PlaceAt(centreX, centreY, turn, headingOffsets), HasAbsoluteHeight, GroundAtWorld());
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is ArgumentException)
            {
                Debug.LogException(exception);
                SetStatusT("paste.status.failed", ("reason", exception.Message));
                return false;
            }

            var steps = new List<ObjectEditStep>(result.Changes.Count);
            foreach (var change in result.Changes)
                steps.Add(change is OmsiObjectChange placed
                    ? new ObjectEditStep { TileX = placed.Tile.X, TileY = placed.Tile.Y, ObjectId = placed.Record.Id, Change = placed }
                    : new ObjectEditStep { AttachedChange = (OmsiAttachedObjectChange)change });
            PushHistory(new ObjectEditStep { Group = steps, Description = Loc.T("paste.step", ("count", result.Placed.Count)) });

            var pasted = new List<MapSelectionItem>(result.Placed.Count);
            foreach (var placed in result.Placed)
                pasted.Add(new MapSelectionItem { Tile = placed.Tile, Record = placed.Record });
            RefreshAfterChange(pasted);
            if (objectsPanelOpen)
                UpdateObjectsCatalog();
            SetStatusT("paste.status.done", ("count", result.Placed.Count), ("attached", result.AttachedPasted), ("variant", EditingVariant ? "yes" : "no"),
                ("hasLeftOut", result.AttachedLeftOut + result.RulesLeftOut > 0 ? "yes" : "no"));
            return true;
        }

        /// <summary>The command-line arguments for copying: -copySelection, -pasteAt and -duplicateSelection (after -select).</summary>
        private void RunClipboardArguments(string[] args)
        {
            string Argument(string name, int offset)
            {
                var index = Array.IndexOf(args, name);
                return index >= 0 && index + offset < args.Length ? args[index + offset] : null;
            }
            if (Array.IndexOf(args, "-copySelection") >= 0)
            {
                CopySelection();
                Debug.Log("Copy: " + statusLabel?.text);
            }
            // -pasteAt <tileX> <tileY> <x> <y> [degrees]: puts the clipboard down with its middle there, turned clockwise.
            if (int.TryParse(Argument("-pasteAt", 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var tileX) &&
                int.TryParse(Argument("-pasteAt", 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var tileY) &&
                double.TryParse(Argument("-pasteAt", 3), NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                double.TryParse(Argument("-pasteAt", 4), NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            {
                double.TryParse(Argument("-pasteAt", 5), NumberStyles.Float, CultureInfo.InvariantCulture, out var degrees);
                if (clipboard == null || currentSummary == null)
                    Debug.Log("Paste: copy something first (-select, -copySelection).");
                else
                {
                    var size = currentSummary.TileSizeMeters;
                    PreparePaste();
                    RunPaste(clipboard, tileX * size + x, tileY * size + y, degrees, pasteHeadingOffsets);
                    Debug.Log("Paste: " + statusLabel?.text);
                }
            }
            // -pastePreview [degrees]: holds the clipboard at the middle of the view, as Ctrl+V does with the pointer there; for screenshots.
            if (Array.IndexOf(args, "-pastePreview") >= 0 && mapCanvas != null)
            {
                BeginPaste();
                if (double.TryParse(Argument("-pastePreview", 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var previewTurn))
                    mapCanvas.PlacementHeading = previewTurn;
                mapCanvas.PointPlacementAtCenter();
                Debug.Log("Paste preview: " + statusLabel?.text);
            }
            if (Array.IndexOf(args, "-duplicateSelection") >= 0)
            {
                DuplicateSelection();
                Debug.Log("Duplicate: " + statusLabel?.text);
            }
        }
    }
}
