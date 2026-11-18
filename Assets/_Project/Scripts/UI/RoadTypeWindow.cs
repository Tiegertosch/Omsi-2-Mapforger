using System;
using System.Collections.Generic;
using System.Globalization;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The road type window (briefing of 18 November 2026, ADR 098): one window that floats beside the map like the signals window, in four
    /// areas from top to bottom: the title bar, the row with the name, undo and redo and the templates, the work area (the cross-section on
    /// the left, the properties of the selected element on the right, or below it in a narrow window) and the footer (the state of the
    /// changes, Discard, Save road type). It works on an <see cref="OmsiRoadTypeDraft"/>, so nothing reaches a file or the map before
    /// saving. The method names of the old road builder panel (SetType, Edit, SelectLane ...) stay as a facade for the command line.
    /// </summary>
    internal sealed partial class RoadTypeWindow : FloatingWindowFrame
    {
        /// <summary>The window is not made smaller than this by its grip.</summary>
        public static readonly Vector2 LeastSize = new Vector2(520f, 340f);

        private const float NarrowWidth = 640f;

        private OmsiRoadTypeDraft draft;
        private bool nameTyped;
        private int uses;
        private string chosenProfile;
        private string installation;
        private ICollection<string> excludedFolders;
        private string projectName = string.Empty;
        private Func<bool> canEdit = () => true;
        private bool saving;

        private readonly TextField nameField;
        private readonly Button undoButton;
        private readonly Button redoButton;
        private readonly Button templatesButton;
        private readonly Label templatesButtonLabel;
        private readonly ScrollView workView;
        private readonly VisualElement workRow;
        private readonly RoadSurfaceCatalog surfaces = new RoadSurfaceCatalog();
        private readonly RoadCrossSectionElement crossSection;
        private readonly Label totalLabel;
        private readonly Label summaryLabel;
        private readonly Label selectionLabel;
        private readonly Button moveLeftButton;
        private readonly Button moveRightButton;
        private readonly Button duplicateButton;
        private readonly Button removeButton;
        private readonly Button addToggle;
        private readonly VisualElement palette;
        private Label addHint;
        private readonly List<(Button Button, OmsiRoadDraftSide Side)> sideButtons = new List<(Button, OmsiRoadDraftSide)>();
        private readonly List<(Button Button, OmsiRoadLaneKind Kind)> kindButtons = new List<(Button, OmsiRoadLaneKind)>();
        private readonly VisualElement inspector;
        private readonly VisualElement templatesView;
        private bool paletteOpen;
        private OmsiRoadDraftSide addSide = OmsiRoadDraftSide.After;
        private Dictionary<int, string> problemTexts = new Dictionary<int, string>();

        /// <param name="close">What the close button does: the shell closes the window and asks first when the draft has changes.</param>
        public RoadTypeWindow(Action close)
            : base(new FloatingWindowOptions
            {
                Icon = "spline",
                TitleKey = "roadbuilder.window.title",
                CollapseTooltipKey = "roadbuilder.window.collapse.tooltip",
                ExpandTooltipKey = "roadbuilder.window.expand.tooltip",
                CloseTooltipKey = "roadbuilder.window.close.tooltip",
                ResizeTooltipKey = "roadbuilder.window.resize.tooltip",
                DefaultSize = new Vector2(860f, 780f),
                LeastSize = LeastSize
            }, close)
        {
            AddToClassList("road-window");
            // A click on an empty place of the window takes the focus, so the keys of the map stay silent whatever was clicked.
            focusable = true;
            tabIndex = -1;

            // 2. The row: name, undo, redo, templates.
            var nameRow = new VisualElement();
            nameRow.AddToClassList("road-window-namerow");
            nameField = new TextField { maxLength = 60 };
            nameField.AddToClassList("picker-field");
            nameField.AddToClassList("road-window-name");
            nameField.textEdition.placeholder = Loc.T("roadbuilder.panel.name.placeholder");
            nameField.RegisterValueChangedCallback(evt =>
            {
                if (nameField.isReadOnly)
                    return;
                nameTyped = !string.IsNullOrWhiteSpace(evt.newValue);
                RenderStatus();
            });
            nameRow.Add(nameField);
            var spacer = new VisualElement();
            spacer.style.flexGrow = 1f;
            nameRow.Add(spacer);
            undoButton = IconButton("undo-2", "roadbuilder.window.undo.tooltip", UndoDraft);
            redoButton = IconButton("redo-2", "roadbuilder.window.redo.tooltip", RedoDraft);
            nameRow.Add(undoButton);
            nameRow.Add(redoButton);
            templatesButton = new Button(ToggleTemplates);
            templatesButton.AddToClassList("map-control-button");
            templatesButton.AddToClassList("road-window-templates-button");
            templatesButton.Add(new IconElement("layout-grid"));
            templatesButtonLabel = new Label();
            templatesButtonLabel.pickingMode = PickingMode.Ignore;
            templatesButton.Add(templatesButtonLabel);
            nameRow.Add(templatesButton);
            Content.Add(nameRow);

            // 3. The work area: the cross-section and the properties; a narrow window puts the properties below.
            workView = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            workView.AddToClassList("road-window-work");
            workRow = workView.contentContainer;
            workRow.AddToClassList("road-window-columns");
            var left = new VisualElement();
            left.AddToClassList("road-window-left");
            var head = new VisualElement();
            head.AddToClassList("road-window-head");
            var headTitle = LocBindings.NewLabel("roadbuilder.window.section.title");
            headTitle.AddToClassList("inspector-section-heading");
            head.Add(headTitle);
            var headHint = LocBindings.NewLabel("roadbuilder.window.section.hint");
            headHint.AddToClassList("road-window-hint");
            head.Add(headHint);
            left.Add(head);
            crossSection = new RoadCrossSectionElement(surfaces) { ProblemOf = id => problemTexts.TryGetValue(id, out var text) ? text : null };
            crossSection.ElementClicked += id => PickElement(id, false);
            crossSection.BoundaryClicked += id => PickElement(id, true);
            crossSection.MoveRequested += MoveElementTo;
            left.Add(crossSection);
            totalLabel = new Label();
            totalLabel.AddToClassList("road-window-total");
            left.Add(totalLabel);

            // The selection and what can be done with it, then adding.
            var actions = new VisualElement();
            actions.AddToClassList("road-window-actions");
            selectionLabel = new Label();
            selectionLabel.AddToClassList("road-window-selection");
            actions.Add(selectionLabel);
            moveLeftButton = IconButton("arrow-left", "roadbuilder.window.action.left.tooltip", () => MoveSelected(-1));
            moveRightButton = IconButton("arrow-right", "roadbuilder.window.action.right.tooltip", () => MoveSelected(1));
            duplicateButton = TextIconButton("copy", "roadbuilder.window.action.duplicate", "roadbuilder.window.action.duplicate.tooltip", DuplicateSelected);
            removeButton = TextIconButton("trash-2", "roadbuilder.window.action.remove", "roadbuilder.window.action.remove.tooltip", RemoveSelected);
            removeButton.AddToClassList("road-window-danger");
            foreach (var button in new[] { moveLeftButton, moveRightButton, duplicateButton, removeButton })
                actions.Add(button);
            left.Add(actions);

            addToggle = new DashedButton(TogglePalette);
            addToggle.AddToClassList("road-window-add-toggle");
            addToggle.Add(new IconElement("plus") { name = "add-icon" });
            var addToggleLabel = new Label { pickingMode = PickingMode.Ignore };
            LocBindings.Text(addToggleLabel, "roadbuilder.window.add");
            addToggle.Add(addToggleLabel);
            LocBindings.Tooltip(addToggle, "roadbuilder.window.add.tooltip");
            left.Add(addToggle);
            palette = BuildPalette();
            left.Add(palette);

            summaryLabel = new Label();
            summaryLabel.AddToClassList("panel-info");
            left.Add(summaryLabel);
            workRow.Add(left);
            inspector = new VisualElement();
            inspector.AddToClassList("road-window-inspector");
            BuildInspector(inspector);
            workRow.Add(inspector);
            workView.RegisterCallback<GeometryChangedEvent>(_ => KeepLayoutFitted());
            Content.Add(workView);
            BuildSurfacesView();

            // The previews of the surfaces arrive from worker threads; what draws them draws again when they have.
            surfaces.PreviewsChanged += OnPreviewsChanged;
            schedule.Execute(() => surfaces.Pump()).Every(100);

            // The templates, in the same window in place of the work area.
            templatesView = BuildTemplatesView();
            Content.Add(templatesView);

            // 4. The question and the footer, which the frame adds below the work area.
            CompleteFrame();
            LocBindings.Text(DiscardButton, "roadbuilder.window.discard");
            LocBindings.Tooltip(DiscardButton, "roadbuilder.window.discard.tooltip");
            DiscardButton.clicked += DiscardChanges;
            LocBindings.Text(ApplyButton, "roadbuilder.window.save");
            ApplyButton.clicked += Save;
            LocBindings.Text(PromptDiscard, "roadbuilder.window.prompt.discard");
            LocBindings.Text(PromptKeep, "roadbuilder.window.prompt.keep");
            PromptApply.style.display = DisplayStyle.None;

            Reset();
        }

        // ----- events for the shell -----

        /// <summary>Save road type for a road type not saved yet: the name and the road type.</summary>
        public event Action<string, OmsiRoadType> SaveRequested;

        /// <summary>Save road type for a saved one: writes it anew, so every spline of it changes.</summary>
        public event Action<OmsiRoadType> ReplaceRequested;

        public event Action DeleteRequested;

        /// <summary>The window took the lanes of the road chosen in the road tool as its draft (Templates, From the chosen road); the shell tells so in its status line.</summary>
        public event Action<RoadChosenSource> ChosenRoadTaken;

        /// <summary>Takes the road chosen in the road tool apart without changing anything: the lanes and the remarks, or no lanes when it has none. Set by the shell.</summary>
        public Func<string, RoadChosenSource> ChosenRoadResolver { get; set; }

        /// <summary>The object of the selected element's row is to be chosen in the object library.</summary>
        public event Action RowObjectRequested;

        /// <summary>The draft changed in some way (a command, undo, redo, discard, saved); the shell drops a question about changes that became stale.</summary>
        public event Action Changed;

        // ----- state the shell reads -----

        public bool HasUnsavedChanges => draft.IsDirty;

        public string EditingPath => draft.AssetPath;

        public OmsiRoadType Type => draft.ToRoadType();

        /// <summary>The name to save under: the one typed, or the suggestion of the road type; for a saved road type its file name.</summary>
        public string RoadName
        {
            get
            {
                if (!draft.IsNew)
                    return System.IO.Path.GetFileNameWithoutExtension(draft.AssetPath);
                return nameTyped && !string.IsNullOrWhiteSpace(nameField.value) ? nameField.value.Trim() : draft.ToRoadType().SuggestName();
            }
        }

        /// <summary>The element selected, from 0, or -1 for none or a line.</summary>
        public int SelectedLane => draft.SelectionKind == OmsiRoadDraftSelectionKind.Element ? draft.SelectedIndex : -1;

        /// <summary>Tells the window the open project and whether it may be changed, for what Save road type may overwrite.</summary>
        public void SetProject(string name, Func<bool> mayEdit)
        {
            projectName = name ?? string.Empty;
            canEdit = mayEdit ?? (() => true);
            Render();
        }

        public void SetInstallation(string root)
        {
            installation = root;
            surfaces.SetInstallation(root);
        }

        public void SetExcludedFolders(ICollection<string> folders)
        {
            excludedFolders = folders;
            surfaces.SetExcludedFolders(folders);
        }

        /// <summary>The road chosen in the road tool, which From the chosen road takes apart; null for none.</summary>
        public void SetChosenProfile(string path)
        {
            if (string.Equals(chosenProfile, path, StringComparison.Ordinal))
                return;
            chosenProfile = path;
            chosenSource = null;
            chosenSourceFor = null;
            RenderTemplates();
        }

        // ----- the draft, under the names of the old panel -----

        /// <summary>A new road type from the template "2 lanes"; the history starts anew. Opening the window does not call it (the draft stays).</summary>
        public void Reset()
        {
            nameTyped = false;
            uses = 0;
            nameField.SetValueWithoutNotify(string.Empty);
            draft = new OmsiRoadTypeDraft(OmsiRoadTypeTemplate.Find("2-lanes").Type());
            crossSection.SetDraft(draft);
            ShowTemplates(false);
            Render();
            Changed?.Invoke();
        }

        /// <summary>Takes a road type as the draft's content (a step of its history); the baseline stays.</summary>
        public void SetType(OmsiRoadType road)
        {
            if (road == null)
                return;
            draft.Apply(road);
            draft.EndGesture();
            ShowTemplates(false);
            Render();
            Changed?.Invoke();
        }

        /// <summary>Opens a saved road type: its draft starts clean, its name is its file name and cannot be changed (decision 4).</summary>
        public void Edit(string assetPath, OmsiRoadType road, int uses)
        {
            this.uses = uses;
            nameTyped = false;
            draft = new OmsiRoadTypeDraft(road, assetPath);
            crossSection.SetDraft(draft);
            ShowTemplates(false);
            Render();
            Changed?.Invoke();
        }

        /// <summary>The draft was saved under a path: it is the new saved state and the window stays open on it.</summary>
        public void Saved(string assetPath)
        {
            draft.Rebase(assetPath);
            Render();
            Changed?.Invoke();
        }

        public void SetName(string name)
        {
            if (!draft.IsNew)
                return;
            nameField.SetValueWithoutNotify(name ?? string.Empty);
            nameTyped = !string.IsNullOrWhiteSpace(name);
            RenderStatus();
        }

        public void SelectLane(int index)
        {
            if (index < 0 || index >= draft.Count)
                return;
            draft.SelectElement(draft.IdAt(index));
            Render();
        }

        public void SelectLine(int afterIndex)
        {
            if (afterIndex < 0 || afterIndex >= draft.Count - 1)
                return;
            draft.SelectBoundary(draft.IdAt(afterIndex));
            Render();
        }

        /// <summary>The object chosen for the selected element's row, with its suggested turn.</summary>
        public void SetRowObject(string assetPath, double turn)
        {
            if (draft.SelectionKind != OmsiRoadDraftSelectionKind.Element)
                return;
            var id = draft.SelectedId;
            var lane = draft.LaneOf(id);
            // A row on a median starts in its middle (as it always did); both changes are one step.
            if (lane.RowObject == null && lane.Kind == OmsiRoadLaneKind.Median)
                draft.SetRowPlace(id, OmsiRoadRowPlace.Middle, "row-object");
            draft.SetRowObject(id, assetPath, turn, "row-object");
            draft.EndGesture();
            equipmentOpen = true;
            Render();
            Changed?.Invoke();
        }

        /// <summary>The window shows the templates in place of the work area, or the work area.</summary>
        public void ShowTemplates(bool show)
        {
            ShowPage(show ? RoadWindowPage.Templates : RoadWindowPage.Work);
        }

        public bool TemplatesShown => page == RoadWindowPage.Templates;

        private RoadWindowPage page = RoadWindowPage.Work;

        /// <summary>One of the three pages the window shows in place of each other, under the name row.</summary>
        private enum RoadWindowPage
        {
            Work,
            Templates,
            Surfaces
        }

        private void ShowPage(RoadWindowPage next)
        {
            page = next;
            templatesView.style.display = next == RoadWindowPage.Templates ? DisplayStyle.Flex : DisplayStyle.None;
            surfacesView.style.display = next == RoadWindowPage.Surfaces ? DisplayStyle.Flex : DisplayStyle.None;
            workView.style.display = next == RoadWindowPage.Work ? DisplayStyle.Flex : DisplayStyle.None;
            var templates = next == RoadWindowPage.Templates;
            LocBindings.Text(templatesButtonLabel, templates ? "roadbuilder.window.templates.back" : "roadbuilder.window.templates"); // i18n:keys
            LocBindings.Tooltip(templatesButton, templates ? "roadbuilder.window.templates.back.tooltip" : "roadbuilder.window.templates.tooltip"); // i18n:keys
            if (templates)
            {
                // The chosen road may have changed on disk or in the road tool since the page was last open.
                chosenSource = null;
                chosenSourceFor = null;
                RenderTemplates();
            }
            if (next == RoadWindowPage.Work && draft != null)
                crossSection.Sync(true);
        }

        /// <summary>Goes back to the saved road type (one step, so undo brings the changes back).</summary>
        public void DiscardChanges()
        {
            if (!draft.IsDirty)
                return;
            draft.Discard();
            draft.EndGesture();
            Render();
            Changed?.Invoke();
        }

        /// <summary>The language changed: what the window wrote from data is written again.</summary>
        public void RefreshTexts()
        {
            nameField.textEdition.placeholder = Loc.T("roadbuilder.panel.name.placeholder");
            surfaceSearch.textEdition.placeholder = Loc.T("roadbuilder.panel.search");
            RefreshPropertyTexts();
            crossSection.Sync(true);
            Render();
        }

        /// <summary>
        /// A key pressed while the focus is in the window (outside a text field): Ctrl+Z and Ctrl+Y act on the draft; every other key does
        /// nothing here and, by the shell, nothing on the map. True when the key was taken.
        /// </summary>
        public bool HandleKey(KeyDownEvent evt)
        {
            switch (RoadTypeWindowRules.KeyAction(evt.keyCode, evt.ctrlKey || evt.commandKey, evt.shiftKey))
            {
                case RoadWindowKeyAction.Undo:
                    UndoDraft();
                    evt.StopPropagation();
                    return true;
                case RoadWindowKeyAction.Redo:
                    RedoDraft();
                    evt.StopPropagation();
                    return true;
                default:
                    return false;
            }
        }

        // ----- commands -----

        /// <summary>Undoes the last step of the draft's history (Ctrl+Z, the button).</summary>
        public void UndoDraft()
        {
            if (!draft.Undo())
                return;
            Render();
            Changed?.Invoke();
        }

        /// <summary>Redoes the step undone last (Ctrl+Y, the button).</summary>
        public void RedoDraft()
        {
            if (!draft.Redo())
                return;
            Render();
            Changed?.Invoke();
        }

        private void ToggleTemplates()
        {
            ShowTemplates(!TemplatesShown);
        }

        // ----- the strip, the actions under it and adding -----

        /// <summary>A click in the strip or on its keyboard: selects the element or the line after it.</summary>
        private void PickElement(int id, bool boundary)
        {
            if (boundary)
                draft.SelectBoundary(id);
            else
                draft.SelectElement(id);
            Render();
        }

        private void MoveElementTo(int id, int index)
        {
            if (draft.MoveTo(id, index))
                AfterCommand();
        }

        private void MoveSelected(int by)
        {
            if (draft.SelectionKind == OmsiRoadDraftSelectionKind.Element && draft.Move(draft.SelectedId, by))
                AfterCommand();
        }

        private void DuplicateSelected()
        {
            if (draft.SelectionKind == OmsiRoadDraftSelectionKind.Element && draft.Duplicate(draft.SelectedId) >= 0)
                AfterCommand();
        }

        private void RemoveSelected()
        {
            if (draft.SelectionKind == OmsiRoadDraftSelectionKind.Element && draft.Remove(draft.SelectedId))
                AfterCommand();
        }

        private void AddElement(OmsiRoadLaneKind kind)
        {
            if (draft.Add(kind, addSide) >= 0)
                AfterCommand();
        }

        /// <summary>A command of the draft that is one step of its own is done: the next change starts a step of its own, the window draws it and the shell hears of it.</summary>
        private void AfterCommand()
        {
            draft.EndGesture();
            Render();
            Changed?.Invoke();
        }

        private void TogglePalette()
        {
            paletteOpen = !paletteOpen;
            RenderAdding();
        }

        private VisualElement BuildPalette()
        {
            var box = new VisualElement();
            box.AddToClassList("road-window-palette");
            var top = new VisualElement();
            top.AddToClassList("road-window-palette-top");
            addHint = LocBindings.NewLabel("roadbuilder.window.add.hint");
            addHint.AddToClassList("road-window-palette-hint");
            top.Add(addHint);
            var sides = new VisualElement();
            sides.AddToClassList("panel-tabs");
            sides.AddToClassList("picker-segmented");
            sides.AddToClassList("road-window-side");
            foreach (var side in new[] { OmsiRoadDraftSide.Before, OmsiRoadDraftSide.After })
            {
                var chosen = side;
                var left = side == OmsiRoadDraftSide.Before;
                var button = SegmentButton(left ? "arrow-left" : "arrow-right",
                    left ? "roadbuilder.window.add.left" : "roadbuilder.window.add.right", // i18n:keys
                    left ? "roadbuilder.window.add.left.tooltip" : "roadbuilder.window.add.right.tooltip", // i18n:keys
                    () =>
                    {
                        addSide = chosen;
                        RenderAdding();
                    });
                sideButtons.Add((button, side));
                sides.Add(button);
            }
            top.Add(sides);
            box.Add(top);
            var kinds = new VisualElement();
            kinds.AddToClassList("road-window-kinds");
            foreach (OmsiRoadLaneKind kind in Enum.GetValues(typeof(OmsiRoadLaneKind)))
            {
                var chosen = kind;
                var button = new Button(() => AddElement(chosen));
                button.AddToClassList("map-control-button");
                button.AddToClassList("road-window-kind");
                var swatch = new VisualElement { pickingMode = PickingMode.Ignore };
                swatch.AddToClassList("road-swatch");
                swatch.AddToClassList("road-swatch-" + OmsiRoadLane.Word(kind));
                button.Add(swatch);
                var label = new Label { pickingMode = PickingMode.Ignore };
                LocBindings.Text(label, RoadTypeWindowRules.LaneKey(kind)); // i18n:keys
                button.Add(label);
                LocBindings.Tooltip(button, "roadbuilder.window.add.kind.tooltip", ("kind", Loc.T(RoadTypeWindowRules.LaneKey(kind)))); // i18n:keys
                kindButtons.Add((button, kind));
                kinds.Add(button);
            }
            box.Add(kinds);
            return box;
        }

        /// <summary>The palette and its side segment as they are now: shown when opened, the elements disabled at the limit with the reason.</summary>
        private void RenderAdding()
        {
            if (draft == null)
                return;
            var actions = RoadTypeWindowRules.Actions(draft.Count, draft.SelectedIndex, draft.SelectionKind == OmsiRoadDraftSelectionKind.Element);
            palette.style.display = paletteOpen ? DisplayStyle.Flex : DisplayStyle.None;
            addToggle.EnableInClassList("road-window-add-open", paletteOpen);
            addToggle.SetEnabled(actions.CanAdd);
            if (actions.CanAdd)
                LocBindings.Tooltip(addToggle, "roadbuilder.window.add.tooltip");
            else
                LocBindings.Tooltip(addToggle, actions.AddBlockedKey); // i18n:keys
            foreach (var pair in sideButtons)
                pair.Button.EnableInClassList("panel-tab-active", pair.Side == addSide);
            foreach (var pair in kindButtons)
                pair.Button.SetEnabled(actions.CanAdd);
            // At the limit the palette says why nothing can be added.
            LocBindings.Text(addHint, actions.CanAdd ? "roadbuilder.window.add.hint" : actions.AddBlockedKey); // i18n:keys
        }

        private void RenderActions()
        {
            var element = draft.SelectionKind == OmsiRoadDraftSelectionKind.Element;
            var actions = RoadTypeWindowRules.Actions(draft.Count, draft.SelectedIndex, element);
            moveLeftButton.SetEnabled(actions.CanMoveLeft);
            moveRightButton.SetEnabled(actions.CanMoveRight);
            duplicateButton.SetEnabled(actions.CanDuplicate);
            removeButton.SetEnabled(actions.CanRemove);
            if (element && !actions.CanDuplicate)
                LocBindings.Tooltip(duplicateButton, actions.AddBlockedKey ?? "roadbuilder.window.action.duplicate.tooltip"); // i18n:keys
            else
                LocBindings.Tooltip(duplicateButton, "roadbuilder.window.action.duplicate.tooltip");
            switch (draft.SelectionKind)
            {
                case OmsiRoadDraftSelectionKind.Element:
                    LocBindings.Text(selectionLabel, "roadbuilder.window.selected.element", ("number", draft.SelectedIndex + 1)); // i18n:keys
                    break;
                case OmsiRoadDraftSelectionKind.Boundary:
                    LocBindings.Text(selectionLabel, "roadbuilder.window.selected.line", ("number", draft.SelectedIndex + 1)); // i18n:keys
                    break;
                default:
                    LocBindings.Text(selectionLabel, draft.Count == 0 ? "roadbuilder.window.selected.empty" : "roadbuilder.window.selected.none"); // i18n:keys
                    break;
            }
        }

        private void Save()
        {
            if (saving || SaveBlock() != RoadSaveBlock.None)
                return;
            // The shell writes at once; a second click while it does (or after, when nothing is left to save) finds the button blocked.
            saving = true;
            try
            {
                if (draft.IsNew)
                    SaveRequested?.Invoke(RoadName, draft.ToRoadType());
                else
                    ReplaceRequested?.Invoke(draft.ToRoadType());
            }
            finally
            {
                saving = false;
                Render();
            }
        }

        private RoadSaveBlock SaveBlock()
        {
            return RoadTypeWindowRules.SaveBlock(draft.IsNew, draft.IsDirty, draft.IsStale, draft.Problems().Count, canEdit(),
                !draft.IsNew && OmsiRoadTypeOwnership.InProjectFolder(draft.AssetPath, projectName));
        }

        // ----- drawing -----

        private Button IconButton(string icon, string tooltipKey, Action clicked)
        {
            var button = new Button(clicked);
            button.AddToClassList("map-control-button");
            button.AddToClassList("road-window-icon-button");
            button.Add(new IconElement(icon));
            LocBindings.Tooltip(button, tooltipKey); // i18n:keys
            return button;
        }

        private bool rendering;

        private void Render()
        {
            // Hiding a control that has the focus makes it commit its text, which asks for a drawing again: that one is skipped, the outer one is complete.
            if (draft == null || rendering)
                return;
            rendering = true;
            try
            {
                RenderAll();
            }
            finally
            {
                rendering = false;
            }
        }

        private void RenderAll()
        {
            // A button that was clicked and is disabled now would take the focus with it, and the map's keys would wake up: the window keeps it.
            var hadFocus = HasFocusInside();
            var edit = !draft.IsNew;
            SetDetailText(edit ? Loc.T("roadbuilder.window.title.edit", ("name", RoadName)) : Loc.T("roadbuilder.window.title.new"));
            nameField.isReadOnly = edit;
            nameField.EnableInClassList("road-window-name-locked", edit);
            if (edit)
                nameField.SetValueWithoutNotify(RoadName);
            else if (!nameTyped)
                nameField.SetValueWithoutNotify(draft.ToRoadType().SuggestName());
            LocBindings.Tooltip(nameField, edit ? "roadbuilder.window.name.tooltip_edit" : "roadbuilder.panel.name.tooltip_new"); // i18n:keys
            undoButton.SetEnabled(draft.CanUndo);
            redoButton.SetEnabled(draft.CanRedo);
            // The surface picker belongs to a selected element.
            if (page == RoadWindowPage.Surfaces && draft.SelectionKind != OmsiRoadDraftSelectionKind.Element)
                ShowPage(RoadWindowPage.Work);
            problemTexts = ProblemTextsByElement();
            crossSection.Sync();
            totalLabel.text = Loc.T("roadbuilder.window.total", ("width", draft.Width));
            summaryLabel.text = Loc.T("roadbuilder.window.summary", ("count", draft.Count), ("traffic", draft.TrafficLanes));
            RenderActions();
            RenderAdding();
            SyncInspector();
            RenderStatus();
            RenderTemplates();
            if (page == RoadWindowPage.Surfaces)
                RenderSurfacesHeader();
            EnableInClassList("signal-dirty", draft.IsDirty);
            var now = panel?.focusController?.focusedElement as VisualElement;
            if (RoadTypeWindowRules.NeedsFocusBack(hadFocus, now != null, now != null && (now == this || Contains(now)), now != null && IsReachable(now)))
                Focus();
        }

        /// <summary>Whether the keyboard focus is on the window or on something in it.</summary>
        public bool HasFocusInside()
        {
            var focused = panel?.focusController?.focusedElement as VisualElement;
            return focused != null && (focused == this || Contains(focused));
        }

        /// <summary>Whether an element can still take keys: it and everything up to the window is enabled and not hidden.</summary>
        private bool IsReachable(VisualElement element)
        {
            for (var each = element; each != null; each = each.parent)
            {
                if (each.style.display == DisplayStyle.None)
                    return false;
                if (each == this)
                    return element.enabledInHierarchy;
            }
            return false;
        }

        /// <summary>The first problem of each element that has one, as translated text, by the element's id.</summary>
        private Dictionary<int, string> ProblemTextsByElement()
        {
            var texts = new Dictionary<int, string>();
            foreach (var problem in draft.Problems())
                if (problem.LaneId >= 0 && !texts.ContainsKey(problem.LaneId))
                    texts[problem.LaneId] = ProblemText(problem);
            return texts;
        }

        private void OnPreviewsChanged()
        {
            if (draft == null)
                return;
            crossSection.Sync(true);
            SyncSurfaceSwatch();
            UpdateSurfaceTiles();
        }

        private void RenderStatus()
        {
            if (draft == null)
                return;
            var problems = draft.Problems();
            var state = RoadTypeWindowRules.Footer(draft.IsNew, draft.IsDirty, draft.IsStale, problems.Count);
            var block = SaveBlock();
            var text = state == RoadFooterState.Invalid ? Loc.T(RoadTypeWindowRules.FooterKey(state), ("problem", ProblemText(problems[0]))) // i18n:keys
                : Loc.T(RoadTypeWindowRules.FooterKey(state)); // i18n:keys
            // The reason Save is blocked that the state does not say already.
            if (block == RoadSaveBlock.ReadOnly || block == RoadSaveBlock.NotOwned)
                text += " " + Loc.T(RoadTypeWindowRules.SaveBlockKey(block)); // i18n:keys
            StatusLabel.text = text;
            StatusLabel.tooltip = state == RoadFooterState.Invalid ? LocBindings.CardTooltip(text, string.Join("\n", problems.ConvertAll(ProblemText))) : text;
            StatusLabel.EnableInClassList("signal-footer-dirty", state == RoadFooterState.Changed || state == RoadFooterState.NewUnsaved);
            StatusLabel.EnableInClassList("signal-footer-warning", state == RoadFooterState.Invalid || state == RoadFooterState.Stale);
            DiscardButton.SetEnabled(draft.IsDirty);
            ApplyButton.SetEnabled(block == RoadSaveBlock.None && !saving);
            if (block == RoadSaveBlock.None)
                LocBindings.Tooltip(ApplyButton, draft.IsNew ? "roadbuilder.window.save.tooltip_new" : "roadbuilder.window.save.tooltip_edit", ("name", RoadName), ("uses", uses)); // i18n:keys
            else
                LocBindings.Tooltip(ApplyButton, RoadTypeWindowRules.SaveBlockKey(block)); // i18n:keys
        }

        private static string ProblemText(OmsiRoadTypeProblem problem)
        {
            var key = RoadTypeWindowRules.ProblemKey(problem.Code);
            var kind = Loc.T(RoadTypeWindowRules.LaneKey(problem.Kind)); // i18n:keys
            switch (problem.Code)
            {
                case OmsiRoadTypeProblemCode.NoLanes:
                    return Loc.T(key); // i18n:keys
                case OmsiRoadTypeProblemCode.TooManyLanes:
                    return Loc.T(key, ("limit", problem.Limit)); // i18n:keys
                case OmsiRoadTypeProblemCode.WidthOutOfRange:
                    return Loc.T(key, ("lane", problem.LaneIndex + 1), ("kind", kind), ("min", problem.Minimum), ("max", problem.Maximum)); // i18n:keys
                case OmsiRoadTypeProblemCode.RowTooDense:
                    return Loc.T(key, ("lane", problem.LaneIndex + 1), ("kind", kind), ("min", problem.Minimum)); // i18n:keys
                default:
                    return Loc.T(key, ("lane", problem.LaneIndex + 1), ("kind", kind)); // i18n:keys
            }
        }

        /// <summary>A narrow window puts the properties below the cross-section instead of beside it.</summary>
        private void KeepLayoutFitted()
        {
            var narrow = workView.layout.width > 0f && workView.layout.width < NarrowWidth;
            workRow.EnableInClassList("road-window-columns-narrow", narrow);
        }
    }
}
