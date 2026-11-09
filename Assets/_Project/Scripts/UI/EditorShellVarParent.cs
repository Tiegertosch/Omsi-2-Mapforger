using System;
using System.Globalization;
using System.IO;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The variable parent of an object or attached object (ADR 082): the element whose data it shows or runs, such as the crossing
    /// a traffic light belongs to. The inspector shows it, takes an ID, picks the object in the map, or takes it away.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        // While an element waits for its parent, the next object clicked in the map becomes it.
        private OmsiTileSummary varParentPickTile;
        private object varParentPickElement;

        private bool PickingVarParent => varParentPickElement != null;

        /// <summary>The group of an object in the inspector; open when the object has a variable parent.</summary>
        private void AddVarParentGroup(VisualElement editor, OmsiTileSummary tile, object element)
        {
            var has = (element as OmsiObjectRecord)?.VarParentId.HasValue ?? ((OmsiAttachedObjectRecord)element).VarParentId.HasValue;
            AddInspectorGroup(editor, "var-parent", Loc.T("varparent.group"), has, body => AddVarParentEditor(body, tile, element, false));
        }

        /// <param name="nested">The element hangs on the selected object: indented, without the hint.</param>
        private void AddVarParentEditor(VisualElement body, OmsiTileSummary tile, object element, bool nested)
        {
            var parentId = element is OmsiObjectRecord item ? item.VarParentId : ((OmsiAttachedObjectRecord)element).VarParentId;
            var canEdit = CanEdit;
            var container = new VisualElement();
            if (nested)
                container.style.marginLeft = 12;
            if (nested)
            {
                var heading = LocBindings.NewLabel("varparent.group");
                heading.AddToClassList("muted");
                container.Add(heading);
            }

            var current = new Label(parentId.HasValue ? Loc.T("varparent.current", ("target", DescribeVarParent(tile, parentId.Value))) : Loc.T("varparent.none"));
            current.AddToClassList("inspector-edit-hint");
            current.style.whiteSpace = WhiteSpace.Normal;
            container.Add(current);

            var field = new TextField { value = parentId.HasValue ? parentId.Value.ToString(CultureInfo.InvariantCulture) : string.Empty };
            LocBindings.Text(field, "varparent.field");
            field.AddToClassList("inspector-edit-field");
            field.SetEnabled(canEdit);
            container.Add(field);

            var buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.flexWrap = Wrap.Wrap;
            if (canEdit)
            {
                buttons.Add(VarParentButton("varparent.set", "varparent.set.tooltip", () => ApplyVarParentText(tile, element, field.value)));
                buttons.Add(VarParentButton("varparent.pick", "varparent.pick.tooltip", () => StartVarParentPick(tile, element)));
                if (parentId.HasValue)
                    buttons.Add(VarParentButton("varparent.remove", "varparent.remove.tooltip", () => ApplyVarParent(tile, element, null)));
            }
            if (parentId.HasValue && session?.FindVarParent(currentSummary, tile, parentId.Value) is OmsiObjectRecord)
                buttons.Add(VarParentButton("varparent.show", "varparent.show.tooltip", () => ShowVarParent(tile, parentId.Value)));
            container.Add(buttons);

            if (!nested)
            {
                var hint = LocBindings.NewLabel("varparent.hint");
                hint.AddToClassList("muted");
                hint.AddToClassList("inspector-edit-hint");
                hint.style.whiteSpace = WhiteSpace.Normal;
                container.Add(hint);
            }
            body.Add(container);
        }

        private static Button VarParentButton(string key, string tooltipKey, Action action)
        {
            var button = LocBindings.WithTooltip(LocBindings.NewButton(key, action), tooltipKey); // i18n:keys
            button.AddToClassList("map-control-button");
            button.AddToClassList("inspector-edit-apply");
            return button;
        }

        /// <summary>What an ID names, for the line under the group's title.</summary>
        private string DescribeVarParent(OmsiTileSummary tile, int parentId)
        {
            switch (session?.FindVarParent(currentSummary, tile, parentId))
            {
                case OmsiObjectRecord item:
                    return Loc.T("varparent.target.object", ("id", parentId), ("name", Path.GetFileNameWithoutExtension(item.AssetPath)));
                case OmsiAttachedObjectRecord attached:
                    return Loc.T("varparent.target.attached", ("id", parentId), ("name", Path.GetFileNameWithoutExtension(attached.AssetPath)));
                case OmsiSplineAttachmentRecord row:
                    return Loc.T("varparent.target.row", ("id", parentId), ("name", Path.GetFileNameWithoutExtension(row.AssetPath)));
                default:
                    return Loc.T("varparent.target.missing", ("id", parentId));
            }
        }

        private void ShowVarParent(OmsiTileSummary tile, int parentId)
        {
            if (session?.FindVarParent(currentSummary, tile, parentId) is OmsiObjectRecord item)
            {
                var holder = currentSummary.Tiles.Find(candidate => candidate.Objects.Contains(item));
                if (holder != null)
                    SelectObject(holder, item, true);
            }
        }

        private void ApplyVarParentText(OmsiTileSummary tile, object element, string text)
        {
            text = (text ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                ApplyVarParent(tile, element, null);
                return;
            }
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || id < 0)
            {
                SetStatusT("varparent.status.bad_id");
                return;
            }
            ApplyVarParent(tile, element, id);
        }

        private void ApplyVarParent(OmsiTileSummary tile, object element, int? parentId)
        {
            if (!CanEdit)
            {
                SetStatus(ReadOnlyMessage("readonly.action.var_parent"));
                return;
            }
            OmsiObjectChange change;
            try
            {
                change = session.SetVarParent(currentSummary, tile, element, parentId);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException || exception is IOException ||
                                              exception is UnauthorizedAccessException)
            {
                Debug.LogException(exception);
                SetStatus(Loc.T("varparent.status.failed", ("element", DescribeElement(element)), ("reason", exception.Message)));
                return;
            }
            var done = parentId.HasValue
                ? Loc.T("varparent.done.set", ("element", DescribeElement(element)), ("parent", parentId.Value))
                : Loc.T("varparent.done.removed", ("element", DescribeElement(element)));
            FinishElementChange(tile, change, Loc.T("varparent.step", ("element", DescribeElement(element))), done,
                Loc.T("varparent.same", ("element", DescribeElement(element))));
        }

        /// <summary>The next object clicked in the map becomes the variable parent of the element; Esc or any other click stops.</summary>
        private void StartVarParentPick(OmsiTileSummary tile, object element)
        {
            if (!CanEdit)
            {
                SetStatus(ReadOnlyMessage("readonly.action.var_parent"));
                return;
            }
            StopPlacement();
            if (activeTool != EditorTool.Drag)
                SetTool(EditorTool.Drag);
            varParentPickTile = tile;
            varParentPickElement = element;
            SetStatus(Loc.T("varparent.status.pick", ("element", DescribeElement(element))));
        }

        private void EndVarParentPick(bool announce)
        {
            if (!PickingVarParent)
                return;
            varParentPickTile = null;
            varParentPickElement = null;
            if (announce)
                SetStatusT("varparent.status.stays");
        }

        /// <summary>Called when an object is clicked: true when the click chose the parent and nothing else should happen.</summary>
        private bool CompleteVarParentPick(OmsiObjectRecord clicked)
        {
            if (!PickingVarParent)
                return false;
            var tile = varParentPickTile;
            var element = varParentPickElement;
            EndVarParentPick(false);
            ApplyVarParent(tile, element, clicked.Id);
            return true;
        }

        /// <summary>
        /// Command line (ADR 082): <c>-varParent id|none</c> sets or takes away the variable parent of the selected object, or with
        /// <c>-elementId n</c> of the attached object with that ID in its tile; "Variable parent:" in the log. <c>-elementUndo</c> and
        /// <c>-elementRedo</c> step through the history as for labels.
        /// </summary>
        private void RunVarParentArguments(string[] args)
        {
            var index = Array.IndexOf(args, "-varParent");
            if (index < 0 || index + 1 >= args.Length)
                return;
            if (selection.Count != 1)
            {
                Debug.LogWarning("-varParent needs one selected object.");
                return;
            }
            var tile = TileHolding(selection[0]) ?? selection[0].Tile;
            object element = selection[0].Record;
            var elementIndex = Array.IndexOf(args, "-elementId");
            if (elementIndex >= 0 && elementIndex + 1 < args.Length && int.TryParse(args[elementIndex + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var elementId))
            {
                element = (object)tile.AttachedObjects.Find(candidate => candidate.Id == elementId) ?? tile.Objects.Find(candidate => candidate.Id == elementId);
                if (element == null)
                {
                    Debug.LogWarning("-elementId " + elementId + " names no object or attached object in tile " + tile.X + ", " + tile.Y + ".");
                    return;
                }
            }
            var value = args[index + 1];
            int? parent = null;
            if (!string.Equals(value, "none", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                {
                    Debug.LogWarning("-varParent takes an ID or none.");
                    return;
                }
                parent = id;
            }
            ApplyVarParent(tile, element, parent);
            Debug.Log("Variable parent: " + statusLabel?.text);
        }
    }
}
