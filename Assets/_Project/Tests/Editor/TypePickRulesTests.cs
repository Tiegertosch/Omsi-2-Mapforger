using System;
using System.Linq;
using NUnit.Framework;
using OmsiMapEditor.UI;

namespace OmsiMapEditor.Tests
{
    /// <summary>When choosing a new object file for an element ends by itself (ADR 060, M4 "Labels"): <see cref="TypePickRules"/>.</summary>
    public sealed class TypePickRulesTests
    {
        private static readonly object View = new object();

        private static TypePickContext Context(EditorTool tool = EditorTool.Drag, string selection = "0,0:1;", object view = null, bool window = false)
        {
            return new TypePickContext(tool, selection, view ?? View, window);
        }

        [Test]
        public void TheSameSituationDoesNotEndThePick()
        {
            Assert.That(TypePickRules.Evaluate(Context(), Context()), Is.EqualTo(TypePickEnd.None));
        }

        [Test]
        public void EveryOtherToolEndsThePickAndTheSameToolDoesNot()
        {
            foreach (var start in EditorToolRules.All)
                foreach (var now in EditorToolRules.All)
                    Assert.That(TypePickRules.Evaluate(Context(start), Context(now)),
                        start == now ? Is.EqualTo(TypePickEnd.None) : Is.EqualTo(TypePickEnd.ToolChanged), start + " -> " + now);
        }

        [Test]
        public void AnotherSelectionEndsThePick()
        {
            Assert.That(TypePickRules.Evaluate(Context(selection: "0,0:1;"), Context(selection: "0,0:2;")), Is.EqualTo(TypePickEnd.SelectionChanged));
            Assert.That(TypePickRules.Evaluate(Context(selection: "0,0:1;"), Context(selection: "0,0:1;0,0:2;")), Is.EqualTo(TypePickEnd.SelectionChanged));
            Assert.That(TypePickRules.Evaluate(Context(selection: "0,0:1;"), Context(selection: string.Empty)), Is.EqualTo(TypePickEnd.SelectionChanged));
            Assert.That(TypePickRules.Evaluate(Context(selection: "0,0:1;"), Context(selection: null)), Is.EqualTo(TypePickEnd.SelectionChanged));
        }

        [Test]
        public void TheSameSelectionDrawnAgainDoesNotEndThePick()
        {
            Assert.That(TypePickRules.Evaluate(Context(selection: "3,4:7;"), Context(selection: "3,4:7;")), Is.EqualTo(TypePickEnd.None));
        }

        [Test]
        public void AnotherMapOrChronoViewEndsThePick()
        {
            Assert.That(TypePickRules.Evaluate(Context(), Context(view: new object())), Is.EqualTo(TypePickEnd.ViewChanged));
        }

        [Test]
        public void AWindowThatOpensEndsThePickButOneThatWasOpenAlreadyDoesNot()
        {
            Assert.That(TypePickRules.Evaluate(Context(), Context(window: true)), Is.EqualTo(TypePickEnd.WindowOpened));
            Assert.That(TypePickRules.Evaluate(Context(window: true), Context(window: true)), Is.EqualTo(TypePickEnd.None));
            Assert.That(TypePickRules.Evaluate(Context(window: true), Context(window: false)), Is.EqualTo(TypePickEnd.None));
        }

        [Test]
        public void TheToolComesBeforeTheOtherReasons()
        {
            var now = Context(EditorTool.Road, "0,0:2;", new object(), true);

            Assert.That(TypePickRules.Evaluate(Context(), now), Is.EqualTo(TypePickEnd.ToolChanged));
        }

        [Test]
        public void TheSelectionKeyNamesTileAndIdInOrder()
        {
            Assert.That(TypePickRules.SelectionKey(new[] { (1, 2, 3), (4, 5, -6) }), Is.EqualTo("1,2:3;4,5:-6;"));
            Assert.That(TypePickRules.SelectionKey(null), Is.EqualTo(string.Empty));
            Assert.That(TypePickRules.SelectionKey(new[] { (1, 2, 3), (4, 5, 6) }), Is.Not.EqualTo(TypePickRules.SelectionKey(new[] { (4, 5, 6), (1, 2, 3) })));
        }

        [Test]
        public void ThePickBeginsOnlyWithTheToolsThatHaveNoWindowInThePlaceOfTheLibrary()
        {
            // The tool rules and the pick rule agree: the tools that work on the selection have no window of their own, every other tool
            // is left for Drag first, and Drag may always be chosen.
            foreach (var tool in EditorToolRules.All)
            {
                Assert.That(TypePickRules.CanStartIn(tool), Is.EqualTo(EditorToolRules.IsSelectionTool(tool)), tool.ToString());
                Assert.That(TypePickRules.CanStartIn(tool), Is.EqualTo(!EditorToolRules.KeyTogglesWithDrag(tool)), tool.ToString());
            }
            Assert.That(EditorToolRules.IsAllowed(EditorTool.Drag, false, false), Is.True);
            Assert.That(TypePickRules.CanStartIn(EditorTool.Drag), Is.True);
        }

        [Test]
        public void LeavingForDragBeforeThePickBeginsNeverEndsThePickLater()
        {
            // The pick starts in Drag (after the tool was left) or in a tool that may start it; in either one it ends only with a change of tool.
            foreach (var tool in EditorToolRules.All.Where(TypePickRules.CanStartIn))
                Assert.That(TypePickRules.Evaluate(Context(tool), Context(tool)), Is.EqualTo(TypePickEnd.None), tool.ToString());
            foreach (var tool in EditorToolRules.All.Where(tool => !TypePickRules.CanStartIn(tool)))
            {
                Assert.That(TypePickRules.Evaluate(Context(EditorTool.Drag), Context(EditorTool.Drag)), Is.EqualTo(TypePickEnd.None), tool.ToString());
                Assert.That(TypePickRules.Evaluate(Context(EditorTool.Drag), Context(tool)), Is.EqualTo(TypePickEnd.ToolChanged), tool.ToString());
            }
        }

        [Test]
        public void EveryReasonHasAWordingForTheLog()
        {
            foreach (var reason in Enum.GetValues(typeof(TypePickEnd)).Cast<TypePickEnd>().Where(reason => reason != TypePickEnd.None))
                Assert.That(TypePickRules.Describe(reason), Is.Not.EqualTo("none"), reason.ToString());
        }
    }
}
