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

        /// <summary>The entries as one readable line: keys, "---" for a divider, "!" after a dangerous entry, "(note)" for the read-only note.</summary>
        private static string Describe(ContextMenuKind kind, ContextMenuState state)
        {
            return string.Join(" ", ContextMenuEntries.For(kind, state).Select(entry =>
            {
                switch (entry.Type)
                {
                    case ContextMenuEntryType.Divider: return "---";
                    case ContextMenuEntryType.ReadOnlyNote: return "(note)";
                    default: return entry.TextKey.Substring("context.".Length) + (entry.Danger ? "!" : string.Empty);
                }
            }));
        }

        private static IEnumerable<ContextMenuState> AllStates()
        {
            for (var bits = 0; bits < 256; bits++)
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
            Assert.That(Describe(ContextMenuKind.Selection, state), Is.EqualTo("properties --- copy duplicate --- delete_many!"));
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
        public void SeveralSelectedObjectsOfferCopyDuplicateLevelAndDeleteAll()
        {
            var entries = ContextMenuEntries.For(ContextMenuKind.Selection, Editable());

            Assert.That(Describe(ContextMenuKind.Selection, Editable()), Is.EqualTo("properties --- copy duplicate level --- delete_many!"));
            Assert.That(entries.Where(entry => entry.Type == ContextMenuEntryType.Item).Select(entry => entry.Shortcut),
                Is.EqualTo(new[] { null, "Ctrl+C", "Ctrl+D", null, "Del" }));
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
                    Assert.That(entries[0].Type, Is.EqualTo(ContextMenuEntryType.Item), where);
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
                        if (entry.TextKey != null)
                            keys.Add(entry.TextKey);

            Assert.That(keys.Count, Is.GreaterThan(15));
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
