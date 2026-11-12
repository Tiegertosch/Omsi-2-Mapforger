using System.Runtime.CompilerServices;
using OmsiMapEditor.Core.Localization;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// Ties the text and tooltip of an interface element to translation keys, so the element follows a language change (ADR 068).
    /// The key and its arguments are kept per element in a weak table, nothing subscribes to the language change itself, and
    /// <see cref="Refresh"/> walks a tree and sets the texts anew; an element that was away from the screen at the change sets its text
    /// when it is attached again. Text written at the moment of an event, such as a status message, needs no binding.
    /// </summary>
    internal static class LocBindings
    {
        private sealed class Binding
        {
            public string TextKey;
            public bool Upper;
            public (string Name, object Value)[] TextArguments;
            public string TooltipKey;
            public string TooltipTitleKey;
            public (string Name, object Value)[] TooltipArguments;
            public int Generation = -1;
        }

        private static readonly ConditionalWeakTable<VisualElement, Binding> Table = new ConditionalWeakTable<VisualElement, Binding>();

        /// <summary>A label whose text is the key's, now and after every language change.</summary>
        public static Label NewLabel(string key, params (string Name, object Value)[] arguments)
        {
            var label = new Label();
            Text(label, key, arguments);
            return label;
        }

        /// <summary>A button whose text is the key's, now and after every language change.</summary>
        public static Button NewButton(string key, System.Action clicked, params (string Name, object Value)[] arguments)
        {
            var button = new Button(clicked);
            Text(button, key, arguments);
            return button;
        }

        /// <summary>Sets the element's text from a key now and after every language change.</summary>
        public static void Text(VisualElement element, string key, params (string Name, object Value)[] arguments)
        {
            if (element == null)
                return;
            var binding = BindingOf(element);
            binding.TextKey = key;
            binding.TextArguments = arguments;
            Apply(element, binding);
        }

        /// <summary>Like <see cref="Text"/>, written in capitals as the language writes them: for the headings of a section.</summary>
        public static void TextUpper(VisualElement element, string key, params (string Name, object Value)[] arguments)
        {
            if (element == null)
                return;
            BindingOf(element).Upper = true;
            Text(element, key, arguments);
        }

        /// <summary>Binds the tooltip and returns the element, so it can stand inside an expression.</summary>
        public static T WithTooltip<T>(T element, string key, params (string Name, object Value)[] arguments) where T : VisualElement
        {
            Tooltip(element, key, arguments);
            return element;
        }

        /// <summary>Takes the element's tooltip binding away and clears the tooltip.</summary>
        public static void ClearTooltip(VisualElement element)
        {
            if (element == null)
                return;
            var binding = BindingOf(element);
            binding.TooltipKey = null;
            binding.TooltipTitleKey = null;
            binding.TooltipArguments = null;
            element.tooltip = string.Empty;
        }

        /// <summary>Sets the element's tooltip from a key now and after every language change.</summary>
        public static void Tooltip(VisualElement element, string key, params (string Name, object Value)[] arguments)
        {
            if (element == null)
                return;
            var binding = BindingOf(element);
            binding.TooltipKey = key;
            binding.TooltipTitleKey = null;
            binding.TooltipArguments = arguments;
            Apply(element, binding);
        }

        /// <summary>Separates title and text in a tooltip made by <see cref="CardTooltip"/>; the tooltip card splits it again.</summary>
        public const char TooltipSeparator = '\u001F';

        /// <summary>A tooltip with an explicit title of any length and a text below it, for the tooltip card; no key cap.</summary>
        public static string CardTooltip(string title, string text)
        {
            return string.IsNullOrEmpty(title) ? text : title + TooltipSeparator + text;
        }

        /// <summary>A tooltip "Name: description" from two keys, which the tooltip card shows with the name as its title; without a description only the name.</summary>
        public static void TitledTooltip(VisualElement element, string titleKey, string key)
        {
            if (element == null)
                return;
            var binding = BindingOf(element);
            binding.TooltipKey = key;
            binding.TooltipTitleKey = titleKey;
            binding.TooltipArguments = null;
            Apply(element, binding);
        }

        /// <summary>Sets the texts of every bound element under <paramref name="root"/> anew.</summary>
        public static void Refresh(VisualElement root)
        {
            if (root == null)
                return;
            if (Table.TryGetValue(root, out var binding))
                Apply(root, binding);
            foreach (var child in root.Children())
                Refresh(child);
        }

        private static Binding BindingOf(VisualElement element)
        {
            if (Table.TryGetValue(element, out var existing))
                return existing;
            var created = new Binding();
            Table.Add(element, created);
            element.RegisterCallback<AttachToPanelEvent>(OnAttach);
            return created;
        }

        private static void OnAttach(AttachToPanelEvent evt)
        {
            if (evt.target is VisualElement element && Table.TryGetValue(element, out var binding) && Loc.Manager != null && binding.Generation != Loc.Manager.Generation)
                Apply(element, binding);
        }

        private static void Apply(VisualElement element, Binding binding)
        {
            if (binding.TextKey != null)
            {
                var text = binding.TextArguments == null || binding.TextArguments.Length == 0 ? Loc.T(binding.TextKey) : Loc.T(binding.TextKey, binding.TextArguments);
                if (binding.Upper && Loc.Manager != null)
                    text = text.ToUpper(Loc.Manager.Culture);
                switch (element)
                {
                    case Toggle toggle:
                        // The text beside the box, as before: not the label at the left of the field.
                        toggle.text = text;
                        break;
                    case FloatField floatField:
                        floatField.label = text;
                        break;
                    case IntegerField integerField:
                        integerField.label = text;
                        break;
                    case TextField field:
                        field.label = text;
                        break;
                    case DropdownField dropdown:
                        dropdown.label = text;
                        break;
                    case Foldout foldout:
                        foldout.text = text;
                        break;
                    case TextElement label:
                        label.text = text;
                        break;
                }
            }
            if (binding.TooltipTitleKey != null)
                element.tooltip = CardTooltip(Loc.T(binding.TooltipTitleKey), binding.TooltipKey == null ? string.Empty : Loc.T(binding.TooltipKey));
            else if (binding.TooltipKey != null)
                element.tooltip = binding.TooltipArguments == null || binding.TooltipArguments.Length == 0 ? Loc.T(binding.TooltipKey) : Loc.T(binding.TooltipKey, binding.TooltipArguments);
            binding.Generation = Loc.Manager != null ? Loc.Manager.Generation : -1;
        }
    }
}
