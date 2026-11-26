using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.UI;

namespace OmsiMapEditor.Tests
{
    /// <summary>
    /// The entries of the right-click menu (ADR 098, M3): which entries a kind of hit offers for the switches of the shell, in which
    /// order, with dividers, shortcuts and the danger mark.
    /// </summary>
    public sealed class ContextMenuEntriesTests
    {
        private static ContextMenuState Editable()
        {
            return new ContextMenuState { CanEdit = true, CanEditObjects = true };
        }

        /// <summary>The entries as one readable line: keys, "---" for a divider, "!" after a dangerous entry, "~" after one greyed with its reason, "(note)" for the read-only note.</summary>
        private static string Describe(ContextMenuKind kind, ContextMenuState state)
        {
            return string.Join(" ", ContextMenuEntries.For(kind, state).Select(entry =>
            {
                switch (entry.Type)
                {
                    case ContextMenuEntryType.Divider: return "---";
                    case ContextMenuEntryType.ReadOnlyNote: return "(note)";
                    default: return entry.TextKey.Substring("context.".Length) + (entry.Danger ? "!" : string.Empty) + (entry.DisabledReasonKey != null ? "~" : string.Empty);
                }
            }));
        }

        private static IEnumerable<ContextMenuState> AllStates()
        {
            for (var bits = 0; bits < 4096; bits++)
            {
                yield return new ContextMenuState
                {
                    CanEdit = (bits & 1) != 0,
                    CanEditObjects = (bits & 2) != 0,
                    CanEditSplinesInVariant = (bits & 4) != 0,
                    PathsGroupShown = (bits & 8) != 0,
                    IsEntryPoint = (bits & 16) != 0,
                    IsBusStop = (bits & 32) != 0,
                    ClipboardFilled = (bits & 64) != 0,
                    TileRevertible = (bits & 128) != 0,
                    IsMadeJunction = (bits & 256) != 0,
                    HasPriorityArms = (bits & 512) != 0,
                    IsHeadOfMadeCrossing = (bits & 1024) != 0,
                    HasSignalArms = (bits & 2048) != 0,
                };
            }
        }

        [Test]
        public void AReadOnlyMapShowsOnlyThePropertiesAndTheNote()
        {
            var readOnly = new ContextMenuState { IsBusStop = true, IsEntryPoint = true, ClipboardFilled = true };

            Assert.That(Describe(ContextMenuKind.Selection, readOnly), Is.EqualTo("properties (note)"));
            Assert.That(Describe(ContextMenuKind.Object, readOnly), Is.EqualTo("properties rename_entry rename_busstop (note)"));
            Assert.That(Describe(ContextMenuKind.Spline, readOnly), Is.EqualTo("properties (note)"));
            Assert.That(Describe(ContextMenuKind.Tile, readOnly), Is.EqualTo("tile_properties (note)"));
        }

        [Test]
        public void AnEditableObjectOffersItsFullMenuInOrder()
        {
            Assert.That(Describe(ContextMenuKind.Object, Editable()),
                Is.EqualTo("properties change_type labels --- copy duplicate level --- delete!"));
        }

        [Test]
        public void AnEntryPointReplacesDeleteByDeleteEntryAndOffersRename()
        {
            var state = Editable();
            state.IsEntryPoint = true;

            Assert.That(Describe(ContextMenuKind.Object, state),
                Is.EqualTo("properties rename_entry change_type labels --- copy duplicate level --- delete_entry!"));
            var last = ContextMenuEntries.For(ContextMenuKind.Object, state).Last();
            Assert.That(last.Action, Is.EqualTo(ContextMenuAction.DeleteEntryPoint));
            Assert.That(last.Shortcut, Is.Null, "deleting an entry point has no key");
        }

        [Test]
        public void ABusStopOffersRenameBeforeTheTypeAndNothingElseChanges()
        {
            var state = Editable();
            state.IsBusStop = true;

            Assert.That(Describe(ContextMenuKind.Object, state),
                Is.EqualTo("properties rename_busstop change_type labels --- copy duplicate level --- delete!"));
        }

