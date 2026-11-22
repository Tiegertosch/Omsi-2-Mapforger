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
        private ModalDialog parkListDialog;
        private ScrollView parkListList;
        private List<string> parkListCurrent = new List<string>();
        private IntegerField parkListNumber;
        private int parkListIndex;
        private readonly Dictionary<string, Toggle> parkListToggles = new Dictionary<string, Toggle>(StringComparer.OrdinalIgnoreCase);

        private void SetUpParkList(VisualElement root)
        {
            parkListDialog = NewDialog(new DialogOptions
            {
                Name = "park-list-dialog",
                TitleKey = "parklist.title",
                DescriptionKey = "parklist.description",
                Width = DialogWidth.Standard,
                BodyScrolls = false,
                EnterPressesPrimary = false,
                Buttons = new[]
                {
                    new DialogButton { TextKey = "parklist.cancel", Role = DialogButtonRole.Cancel },
                    new DialogButton { TextKey = "parklist.apply", Role = DialogButtonRole.Primary, Clicked = ApplyParkList, Closes = false }
                }
            });
            // The list scrolls by itself and shrinks with the window; the body only holds the number row and the list.
            parkListDialog.Body.AddToClassList("dialog-body-fill");
            var dialog = parkListDialog.Body;
            // The list a car park draws from is named by the first label of its object: 0 is parklist_p.txt, n is parklist_p_n.txt.
            var numberRow = new VisualElement();
            numberRow.AddToClassList("form-row");
            var numberLabel = LocBindings.NewLabel("parklist.number");
            numberLabel.AddToClassList("popup-description");
            numberLabel.AddToClassList("form-inline-label");
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
        }

        /// <summary>The button under the map settings button in the Map tab.</summary>
        private Button ParkListButton()
        {
            var button = new Button(OpenParkList) { text = Loc.T("parklist.open"), tooltip = Loc.T("parklist.open.tooltip") };
            button.AddToClassList("map-control-button");
            button.AddToClassList("form-button-top");
            return button;
        }

        private void OpenParkList()
        {
            if (parkListDialog == null || currentSummary == null || session == null || !session.CanEdit)
                return;
            parkListNumber.SetValueWithoutNotify(0);
            ShowParkList(0);
            parkListDialog.Show();
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
            parkListDialog.Hide();
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
