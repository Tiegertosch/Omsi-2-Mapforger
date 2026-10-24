using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The dialog for a new map (ADR 032): names and description, the template, size, ground height and texture, which
    /// side traffic keeps to, how busy cars and passengers are, rail, years, money, tickets and seasons. Every setting
    /// starts with the template's value. The shell creates the map when <see cref="CreateRequested"/> fires.
    /// </summary>
    internal sealed class NewMapDialog : VisualElement
    {
        private static readonly int[] Sizes = { 1, 2, 3, 4, 5, 7, 9 };
        private const int SwatchSize = 64;

        private readonly Label templateLabel = new Label();
        private readonly TextField nameField = new TextField();
        private readonly TextField folderField = new TextField();
        private readonly TextField descriptionField = new TextField { multiline = true };
        private readonly Dictionary<int, Button> sizeButtons = new Dictionary<int, Button>();
        private readonly Label sizeLabel = new Label();
        private readonly TextField heightField = new TextField();
        private readonly Button rightButton = new Button { text = "Right" };
        private readonly Button leftButton = new Button { text = "Left" };
        private readonly Slider carSlider = new Slider(0f, 2f);
        private readonly Slider passengerSlider = new Slider(0f, 2f);
        private readonly Label carLabel = new Label();
        private readonly Label passengerLabel = new Label();
        private readonly Toggle railToggle = new Toggle();
        private readonly TextField firstYearField = new TextField();
        private readonly TextField lastYearField = new TextField();
        private readonly TextField offsetField = new TextField();
        private readonly DropdownField moneyField = new DropdownField();
        private readonly DropdownField ticketField = new DropdownField();
        private readonly Dictionary<OmsiSeasonPreset, Button> seasonButtons = new Dictionary<OmsiSeasonPreset, Button>();
        private readonly ScrollView swatches = new ScrollView(ScrollViewMode.Horizontal);
        private readonly Label groundLabel = new Label();
        private readonly Label targetLabel = new Label();
        private readonly Button createButton;

        private readonly Dictionary<string, VisualElement> swatchByTexture = new Dictionary<string, VisualElement>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> moneyPaths = new List<string>();
        private readonly List<string> ticketPaths = new List<string>();
        private readonly object swatchLock = new object();
        private readonly List<KeyValuePair<string, OmsiImage>> swatchesDone = new List<KeyValuePair<string, OmsiImage>>();
        private readonly List<Texture2D> swatchTextures = new List<Texture2D>();

        private string templatePath;
        private string installationRoot;
        private OmsiNewMapOptions options = new OmsiNewMapOptions();
        private bool folderFollowsName = true;
        private int swatchGeneration;

        public NewMapDialog()
        {
            AddToClassList("dialog");
            AddToClassList("new-map-dialog");
            var title = new Label("New map");
            title.AddToClassList("popup-title");
            Add(title);
            var intro = new Label("Creates a map folder in the maps folder of your OMSI installation. AI lists, drivers, parked cars, holidays and every " +
                                  "setting you leave alone come from the template map; its tiles, timetables and entry points do not. The template is not changed.");
            intro.AddToClassList("popup-description");
            Add(intro);

            var body = new ScrollView(ScrollViewMode.Vertical);
            body.AddToClassList("new-map-body");
            Add(body);
            var columns = new VisualElement();
            columns.AddToClassList("new-map-columns");
            body.Add(columns);
            var left = Column(columns);
            var right = Column(columns);

            // The map: template, names and description.
            Section(left, "Map");
            var templateRow = Row(left, "Template");
            templateLabel.AddToClassList("new-map-value");
            templateRow.Add(templateLabel);
            var templateButton = new Button(ChooseTemplate) { text = "Choose...", tooltip = "Another map of your installation as template; the new map goes next to it." };
            templateButton.AddToClassList("map-control-button");
            templateButton.AddToClassList("new-map-small-button");
            templateRow.Add(templateButton);
            AddField(Row(left, "Name in OMSI"), nameField, "The name OMSI's map list shows");
            nameField.RegisterValueChangedCallback(evt =>
            {
                if (folderFollowsName)
                    folderField.SetValueWithoutNotify(FolderNameFor(evt.newValue));
                UpdateTarget();
            });
            AddField(Row(left, "Folder"), folderField, "The folder in OMSI's maps folder; follows the name until you change it");
            folderField.RegisterValueChangedCallback(evt =>
            {
                folderFollowsName = evt.newValue == FolderNameFor(nameField.value);
                UpdateTarget();
            });
            var descriptionRow = Row(left, "Description");
            descriptionRow.AddToClassList("new-map-row-tall");
            AddField(descriptionRow, descriptionField, "What the map is, for the map list in the game; several lines are fine");
            descriptionField.AddToClassList("new-map-description");

            // The land: size and ground height.
            Section(left, "Land");
            var sizeRow = Row(left, "Size (tiles)");
            var sizes = Segmented(sizeRow);
            foreach (var size in Sizes)
            {
                var captured = size;
                var button = new Button(() => SetSize(captured)) { text = size + "×" + size };
                button.AddToClassList("panel-tab");
                sizes.Add(button);
                sizeButtons[size] = button;
            }
            sizeLabel.AddToClassList("new-map-hint");
            left.Add(sizeLabel);
            var heightRow = Row(left, "Ground height (m)");
            AddField(heightRow, heightField, "The height of all terrain; roads and objects built later follow it");
            heightField.AddToClassList("new-map-number");
            heightField.RegisterValueChangedCallback(_ => UpdateTarget());

            // Traffic: side, how busy, rail.
            Section(right, "Traffic");
            var sideRow = Row(right, "Traffic keeps");
            var sides = Segmented(sideRow);
            rightButton.clicked += () => SetLeftHand(false);
            leftButton.clicked += () => SetLeftHand(true);
            rightButton.tooltip = "Right-hand traffic, as in most of Europe";
            leftButton.tooltip = "Left-hand traffic, as in Britain: OMSI's [LHT]";
            foreach (var button in new[] { rightButton, leftButton })
            {
                button.AddToClassList("panel-tab");
                sides.Add(button);
            }
            AddSlider(Row(right, "Cars"), carSlider, carLabel, "How busy the streets are over the day, as a factor on the template's daily curve");
            AddSlider(Row(right, "Passengers"), passengerSlider, passengerLabel, "How many passengers wait at the stops, as a factor on the template's daily curve");
            railToggle.AddToClassList("picker-option-toggle");
            railToggle.tooltip = "Trams and trains follow the switches of their tracks: OMSI's [realrail]";
            Row(right, "Real rail").Add(railToggle);

            // Era and money.
            Section(right, "Era and fares");
            var yearsRow = Row(right, "Years");
            AddField(yearsRow, firstYearField, "The first year a timetable may be set in");
            var dash = new Label("–");
            dash.AddToClassList("new-map-dash");
            yearsRow.Add(dash);
            AddField(yearsRow, lastYearField, "The last year a timetable may be set in");
            firstYearField.AddToClassList("new-map-number");
            lastYearField.AddToClassList("new-map-number");
            firstYearField.RegisterValueChangedCallback(_ => UpdateTarget());
            lastYearField.RegisterValueChangedCallback(_ => UpdateTarget());
            var offsetRow = Row(right, "Year offset");
            AddField(offsetRow, offsetField, "Added to today's year when the game starts at the real date; Berlin-Spandau has -25 for the 1980s");
            offsetField.AddToClassList("new-map-number");
            offsetField.RegisterValueChangedCallback(_ => UpdateTarget());
            AddDropdown(Row(right, "Money"), moneyField, "The money system: coins and notes the driver hands out");
            AddDropdown(Row(right, "Tickets"), ticketField, "The ticket pack: tickets passengers buy");

            Section(right, "Seasons");
            var seasons = Segmented(right);
            seasons.AddToClassList("new-map-seasons");
            AddSeason(seasons, OmsiSeasonPreset.Template, "As template", "The template's seasons, unchanged");
            AddSeason(seasons, OmsiSeasonPreset.FourWithSnow, "Snowy winter", "Spring, summer, autumn and a winter with snow from January to early February");
            AddSeason(seasons, OmsiSeasonPreset.MildWinter, "Mild winter", "Spring, summer, autumn and a winter without snow");
            AddSeason(seasons, OmsiSeasonPreset.SummerOnly, "Summer only", "Summer all year");

            // The ground texture across both columns.
            Section(body, "Ground texture (all tiles)");
            swatches.AddToClassList("new-map-swatches");
            swatches.contentContainer.AddToClassList("new-map-swatches-content");
            body.Add(swatches);
            groundLabel.AddToClassList("new-map-hint");
            body.Add(groundLabel);

            targetLabel.AddToClassList("popup-description");
            targetLabel.AddToClassList("export-target");
            Add(targetLabel);
            var buttons = new VisualElement();
            buttons.AddToClassList("dialog-buttons");
            var cancel = new Button(() => Cancelled?.Invoke()) { text = "Cancel" };
            cancel.AddToClassList("map-control-button");
            createButton = new Button(Create) { text = "Create" };
            createButton.AddToClassList("map-control-button");
            createButton.AddToClassList("dialog-primary");
            buttons.Add(cancel);
            buttons.Add(createButton);
            Add(buttons);

            RegisterCallback<AttachToPanelEvent>(_ => schedule.Execute(TakeSwatches).Every(100));
        }

        /// <summary>The map is to be created: template, folder, display name and options.</summary>
        public event Action<string, string, string, OmsiNewMapOptions> CreateRequested;

        public event Action Cancelled;

        /// <summary>Starts over with a template: its settings, a free name and its installation's lists.</summary>
        public void Open(string templateGlobalConfigPath)
        {
            nameField.SetValueWithoutNotify(string.Empty);
            descriptionField.SetValueWithoutNotify(string.Empty);
            SetTemplate(templateGlobalConfigPath);
            var name = "New map";
            var mapsFolder = MapsFolder();
            for (var number = 2; mapsFolder != null && Directory.Exists(Path.Combine(mapsFolder, name)); number++)
                name = "New map " + number;
            folderFollowsName = true;
            nameField.SetValueWithoutNotify(name);
            folderField.SetValueWithoutNotify(FolderNameFor(name));
            UpdateTarget();
            nameField.schedule.Execute(() => nameField.Focus());
        }

        private void ChooseTemplate()
        {
            var path = WindowsFileDialog.OpenFile("Choose a map as template for the new map", WindowsFileDialog.GlobalConfigFilter);
            if (string.IsNullOrEmpty(path))
                return;
            SetTemplate(path);
            UpdateTarget();
        }

        /// <summary>Reads the template's settings into the fields; without a template they show the defaults.</summary>
        private void SetTemplate(string path)
        {
            templatePath = path != null && File.Exists(path) ? path : null;
            OmsiRecordFile config = null;
            options = new OmsiNewMapOptions();
            if (templatePath != null)
            {
                try
                {
                    config = OmsiRecordFile.Load(templatePath, OmsiGlobalConfigLayouts.Instance);
                    options = OmsiNewMapOptions.FromTemplate(config);
                }
                catch (Exception exception) when (exception is IOException || exception is InvalidDataException || exception is UnauthorizedAccessException)
                {
                    Debug.LogWarning("Could not read the template " + templatePath + ": " + exception.Message);
                    templatePath = null;
                }
            }
            installationRoot = templatePath == null ? null : OmsiNewMapContent.InstallationOf(templatePath);
            templateLabel.text = templatePath == null ? "None chosen" : Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(templatePath)));
            templateLabel.tooltip = templatePath ?? string.Empty;

            SetSize(1);
            heightField.SetValueWithoutNotify("0");
            SetLeftHand(options.LeftHandTraffic);
            carSlider.SetValueWithoutNotify(1f);
            passengerSlider.SetValueWithoutNotify(1f);
            ShowFactor(carLabel, 1f);
            ShowFactor(passengerLabel, 1f);
            railToggle.SetValueWithoutNotify(options.RealRail);
            firstYearField.SetValueWithoutNotify(options.FirstYear.ToString(CultureInfo.InvariantCulture));
            lastYearField.SetValueWithoutNotify(options.LastYear.ToString(CultureInfo.InvariantCulture));
            offsetField.SetValueWithoutNotify(options.YearOffset.ToString(CultureInfo.InvariantCulture));
            FillDropdown(moneyField, moneyPaths, installationRoot == null ? new List<string>() : OmsiNewMapContent.Files(installationRoot, "Money", ".cfg"),
                options.MoneySystem);
            FillDropdown(ticketField, ticketPaths, installationRoot == null ? new List<string>() : OmsiNewMapContent.Files(installationRoot, "TicketPacks", ".otp"),
                options.TicketPack);
            SetSeasons(OmsiSeasonPreset.Template);
            FillSwatches(installationRoot == null ? new List<string>() : OmsiNewMapContent.GroundTextures(installationRoot, config), options.GroundTexture);
        }

        private string MapsFolder()
        {
            return templatePath == null ? null : Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(templatePath)));
        }

        /// <summary>A folder name from a display name: characters Windows forbids become underscores.</summary>
        private static string FolderNameFor(string name)
        {
            var characters = (name ?? string.Empty).Trim().ToCharArray();
            var invalid = Path.GetInvalidFileNameChars();
            for (var index = 0; index < characters.Length; index++)
                if (Array.IndexOf(invalid, characters[index]) >= 0)
                    characters[index] = '_';
            return new string(characters).Trim().TrimEnd('.');
        }

        private void SetSize(int size)
        {
            options.Size = size;
            foreach (var pair in sizeButtons)
                pair.Value.EnableInClassList("panel-tab-active", pair.Key == size);
            var metres = size * 300;
            sizeLabel.text = size * size + (size == 1 ? " tile, " : " tiles, ") + metres.ToString("N0", CultureInfo.InvariantCulture) + " × " +
                             metres.ToString("N0", CultureInfo.InvariantCulture) + " m around tile 0, 0; more can be added beside the map later.";
        }

        private void SetLeftHand(bool leftHand)
        {
            options.LeftHandTraffic = leftHand;
            rightButton.EnableInClassList("panel-tab-active", !leftHand);
            leftButton.EnableInClassList("panel-tab-active", leftHand);
        }

        private void SetSeasons(OmsiSeasonPreset preset)
        {
            options.Seasons = preset;
            foreach (var pair in seasonButtons)
                pair.Value.EnableInClassList("panel-tab-active", pair.Key == preset);
        }

        private void AddSeason(VisualElement parent, OmsiSeasonPreset preset, string text, string tooltip)
        {
            var button = new Button(() => SetSeasons(preset)) { text = text, tooltip = tooltip };
            button.AddToClassList("panel-tab");
            parent.Add(button);
            seasonButtons[preset] = button;
        }

        /// <summary>Fills a dropdown with the installation's files, named by their folder, and the template's choice even when missing.</summary>
        private static void FillDropdown(DropdownField field, List<string> paths, List<string> found, string templateValue)
        {
            paths.Clear();
            paths.AddRange(found);
            if (templateValue.Length > 0 && !paths.Exists(path => string.Equals(path, templateValue, StringComparison.OrdinalIgnoreCase)))
                paths.Insert(0, templateValue);
            var names = new List<string>();
            foreach (var path in paths)
            {
                var parts = path.Split('\\');
                var name = parts.Length >= 2 ? parts[parts.Length - 2] : Path.GetFileNameWithoutExtension(path);
                // Several files in one folder, such as DM, DM_91 and DM_92, go by their file names.
                if (paths.FindAll(other => other.Split('\\').Length >= 2 && other.Split('\\')[other.Split('\\').Length - 2] == name).Count > 1)
                    name = Path.GetFileNameWithoutExtension(path);
                if (string.Equals(path, templateValue, StringComparison.OrdinalIgnoreCase))
                    name += " (template)";
                names.Add(name);
            }
            field.choices = names;
            var index = paths.FindIndex(path => string.Equals(path, templateValue, StringComparison.OrdinalIgnoreCase));
            field.SetValueWithoutNotify(index >= 0 ? names[index] : names.Count > 0 ? names[0] : string.Empty);
            field.SetEnabled(names.Count > 0);
        }

        private static string Chosen(DropdownField field, List<string> paths)
        {
            var index = field.index;
            return index >= 0 && index < paths.Count ? paths[index] : string.Empty;
        }

        private void FillSwatches(List<string> textures, string chosen)
        {
            swatches.Clear();
            swatchByTexture.Clear();
            foreach (var texture in swatchTextures)
                UnityEngine.Object.Destroy(texture);
            swatchTextures.Clear();
            var generation = ++swatchGeneration;
            if (chosen.Length > 0 && !textures.Exists(texture => string.Equals(texture, chosen, StringComparison.OrdinalIgnoreCase)))
                textures.Insert(0, chosen);
            foreach (var texture in textures)
            {
                var captured = texture;
                var tile = new VisualElement { tooltip = texture };
                tile.AddToClassList("new-map-swatch");
                var image = new VisualElement { name = "image" };
                image.AddToClassList("new-map-swatch-image");
                image.Add(new SpinnerElement());
                tile.Add(image);
                var name = new Label(Path.GetFileNameWithoutExtension(texture));
                name.AddToClassList("new-map-swatch-name");
                tile.Add(name);
                tile.RegisterCallback<ClickEvent>(_ => ChooseGround(captured));
                swatches.Add(tile);
                swatchByTexture[texture] = tile;
            }
            ChooseGround(chosen);
            if (installationRoot == null || textures.Count == 0)
            {
                groundLabel.text = "No ground textures found; the template's ground stays.";
                return;
            }
            var root = installationRoot;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                foreach (var texture in textures)
                {
                    if (generation != swatchGeneration)
                        return;
                    var image = ReadSwatch(root, texture);
                    lock (swatchLock)
                        swatchesDone.Add(new KeyValuePair<string, OmsiImage>(texture, image));
                }
            });
        }

        private void ChooseGround(string texture)
        {
            options.GroundTexture = texture ?? string.Empty;
            foreach (var pair in swatchByTexture)
                pair.Value.EnableInClassList("new-map-swatch-selected", string.Equals(pair.Key, texture, StringComparison.OrdinalIgnoreCase));
            groundLabel.text = texture == null || texture.Length == 0
                ? "The template's ground stays."
                : texture + " as the base ground; more ground textures are painted on later.";
        }

        /// <summary>A texture of the installation reduced to a swatch, or null when it cannot be read; OMSI prefers a .dds of the same name.</summary>
        private static OmsiImage ReadSwatch(string root, string relativePath)
        {
            try
            {
                var file = OmsiRelativePath.Resolve(root, relativePath);
                var dds = Path.ChangeExtension(file, ".dds");
                var bytes = File.Exists(dds) ? File.ReadAllBytes(dds) : File.Exists(file) ? File.ReadAllBytes(file) : null;
                return bytes != null && OmsiImageReader.TryDecode(bytes, out var image) ? image.Reduce(SwatchSize) : null;
            }
            catch (Exception exception) when (exception is IOException || exception is InvalidDataException || exception is ArgumentException ||
                                              exception is UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>Shows decoded swatches on the main thread.</summary>
        private void TakeSwatches()
        {
            List<KeyValuePair<string, OmsiImage>> done;
            lock (swatchLock)
            {
                if (swatchesDone.Count == 0)
                    return;
                done = new List<KeyValuePair<string, OmsiImage>>(swatchesDone);
                swatchesDone.Clear();
            }
            foreach (var pair in done)
            {
                if (!swatchByTexture.TryGetValue(pair.Key, out var tile))
                    continue;
                var image = tile.Q<VisualElement>("image");
                image.Clear();
                if (pair.Value == null)
                    continue;
                var pixels = pair.Value.Channels == 4 ? pair.Value.Pixels : Grey(pair.Value.Pixels);
                var texture = new Texture2D(pair.Value.Width, pair.Value.Height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat };
                texture.LoadRawTextureData(pixels);
                texture.Apply(false, true);
                swatchTextures.Add(texture);
                image.style.backgroundImage = new StyleBackground(texture);
            }
        }

        private static byte[] Grey(byte[] values)
        {
            var pixels = new byte[values.Length * 4];
            for (var index = 0; index < values.Length; index++)
            {
                pixels[index * 4] = pixels[index * 4 + 1] = pixels[index * 4 + 2] = values[index];
                pixels[index * 4 + 3] = 255;
            }
            return pixels;
        }

        /// <summary>The options as the fields show them, or null with the problem.</summary>
        private OmsiNewMapOptions ReadOptions(out string problem)
        {
            problem = null;
            if (!TryNumber(heightField, out var height) || !int.TryParse(firstYearField.value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var first) ||
                !int.TryParse(lastYearField.value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var last) ||
                !int.TryParse(offsetField.value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var offset))
            {
                problem = "Ground height, years and year offset must be numbers.";
                return null;
            }
            var result = new OmsiNewMapOptions
            {
                Description = descriptionField.value ?? string.Empty,
                Size = options.Size,
                GroundHeight = (float)height,
                LeftHandTraffic = options.LeftHandTraffic,
                RealRail = railToggle.value,
                CarTraffic = Math.Round(carSlider.value, 2),
                PassengerTraffic = Math.Round(passengerSlider.value, 2),
                FirstYear = first,
                LastYear = last,
                YearOffset = offset,
                MoneySystem = Chosen(moneyField, moneyPaths),
                TicketPack = Chosen(ticketField, ticketPaths),
                Seasons = options.Seasons,
                GroundTexture = options.GroundTexture
            };
            problem = result.Check();
            return problem == null ? result : null;
        }

        private static bool TryNumber(TextField field, out double value)
        {
            return double.TryParse((field.value ?? string.Empty).Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
                   !double.IsNaN(value) && !double.IsInfinity(value);
        }

        /// <summary>The problem that keeps the map from being created, or null; also the folder it would go to.</summary>
        private string Problem(out string target)
        {
            target = null;
            var mapsFolder = MapsFolder();
            if (mapsFolder == null)
                return "Choose a map of your OMSI installation as template; the new map goes next to it.";
            if (string.IsNullOrWhiteSpace(nameField.value))
                return "Enter the name OMSI shows for the new map.";
            var problem = OmsiNewMap.CheckFolderName(mapsFolder, folderField.value ?? string.Empty);
            if (problem != null)
                return problem;
            target = Path.Combine(mapsFolder, folderField.value);
            ReadOptions(out problem);
            return problem;
        }

        private void UpdateTarget()
        {
            var problem = Problem(out var target);
            targetLabel.text = problem ?? "Creates " + target + ", then asks where to save its project.";
            targetLabel.EnableInClassList("export-problem", problem != null);
            createButton.SetEnabled(problem == null);
        }

        private void Create()
        {
            var problem = Problem(out _);
            var result = problem == null ? ReadOptions(out problem) : null;
            if (result == null)
            {
                UpdateTarget();
                return;
            }
            CreateRequested?.Invoke(templatePath, folderField.value, nameField.value.Trim(), result);
        }

        private void ShowFactor(Label label, float value)
        {
            label.text = value.ToString("0.00", CultureInfo.InvariantCulture) + "×";
        }

        private static VisualElement Column(VisualElement parent)
        {
            var column = new VisualElement();
            column.AddToClassList("new-map-column");
            parent.Add(column);
            return column;
        }

        private static void Section(VisualElement parent, string text)
        {
            var label = new Label(text.ToUpperInvariant());
            label.AddToClassList("inspector-section-heading");
            label.AddToClassList("new-map-section");
            parent.Add(label);
        }

        private static VisualElement Row(VisualElement parent, string label)
        {
            var row = new VisualElement();
            row.AddToClassList("picker-option");
            row.AddToClassList("new-map-row");
            var text = new Label(label);
            text.AddToClassList("picker-option-label");
            text.AddToClassList("new-map-label");
            row.Add(text);
            parent.Add(row);
            return row;
        }

        private static VisualElement Segmented(VisualElement parent)
        {
            var group = new VisualElement();
            group.AddToClassList("panel-tabs");
            group.AddToClassList("picker-segmented");
            parent.Add(group);
            return group;
        }

        private static void AddField(VisualElement row, TextField field, string tooltip)
        {
            field.AddToClassList("picker-field");
            field.AddToClassList("new-map-field");
            field.tooltip = tooltip;
            row.tooltip = tooltip;
            row.Add(field);
        }

        private void AddSlider(VisualElement row, Slider slider, Label value, string tooltip)
        {
            slider.AddToClassList("new-map-slider");
            slider.tooltip = tooltip;
            row.tooltip = tooltip;
            slider.RegisterValueChangedCallback(evt => ShowFactor(value, evt.newValue));
            row.Add(slider);
            value.AddToClassList("new-map-factor");
            row.Add(value);
        }

        private static void AddDropdown(VisualElement row, DropdownField field, string tooltip)
        {
            field.AddToClassList("new-map-dropdown");
            field.tooltip = tooltip;
            row.tooltip = tooltip;
            row.Add(field);
        }
    }
}