        [Test]
        public void ThePathsEntryStandsBeforeTheDividerAndOnlyWhenThePathsAreRead()
        {
            var state = Editable();
            state.PathsGroupShown = true;

            Assert.That(Describe(ContextMenuKind.Object, state), Is.EqualTo("properties change_type labels paths --- copy duplicate level --- delete!"));
            Assert.That(Describe(ContextMenuKind.Spline, state), Is.EqualTo("properties spline_heights paths --- delete_spline!"));
        }

        [Test]
        public void ObjectsOfAVariantCanBeEditedButTheGroundCannotBeLevelled()
        {
            var state = new ContextMenuState { CanEditObjects = true };

            Assert.That(Describe(ContextMenuKind.Object, state), Is.EqualTo("properties change_type labels --- copy duplicate --- delete!"));
            Assert.That(Describe(ContextMenuKind.Selection, state), Is.EqualTo("properties --- copy duplicate randomize --- delete_many!"));
        }

        [Test]
        public void LevellingNeedsTheWholeMapToBeEditable()
        {
            foreach (var kind in new[] { ContextMenuKind.Selection, ContextMenuKind.Object })
            {
                var withEdit = ContextMenuEntries.For(kind, Editable());
                var withoutEdit = ContextMenuEntries.For(kind, new ContextMenuState { CanEditObjects = true });

                Assert.That(withEdit.Any(entry => entry.Action == ContextMenuAction.LevelGround), Is.True, kind.ToString());
                Assert.That(withoutEdit.Any(entry => entry.Action == ContextMenuAction.LevelGround), Is.False, kind.ToString());
            }
        }

        [Test]
        public void SeveralSelectedObjectsOfferCopyDuplicateLevelRandomizeAndDeleteAll()
        {
            var entries = ContextMenuEntries.For(ContextMenuKind.Selection, Editable());

            Assert.That(Describe(ContextMenuKind.Selection, Editable()), Is.EqualTo("properties --- copy duplicate level randomize --- delete_many!"));
            Assert.That(entries.Where(entry => entry.Type == ContextMenuEntryType.Item).Select(entry => entry.Shortcut),
                Is.EqualTo(new[] { null, "Ctrl+C", "Ctrl+D", null, null, "Del" }));
        }

        [Test]
        public void RandomizingHeadingsIsOnlyOfferedForSeveralObjectsAndNeedsEditableObjects()
        {
            foreach (var kind in new[] { ContextMenuKind.Object, ContextMenuKind.Spline, ContextMenuKind.Tile, ContextMenuKind.Row })
                Assert.That(ContextMenuEntries.For(kind, Editable()).Any(entry => entry.Action == ContextMenuAction.RandomizeHeadings), Is.False, kind.ToString());
            Assert.That(ContextMenuEntries.For(ContextMenuKind.Selection, new ContextMenuState()).Any(entry => entry.Action == ContextMenuAction.RandomizeHeadings), Is.False);
            var randomize = ContextMenuEntries.For(ContextMenuKind.Selection, new ContextMenuState { CanEditObjects = true }).Single(entry => entry.Action == ContextMenuAction.RandomizeHeadings);
            Assert.That(randomize.Danger, Is.False);
            Assert.That(randomize.DisabledReasonKey, Is.Null);
        }

        private static ContextMenuState MadeJunction()
        {
            var state = Editable();
            state.IsMadeJunction = true;
            state.HasPriorityArms = true;
            return state;
        }

        [Test]
        public void AJunctionTheEditorMadeOpensFourToolsOnItselfAfterADivider()
        {
            Assert.That(Describe(ContextMenuKind.Object, MadeJunction()),
                Is.EqualTo("properties change_type labels --- markings traffic_rules lanes traffic_lights --- copy duplicate level --- delete!"));
            var actions = ContextMenuEntries.For(ContextMenuKind.Object, MadeJunction()).Select(entry => entry.Action).ToList();
            Assert.That(actions, Does.Contain(ContextMenuAction.OpenMarkings));
            Assert.That(actions, Does.Contain(ContextMenuAction.OpenTrafficRules));
            Assert.That(actions, Does.Contain(ContextMenuAction.OpenLanes));
            Assert.That(actions, Does.Contain(ContextMenuAction.OpenTrafficLights));
        }

