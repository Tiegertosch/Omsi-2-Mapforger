using System;
using System.Collections.Generic;
using System.IO;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The parking list of the open map (ADR 087): the Map tab's "Parking list..." button opens a dialog with a tick for each parked
    /// car of the installation (and each car the list names that is not found there). Apply writes <c>parklist_p.txt</c> of the
    /// project copy with exactly the ticked cars; a car that was in the list keeps its weight.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        private VisualElement parkListDialog;
        private ScrollView parkListList;
        private List<string> parkListCurrent = new List<string>();
        private IntegerField parkListNumber;
        private int parkListIndex;
        private readonly Dictionary<string, Toggle> parkListToggles = new Dictionary<string, Toggle>(StringComparer.OrdinalIgnoreCase);

        private void SetUpParkList(VisualElement root)
        {
            parkListDialog = new VisualElement { name = "park-list-dialog" };
            parkListDialog.AddToClassList("dialog-overlay");
            parkListDialog.style.display = DisplayStyle.None;
            var dialog = new VisualElement();
            dialog.AddToClassList("dialog");
            dialog.AddToClassList("content-folders");
            var title = LocBindings.NewLabel("parklist.title");
            title.AddToClassList("popup-title");
            dialog.Add(title);
            var description = LocBindings.NewLabel("parklist.description");
            description.AddToClassList("popup-description");
            dialog.Add(description);
            // The list a car park draws from is named by the first label of its object: 0 is parklist_p.txt, n is parklist_p_n.txt.
            var numberRow = new VisualElement();
            numberRow.style.flexDirection = FlexDirection.Row;
            numberRow.style.alignItems = Align.Center;
            var numberLabel = LocBindings.NewLabel("parklist.number");
            numberLabel.AddToClassList("popup-description");
            numberLabel.style.marginRight = 8;
            numberRow.Add(numberLabel);
            parkListNumber = new IntegerField { value = 0 };
            parkListNumber.AddToClassList("picker-field");
            LocBindings.Tooltip(parkListNumber, "parklist.number.tooltip");
            parkListNumber.RegisterValueChangedCallback(change =>
            {
                var number = Mathf.Clamp(change.newValue, 0, 99);
                if (number != change.newValue)
                    parkListNumber.SetValueWithoutNotify(number);
                if (number != parkListIndex)
                    ShowParkList(number);
            });
            numberRow.Add(parkListNumber);
            dialog.Add(numberRow);
            parkListList = new ScrollView(ScrollViewMode.Vertical);
            parkListList.AddToClassList("content-folders-list");
            dialog.Add(parkListList);
            var buttons = new VisualElement();
            buttons.AddToClassList("dialog-buttons");
            var cancel = LocBindings.NewButton("parklist.cancel", () => HidePopup(parkListDialog));
            cancel.AddToClassList("map-control-button");
            buttons.Add(cancel);
            var apply = LocBindings.NewButton("parklist.apply", ApplyParkList);
            apply.AddToClassList("map-control-button");
            apply.AddToClassList("dialog-primary");
            buttons.Add(apply);
            dialog.Add(buttons);
            parkListDialog.Add(dialog);
            root.Add(parkListDialog);
        }

        /// <summary>The button under the map settings button in the Map tab.</summary>
        private Button ParkListButton()
        {
            var button = new Button(OpenParkList) { text = Loc.T("parklist.open"), tooltip = Loc.T("parklist.open.tooltip") };
            button.AddToClassList("map-control-button");
            button.style.marginTop = 4;
            return button;
        }

        private void OpenParkList()
        {
            if (parkListDialog == null || currentSummary == null || session == null || !session.CanEdit)
                return;
            parkListNumber.SetValueWithoutNotify(0);
            ShowParkList(0);
            parkListDialog.style.display = DisplayStyle.Flex;
        }

        /// <summary>Fills the dialog with the cars of one list; a list that does not exist yet starts with no car ticked (list 0 with all).</summary>
        private void ShowParkList(int index)
        {
            parkListIndex = index;
            var root = OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath);
            parkListCurrent = session.ReadParkList(index);
            var exists = session.HasParkList(index);
            parkListList.Clear();
            parkListToggles.Clear();
            var candidates = OmsiParkList.Candidates(root, parkListCurrent);
            if (candidates.Count == 0)
            {
                var empty = new Label(Loc.T("parklist.empty"));
                empty.AddToClassList("panel-info");
                parkListList.Add(empty);
            }
            var inList = new HashSet<string>(parkListCurrent, StringComparer.OrdinalIgnoreCase);
            foreach (var entry in candidates)
            {
                var weight = parkListCurrent.FindAll(each => string.Equals(each, entry, StringComparison.OrdinalIgnoreCase)).Count;
                var name = Path.GetFileNameWithoutExtension(entry);
                var toggle = new Toggle
                {
                    text = weight > 1 ? Loc.T("parklist.entry_weight", ("name", name), ("count", weight)) : name,
                    value = inList.Contains(entry) || (index == 0 && !exists),
                    tooltip = entry
                };
                toggle.AddToClassList("content-folders-toggle");
                parkListToggles[entry] = toggle;
                parkListList.Add(toggle);
            }
            parkListList.scrollOffset = Vector2.zero;
        }

        private void ApplyParkList()
        {
            if (session == null || !session.CanEdit)
                return;
            var chosen = new List<string>();
            foreach (var pair in parkListToggles)
                if (pair.Value.value)
                    chosen.Add(pair.Key);
            var entries = OmsiParkList.Merge(parkListCurrent, chosen);
            bool changed;
            try
            {
                changed = session.WriteParkList(entries, parkListIndex);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogException(exception);
                SetStatusT("parklist.status.failed", ("reason", exception.Message));
                return;
            }
            HidePopup(parkListDialog);
            UpdateMapStateLabel();
            SetStatusT(changed ? "parklist.status.changed" : "parklist.status.unchanged", ("count", chosen.Count)); // i18n:keys
        }

        /// <summary>
        /// Command line: <c>-pickAt x y</c> (panel pixels from the top left) writes the element of the interface under that point to the
        /// log as "Pick at:", so that a screenshot check can show whether a transparent part of a panel catches clicks meant for the map.
        /// </summary>
        private void RunPickArguments(string[] args)
        {
            var index = Array.IndexOf(args, "-pickAt");
            if (index < 0 || index + 2 >= args.Length || rootElement?.panel == null)
                return;
            if (!float.TryParse(args[index + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x) ||
                !float.TryParse(args[index + 2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y))
                return;
            var picked = rootElement.panel.Pick(new Vector2(x, y));
            Debug.Log("Pick at: " + (picked == null ? "nothing" : picked.GetType().Name + " " + (picked.name ?? string.Empty) + " [" + string.Join(" ", picked.GetClasses()) + "]"));
        }

        /// <summary>Command line, with a project: <c>-parkList</c> opens the dialog (for a screenshot).</summary>
        private void RunParkListArguments(string[] args)
        {
            if (Array.IndexOf(args, "-parkList") >= 0)
                OpenParkList();
        }
    }
}
