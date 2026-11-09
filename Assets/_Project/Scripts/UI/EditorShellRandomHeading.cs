using System;
using System.Collections.Generic;
using System.Globalization;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// Random headings (parity list, "random rotation of object groups"): with the option ticked in the Objects tool, every object put
    /// down gets a heading of its own, and so does every object of a pasted group; a button in the inspector gives the selected
    /// objects new random headings in one undo step. Trees and bushes stand more naturally that way.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        private Random headingDice = new Random();

        /// <summary>One extra turn per object of the clipboard group being held, or null when the option is off.</summary>
        private List<double> pasteHeadingOffsets;

        private bool RandomHeadingOn => objectsPanel != null && objectsPanel.RandomHeading;

        /// <summary>A random heading in degrees with one decimal, as the heading field shows it.</summary>
        private double RollHeading()
        {
            return Math.Round(headingDice.NextDouble() * 3600.0) / 10.0 % 360.0;
        }

        /// <summary>The option was ticked or cleared while something is held at the pointer: the next heading follows it at once.</summary>
        private void RandomHeadingToggled()
        {
            if (mapCanvas == null || placementAsset == null)
                return;
            if (clipboardPlacing)
                RestartPasteGroup(mapCanvas.PlacementHeading);
            else if (RandomHeadingOn)
                RollPlacementHeading();
        }

        /// <summary>Gives the object about to be put down a new random heading, shown in the heading field and the preview.</summary>
        private void RollPlacementHeading()
        {
            if (mapCanvas == null || clipboardPlacing)
                return;
            mapCanvas.PlacementHeading = RollHeading();
            objectsPanel?.ShowHeading(mapCanvas.PlacementHeading);
        }

        /// <summary>New random turns for the group held at the pointer (or none when the option is off).</summary>
        private void RollPasteOffsets()
        {
            pasteHeadingOffsets = null;
            if (clipboard == null || !RandomHeadingOn)
                return;
            pasteHeadingOffsets = new List<double>(clipboard.Items.Count);
            for (var index = 0; index < clipboard.Items.Count; index++)
                pasteHeadingOffsets.Add(RollHeading());
        }

        /// <summary>Rolls new turns after a paste that keeps the group in hand, and shows them at the pointer.</summary>
        private void RestartPasteGroup(double turn)
        {
            RollPasteOffsets();
            StartPasteGroup(turn);
        }

        /// <summary>The "Randomize headings" button and <c>-randomizeHeadings</c>: every selected object gets a heading of its own, as one undo step.</summary>
        private void RandomizeSelectedHeadings()
        {
            if (currentSummary == null || session == null)
                return;
            if (!CanEditObjects)
            {
                SetStatus(ReadOnlyMessage("readonly.action.edit_it"));
                return;
            }
            if (selection.Count == 0)
            {
                SetStatusT("random.select_first");
                return;
            }
            var edits = new List<MapObjectEdit>(selection.Count);
            foreach (var item in selection)
                edits.Add(new MapObjectEdit
                {
                    Tile = item.Tile, Record = item.Record, LocalX = item.Record.LocalX, LocalY = item.Record.LocalY, Height = item.Record.Height,
                    HeadingDegrees = RollHeading(), PitchDegrees = item.Record.PitchDegrees, BankDegrees = item.Record.BankDegrees
                });
            var count = edits.Count;
            CommitEdits(edits);
            SetStatusT("random.done", ("count", count));
        }

        /// <summary>
        /// The command-line arguments: <c>-randomHeading [seed]</c> ticks the option for this run only (not stored), with a seed for repeatable
        /// numbers, and <c>-randomizeHeadings</c> gives the selection new random headings ("Random headings:" in the log).
        /// </summary>
        private void RunRandomHeadingArguments(string[] args, bool afterSelect)
        {
            var index = Array.IndexOf(args, "-randomHeading");
            if (!afterSelect && index >= 0)
            {
                if (index + 1 < args.Length && int.TryParse(args[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed))
                    headingDice = new Random(seed);
                objectsPanel?.SetRandomHeadingQuietly(true);
            }
            if (afterSelect && Array.IndexOf(args, "-randomizeHeadings") >= 0)
            {
                RandomizeSelectedHeadings();
                UnityEngine.Debug.Log("Random headings: " + statusLabel?.text);
            }
        }
    }
}
