using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// Mirroring and the texture offset of the selected spline, in the inspector: ADR 074. A mirrored spline has its cross-section turned
    /// over (paths, rows and the profile lie at the negated offset); the texture offset is the distance from the start of the chain, from where
    /// the textures run and rows along the spline count.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        /// <summary>The mirror switch and the texture offset under the cant and skew of a selected spline, with the button that applies them.</summary>
        private void AddSplineLookFields(VisualElement editor, OmsiSplineRecord spline)
        {
            var mirror = new Toggle { value = spline.IsMirrored };
            LocBindings.Text(mirror, "inspector.field.mirror");
            LocBindings.Tooltip(mirror, "inspector.mirror.tooltip");
            mirror.AddToClassList("inspector-edit-field");
            editor.Add(mirror);
            TextField offset = null;
            if (spline.ChainOffset.HasValue)
            {
                offset = AddEditField(editor, "inspector.field.tex_offset", spline.ChainOffset.Value);
                offset.tooltip = Loc.T("inspector.tex_offset.tooltip");
            }
            var apply = new Button(() =>
            {
                double? texture = null;
                if (offset != null)
                {
                    if (!TryParseField(offset, Loc.T("inspector.name.tex_offset"), out var parsed))
                        return;
                    texture = parsed;
                }
                SetSplineLook(mirror.value, texture);
            })
            {
                text = Loc.T("inspector.apply_look"),
                tooltip = Loc.T("inspector.apply_look.tooltip")};
            apply.AddToClassList("map-control-button");
            apply.AddToClassList("inspector-edit-apply");
            editor.Add(apply);
        }

        /// <summary>Gives the selected spline its mirroring and texture offset as one undo step. <c>-splineLook mirror|plain [offset]</c> does this from the command line.</summary>
        private void SetSplineLook(bool mirrored, double? textureOffset)
        {
            if (selectedSpline == null || currentSummary == null)
                return;
            if (!CanEdit)
            {
                SetStatus(ReadOnlyMessage("readonly.action.spline_look"));
                return;
            }
            var id = selectedSpline.Id;
            var tile = currentSummary.Tiles.Find(candidate => candidate.Splines.Exists(spline => spline.Id == id));
            var record = tile?.Splines.Find(candidate => candidate.Id == id);
            if (record == null)
                return;
            OmsiSplineChange change;
            try
            {
                change = session.UpdateSplineLook(currentSummary, new[] { new OmsiSplineLook { Tile = tile, Spline = record, Mirrored = mirrored, TextureOffset = textureOffset } });
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is ArgumentException || exception is IOException)
            {
                Debug.LogException(exception);
                SetStatusT("spline.look.failed", ("id", id), ("failure", exception.Message));
                return;
            }
            if (change == null)
            {
                SetStatusT("spline.look.unchanged", ("id", id));
                return;
            }
            PushHistory(new ObjectEditStep { SplineChange = change });
            RefreshSplineViews(change);
            RefreshAfterChange(null);
            SetStatusT("spline.look.changed", ("id", id), ("mirrored", mirrored ? "yes" : "no"));
        }

        /// <summary>The command-line argument <c>-splineLook mirror|plain [offset]</c> (with <c>-selectSpline</c>); "Spline look:" in the log.</summary>
        private void RunSplineLookArguments(string[] args)
        {
            var index = Array.IndexOf(args, "-splineLook");
            if (index < 0 || index + 1 >= args.Length)
                return;
            double? offset = null;
            if (index + 2 < args.Length && double.TryParse(args[index + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                offset = parsed;
            SetSplineLook(string.Equals(args[index + 1], "mirror", StringComparison.OrdinalIgnoreCase), offset);
            Debug.Log("Spline look: " + statusLabel?.text);
        }
    }
}
