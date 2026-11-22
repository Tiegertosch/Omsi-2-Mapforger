using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// M6 step 2: the focus ring is only for keyboard operation. Tab or an arrow key sets <see cref="KeyboardClass"/> on the
    /// root, a pointer press removes it; the focus rules of EditorShell.uss apply only below that class.
    /// </summary>
    public static class EditorShellFocusRing
    {
        public const string KeyboardClass = "mf-keyboard-focus";

        public static void Attach(VisualElement root)
        {
            root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            root.RegisterCallback<NavigationMoveEvent>(evt => Show(root), TrickleDown.TrickleDown);
            root.RegisterCallback<PointerDownEvent>(evt => Hide(root), TrickleDown.TrickleDown);
        }

        public static void Show(VisualElement root)
        {
            if (!root.ClassListContains(KeyboardClass))
                root.AddToClassList(KeyboardClass);
        }

        public static void Hide(VisualElement root)
        {
            if (root.ClassListContains(KeyboardClass))
                root.RemoveFromClassList(KeyboardClass);
        }

        private static void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == UnityEngine.KeyCode.Tab && evt.currentTarget is VisualElement root)
                Show(root);
        }
    }
}
