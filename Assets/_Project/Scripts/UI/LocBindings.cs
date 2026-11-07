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
            public (string Name, object Value)[] TextArguments;
            public string TooltipKey;
            public (string Name, object Value)[] TooltipArguments;
            public int Generation = -1;
        }

        private static readonly ConditionalWeakTable<VisualElement, Binding> Table = new ConditionalWeakTable<VisualElement, Binding>();

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

        /// <summary>Sets the element's tooltip from a key now and after every language change.</summary>
        public static void Tooltip(VisualElement element, string key, params (string Name, object Value)[] arguments)
        {
            if (element == null)
                return;
            var binding = BindingOf(element);
            binding.TooltipKey = key;
            binding.TooltipArguments = arguments;
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
                switch (element)
                {
                    case Toggle toggle:
                        toggle.label = text;
                        break;
                    case TextField field:
                        field.label = text;
                        break;
                    case Foldout foldout:
                        foldout.text = text;
                        break;
                    case TextElement label:
                        label.text = text;
                        break;
                }
            }
            if (binding.TooltipKey != null)
                element.tooltip = binding.TooltipArguments == null || binding.TooltipArguments.Length == 0 ? Loc.T(binding.TooltipKey) : Loc.T(binding.TooltipKey, binding.TooltipArguments);
            binding.Generation = Loc.Manager != null ? Loc.Manager.Generation : -1;
        }
    }
}