        [Test]
        public void TheToolsStandAfterThePathsEntry()
        {
            var state = MadeJunction();
            state.PathsGroupShown = true;

            Assert.That(Describe(ContextMenuKind.Object, state),
                Is.EqualTo("properties change_type labels paths --- markings traffic_rules lanes traffic_lights --- copy duplicate level --- delete!"));
        }

        [Test]
        public void OnlyTheTrafficRulesWorkOnAnyObjectWithArmsTheOthersAreGreyedWithTheirReason()
        {
            var state = Editable();
            state.HasPriorityArms = true;

            Assert.That(Describe(ContextMenuKind.Object, state),
                Is.EqualTo("properties change_type labels --- markings~ traffic_rules lanes~ traffic_lights~ --- copy duplicate level --- delete!"));
            var entries = ContextMenuEntries.For(ContextMenuKind.Object, state);
            Assert.That(entries.Single(entry => entry.Action == ContextMenuAction.OpenMarkings).DisabledReasonKey, Is.EqualTo(ContextMenuEntries.ReasonForeignJunction));
            Assert.That(entries.Single(entry => entry.Action == ContextMenuAction.OpenLanes).DisabledReasonKey, Is.EqualTo(ContextMenuEntries.ReasonForeignJunction));
            Assert.That(entries.Single(entry => entry.Action == ContextMenuAction.OpenTrafficLights).DisabledReasonKey, Is.EqualTo(ContextMenuEntries.ReasonFewArms),
                "one arm is no crossing for the lights");
            Assert.That(entries.Single(entry => entry.Action == ContextMenuAction.OpenTrafficRules).DisabledReasonKey, Is.Null);
        }

        [Test]
        public void ALightsToolOnAForeignCrossingOpensReadOnly()
        {
            var state = Editable();
            state.HasPriorityArms = true;
            state.HasSignalArms = true;

            Assert.That(Describe(ContextMenuKind.Object, state),
                Is.EqualTo("properties change_type labels --- markings~ traffic_rules lanes~ traffic_lights_view --- copy duplicate level --- delete!"));
            var lights = ContextMenuEntries.For(ContextMenuKind.Object, state).Single(entry => entry.Action == ContextMenuAction.OpenTrafficLights);
            Assert.That(lights.DisabledReasonKey, Is.Null, "it can be chosen: the window shows the crossing and changes nothing");
        }

        [Test]
        public void AForeignCrossingOfAMapThatCannotBeEditedGreysTheLightsToo()
        {
            var state = new ContextMenuState { CanEditObjects = true, HasPriorityArms = true, HasSignalArms = true };

            var lights = ContextMenuEntries.For(ContextMenuKind.Object, state).Single(entry => entry.Action == ContextMenuAction.OpenTrafficLights);
            Assert.That(lights.DisabledReasonKey, Is.EqualTo(ContextMenuEntries.ReasonReadOnlyMap));
        }

        [Test]
        public void AnObjectWithoutArmsAndNotMadeOffersNoTool()
        {
            Assert.That(Describe(ContextMenuKind.Object, Editable()), Does.Not.Contain("traffic_rules"));
            Assert.That(Describe(ContextMenuKind.Object, Editable()), Does.Not.Contain("markings"));
        }

        [Test]
        public void InAVariantOnlyTheTrafficRulesAreChoosable()
        {
            var state = MadeJunction();
            state.CanEdit = false;

            Assert.That(Describe(ContextMenuKind.Object, state),
                Is.EqualTo("properties change_type labels --- markings~ traffic_rules lanes~ traffic_lights~ --- copy duplicate --- delete!"));
            Assert.That(ContextMenuEntries.For(ContextMenuKind.Object, state).Single(entry => entry.Action == ContextMenuAction.OpenMarkings).DisabledReasonKey,
                Is.EqualTo(ContextMenuEntries.ReasonReadOnlyMap));
        }

