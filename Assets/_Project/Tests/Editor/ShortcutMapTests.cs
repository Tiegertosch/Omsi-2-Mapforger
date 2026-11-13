using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OmsiMapEditor.UI;
using UnityEngine;

namespace OmsiMapEditor.Tests
{
    /// <summary>
    /// The rules of the editor's tools that need no shell (ADR 098, M3): the key of each tool, who may choose it, how it begins and
    /// ends, and which tool takes a click in the map's road mode.
    /// </summary>
    public sealed class ShortcutMapTests
    {
        private static EditorTool[] Tools()
        {
            return (EditorTool[])Enum.GetValues(typeof(EditorTool));
        }

        private static HashSet<EditorTool> Set(params EditorTool[] tools)
        {
            return new HashSet<EditorTool>(tools);
        }

        [Test]
        public void TheListHoldsEveryToolOfTheEnumOnceInTheOrderOfTheEnum()
        {
            Assert.That(EditorToolRules.All, Is.EqualTo(Tools()));
            Assert.That(EditorToolRules.All.Distinct().Count(), Is.EqualTo(EditorToolRules.All.Length));
            for (var index = 0; index < EditorToolRules.All.Length; index++)
                Assert.That((int)EditorToolRules.All[index], Is.EqualTo(index), "the table is indexed by the enum value");
        }

        [Test]
        public void EveryToolHasAKeyAndNoTwoToolsShareOne()
        {
            var keys = Tools().Select(EditorToolRules.KeyOf).ToList();

            Assert.That(keys, Has.None.EqualTo(KeyCode.None));
            Assert.That(keys.Distinct().Count(), Is.EqualTo(keys.Count));
        }

        [Test]
        public void EachKeyChoosesItsToolAndOnlyThat()
        {
            foreach (var tool in Tools())
            {
                Assert.That(EditorToolRules.TryGetToolOfKey(EditorToolRules.KeyOf(tool), out var found), Is.True, tool.ToString());
                Assert.That(found, Is.EqualTo(tool));
            }
        }

        [TestCase(KeyCode.None)]
        [TestCase(KeyCode.Escape)]
        [TestCase(KeyCode.Delete)]
        [TestCase(KeyCode.C)]
        [TestCase(KeyCode.D)]
        [TestCase(KeyCode.Y)]
        [TestCase(KeyCode.Z)]
        [TestCase(KeyCode.Alpha1)]
        [TestCase(KeyCode.Alpha3)]
        [TestCase(KeyCode.Keypad3)]
        [TestCase(KeyCode.Return)]
        [TestCase(KeyCode.Space)]
        public void KeysThatChooseNoToolAreRefused(KeyCode key)
        {
            Assert.That(EditorToolRules.TryGetToolOfKey(key, out var tool), Is.False);
            Assert.That(tool, Is.EqualTo(EditorTool.Drag), "the out value is the harmless Drag");
        }

        [Test]
        public void TheKeysOfTheShortcutsOutsideTheToolsAreNotToolKeys()
        {
            // C, D, Z and Y carry Ctrl shortcuts (copy, duplicate, undo, redo) and Y the marking style; Delete and Esc have their own rules.
            var used = Tools().Select(EditorToolRules.KeyOf).ToList();

            Assert.That(used, Has.None.EqualTo(KeyCode.C));
            Assert.That(used, Has.None.EqualTo(KeyCode.D));
            Assert.That(used, Has.None.EqualTo(KeyCode.Y));
            Assert.That(used, Has.None.EqualTo(KeyCode.Z));
            Assert.That(used, Has.None.EqualTo(KeyCode.Delete));
            Assert.That(used, Has.None.EqualTo(KeyCode.Escape));
        }

        [Test]
        public void QWAndEAlwaysChooseTheirToolAndAllOthersToggleWithDrag()
        {
            Assert.That(EditorToolRules.KeyOf(EditorTool.Drag), Is.EqualTo(KeyCode.Q));
            Assert.That(EditorToolRules.KeyOf(EditorTool.Move), Is.EqualTo(KeyCode.W));
            Assert.That(EditorToolRules.KeyOf(EditorTool.Rotate), Is.EqualTo(KeyCode.E));
            foreach (var tool in Tools())
                Assert.That(EditorToolRules.KeyTogglesWithDrag(tool), Is.EqualTo(!Set(EditorTool.Drag, EditorTool.Move, EditorTool.Rotate).Contains(tool)), tool.ToString());
        }

        [Test]
        public void OnlyDragMoveAndRotateAreSelectionTools()
        {
            foreach (var tool in Tools())
                Assert.That(EditorToolRules.IsSelectionTool(tool), Is.EqualTo(Set(EditorTool.Drag, EditorTool.Move, EditorTool.Rotate).Contains(tool)), tool.ToString());
        }

