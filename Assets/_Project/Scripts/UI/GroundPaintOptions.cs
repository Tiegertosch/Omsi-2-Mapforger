using System;
using System.Collections.Generic;
using System.Globalization;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>A ground texture of the map as the paint list shows it.</summary>
    internal struct GroundTextureChoice
    {
        public int Index;
        public string Name;
        public Color Color;
    }

    /// <summary>The options of ground painting inside the terrain tool (ADR 091): the map's textures, the brush's kind and hardness, adding a texture.</summary>
    internal sealed class GroundPaintOptions : VisualElement
    {
        private static readonly int[] MaskExponents = { 8, 9, 10 };

        private readonly VisualElement list = new VisualElement();
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
        private List<string> available = new List<string>();

        public GroundPaintOptions()
        {
            var title = LocBindings.NewLabel("paint.title");
            title.AddToClassList("picker-options-title");
            Add(title);
            var hint = LocBindings.NewLabel("paint.hint");
            hint.AddToClassList("panel-info");
            Add(hint);

            list.AddToClassList("ground-texture-list");
            Add(list);

            var how = new VisualElement();
            how.AddToClassList("panel-tabs");
            how.AddToClassList("picker-segmented");
            foreach (var pair in new[]
                     {
                         new KeyValuePair<string, OmsiPaintMode>("paint.mode.paint", OmsiPaintMode.Paint),
                         new KeyValuePair<string, OmsiPaintMode>("paint.mode.erase", OmsiPaintMode.Erase),
                         new KeyValuePair<string, OmsiPaintMode>("paint.mode.soften", OmsiPaintMode.Soften)
                     })
            {
                var mode = pair.Value;
                var button = LocBindings.NewButton(pair.Key, () => SetPaintMode(mode)); // i18n:keys
                LocBindings.Tooltip(button, pair.Key + ".tooltip"); // i18n:keys
                button.AddToClassList("panel-tab");
                how.Add(button);
                modeButtons[button] = mode;
            }
            Add(how);

            var hardnessRow = new VisualElement();
            hardnessRow.AddToClassList("picker-option");
            var hardnessTitle = LocBindings.NewLabel("paint.hardness");
            hardnessTitle.AddToClassList("picker-option-label");
            hardnessRow.Add(hardnessTitle);
            LocBindings.Tooltip(hardnessRow, "paint.hardness.tooltip");
            hardnessSlider.AddToClassList("new-map-slider");
            hardnessSlider.SetValueWithoutNotify(1f);
            hardnessSlider.RegisterValueChangedCallback(_ => ShowHardness());
            hardnessRow.Add(hardnessSlider);
            hardnessLabel.AddToClassList("new-map-factor");
            hardnessRow.Add(hardnessLabel);
            Add(hardnessRow);
            ShowHardness();

            var tileButtons = new VisualElement();
            tileButtons.AddToClassList("panel-tabs");
            tileButtons.AddToClassList("picker-segmented");
            foreach (var button in new[]
                     {
                         LocBindings.WithTooltip(LocBindings.NewButton("paint.cover", () => CoverRequested?.Invoke(true)), "paint.cover.tooltip"),
                         LocBindings.WithTooltip(LocBindings.NewButton("paint.clear", () => CoverRequested?.Invoke(false)), "paint.clear.tooltip")
                     })
            {
                button.AddToClassList("panel-tab");
                tileButtons.Add(button);
            }
            Add(tileButtons);

            var add = LocBindings.WithTooltip(LocBindings.NewButton("paint.add", () => ToggleAdd(true)), "paint.add.tooltip");
            add.AddToClassList("map-control-button");
            Add(add);

            addBox.style.display = DisplayStyle.None;
            Add(addBox);
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
            foreach (var button in new[] { LocBindings.NewButton("paint.add.confirm", Confirm), LocBindings.NewButton("paint.add.cancel", () => ToggleAdd(false)) })
            {
                button.AddToClassList("panel-tab");
                actions.Add(button);
            }
            addBox.Add(actions);
            SetPaintMode(OmsiPaintMode.Paint);
        }

        /// <summary>A texture of the list was chosen.</summary>
        public event Action TextureChosen;

        /// <summary>Cover (true) or clear (false) the tile under the pointer with the chosen texture.</summary>
        public event Action<bool> CoverRequested;

        /// <summary>Add a texture: path, detail path (empty for none), mask size exponent, repeats, detail repeats.</summary>
        public event Action<string, string, int, double, double> AddConfirmed;

        /// <summary>The number of the chosen ground texture (its [groundtex] position); 0 is the base the whole map starts as.</summary>
        public int Texture { get; private set; }

        public OmsiPaintMode PaintMode { get; private set; }

        /// <summary>0 to 1: how much of the brush's radius paints fully.</summary>
        public double Hardness => Math.Round(hardnessSlider.value, 2);

        public void SetTextures(IList<GroundTextureChoice> textures)
        {
            list.Clear();
            rows.Clear();
            foreach (var texture in textures)
            {
                var index = texture.Index;
                var row = new Button(() => Choose(index));
                row.AddToClassList("ground-texture-row");
                var swatch = new VisualElement();
                swatch.AddToClassList("ground-texture-swatch");
                swatch.style.backgroundColor = texture.Color;
                swatch.pickingMode = PickingMode.Ignore;
                row.Add(swatch);
                var name = new Label(texture.Name) { pickingMode = PickingMode.Ignore };
                name.AddToClassList("ground-texture-name");
                row.Add(name);
                list.Add(row);
                rows[row] = index;
            }
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
            foreach (var pair in modeButtons)
                pair.Key.EnableInClassList("panel-tab-active", pair.Value == mode);
        }

        private void MarkChosen()
        {
            foreach (var pair in rows)
                pair.Key.EnableInClassList("ground-texture-chosen", pair.Value == Texture);
        }

        private void ShowHardness()
        {
            hardnessLabel.text = Hardness.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private void ToggleAdd(bool open)
        {
            addBox.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
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
            row.AddToClassList("picker-option");
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
            row.AddToClassList("picker-option");
            var label = LocBindings.NewLabel(labelKey); // i18n:keys
            label.AddToClassList("picker-option-label");
            row.Add(label);
            field.AddToClassList("picker-field");
            row.Add(field);
            parent.Add(row);
        }
    }
}