        [Test]
        public void AReadOnlyMapOffersNoToolOnAJunction()
        {
            var state = new ContextMenuState { IsMadeJunction = true, HasPriorityArms = true, IsHeadOfMadeCrossing = true };

            Assert.That(Describe(ContextMenuKind.Object, state), Is.EqualTo("properties (note)"));
        }

        [Test]
        public void ASignalHeadOffersTheLightsOfItsCrossing()
        {
            var state = Editable();
            state.IsHeadOfMadeCrossing = true;

            Assert.That(Describe(ContextMenuKind.Object, state),
                Is.EqualTo("properties change_type labels --- edit_traffic_lights --- copy duplicate level --- delete!"));
            Assert.That(ContextMenuEntries.For(ContextMenuKind.Object, state).Any(entry => entry.Action == ContextMenuAction.OpenTrafficLightsOfHead), Is.True);
        }

        [Test]
        public void ARowOffersEditingAndADangerousDeleteWithoutAKey()
        {
            Assert.That(Describe(ContextMenuKind.Row, Editable()), Is.EqualTo("edit_row --- delete_row!"));
            var entries = ContextMenuEntries.For(ContextMenuKind.Row, Editable());
            Assert.That(entries[0].Action, Is.EqualTo(ContextMenuAction.EditRow));
            Assert.That(entries.Last().Action, Is.EqualTo(ContextMenuAction.DeleteRow));
            Assert.That(entries.Last().Shortcut, Is.Null, "Delete takes a row only in the rows tool");
        }

        [Test]
        public void ARowOfAReadOnlyMapOffersOnlyTheNote()
        {
            Assert.That(Describe(ContextMenuKind.Row, new ContextMenuState { CanEditObjects = true }), Is.EqualTo("(note)"));
        }

        [Test]
        public void ARowThatIsASignalOfARouteOffersTheRoutesEvenOnAReadOnlyMap()
        {
            var editable = Editable();
            editable.HasSignalRoutes = true;
            var entries = ContextMenuEntries.For(ContextMenuKind.Row, editable);
            Assert.That(entries.Count(entry => entry.Action == ContextMenuAction.OpenSignalRoutes), Is.EqualTo(1));
            Assert.That(entries.Last().Action, Is.EqualTo(ContextMenuAction.DeleteRow));

            var readOnly = new ContextMenuState { CanEditObjects = true, HasSignalRoutes = true };
            Assert.That(ContextMenuEntries.For(ContextMenuKind.Row, readOnly).Count(entry => entry.Action == ContextMenuAction.OpenSignalRoutes), Is.EqualTo(1));
            Assert.That(Describe(ContextMenuKind.Row, Editable()), Does.Not.Contain("signal_routes"));
        }

        [Test]
        public void ASignalOnAnEditableMapOffersANewRouteAndOtherElementsDoNot()
        {
            var signal = Editable();
            signal.IsSignal = true;
            foreach (var kind in new[] { ContextMenuKind.Object, ContextMenuKind.Row })
            {
                Assert.That(ContextMenuEntries.For(kind, signal).Count(entry => entry.Action == ContextMenuAction.NewSignalRoute), Is.EqualTo(1), kind.ToString());
                Assert.That(ContextMenuEntries.For(kind, Editable()).Count(entry => entry.Action == ContextMenuAction.NewSignalRoute), Is.EqualTo(0), kind.ToString());
            }

            // Creating a route writes signalroutes.cfg: a map that cannot be edited offers nothing of the kind.
            var readOnly = new ContextMenuState { CanEditObjects = true, IsSignal = true };
            foreach (var kind in new[] { ContextMenuKind.Object, ContextMenuKind.Row })
                Assert.That(ContextMenuEntries.For(kind, readOnly).Count(entry => entry.Action == ContextMenuAction.NewSignalRoute), Is.EqualTo(0), kind.ToString());
        }