        [Test]
        public void DragIsAlwaysAllowedAndTheOthersNeedTheirPermission()
        {
            var objectsOnly = Set(EditorTool.Move, EditorTool.Rotate, EditorTool.Priorities, EditorTool.Road);
            foreach (var tool in Tools())
            {
                Assert.That(EditorToolRules.IsAllowed(tool, false, false), Is.EqualTo(tool == EditorTool.Drag), tool + " on a read-only map");
                Assert.That(EditorToolRules.IsAllowed(tool, true, true), Is.True, tool + " on an editable map");
                // Objects of a variant can be edited without the whole map: only the tools of objects (and roads) work there.
                Assert.That(EditorToolRules.IsAllowed(tool, false, true), Is.EqualTo(tool == EditorTool.Drag || objectsOnly.Contains(tool)), tool + " with objects only");
                Assert.That(EditorToolRules.IsAllowed(tool, true, false), Is.EqualTo(tool == EditorTool.Drag || !objectsOnly.Contains(tool)), tool + " with the map but not objects");
            }
        }

        [Test]
        public void ReadOnlyKeysExistForEveryToolButDragAndAreInTheEnglishTexts()
        {
            var english = ContextMenuEntriesTests.LoadMessages("en-US");
            foreach (var tool in Tools())
            {
                var key = EditorToolRules.ReadOnlyKey(tool);
                if (tool == EditorTool.Drag)
                {
                    Assert.That(key, Is.Null);
                    continue;
                }
                Assert.That(key, Is.Not.Null.And.Not.Empty, tool.ToString());
                Assert.That(english.ContainsKey(key), Is.True, tool + ": " + key + " is not in locales/en-US");
            }
            Assert.That(Tools().Where(tool => tool != EditorTool.Drag).Select(EditorToolRules.ReadOnlyKey).Distinct().Count(), Is.EqualTo(Tools().Length - 2),
                "Move and Rotate share one message, every other tool has its own");
        }

        [Test]
        public void OnlyRoadRowsAndTerrainBeginAgainWhenChosenWhileOn()
        {
            foreach (var tool in Tools())
                Assert.That(EditorToolRules.BeginsAgain(tool), Is.EqualTo(Set(EditorTool.Road, EditorTool.Rows, EditorTool.Terrain).Contains(tool)), tool.ToString());
        }

        [Test]
        public void TheToolsThatHoldRecordsOfTheSummaryEndOnEveryView()
        {
            var ending = Set(EditorTool.Road, EditorTool.Markings, EditorTool.Priorities, EditorTool.Signals, EditorTool.Lanes);
            foreach (var tool in Tools())
                Assert.That(EditorToolRules.EndsOnEveryView(tool), Is.EqualTo(ending.Contains(tool)), tool.ToString());
            Assert.That(EditorToolRules.EndsOnEveryView(EditorTool.Drag), Is.False, "Drag has nothing to end");
        }

        [Test]
        public void ADragMoveOrRotateClickInRoadModeIsTakenByNoToolAndBuildsNothing()
        {
            foreach (var tool in new[] { EditorTool.Drag, EditorTool.Move, EditorTool.Rotate })
            {
                Assert.That(EditorToolRules.TakesRoadModeClicks(tool), Is.False, tool.ToString());
                Assert.That(EditorToolRules.BuildsRoadOnClick(tool), Is.False, tool.ToString());
            }
        }

        [Test]
        public void EveryToolWithAModeTakesRoadModeClicksButOnlyTheRoadToolBuildsARoad()
        {
            foreach (var tool in Tools().Where(candidate => !EditorToolRules.IsSelectionTool(candidate)))
                Assert.That(EditorToolRules.TakesRoadModeClicks(tool), Is.True, tool.ToString());
            Assert.That(Tools().Where(EditorToolRules.BuildsRoadOnClick).ToList(), Is.EqualTo(new[] { EditorTool.Road }));
            Assert.That(EditorToolRules.TakesRoadModeClicks(EditorTool.Road), Is.True, "a road click needs the road tool to take it");
        }

        [Test]
        public void EveryRoadModeClickHasExactlyOneOutcomeForEachOfTheElevenTools()
        {
            Assert.That(Tools().Length, Is.EqualTo(11));
            foreach (var tool in Tools())
            {
                var takes = EditorToolRules.TakesRoadModeClicks(tool);
                var builds = EditorToolRules.BuildsRoadOnClick(tool);
                // A road is built only by a tool that takes the click, and no tool outside the road tool builds one.
                Assert.That(!builds || takes, Is.True, tool.ToString());
                Assert.That(builds, Is.EqualTo(tool == EditorTool.Road), tool.ToString());
            }
        }
    }
}
