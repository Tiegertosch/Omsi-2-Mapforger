using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The vehicles dialog (ADR 095): the vehicle groups of the map's <c>ailists.cfg</c>, which the tours of a line name. A depot lists vehicle types with the
    /// buses of each (fleet number, plate, repaint, days); a plain group lists vehicle files with a weight. Each change is one undoable step.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        private ModalDialog vehiclesDialog;
        private VisualElement vehiclesBody;
        private ScrollView vehiclesScroll;
        private List<string> installedVehicles = new List<string>();
        private int vehicleGroup;
        private int vehicleNew;
        private string vehicleNewName = string.Empty;
        private string vehicleNewHof = string.Empty;
        private string vehicleFilter = string.Empty;
        private string vehicleChosenFile;
        private string vehicleWeight = "10";

        private void SetUpVehicles(VisualElement root)
        {
            // Every change is saved as its own undo step, so the only button is Close; Enter would close it by accident while a field is edited.
            vehiclesDialog = NewDialog(new DialogOptions
            {
                Name = "vehicles-dialog",
                TitleKey = "vehicles.title",
                DescriptionKey = "vehicles.description",
                Width = DialogWidth.Wide,
                EnterPressesPrimary = false,
                Buttons = new[] { new DialogButton { TextKey = "vehicles.close", Role = DialogButtonRole.Primary } }
            });
            vehiclesBody = vehiclesDialog.Body;
            vehiclesScroll = vehiclesBody.GetFirstAncestorOfType<ScrollView>();
        }

        /// <summary>The button in the Lines tab.</summary>
        private Button VehiclesButton()
        {
            var button = new Button(OpenVehicles) { text = Loc.T("vehicles.open"), tooltip = Loc.T("vehicles.open.tooltip") };
            button.AddToClassList("map-control-button");
            button.AddToClassList("form-button-bottom");
            return button;
        }

        private void OpenVehicles()
        {
            if (vehiclesDialog == null || session == null || !session.CanEdit || EditingVariant)
                return;
            try
            {
                installedVehicles = session.InstalledVehicleFiles();
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogException(exception);
                installedVehicles = new List<string>();
            }
            vehicleNew = 0;
            vehicleFilter = string.Empty;
            vehicleChosenFile = null;
            RefreshVehicles();
            vehiclesDialog.Show();
        }

        /// <summary>Draws the dialog again when it is open, such as after an undo.</summary>
        private void RefreshVehiclesIfOpen()
        {
            if (vehiclesDialog != null && vehiclesDialog.IsShown)
                RefreshVehicles();
        }

        /// <summary>Runs one change of the vehicle groups as an undoable step and draws the dialog again.</summary>
        private bool ChangeVehicles(Func<OmsiTerrainChange> change, bool tours = false)
        {
            try
            {
                var made = change();
                if (made == null)
                    return false;
                PushHistory(new ObjectEditStep { TerrainChange = made, Description = Loc.T("vehicles.step") });
                if (tours && currentSummary != null)
                    StartRoutesBuild(currentSummary);
                SetStatusT("vehicles.status.changed");
                RefreshVehicles();
                return true;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException || exception is IOException ||
                                              exception is UnauthorizedAccessException)
            {
                Debug.LogException(exception);
                SetStatusT("vehicles.status.failed", ("reason", exception.Message));
                RefreshVehicles();
                return false;
            }
        }

        private TextField VehicleField(string value, Action<string> changed, string tooltip = null, bool grow = true)
        {
            var field = new TextField { value = value ?? string.Empty, isDelayed = true };
            field.AddToClassList("picker-field");
            if (grow)
                field.AddToClassList("form-field-grow");
            if (tooltip != null)
                field.tooltip = tooltip;
            field.RegisterValueChangedCallback(change => changed(change.newValue));
            return field;
        }

        private void AddVehicleRow(string labelKey, VisualElement control)
        {
            var row = new VisualElement();
            row.AddToClassList("form-row");
            row.AddToClassList("form-row-dense-3");
            var label = LocBindings.NewLabel(labelKey); // i18n:keys
            label.AddToClassList("popup-description");
            label.AddToClassList("form-row-label");
            row.Add(label);
            control.AddToClassList("form-row-field");
            row.Add(control);
            vehiclesBody.Add(row);
        }

        private void RefreshVehicles()
        {
            var scroll = vehiclesScroll.scrollOffset;
            vehiclesBody.Clear();
            OmsiAiListFile lists;
            List<OmsiAiGroup> groups;
            try
            {
                lists = session.ReadAiLists();
                groups = lists.Groups;
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException)
            {
                Debug.LogException(exception);
                SetStatusT("vehicles.status.failed", ("reason", exception.Message));
                return;
            }
            vehicleGroup = Mathf.Clamp(vehicleGroup, 0, Math.Max(0, groups.Count - 1));

            var top = new VisualElement();
            top.AddToClassList("form-toolbar");
            top.Add(SmallButton("vehicles.new_depot", "vehicles.new_depot.tooltip", () => { vehicleNew = vehicleNew == 1 ? 0 : 1; RefreshVehicles(); }));
            top.Add(SmallButton("vehicles.new_pool", "vehicles.new_pool.tooltip", () => { vehicleNew = vehicleNew == 2 ? 0 : 2; RefreshVehicles(); }));
            vehiclesBody.Add(top);
            if (vehicleNew != 0)
                AddNewVehicleGroup(groups);

            if (groups.Count == 0)
            {
                var empty = new Label(Loc.T("vehicles.empty"));
                empty.AddToClassList("panel-info");
                vehiclesBody.Add(empty);
                return;
            }
            var names = groups.Select(group => group.IsDepot
                ? Loc.T("vehicles.group.depot", ("name", group.Name), ("count", group.TypeGroups.Sum(type => type.Vehicles.Count)))
                : Loc.T("vehicles.group.pool", ("name", group.Name), ("count", group.PoolVehicles.Count))).ToList();
            var choice = new DropdownField(names, vehicleGroup);
            choice.AddToClassList("new-map-dropdown");
            choice.RegisterValueChangedCallback(change =>
            {
                vehicleGroup = names.IndexOf(change.newValue);
                RefreshVehicles();
            });
            AddVehicleRow("vehicles.group", choice);

            var current = groups[vehicleGroup];
            var index = vehicleGroup;
            AddVehicleRow("vehicles.name", VehicleField(current.Name, text =>
            {
                if (!string.Equals(text.Trim(), current.Name, StringComparison.Ordinal))
                    ChangeVehicles(() => session.RenameAiGroup(index, text), true);
            }));
            if (current.IsDepot)
            {
                var hof = VehicleField(current.Hof, text => ChangeVehicles(() => session.EditAiLists(file => file.SetHof(index, text))));
                var hofNames = current.TypeGroups.Count > 0 ? session.DepotNamesOfVehicle(current.TypeGroups[0].File) : new List<string>();
                if (hofNames.Count > 0)
                    hof.tooltip = Loc.T("vehicles.hof.hint", ("names", string.Join(", ", hofNames)));
                AddVehicleRow("vehicles.hof", hof);
            }
            var users = session.AiGroupUsers(current.Name);
            var usage = new Label(users.Count == 0 ? Loc.T("vehicles.unused") : Loc.T("vehicles.users", ("tours", string.Join(", ", users))));
            usage.AddToClassList("panel-info");
            vehiclesBody.Add(usage);
            var delete = SmallButton("vehicles.delete", "vehicles.delete.tooltip", () => ChangeVehicles(() => session.RemoveAiGroup(index), true));
            delete.AddToClassList("form-action-block");
            delete.SetEnabled(users.Count == 0);
            vehiclesBody.Add(delete);

            if (current.IsDepot)
                AddDepotTypes(current, index);
            else
                AddPoolVehicles(current, index);
            vehiclesScroll.schedule.Execute(() => vehiclesScroll.scrollOffset = scroll);
        }

        private void AddVehicleFileRows(string heading)
        {
            var title = new Label(Loc.T(heading)); // i18n:keys
            title.AddToClassList("inspector-group-title");
            title.AddToClassList("form-group-heading");
            vehiclesBody.Add(title);
            AddVehicleRow("vehicles.filter", VehicleField(vehicleFilter, text =>
            {
                vehicleFilter = text.Trim();
                RefreshVehicles();
            }));
            var matches = installedVehicles.Where(file => vehicleFilter.Length == 0 || file.IndexOf(vehicleFilter, StringComparison.OrdinalIgnoreCase) >= 0).Take(300).ToList();
            if (vehicleChosenFile == null || !matches.Contains(vehicleChosenFile))
                vehicleChosenFile = matches.Count > 0 ? matches[0] : null;
            if (matches.Count == 0)
            {
                var none = new Label(Loc.T("vehicles.none_found"));
                none.AddToClassList("panel-info");
                vehiclesBody.Add(none);
                return;
            }
            var choice = new DropdownField(matches, matches.IndexOf(vehicleChosenFile));
            choice.AddToClassList("new-map-dropdown");
            choice.RegisterValueChangedCallback(change =>
            {
                vehicleChosenFile = change.newValue;
                if (vehicleNew == 1)
                    RefreshVehicles();
            });
            AddVehicleRow("vehicles.file", choice);
        }

        private void AddNewVehicleGroup(List<OmsiAiGroup> groups)
        {
            var depot = vehicleNew == 1;
            AddVehicleRow("vehicles.name", VehicleField(vehicleNewName, text => vehicleNewName = text.Trim()));
            if (depot)
            {
                AddVehicleFileRows("vehicles.types");
                var hofNames = vehicleChosenFile == null ? new List<string>() : session.DepotNamesOfVehicle(vehicleChosenFile);
                if (string.IsNullOrEmpty(vehicleNewHof) && hofNames.Count > 0)
                    vehicleNewHof = hofNames[0];
                var hof = VehicleField(vehicleNewHof, text => vehicleNewHof = text.Trim());
                if (hofNames.Count > 0)
                    hof.tooltip = Loc.T("vehicles.hof.hint", ("names", string.Join(", ", hofNames)));
                AddVehicleRow("vehicles.hof", hof);
            }
            var create = SmallButton("vehicles.create", "vehicles.create.tooltip", () =>
            {
                if (string.IsNullOrWhiteSpace(vehicleNewName))
                {
                    SetStatusT("vehicles.status.need_name");
                    return;
                }
                if (depot && vehicleChosenFile == null)
                {
                    SetStatusT("vehicles.status.need_file");
                    return;
                }
                var name = vehicleNewName;
                var file = vehicleChosenFile;
                var hofName = vehicleNewHof;
                var count = groups.Count;
                if (ChangeVehicles(() => session.EditAiLists(lists =>
                {
                    if (depot)
                    {
                        var added = lists.AddDepot(name, hofName);
                        lists.AddTypeGroup(added, file);
                    }
                    else
                    {
                        lists.AddPool(name);
                    }
                })))
                {
                    vehicleGroup = count;
                    vehicleNew = 0;
                    vehicleNewName = string.Empty;
                    vehicleNewHof = string.Empty;
                    RefreshVehicles();
                }
            });
            create.AddToClassList("form-action-block");
            vehiclesBody.Add(create);
        }

        private void AddDepotTypes(OmsiAiGroup depot, int group)
        {
            var heading = new Label(Loc.T("vehicles.types"));
            heading.AddToClassList("inspector-group-title");
            heading.AddToClassList("form-group-heading");
            vehiclesBody.Add(heading);
            if (depot.TypeGroups.Count == 0)
            {
                var none = new Label(Loc.T("vehicles.depot_empty"));
                none.AddToClassList("panel-info");
                vehiclesBody.Add(none);
            }
            for (var typeIndex = 0; typeIndex < depot.TypeGroups.Count; typeIndex++)
            {
                var type = depot.TypeGroups[typeIndex];
                var typeNumber = typeIndex;
                var head = new VisualElement();
                head.AddToClassList("form-row");
                head.AddToClassList("form-row-dense");
                head.AddToClassList("form-type-head");
                var title = new Label(Loc.T("vehicles.type.item", ("file", type.File), ("count", type.Vehicles.Count)));
                title.AddToClassList("popup-description");
                title.AddToClassList("form-cell-flex");
                title.AddToClassList("form-cell-wrap");
                head.Add(title);
                var addBus = SmallButton("vehicles.bus.add", "vehicles.bus.add.tooltip", () =>
                {
                    var number = NextFleetNumber(type);
                    ChangeVehicles(() => session.EditAiLists(file => file.AddVehicle(group, typeNumber, new OmsiDepotVehicle { Number = number })));
                });
                head.Add(addBus);
                head.Add(SmallButton("vehicles.type.remove", "vehicles.type.remove.tooltip", () => ChangeVehicles(() => session.EditAiLists(file => file.RemoveTypeGroup(group, typeNumber)))));
                vehiclesBody.Add(head);
                if (type.Vehicles.Count > 0)
                    vehiclesBody.Add(BusHeader());
                for (var busIndex = 0; busIndex < type.Vehicles.Count; busIndex++)
                    vehiclesBody.Add(BusRow(type.Vehicles[busIndex], group, typeNumber, busIndex));
            }
            AddVehicleFileRows("vehicles.type.add");
            var add = SmallButton("vehicles.type.add.button", "vehicles.type.add.tooltip", () =>
            {
                var chosen = vehicleChosenFile;
                if (chosen == null)
                {
                    SetStatusT("vehicles.status.need_file");
                    return;
                }
                ChangeVehicles(() => session.EditAiLists(file => file.AddTypeGroup(group, chosen)));
            });
            add.AddToClassList("form-action-block");
            vehiclesBody.Add(add);
        }

        private static string NextFleetNumber(OmsiAiTypeGroup type)
        {
            var highest = 0;
            foreach (var bus in type.Vehicles)
                if (int.TryParse(bus.Number, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                    highest = Math.Max(highest, number);
            return highest > 0 ? (highest + 1).ToString(CultureInfo.InvariantCulture) : "1";
        }

        private static VisualElement BusHeader()
        {
            var row = new VisualElement();
            row.AddToClassList("form-header-row");
            foreach (var key in new[] { "vehicles.col.number", "vehicles.col.plate", "vehicles.col.paint", "vehicles.col.from", "vehicles.col.to" })
            {
                var label = LocBindings.NewLabel(key); // i18n:keys
                label.AddToClassList("popup-description");
                label.AddToClassList("form-cell-flex");
                row.Add(label);
            }
            var spacer = new VisualElement();
            spacer.AddToClassList("form-bus-spacer");
            row.Add(spacer);
            return row;
        }

        private VisualElement BusRow(OmsiDepotVehicle bus, int group, int type, int index)
        {
            var row = new VisualElement();
            row.AddToClassList("form-row");
            row.AddToClassList("form-row-dense");
            void Edit(Action<OmsiDepotVehicle> set)
            {
                var edited = new OmsiDepotVehicle { Number = bus.Number, Registration = bus.Registration, Paint = bus.Paint, From = bus.From, To = bus.To };
                set(edited);
                ChangeVehicles(() => session.EditAiLists(file => file.SetVehicle(group, type, index, edited)));
            }
            var fields = new[]
            {
                VehicleField(bus.Number, text => Edit(edited => edited.Number = text)),
                VehicleField(bus.Registration, text => Edit(edited => edited.Registration = text)),
                VehicleField(bus.Paint, text => Edit(edited => edited.Paint = text)),
                VehicleField(bus.From, text => Edit(edited => edited.From = text), Loc.T("vehicles.day.tooltip")),
                VehicleField(bus.To, text => Edit(edited => edited.To = text), Loc.T("vehicles.day.tooltip"))
            };
            foreach (var field in fields)
            {
                field.AddToClassList("form-cell-field");
                row.Add(field);
            }
            var remove = new Button(() => ChangeVehicles(() => session.EditAiLists(file => file.RemoveVehicle(group, type, index)))) { text = "×", tooltip = Loc.T("vehicles.bus.remove.tooltip") };
            remove.AddToClassList("map-control-button");
            remove.AddToClassList("form-small-button");
            row.Add(remove);
            return row;
        }

        private void AddPoolVehicles(OmsiAiGroup pool, int group)
        {
            var heading = new Label(Loc.T("vehicles.pool.vehicles", ("count", pool.PoolVehicles.Count)));
            heading.AddToClassList("inspector-group-title");
            heading.AddToClassList("form-group-heading");
            vehiclesBody.Add(heading);
            for (var index = 0; index < pool.PoolVehicles.Count; index++)
            {
                var vehicle = pool.PoolVehicles[index];
                var number = index;
                var row = new VisualElement();
                row.AddToClassList("form-row");
                row.AddToClassList("form-row-dense");
                var file = new Label(vehicle.File);
                file.AddToClassList("popup-description");
                file.AddToClassList("form-cell-flex");
                file.AddToClassList("form-cell-wrap");
                row.Add(file);
                var weight = VehicleField(vehicle.Weight, text => ChangeVehicles(() => session.EditAiLists(lists => lists.SetPoolVehicle(group, number, vehicle.File, text))),
                    Loc.T("vehicles.weight.tooltip"), false);
                weight.AddToClassList("form-narrow-field");
                row.Add(weight);
                var remove = new Button(() => ChangeVehicles(() => session.EditAiLists(lists => lists.RemovePoolVehicle(group, number)))) { text = "×", tooltip = Loc.T("vehicles.bus.remove.tooltip") };
                remove.AddToClassList("map-control-button");
                remove.AddToClassList("form-small-button");
                row.Add(remove);
                vehiclesBody.Add(row);
            }
            AddVehicleFileRows("vehicles.pool.add");
            AddVehicleRow("vehicles.weight", VehicleField(vehicleWeight, text => vehicleWeight = text.Trim(), Loc.T("vehicles.weight.tooltip")));
            var add = SmallButton("vehicles.pool.add.button", "vehicles.pool.add.tooltip", () =>
            {
                var chosen = vehicleChosenFile;
                if (chosen == null)
                {
                    SetStatusT("vehicles.status.need_file");
                    return;
                }
                var weight = vehicleWeight;
                ChangeVehicles(() => session.EditAiLists(lists => lists.AddPoolVehicle(group, chosen, weight)));
            });
            add.AddToClassList("form-action-block");
            vehiclesBody.Add(add);
        }

        /// <summary>For checks: -vehicles [group number] opens the dialog.</summary>
        private void RunVehicleArguments(Func<string, int, string> argument, string[] args)
        {
            if (Array.IndexOf(args, "-vehicles") < 0 || session == null || !session.CanEdit)
                return;
            OpenVehicles();
            if (int.TryParse(argument("-vehicles", 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var group))
            {
                vehicleGroup = group;
                RefreshVehicles();
            }
        }
    }
}