        [Test]
        public void TheFocusMovesOverActiveEntriesAndSkipsTheOthers()
        {
            var active = new[] { true, false, true, true, false };

            Assert.That(ContextMenuNavigation.Next(active, -1, 1), Is.EqualTo(0), "down without a focus: the first");
            Assert.That(ContextMenuNavigation.Next(active, 0, 1), Is.EqualTo(2), "a disabled entry is skipped");
            Assert.That(ContextMenuNavigation.Next(active, 2, 1), Is.EqualTo(3));
            Assert.That(ContextMenuNavigation.Next(active, 3, 1), Is.EqualTo(0), "wraps round at the end");
            Assert.That(ContextMenuNavigation.Next(active, -1, -1), Is.EqualTo(3), "up without a focus: the last active");
            Assert.That(ContextMenuNavigation.Next(active, 0, -1), Is.EqualTo(3), "wraps round at the start");
            Assert.That(ContextMenuNavigation.Next(active, 3, -1), Is.EqualTo(2));
        }

        [Test]
        public void TheFocusStaysOnALoneActiveEntryAndIsNoneWithoutAny()
        {
            Assert.That(ContextMenuNavigation.Next(new[] { false, true, false }, 1, 1), Is.EqualTo(1));
            Assert.That(ContextMenuNavigation.Next(new[] { false, true, false }, 1, -1), Is.EqualTo(1));
            Assert.That(ContextMenuNavigation.Next(new[] { false, false }, -1, 1), Is.EqualTo(-1));
            Assert.That(ContextMenuNavigation.Next(new bool[0], -1, 1), Is.EqualTo(-1));
        }

        [Test]
        public void AFocusPastTheEndCountsAsNone()
        {
            Assert.That(ContextMenuNavigation.Next(new[] { true, true }, 7, 1), Is.EqualTo(0));
        }

        [Test]
        public void ASplineOfAnEditableMapOffersHeightsAndDelete()
        {
            Assert.That(Describe(ContextMenuKind.Spline, Editable()), Is.EqualTo("properties spline_heights --- delete_spline!"));
        }

        [Test]
        public void ASplineInAVariantOffersChangeTypeAndTheVariantsDelete()
        {
            var state = new ContextMenuState { CanEditObjects = true, CanEditSplinesInVariant = true };

            Assert.That(Describe(ContextMenuKind.Spline, state), Is.EqualTo("properties --- change_spline_type delete_spline_variant!"));
        }

        [Test]
        public void TheWholeMapsRightsWinOverTheVariantsForASpline()
        {
            var state = Editable();
            state.CanEditSplinesInVariant = true;

            Assert.That(Describe(ContextMenuKind.Spline, state), Is.EqualTo("properties spline_heights --- delete_spline!"));
        }

        [Test]
        public void PasteIsOfferedOnlyWithAFilledClipboard()
        {
            var empty = Editable();
            var filled = Editable();
            filled.ClipboardFilled = true;

            Assert.That(Describe(ContextMenuKind.Tile, empty), Is.EqualTo("tile_properties"));
            Assert.That(Describe(ContextMenuKind.Tile, filled), Is.EqualTo("tile_properties paste"));
            Assert.That(ContextMenuEntries.For(ContextMenuKind.Tile, filled).Last().Shortcut, Is.EqualTo("Ctrl+V"));
        }

        [Test]
        public void RevertingATileIsOfferedOnlyForAnEditedTileAndAfterADivider()
        {
            var state = Editable();
            state.ClipboardFilled = true;
            state.TileRevertible = true;

            Assert.That(Describe(ContextMenuKind.Tile, state), Is.EqualTo("tile_properties paste --- revert_tile"));
            state.TileRevertible = false;
            Assert.That(Describe(ContextMenuKind.Tile, state), Is.EqualTo("tile_properties paste"));
        }

        [Test]
        public void ARevertableTileOfAReadOnlyMapOffersNoRevert()
        {
            var state = new ContextMenuState { TileRevertible = true, ClipboardFilled = true };

            Assert.That(Describe(ContextMenuKind.Tile, state), Is.EqualTo("tile_properties (note)"));
        }

