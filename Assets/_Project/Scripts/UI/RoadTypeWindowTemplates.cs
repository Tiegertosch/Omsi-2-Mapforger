using System.Collections.Generic;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>The road chosen in the road tool, taken apart into lanes for Templates, From the chosen road.</summary>
    internal sealed class RoadChosenSource
    {
        public string Path;
        public string Name;

        /// <summary>The lanes, or null when the road has no carriageway or footpath to take apart.</summary>
        public OmsiRoadType Type;

        /// <summary>What was left out or changed while taking it apart, as codes with their values; <see cref="NotesText"/> translates them.</summary>
        public readonly List<OmsiRoadTypeNote> Notes = new List<OmsiRoadTypeNote>();

        /// <summary>The remarks in the language of the window, in one line.</summary>
        public string NotesText()
        {
            return string.Join(" ", Notes.ConvertAll(RoadTypeWindow.NoteText));
        }
    }

    /// <summary>
    /// The templates page of the road type window (briefing of 18 November 2026): a grid of cards with the name, a small cross-section, the total
    /// width and the number of elements of every template, and a card for the road chosen in the road tool. Choosing a card changes nothing; the
    /// bar below says what Use template does, and using it replaces the draft's content in one step of its history and returns to the work area.
    /// </summary>
    internal sealed partial class RoadTypeWindow
    {
        /// <summary>The id of the card for the chosen road; the templates have the ids of <see cref="OmsiRoadTypeTemplate"/>.</summary>
        private const string ChosenCardId = "@chosen";

        private sealed class TemplateCard
        {
            public string Id;
            public Button Button;
            public Label Detail;
            public RoadMiniSection Mini;
            public Label Info;
        }

        private readonly List<TemplateCard> templateCards = new List<TemplateCard>();
        private ScrollView templateScroll;
        private int keyMovedAtFrame = -10;
        private TemplateCard chosenCard;
        private Label templateSelected;
        private Label templateReplace;
        private Label templateNotes;
        private Button templateUse;
        private string templateChosen;
        private RoadChosenSource chosenSource;
        private string chosenSourceFor;

        private VisualElement BuildTemplatesView()
        {
            var view = new VisualElement();
            view.AddToClassList("road-window-templates");
            view.style.display = DisplayStyle.None;
            var intro = LocBindings.NewLabel("roadbuilder.window.templates.hint");
            intro.AddToClassList("panel-info");
            view.Add(intro);

            templateScroll = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            templateScroll.AddToClassList("road-window-template-scroll");
            // The chosen road first and by itself, then the templates as a grid.
            chosenCard = NewCard(ChosenCardId, "roadbuilder.panel.from_chosen");
            chosenCard.Button.AddToClassList("road-window-template-wide");
            templateScroll.Add(chosenCard.Button);
            var grid = new VisualElement();
            grid.AddToClassList("road-window-template-grid");
            foreach (var template in OmsiRoadTypeTemplate.All)
            {
                var card = NewCard(template.Id, RoadTypeWindowRules.TemplateKey(template.Id));
                card.Mini.SetType(template.Type());
                grid.Add(card.Button);
            }
            templateScroll.Add(grid);
            view.Add(templateScroll);

            // The bar below: what is chosen, what using it does and the button.
            var footer = new VisualElement();
            footer.AddToClassList("road-window-template-footer");
            var texts = new VisualElement();
            texts.AddToClassList("road-window-template-texts");
            templateSelected = new Label();
            templateSelected.AddToClassList("road-window-template-selected");
            texts.Add(templateSelected);
            templateReplace = LocBindings.NewLabel("roadbuilder.window.templates.replace");
            templateReplace.AddToClassList("road-window-template-info");
            texts.Add(templateReplace);
            templateNotes = new Label();
            templateNotes.AddToClassList("road-window-template-info");
            texts.Add(templateNotes);
            footer.Add(texts);
            templateUse = LocBindings.NewButton("roadbuilder.window.templates.use", UseTemplate);
            templateUse.AddToClassList("map-control-button");
            templateUse.AddToClassList("dialog-primary");
            footer.Add(templateUse);
            view.Add(footer);

            view.RegisterCallback<KeyDownEvent>(OnTemplateKey);
            view.RegisterCallback<NavigationMoveEvent>(OnTemplateNavigation, TrickleDown.TrickleDown);
            return view;
        }

        private TemplateCard NewCard(string id, string nameKey)
        {
            var card = new TemplateCard { Id = id };
            card.Button = new Button(() => ChooseTemplate(id));
            card.Button.AddToClassList("map-control-button");
            card.Button.AddToClassList("road-window-template");
            var name = LocBindings.NewLabel(nameKey); // i18n:keys
            name.pickingMode = PickingMode.Ignore;
            name.AddToClassList("road-window-template-name");
            card.Button.Add(name);
            card.Detail = new Label { pickingMode = PickingMode.Ignore };
            card.Detail.AddToClassList("road-window-template-detail");
            card.Detail.style.display = DisplayStyle.None;
            card.Button.Add(card.Detail);
            card.Mini = new RoadMiniSection();
            card.Button.Add(card.Mini);
            card.Info = new Label { pickingMode = PickingMode.Ignore };
            card.Info.AddToClassList("road-window-template-small");
            card.Button.Add(card.Info);
            templateCards.Add(card);
            return card;
        }

        /// <summary>A card was clicked or confirmed: it is chosen, nothing changes in the draft until Use template.</summary>
        private void ChooseTemplate(string id)
        {
            templateChosen = id;
            RenderTemplates();
        }

        /// <summary>The road chosen in the road tool taken apart, once for each road until the page is opened again.</summary>
        private RoadChosenSource ChosenSource()
        {
            if (chosenProfile == null)
                return null;
            if (chosenSource == null || chosenSourceFor != chosenProfile)
            {
                chosenSource = ChosenRoadResolver?.Invoke(chosenProfile);
                chosenSourceFor = chosenProfile;
            }
            return chosenSource;
        }

        private void RenderTemplates()
        {
            // Only the page shown is drawn; showing it draws it.
            if (templateCards.Count == 0 || draft == null || page != RoadWindowPage.Templates)
                return;
            var source = ChosenSource();
            var usable = source != null && source.Type != null;
            RenderChosenCard(source, usable);
            if (templateChosen == ChosenCardId && !usable)
                templateChosen = null;
            foreach (var card in templateCards)
            {
                card.Button.EnableInClassList("road-window-template-chosen", card.Id == templateChosen);
                // The numbers are written in the language's format, so they are written again with every drawing.
                var template = OmsiRoadTypeTemplate.Find(card.Id);
                if (template != null)
                    card.Info.text = Loc.T("roadbuilder.window.templates.info", ("width", template.Width), ("count", template.Elements));
            }

            string name = null;
            if (templateChosen == ChosenCardId)
                name = Loc.T("roadbuilder.panel.from_chosen") + ": " + source.Name;
            else if (templateChosen != null)
                name = Loc.T(RoadTypeWindowRules.TemplateKey(templateChosen)); // i18n:keys
            var picked = name != null;
            templateSelected.text = picked ? Loc.T("roadbuilder.window.templates.selection", ("name", name)) : Loc.T("roadbuilder.window.templates.pick");
            templateReplace.style.display = picked ? DisplayStyle.Flex : DisplayStyle.None;
            var notes = picked && templateChosen == ChosenCardId && source.Notes.Count > 0 ? source.NotesText() : null;
            templateNotes.style.display = notes != null ? DisplayStyle.Flex : DisplayStyle.None;
            templateNotes.text = notes != null ? Loc.T("roadbuilder.window.templates.from.notes", ("notes", notes)) : string.Empty;
            templateUse.SetEnabled(picked);
        }

        /// <summary>The card of the chosen road: what is taken over, or why nothing can be.</summary>
        private void RenderChosenCard(RoadChosenSource source, bool usable)
        {
            var card = chosenCard;
            card.Button.SetEnabled(usable);
            card.Detail.style.display = DisplayStyle.Flex;
            card.Mini.style.display = usable ? DisplayStyle.Flex : DisplayStyle.None;
            card.Info.style.display = usable ? DisplayStyle.Flex : DisplayStyle.None;
            if (usable)
            {
                card.Detail.text = Loc.T("roadbuilder.window.templates.from.source", ("name", source.Name));
                card.Mini.SetType(source.Type);
                card.Info.text = Loc.T("roadbuilder.window.templates.info", ("width", source.Type.Width), ("count", source.Type.Lanes.Count));
                return;
            }
            // The reason stands on the card itself, because a disabled button shows no tooltip.
            card.Detail.text = chosenProfile == null
                ? Loc.T("roadbuilder.window.templates.from.none")
                : Loc.T("roadbuilder.window.templates.from.unsuitable", ("name", source != null ? source.Name : System.IO.Path.GetFileNameWithoutExtension(chosenProfile)));
            card.Mini.SetType(null);
        }

        private void UseTemplate()
        {
            if (templateChosen == null)
                return;
            if (templateChosen == ChosenCardId)
            {
                var source = ChosenSource();
                if (source?.Type == null)
                    return;
                // One step of the draft's history; the chosen road itself is not touched.
                draft.Apply(source.Type);
                draft.EndGesture();
                ChosenRoadTaken?.Invoke(source);
            }
            else if (draft.ApplyTemplate(templateChosen))
                draft.EndGesture();
            templateChosen = null;
            ShowTemplates(false);
            Render();
            Changed?.Invoke();
        }

        /// <summary>The arrow keys move the focus from card to card in the direction pressed (the cards sit in a grid); Enter and Space choose a card as for any button.</summary>
        private void OnTemplateKey(KeyDownEvent evt)
        {
            int across;
            int down;
            switch (evt.keyCode)
            {
                case KeyCode.LeftArrow: across = -1; down = 0; break;
                case KeyCode.RightArrow: across = 1; down = 0; break;
                case KeyCode.UpArrow: across = 0; down = -1; break;
                case KeyCode.DownArrow: across = 0; down = 1; break;
                default: return;
            }
            if (MoveCardFocus(across, down))
            {
                // UI Toolkit sends a NavigationMoveEvent for the same press, after this key event, to the card that has the focus by then;
                // its default action would move the focus a second card on. OnTemplateNavigation swallows that one.
                keyMovedAtFrame = Time.frameCount;
                evt.StopPropagation();
            }
        }

        /// <summary>The navigation event UI Toolkit makes of an arrow key (or a gamepad): it must not move the focus again after the key handler did.</summary>
        private void OnTemplateNavigation(NavigationMoveEvent evt)
        {
            int across = 0;
            int down = 0;
            switch (evt.direction)
            {
                case NavigationMoveEvent.Direction.Left: across = -1; break;
                case NavigationMoveEvent.Direction.Right: across = 1; break;
                case NavigationMoveEvent.Direction.Up: down = -1; break;
                case NavigationMoveEvent.Direction.Down: down = 1; break;
                default: return; // Next and Previous (Tab) keep their normal order.
            }
            if (!IsCardFocused())
                return;
            evt.StopPropagation();
            evt.PreventDefault();
            if (Time.frameCount - keyMovedAtFrame > 1)
                MoveCardFocus(across, down); // no key event came before: a gamepad
        }

        private bool IsCardFocused()
        {
            var focused = panel?.focusController?.focusedElement as VisualElement;
            foreach (var card in templateCards)
                if (card.Button == focused)
                    return true;
            return false;
        }

        private bool MoveCardFocus(int across, int down)
        {
            var focused = panel?.focusController?.focusedElement as VisualElement;
            var from = templateCards.FindIndex(card => card.Button == focused);
            if (from < 0)
                return false;
            var bounds = new Rect[templateCards.Count];
            var enabled = new bool[templateCards.Count];
            for (var i = 0; i < bounds.Length; i++)
            {
                bounds[i] = templateCards[i].Button.worldBound;
                enabled[i] = templateCards[i].Button.enabledSelf;
            }
            var best = RoadTypeWindowRules.NextCard(bounds, enabled, from, across, down);
            if (best >= 0)
            {
                templateCards[best].Button.Focus();
                templateScroll.ScrollTo(templateCards[best].Button);
            }
            return true;
        }
    }
}
