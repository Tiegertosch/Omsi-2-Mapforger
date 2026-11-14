using System;
using System.Collections.Generic;
using System.Globalization;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>What a click on a tile of the map does in the Paint tab besides painting with the brush.</summary>
    internal enum PaintTileArm
    {
        None,
        Cover,
        Clear
    }

    /// <summary>A ground texture of the map as the paint list shows it.</summary>
    internal struct GroundTextureChoice
    {
        public int Index;
        public string Name;

        /// <summary>A small picture of the texture, or null when it cannot be read.</summary>
        public Texture2D Thumbnail;
    }

    /// <summary>
    /// Ground painting inside the terrain tool (ADR 091). It is two parts: the options column (the brush kind and hardness, covering a
    /// tile; this element) and the page of the catalog, <see cref="Page"/>, with one tile for each ground texture of the map, the tile that
    /// adds a texture and the form that tile opens.
    /// </summary>
    internal sealed class GroundPaintOptions : VisualElement
    {
        private static readonly int[] MaskExponents = { 8, 9, 10 };

        private readonly VisualElement tileArea = new VisualElement();
        private readonly Dictionary<Button, int> rows = new Dictionary<Button, int>();
        private readonly Dictionary<Button, OmsiPaintMode> modeButtons = new Dictionary<Button, OmsiPaintMode>();
        private readonly Slider hardnessSlider = new Slider(0f, 1f);
        private readonly Label hardnessLabel = new Label();
        private readonly VisualElement addBox = new VisualElement();
        private readonly DropdownField textureField = new DropdownField();
        private readonly DropdownField detailField = new DropdownField();
        private readonly DropdownField sizeField = new DropdownField();
        private readonly TextField repeatsField = new TextField { value = "1" };
        private readonly TextField detailRepeatsField = new TextField { value = "30" };
        private readonly Button coverButton;
        private readonly Button clearButton;
        private readonly Dictionary<int, string> textureNames = new Dictionary<int, string>();
        private List<string> available = new List<string>();

        public GroundPaintOptions()
        {
            // Rows of icons as in the other tools (ADR 098, M4): how the brush works, how hard it is, what happens to a whole tile, and
            // under them the form to add a texture and the list of the map's textures.
            var modes = OptionControls.Group(OptionControls.Row(this, "paint.row.mode"));
            modeButtons[OptionControls.Choice(modes, "paintbrush", "paint.mode.paint", "paint.mode.paint.tooltip", () => SetPaintMode(OmsiPaintMode.Paint))] = OmsiPaintMode.Paint;
            modeButtons[OptionControls.Choice(modes, "eraser", "paint.mode.erase", "paint.mode.erase.tooltip", () => SetPaintMode(OmsiPaintMode.Erase))] = OmsiPaintMode.Erase;
            modeButtons[OptionControls.Choice(modes, "blend", "paint.mode.soften", "paint.mode.soften.tooltip", () => SetPaintMode(OmsiPaintMode.Soften))] = OmsiPaintMode.Soften;

            hardnessSlider.SetValueWithoutNotify(1f);
            hardnessSlider.RegisterValueChangedCallback(_ => ShowHardness());
            OptionControls.SliderBox(OptionControls.Row(this, "paint.hardness"), "circle-dot", hardnessSlider, hardnessLabel, "paint.hardness", "paint.hardness.tooltip");
            ShowHardness();

            var tileActions = OptionControls.Group(OptionControls.Row(this, "paint.row.tile"));
            // Both arm a mode: the pointer in the map becomes the tool and each click on a tile acts on that tile, until the button is pressed
            // again, the other one is chosen, Esc is pressed or the tab or the brush changes.
            coverButton = OptionControls.Choice(tileActions, "paint-bucket", "paint.cover", "paint.cover.tooltip", () => Arm(Armed == PaintTileArm.Cover ? PaintTileArm.None : PaintTileArm.Cover));
            clearButton = OptionControls.Choice(tileActions, "square-x", "paint.clear", "paint.clear.tooltip", () => Arm(Armed == PaintTileArm.Clear ? PaintTileArm.None : PaintTileArm.Clear));

            // The catalog page: the textures as tiles, the add tile last. Its form takes the place of the tiles while it is open, so it
            // is always in view, and Cancel or Add bring the tiles back.
            Page = new ScrollView(ScrollViewMode.Vertical)
            {
                verticalScrollerVisibility = ScrollerVisibility.AlwaysVisible, horizontalScrollerVisibility = ScrollerVisibility.Hidden
            };
            Page.AddToClassList("picker-tiles");
            tileArea.AddToClassList("picker-tiles-content");
            Page.Add(tileArea);
            addBox.AddToClassList("paint-add-form");
            addBox.style.display = DisplayStyle.None;
            Page.Add(addBox);
            AddField(addBox, "paint.add.texture", textureField);
            AddField(addBox, "paint.add.detail", detailField);
            AddField(addBox, "paint.add.size", sizeField);
            sizeField.choices = new List<string> { "256", "512", "1024" };
            sizeField.index = 1;
            AddText(addBox, "paint.add.repeats", repeatsField);
            AddText(addBox, "paint.add.detail_repeats", detailRepeatsField);
            var actions = new VisualElement();
            actions.AddToClassList("panel-tabs");
            actions.AddToClassList("picker-segmented");
            actions.AddToClassList("paint-add-actions");
            foreach (var button in new[] { LocBindings.NewButton("paint.add.confirm", Confirm), LocBindings.NewButton("paint.add.cancel", () => ToggleAdd(false)) })
            {
                button.AddToClassList("panel-tab");
                actions.Add(button);
            }
            addBox.Add(actions);

            var hint = LocBindings.NewLabel("paint.hint");
            hint.AddToClassList("panel-info");
            Add(hint);
            SetPaintMode(OmsiPaintMode.Paint);
        }

        /// <summary>The catalog page of the ground textures; the tool puts it beside the options column and shows it on the Paint tab.</summary>
        public VisualElement Page { get; }

        /// <summary>A texture of the list was chosen.</summary>
        public event Action TextureChosen;

        /// <summary>Which whole-tile action the next click in the map does, or none while the brush paints.</summary>
        public PaintTileArm Armed { get; private set; }

        /// <summary>The armed whole-tile action changed; the shell shows the pointer and the hint of it.</summary>
        public event Action ArmedChanged;

        /// <summary>The name of the chosen ground texture as the list shows it.</summary>
        public string TextureName { get; private set; } = string.Empty;

        /// <summary>Arms covering or clearing a tile by click, or lets go of it with <see cref="PaintTileArm.None"/>.</summary>
        public void Arm(PaintTileArm arm)
        {
            if (arm == Armed)
                return;
            Armed = arm;
            coverButton.EnableInClassList("panel-tab-active", arm == PaintTileArm.Cover);
            clearButton.EnableInClassList("panel-tab-active", arm == PaintTileArm.Clear);
            ArmedChanged?.Invoke();
        }

        /// <summary>Add a texture: path, detail path (empty for none), mask size exponent, repeats, detail repeats.</summary>
        public event Action<string, string, int, double, double> AddConfirmed;

        /// <summary>The number of the chosen ground texture (its [groundtex] position); 0 is the base the whole map starts as.</summary>
        public int Texture { get; private set; }

        public OmsiPaintMode PaintMode { get; private set; }

        /// <summary>0 to 1: how much of the brush's radius paints fully.</summary>
        public double Hardness => Math.Round(hardnessSlider.value, 2);

        public void SetTextures(IList<GroundTextureChoice> textures)
        {
            tileArea.Clear();
            rows.Clear();
            textureNames.Clear();
            foreach (var texture in textures)
            {
                var index = texture.Index;
                textureNames[index] = texture.Name;
                var tile = ToolCatalog.ImageTile(texture.Thumbnail, "image", texture.Name, Loc.T("paint.texture.tooltip"), () => Choose(index));
                tileArea.Add(tile);
                rows[tile] = index;
            }
            // The tile that adds a texture closes the row of them.
            var add = new Button(() => ToggleAdd(true));
            LocBindings.TitledTooltip(add, "paint.add", "paint.add.tooltip");
            add.AddToClassList("picker-tile");
            add.AddToClassList("picker-tile-captioned");
            var plus = new VisualElement { pickingMode = PickingMode.Ignore };
            plus.AddToClassList("picker-tile-preview");
            plus.AddToClassList("picker-tile-icon");
            plus.Add(new IconElement("image-plus"));
            add.Add(plus);
            var addName = LocBindings.NewLabel("paint.add");
            addName.pickingMode = PickingMode.Ignore;
            addName.AddToClassList("picker-tile-name");
            add.Add(addName);
            tileArea.Add(add);
            var found = false;
            foreach (var texture in textures)
                found |= texture.Index == Texture;
            if (!found)
                Texture = textures.Count > 0 ? textures[0].Index : 0;
            MarkChosen();
        }

        /// <summary>The picture files of the installation's Texture folder a texture can be added from, relative to the installation.</summary>
        public void SetAvailable(List<string> files)
        {
            available = files ?? new List<string>();
            textureField.choices = new List<string>(available);
            var detail = new List<string> { Loc.T("paint.add.no_detail") };
            detail.AddRange(available);
            detailField.choices = detail;
            if (available.Count > 0 && textureField.index < 0)
                textureField.index = 0;
            if (detailField.index < 0)
                detailField.index = 0;
        }

        /// <summary>True when the map has a ground texture with this number, so the list has a tile for it.</summary>
        public bool HasTexture(int index)
        {
            foreach (var number in rows.Values)
                if (number == index)
                    return true;
            return false;
        }

        public void Choose(int index)
        {
            Texture = index;
            MarkChosen();
            TextureChosen?.Invoke();
        }

        public void SetHardness(double hardness)
        {
            hardnessSlider.value = (float)Math.Max(0.0, Math.Min(1.0, hardness));
        }

        public void SetPaintMode(OmsiPaintMode mode)
        {
            PaintMode = mode;
            Arm(PaintTileArm.None);
            foreach (var pair in modeButtons)
                pair.Key.EnableInClassList("panel-tab-active", pair.Value == mode);
        }

        private void MarkChosen()
        {
            TextureName = textureNames.TryGetValue(Texture, out var name) ? name : string.Empty;
            foreach (var pair in rows)
                pair.Key.EnableInClassList("picker-tile-selected", pair.Value == Texture);
        }

        private void ShowHardness()
        {
            hardnessLabel.text = Hardness.ToString("0.00", CultureInfo.InvariantCulture);
        }

        /// <summary>Opens the form that adds a texture, in place of the tiles; also what the add tile does.</summary>
        public void OpenAddForm()
        {
            ToggleAdd(true);
        }

        private void ToggleAdd(bool open)
        {
            addBox.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            tileArea.style.display = open ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void Confirm()
        {
            if (textureField.index < 0 || textureField.index >= available.Count)
                return;
            var detail = detailField.index > 0 && detailField.index - 1 < available.Count ? available[detailField.index - 1] : string.Empty;
            var repeats = Number(repeatsField.value, 1.0);
            var detailRepeats = Number(detailRepeatsField.value, 30.0);
            var exponent = MaskExponents[Math.Max(0, Math.Min(MaskExponents.Length - 1, sizeField.index))];
            ToggleAdd(false);
            AddConfirmed?.Invoke(available[textureField.index], detail, exponent, repeats, detailRepeats);
        }

        private static double Number(string text, double fallback)
        {
            return double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0.0 && !double.IsInfinity(value)
                ? value
                : fallback;
        }

        private static void AddField(VisualElement parent, string labelKey, DropdownField field)
        {
            var row = new VisualElement();
            row.AddToClassList("paint-add-field");
            var label = LocBindings.NewLabel(labelKey); // i18n:keys
            label.AddToClassList("picker-option-label");
            row.Add(label);
            field.AddToClassList("new-map-dropdown");
            row.Add(field);
            parent.Add(row);
        }

        private static void AddText(VisualElement parent, string labelKey, TextField field)
        {
            var row = new VisualElement();
            row.AddToClassList("paint-add-field");
            var label = LocBindings.NewLabel(labelKey); // i18n:keys
            label.AddToClassList("picker-option-label");
            row.Add(label);
            field.AddToClassList("picker-field");
            row.Add(field);
            parent.Add(row);
        }
    }
}