        [Test]
        public void ForEveryCombinationTheMenuIsWellFormed()
        {
            foreach (var kind in (ContextMenuKind[])Enum.GetValues(typeof(ContextMenuKind)))
            {
                foreach (var state in AllStates())
                {
                    var entries = ContextMenuEntries.For(kind, state);
                    var where = kind + " " + Describe(kind, state);

                    Assert.That(entries.Count, Is.GreaterThan(0), where);
                    // A row has no properties: it offers editing, or only the note on a map that is read-only.
                    var firstOfRow = kind == ContextMenuKind.Row;
                    Assert.That(entries[0].Type, Is.EqualTo(firstOfRow && !state.CanEdit ? ContextMenuEntryType.ReadOnlyNote : ContextMenuEntryType.Item), where);
                    if (!firstOfRow)
                        Assert.That(entries[0].Action == ContextMenuAction.OpenProperties || entries[0].Action == ContextMenuAction.OpenTileProperties, Is.True, where);
                    for (var index = 0; index < entries.Count; index++)
                    {
                        if (entries[index].Type != ContextMenuEntryType.Divider)
                            continue;
                        Assert.That(index, Is.GreaterThan(0), "no divider first: " + where);
                        Assert.That(index, Is.LessThan(entries.Count - 1), "no divider last: " + where);
                        Assert.That(entries[index + 1].Type, Is.Not.EqualTo(ContextMenuEntryType.Divider), "no two dividers: " + where);
                    }
                    var dangerous = entries.Where(entry => entry.Danger).ToList();
                    Assert.That(dangerous.Count, Is.LessThanOrEqualTo(1), where);
                    if (dangerous.Count == 1)
                        Assert.That(entries.Last().Danger, Is.True, "the dangerous entry is last: " + where);
                    var notes = entries.Count(entry => entry.Type == ContextMenuEntryType.ReadOnlyNote);
                    Assert.That(notes, Is.LessThanOrEqualTo(1), where);
                    if (notes == 1)
                        Assert.That(entries.Last().Type, Is.EqualTo(ContextMenuEntryType.ReadOnlyNote), "the note is last: " + where);
                    Assert.That(entries.Where(entry => entry.Type == ContextMenuEntryType.Item).All(entry => entry.TextKey != null && entry.Action != ContextMenuAction.None), Is.True, where);
                    Assert.That(entries.Where(entry => entry.DisabledReasonKey != null).All(entry => entry.Type == ContextMenuEntryType.Item && !entry.Danger), Is.True,
                        "only an entry can be greyed, and not a dangerous one: " + where);
                    Assert.That(firstOfRow && !state.CanEdit || entries.Any(entry => entry.Type == ContextMenuEntryType.Item && entry.DisabledReasonKey == null), Is.True, "something can be chosen: " + where);
                }
            }
        }

        [Test]
        public void ANoteAndADangerousEntryNeverMeet()
        {
            foreach (var kind in (ContextMenuKind[])Enum.GetValues(typeof(ContextMenuKind)))
                foreach (var state in AllStates())
                {
                    var entries = ContextMenuEntries.For(kind, state);
                    Assert.That(entries.Any(entry => entry.Type == ContextMenuEntryType.ReadOnlyNote) && entries.Any(entry => entry.Danger), Is.False,
                        kind + " " + Describe(kind, state));
                }
        }

        [Test]
        public void EveryTextKeyOfTheMenuIsInTheEnglishTexts()
        {
            var english = LoadMessages("en-US");
            var keys = new HashSet<string> { "context.readonly" };
            foreach (var kind in (ContextMenuKind[])Enum.GetValues(typeof(ContextMenuKind)))
                foreach (var state in AllStates())
                    foreach (var entry in ContextMenuEntries.For(kind, state))
                    {
                        if (entry.TextKey != null)
                            keys.Add(entry.TextKey);
                        if (entry.DisabledReasonKey != null)
                            keys.Add(entry.DisabledReasonKey);
                    }

            Assert.That(keys.Count, Is.GreaterThan(20));
            foreach (var key in keys)
                Assert.That(english.ContainsKey(key), Is.True, key + " is not in locales/en-US");
        }

        internal static Dictionary<string, string> LoadMessages(string code)
        {
            var root = TooltipTextTests.LocalesFolder();
            var info = LocaleLoader.List(root, null).Find(item => item.Code == code);
            Assert.That(info, Is.Not.Null, code + " is not listed under " + root);
            return LocaleLoader.Load(info).Messages;
        }
    }
}
