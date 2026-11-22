using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>The widths of a dialog (owner's decision 4A, M7); each is a USS class with <c>max-width: 94 %</c>.</summary>
    internal enum DialogWidth
    {
        /// <summary>420 px: a notice or one question.</summary>
        Narrow,
        /// <summary>560 px: the usual dialog.</summary>
        Standard,
        /// <summary>720 px: lists and tables.</summary>
        Wide,
        /// <summary>880 px: a form with several columns.</summary>
        Form
    }

    /// <summary>What a button of a dialog is for; it decides the look, the place in the row and what Esc does.</summary>
    internal enum DialogButtonRole
    {
        /// <summary>The main action (orange, rightmost); at most one per dialog.</summary>
        Primary,
        /// <summary>Leaves without acting; Esc does what it does. At most one per dialog.</summary>
        Cancel,
        /// <summary>Another action next to the main one (Copy report, Skip).</summary>
        Secondary,
        /// <summary>An action that deletes or cannot be undone (red).</summary>
        Danger
    }

    /// <summary>What a dialog focuses when it opens.</summary>
    internal enum DialogInitialFocus
    {
        /// <summary>The first element with the class <c>dialog-autofocus</c>.</summary>
        Marked,
        /// <summary>The first editable text field.</summary>
        TextField,
        /// <summary>The primary button.</summary>
        Primary,
        /// <summary>The first button of the row.</summary>
        FirstButton,
        /// <summary>Nothing to focus.</summary>
        None
    }

    /// <summary>
    /// The rules of the keyboard and of the button row without any element, so they run in a test without a panel
    /// (M7 step 1; owner's decisions 1A, 2B, 3A).
    /// </summary>
    internal static class DialogRules
    {
        public const string NarrowClass = "dialog-narrow";
        public const string StandardClass = "dialog-standard";
        public const string WideClass = "dialog-wide";
        public const string FormClass = "dialog-form";

        /// <summary>The USS class of a width.</summary>
        public static string WidthClass(DialogWidth width)
        {
            switch (width)
            {
                case DialogWidth.Narrow: return NarrowClass;
                case DialogWidth.Wide: return WideClass;
                case DialogWidth.Form: return FormClass;
                default: return StandardClass;
            }
        }

        /// <summary>The width in pixels that the USS class gives (the dialog is never wider than 94 % of the window).</summary>
        public static int WidthPixels(DialogWidth width)
        {
            switch (width)
            {
                case DialogWidth.Narrow: return 420;
                case DialogWidth.Wide: return 720;
                case DialogWidth.Form: return 880;
                default: return 560;
            }
        }

        /// <summary>
        /// The place of a role in the row, left to right: Secondary, Cancel, Danger, Primary. Cancel stands left of the main action
        /// (decision 1A), the main action is always rightmost.
        /// </summary>
        public static int Rank(DialogButtonRole role)
        {
            switch (role)
            {
                case DialogButtonRole.Secondary: return 0;
                case DialogButtonRole.Cancel: return 1;
                case DialogButtonRole.Danger: return 2;
                default: return 3;
            }
        }

        /// <summary>The indices of the buttons in the order they stand in the row; equal ranks keep the order they were given in.</summary>
        public static int[] RowOrder(IList<DialogButtonRole> roles)
        {
            var order = new int[roles.Count];
            for (var i = 0; i < order.Length; i++)
                order[i] = i;
            // Insertion sort: stable, and a row has a handful of buttons.
            for (var i = 1; i < order.Length; i++)
            {
                var current = order[i];
                var j = i - 1;
                while (j >= 0 && Rank(roles[order[j]]) > Rank(roles[current]))
                {
                    order[j + 1] = order[j];
                    j--;
                }
                order[j + 1] = current;
            }
            return order;
        }

        /// <summary>Throws if a dialog has no button, more than one primary or more than one cancel button.</summary>
        public static void ValidateButtons(IList<DialogButtonRole> roles)
        {
            if (roles == null || roles.Count == 0)
                throw new ArgumentException("A dialog needs at least one button.");
            var primary = 0;
            var cancel = 0;
            foreach (var role in roles)
            {
                if (role == DialogButtonRole.Primary)
                    primary++;
                else if (role == DialogButtonRole.Cancel)
                    cancel++;
            }
            if (primary > 1)
                throw new ArgumentException("A dialog has at most one primary button, not " + primary + ".");
            if (cancel > 1)
                throw new ArgumentException("A dialog has at most one cancel button, not " + cancel + ".");
        }

        /// <summary>
        /// The index of the button Esc stands for (the one with the cancel role), or -1 when the dialog has none. Esc closes every dialog
        /// all the same; without a cancel button it closes it without pressing any button (a main action never runs by Esc).
        /// </summary>
        public static int EscapeButton(IList<DialogButtonRole> roles)
        {
            for (var i = 0; i < roles.Count; i++)
                if (roles[i] == DialogButtonRole.Cancel)
                    return i;
            return -1;
        }

        /// <summary>
        /// Whether Enter presses the primary button (decision 2B): only when the dialog has exactly one editable text field and that
        /// field is a single line, the focus is not on a button, and the primary button exists and is enabled. Dialogs with several
        /// fields or a multi-line field never take Enter, so Enter cannot apply a timetable by mistake.
        /// </summary>
        /// <param name="editableTextFields">All editable text fields in the dialog, single line or not.</param>
        /// <param name="multilineFields">How many of them are multi-line.</param>
        public static bool EnterActivatesPrimary(int editableTextFields, int multilineFields, bool focusOnButton, bool primaryEnabled)
        {
            return editableTextFields == 1 && multilineFields == 0 && !focusOnButton && primaryEnabled;
        }

        /// <summary>What a dialog focuses when it opens: a marked element, else the first text field, else the primary button.</summary>
        public static DialogInitialFocus InitialFocus(bool hasMarked, bool hasTextField, bool hasPrimary, bool hasAnyButton)
        {
            if (hasMarked)
                return DialogInitialFocus.Marked;
            if (hasTextField)
                return DialogInitialFocus.TextField;
            if (hasPrimary)
                return DialogInitialFocus.Primary;
            return hasAnyButton ? DialogInitialFocus.FirstButton : DialogInitialFocus.None;
        }

        /// <summary>
        /// The Tab trap: the index to focus when Tab leaves the first or last element of the dialog (or the focus is outside it),
        /// or -1 when the normal order may run.
        /// </summary>
        /// <param name="count">How many elements of the dialog can take the focus.</param>
        /// <param name="current">Index of the focused element among them, -1 when the focus is not in the dialog.</param>
        public static int TabTarget(int count, int current, bool backwards)
        {
            if (count <= 0)
                return -1;
            if (current < 0)
                return backwards ? count - 1 : 0;
            if (!backwards && current == count - 1)
                return 0;
            if (backwards && current == 0)
                return count - 1;
            return -1;
        }
    }

    /// <summary>One button in the row at the bottom of a dialog.</summary>
    internal sealed class DialogButton
    {
        /// <summary>Translation key of the text; the button follows a language change.</summary>
        public string TextKey;
        public (string Name, object Value)[] TextArguments;
        public DialogButtonRole Role = DialogButtonRole.Secondary;
        /// <summary>Runs when the button is pressed (for the cancel button also when Esc closes); null for nothing.</summary>
        public Action Clicked;
        /// <summary>Whether the dialog closes after <see cref="Clicked"/>; false when the action may fail and the dialog stays open.</summary>
        public bool Closes = true;
        /// <summary>Optional element name of the button for tests and the check arguments.</summary>
        public string Name;
    }

    /// <summary>What a dialog is made of; <see cref="ModalDialog.Create"/> builds it.</summary>
    internal sealed class DialogOptions
    {
        /// <summary>Element name of the overlay (<c>timetable-dialog</c>); the check arguments and tests look for it.</summary>
        public string Name;
        public string TitleKey;
        /// <summary>Optional line below the title.</summary>
        public string DescriptionKey;
        public DialogWidth Width = DialogWidth.Standard;
        /// <summary>Whether the middle is a scroll view that shrinks when the window is small; false for a short dialog.</summary>
        public bool BodyScrolls = true;
        public IList<DialogButton> Buttons;
        /// <summary>
        /// Runs after the dialog was dismissed: by Esc or by the cancel button, not after the primary button. The clean-up of a
        /// discarded answer goes here, so Esc and Cancel cannot differ.
        /// </summary>
        public Action Closed;
        /// <summary>
        /// Whether Enter may press the primary button when the dialog has a single text field (decision 2B). False for a dialog whose
        /// only field is a helper (a list number) and whose primary action writes files: Enter then only commits the field.
        /// </summary>
        public bool EnterPressesPrimary = true;
        /// <summary>Gives the focus back when the dialog closes and the element focused before it is gone; null for none.</summary>
        public Action ReturnFocus;
    }

    /// <summary>
    /// The one way to build a modal dialog (M7): the overlay, the panel with title, description, scrolling body and button row, and
    /// the behaviour all dialogs share. Esc runs through the overlay registry (<c>Closed</c>); a click beside the dialog closes
    /// nothing (decision 3A); Enter and Tab follow <see cref="DialogRules"/>.
    /// </summary>
    internal sealed class ModalDialog
    {
        public const string AutofocusClass = "dialog-autofocus";

        private readonly DialogOptions options;
        private readonly List<DialogButton> definitions = new List<DialogButton>();
        private readonly List<Button> buttons = new List<Button>();
        private VisualElement previousFocus;

        private ModalDialog(DialogOptions options)
        {
            this.options = options;
            Overlay = new VisualElement { name = options.Name };
            Overlay.AddToClassList("dialog-overlay");
            Overlay.style.display = DisplayStyle.None;
            Overlay.focusable = true;
            Overlay.tabIndex = -1;

            Dialog = new VisualElement();
            Dialog.AddToClassList("dialog");
            Dialog.AddToClassList("dialog-modal");
            Dialog.AddToClassList(DialogRules.WidthClass(options.Width));
            Overlay.Add(Dialog);

            Title = LocBindings.NewLabel(options.TitleKey); // i18n:keys
            Title.AddToClassList("popup-title");
            Dialog.Add(Title);
            if (!string.IsNullOrEmpty(options.DescriptionKey))
            {
                Description = LocBindings.NewLabel(options.DescriptionKey); // i18n:keys
                Description.AddToClassList("popup-description");
                Dialog.Add(Description);
            }

            if (options.BodyScrolls)
            {
                var scroll = new ScrollView(ScrollViewMode.Vertical);
                scroll.AddToClassList("dialog-body");
                scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                Dialog.Add(scroll);
                Body = scroll.contentContainer;
            }
            else
            {
                Body = new VisualElement();
                Body.AddToClassList("dialog-body");
                Body.AddToClassList("dialog-body-plain");
                Dialog.Add(Body);
            }

            BuildButtonRow();

            Overlay.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            // Esc also closes a dialog whose own focused element sees the key first (a button without a field in the dialog): the shell
            // closes through the registry at its root, this is the same close from the dialog itself when the key arrives only here, as a
            // key or as the navigation event UI Toolkit makes of it. A dialog already hidden by the registry ignores both.
            Overlay.RegisterCallback<NavigationCancelEvent>(OnNavigationCancel, TrickleDown.TrickleDown);
            // A click beside the dialog closes nothing (3A); it only keeps the focus in the dialog and the map behind from seeing it.
            Overlay.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.target == Overlay)
                    evt.StopPropagation();
            });
        }

        /// <summary>The element the overlay registry sees; the dialog is added to the shell root through it.</summary>
        public VisualElement Overlay { get; }

        /// <summary>The panel with the background and the width class.</summary>
        public VisualElement Dialog { get; }

        /// <summary>Where the content goes: the content container of the scroll view, or a plain element.</summary>
        public VisualElement Body { get; }

        public Label Title { get; }

        /// <summary>The line below the title; null when the options gave no key.</summary>
        public Label Description { get; }

        /// <summary>The button with the primary role, or null; its text and enabled state may change (Updates).</summary>
        public Button Primary { get; private set; }

        /// <summary>The buttons in the order they stand in the row.</summary>
        public IReadOnlyList<Button> Buttons => buttons;

        /// <summary>Shown: not hidden through its inline style (which counts at once) and not resolved as hidden.</summary>
        public bool IsShown => Overlay.style.display != DisplayStyle.None && OverlayRegistry.IsShown(Overlay);

        /// <summary>Builds the dialog hidden; <see cref="Show"/> opens it. Throws if the options are incomplete or the buttons contradict the rules.</summary>
        public static ModalDialog Create(DialogOptions options)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrEmpty(options.Name))
                throw new ArgumentException("A dialog needs an element name.", nameof(options));
            if (string.IsNullOrEmpty(options.TitleKey))
                throw new ArgumentException("A dialog needs a title key.", nameof(options));
            var roles = new List<DialogButtonRole>();
            if (options.Buttons != null)
                foreach (var definition in options.Buttons)
                    roles.Add(definition.Role);
            DialogRules.ValidateButtons(roles);
            return new ModalDialog(options);
        }

        private void BuildButtonRow()
        {
            var row = new VisualElement();
            row.AddToClassList("dialog-buttons");
            var roles = new List<DialogButtonRole>();
            foreach (var definition in options.Buttons)
                roles.Add(definition.Role);
            foreach (var index in DialogRules.RowOrder(roles))
            {
                var definition = options.Buttons[index];
                var button = LocBindings.NewButton(definition.TextKey, () => OnButton(definition), definition.TextArguments ?? new (string, object)[0]); // i18n:keys
                if (!string.IsNullOrEmpty(definition.Name))
                    button.name = definition.Name;
                button.AddToClassList("map-control-button");
                if (definition.Role == DialogButtonRole.Primary)
                {
                    button.AddToClassList("dialog-primary");
                    Primary = button;
                }
                else if (definition.Role == DialogButtonRole.Danger)
                {
                    button.AddToClassList("dialog-danger");
                }
                definitions.Add(definition);
                buttons.Add(button);
                row.Add(button);
            }
            Dialog.Add(row);
        }

        private DialogButton CancelDefinition()
        {
            foreach (var definition in definitions)
                if (definition.Role == DialogButtonRole.Cancel)
                    return definition;
            return null;
        }

        /// <summary>Does what pressing one of this dialog's buttons does (for tests and the shell's own triggers).</summary>
        internal void Press(Button button)
        {
            var index = buttons.IndexOf(button);
            if (index < 0)
                throw new ArgumentException("The button does not belong to this dialog.", nameof(button));
            OnButton(definitions[index]);
        }

        private void OnButton(DialogButton definition)
        {
            if (definition.Role == DialogButtonRole.Cancel)
            {
                Dismiss();
                return;
            }
            definition.Clicked?.Invoke();
            if (definition.Closes)
                Hide();
        }

        /// <summary>Opens the dialog: remembers the focus, shows the overlay and focuses the first input or the primary button.</summary>
        public void Show()
        {
            if (!IsShown)
                previousFocus = Overlay.panel?.focusController?.focusedElement as VisualElement;
            Overlay.style.display = DisplayStyle.Flex;
            Overlay.BringToFront();
            if (Overlay.panel != null)
                Overlay.schedule.Execute(FocusInitial);
            else
                FocusInitial();
        }

        /// <summary>Closes the dialog after an action (not a dismissal): hides it and gives the focus back; <c>Closed</c> does not run.</summary>
        public void Hide()
        {
            if (!IsShown)
                return;
            OverlayRegistry.Hide(Overlay);
            ReturnFocus();
        }

        /// <summary>Closes the dialog as Cancel and Esc do: the cancel button's action, then <c>Closed</c>, then the focus returns.</summary>
        public void Dismiss()
        {
            if (!IsShown)
                return;
            OverlayRegistry.Hide(Overlay);
            AfterDismissed();
        }

        /// <summary>
        /// For the overlay registry: Esc already hid the overlay. Does what Cancel does after it hid the dialog, so the two cannot
        /// differ.
        /// </summary>
        public void OnEscaped()
        {
            AfterDismissed();
        }

        private void AfterDismissed()
        {
            CancelDefinition()?.Clicked?.Invoke();
            options.Closed?.Invoke();
            ReturnFocus();
        }

        private void ReturnFocus()
        {
            var target = previousFocus;
            previousFocus = null;
            if (target != null && target.panel != null && target.focusable)
                target.Focus();
            else
                options.ReturnFocus?.Invoke();
        }

        private List<VisualElement> EditableFields(out int multiline)
        {
            var fields = new List<VisualElement>();
            multiline = 0;
            foreach (var element in Body.Query<VisualElement>().ToList())
            {
                // Counted by the field types, not through ITextEdition: the input inside a field is a text element of its own.
                if (element is TextField text)
                {
                    if (text.isReadOnly)
                        continue;
                    fields.Add(text);
                    if (text.multiline)
                        multiline++;
                }
                else if ((element is IntegerField integer && !integer.isReadOnly) || (element is LongField number && !number.isReadOnly)
                    || (element is FloatField real && !real.isReadOnly) || (element is DoubleField precise && !precise.isReadOnly))
                {
                    fields.Add(element);
                }
            }
            return fields;
        }

        private void FocusInitial()
        {
            var marked = Body.Q(className: AutofocusClass);
            var fields = EditableFields(out _);
            var choice = DialogRules.InitialFocus(marked != null, fields.Count > 0, Primary != null, buttons.Count > 0);
            switch (choice)
            {
                case DialogInitialFocus.Marked:
                    marked.Focus();
                    break;
                case DialogInitialFocus.TextField:
                    fields[0].Focus();
                    break;
                case DialogInitialFocus.Primary:
                    Primary.Focus();
                    break;
                case DialogInitialFocus.FirstButton:
                    buttons[0].Focus();
                    break;
            }
        }

        private void OnNavigationCancel(NavigationCancelEvent evt)
        {
            if (!IsShown)
                return;
            Dismiss();
            evt.StopPropagation();
            evt.PreventDefault();
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (!IsShown)
                return;
            if (evt.keyCode == UnityEngine.KeyCode.Escape)
            {
                Dismiss();
                evt.StopPropagation();
                evt.PreventDefault();
            }
            else if (evt.keyCode == UnityEngine.KeyCode.Tab)
            {
                if (TrapTab(evt.shiftKey))
                {
                    evt.StopPropagation();
                    evt.PreventDefault();
                }
            }
            else if (evt.keyCode == UnityEngine.KeyCode.Return || evt.keyCode == UnityEngine.KeyCode.KeypadEnter)
            {
                if (TryEnter(evt.target as VisualElement))
                {
                    evt.StopPropagation();
                    evt.PreventDefault();
                }
            }
        }

        /// <summary>Presses the primary button for Enter if <see cref="DialogRules.EnterActivatesPrimary"/> allows it; true when it did.</summary>
        internal bool TryEnter(VisualElement focused)
        {
            if (!options.EnterPressesPrimary)
                return false;
            var fields = EditableFields(out var multiline);
            var onButton = false;
            for (var element = focused; element != null && element != Dialog; element = element.parent)
                if (element is Button)
                    onButton = true;
            var primaryEnabled = Primary != null && Primary.enabledInHierarchy;
            if (!DialogRules.EnterActivatesPrimary(fields.Count, multiline, onButton, primaryEnabled))
                return false;
            var index = buttons.IndexOf(Primary);
            OnButton(definitions[index]);
            return true;
        }

        private bool TrapTab(bool backwards)
        {
            var candidates = new List<VisualElement>();
            foreach (var element in Dialog.Query<VisualElement>().ToList())
                if (element.focusable && !element.delegatesFocus && element.tabIndex >= 0 && element.enabledInHierarchy
                    && element.resolvedStyle.display != DisplayStyle.None && element.resolvedStyle.visibility == Visibility.Visible)
                    candidates.Add(element);
            var focused = Overlay.panel?.focusController?.focusedElement as VisualElement;
            var target = DialogRules.TabTarget(candidates.Count, candidates.IndexOf(focused), backwards);
            if (target < 0)
                return false;
            candidates[target].Focus();
            return true;
        }
    }
}
