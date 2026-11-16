using System.Linq;
using NUnit.Framework;
using OmsiMapEditor.OMSI;
using OmsiMapEditor.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.Tests
{
    /// <summary>The signals window (ADR 098, signal window): its frame, the pure rules for what it shows, the colours of the groups, the question about unapplied changes, and the place of a floating window.</summary>
    public sealed class SignalWindowTests
    {
        // ----- place and size of a floating window -----

        [Test]
        public void AWindowInsideTheAreaStaysWhereItIs()
        {
            Assert.AreEqual(new Vector2(100f, 50f), FloatingWindowRules.Clamp(new Vector2(100f, 50f), new Vector2(300f, 200f), new Vector2(1000f, 700f)));
        }

        [Test]
        public void AWindowPutBeyondTheAreaIsPulledBackInside()
        {
            var size = new Vector2(300f, 200f);
            var area = new Vector2(1000f, 700f);
            Assert.AreEqual(new Vector2(700f, 500f), FloatingWindowRules.Clamp(new Vector2(900f, 650f), size, area));
            Assert.AreEqual(new Vector2(0f, 0f), FloatingWindowRules.Clamp(new Vector2(-40f, -9f), size, area));
        }

        [Test]
        public void ASmallerAreaAfterAChangeOfResolutionKeepsTheWindowReachable()
        {
            // The window stood at 1200, 600 on a 1920 x 1080 screen; the screen is now 1100 x 700: the window moves to the right and bottom edge.
            var place = FloatingWindowRules.Clamp(new Vector2(1200f, 600f), new Vector2(780f, 640f), new Vector2(1100f, 700f));
            Assert.AreEqual(new Vector2(320f, 60f), place);
        }

        [Test]
        public void AWindowLargerThanTheAreaStandsAtItsTopLeft()
        {
            Assert.AreEqual(Vector2.zero, FloatingWindowRules.Clamp(new Vector2(50f, 50f), new Vector2(900f, 800f), new Vector2(800f, 600f)));
        }

        [Test]
        public void TheSizeStaysWithinTheLeastAndTheAreaAndTheLeastGivesWay()
        {
            var least = new Vector2(560f, 400f);
            Assert.AreEqual(new Vector2(560f, 400f), FloatingWindowRules.ClampSize(new Vector2(100f, 100f), least, new Vector2(1000f, 800f)));
            Assert.AreEqual(new Vector2(1000f, 800f), FloatingWindowRules.ClampSize(new Vector2(3000f, 3000f), least, new Vector2(1000f, 800f)));
            Assert.AreEqual(new Vector2(500f, 300f), FloatingWindowRules.ClampSize(new Vector2(700f, 700f), least, new Vector2(500f, 300f)));
        }

        [Test]
        public void TheFirstPlaceIsAtTheRightEdgeWithAMarginAndStaysInside()
        {
            Assert.AreEqual(new Vector2(1000f - 780f - 8f, 8f), FloatingWindowRules.FirstPlace(new Vector2(780f, 640f), new Vector2(1000f, 700f), 8f));
            Assert.AreEqual(Vector2.zero, FloatingWindowRules.FirstPlace(new Vector2(780f, 640f), new Vector2(600f, 500f), 8f));
        }

        // ----- colours of the groups -----

        [Test]
        public void NoGroupColourIsRedYellowOrGreen()
        {
            // Red, yellow and green belong to the states of a light: hues around the red (above 340 and below 20), from 20 to 165 degrees (orange, yellow, green).
            for (var slot = 0; slot < SignalGroupIdentities.PaletteSize; slot++)
            {
                var colour = SignalGroupIdentities.ColourOfSlot(slot);
                Color.RGBToHSV(colour, out var hue, out var saturation, out _);
                var degrees = hue * 360f;
                var inLightBand = degrees < 175f || degrees >= 340f;
                Assert.IsFalse(inLightBand && saturation > 0.25f, "slot " + slot + " has the hue " + degrees + " and the saturation " + saturation);
            }
        }

        [Test]
        public void TheGroupColoursDifferFromEachOther()
        {
            var colours = Enumerable.Range(0, SignalGroupIdentities.PaletteSize).Select(SignalGroupIdentities.ColourOfSlot).ToList();
            Assert.AreEqual(colours.Count, colours.Distinct().Count());
        }

        [Test]
        public void AColourStaysWithItsGroupWhenAnotherIsDeleted()
        {
            var identities = new SignalGroupIdentities();
            identities.Reset(4);
            var kept = new[] { identities.ColourOf(0), identities.ColourOf(2), identities.ColourOf(3) };
            identities.Removed(1);
            Assert.AreEqual(3, identities.Count);
            Assert.AreEqual(kept[0], identities.ColourOf(0));
            Assert.AreEqual(kept[1], identities.ColourOf(1));
            Assert.AreEqual(kept[2], identities.ColourOf(2));
        }

        [Test]
        public void ANewGroupTakesTheLowestColourNoGroupHasSoADeletedOneComesBack()
        {
            var identities = new SignalGroupIdentities();
            identities.Reset(3);
            var second = identities.ColourOf(1);
            identities.Removed(1);
            identities.Added();
            Assert.AreEqual(second, identities.ColourOf(2));
            Assert.AreEqual(3, identities.Count);
            // Beyond the palette the colours repeat, and no id is handed out twice.
            identities.Reset(0);
            var ids = Enumerable.Range(0, 12).Select(_ => identities.Added()).ToList();
            Assert.AreEqual(12, ids.Distinct().Count());
        }

        [Test]
        public void AColourStaysWithItsGroupWhenTheGroupMovesInTheList()
        {
            var identities = new SignalGroupIdentities();
            identities.Reset(3);
            var first = identities.ColourOf(0);
            identities.Moved(0, 2);
            Assert.AreEqual(first, identities.ColourOf(2));
            Assert.AreEqual(SignalGroupIdentities.ColourOfSlot(1), identities.ColourOf(0));
        }

        [Test]
        public void ARenameDoesNotTouchTheIdentities()
        {
            // The colour is looked up by the id of the group; names are not part of it.
            var identities = new SignalGroupIdentities();
            identities.Reset(2);
            var before = identities.ColourOf(1);
            var program = new OmsiSignalProgram();
            program.AddLight("A");
            program.AddLight("B");
            program.Lights[1].Name = "Renamed";
            identities.Sync(program.Lights.Count);
            Assert.AreEqual(before, identities.ColourOf(1));
        }

        [Test]
        public void AProgramThatChangedByItselfStartsTheIdsAgain()
        {
            var identities = new SignalGroupIdentities();
            identities.Reset(4);
            identities.Removed(0);
            identities.Sync(5);
            Assert.AreEqual(5, identities.Count);
            Assert.AreEqual(SignalGroupIdentities.ColourOfSlot(4), identities.ColourOf(4));
        }

        // ----- changes not applied -----

        [Test]
        public void LeavingWithoutChangesNeedsNoQuestion()
        {
            var guard = new SignalLeaveGuard();
            Assert.IsTrue(guard.Request(false, new SignalExit { Kind = SignalExitKind.OtherTool }));
            Assert.IsFalse(guard.Asking);
        }

        [Test]
        public void LeavingWithChangesAsksAndWaits()
        {
            var guard = new SignalLeaveGuard();
            var exit = new SignalExit { Kind = SignalExitKind.OtherCrossing, Payload = 7 };
            Assert.IsFalse(guard.Request(true, exit));
            Assert.IsTrue(guard.Asking);
            Assert.AreSame(exit, guard.Pending);
        }

        [Test]
        public void KeepEditingDropsTheWayOutAndNothingHappens()
        {
            var guard = new SignalLeaveGuard();
            guard.Request(true, new SignalExit { Kind = SignalExitKind.OtherTool });
            Assert.IsNull(guard.Answer(SignalPromptChoice.KeepEditing, true));
            Assert.IsFalse(guard.Asking);
        }

        [Test]
        public void ApplyOrDiscardCarryOutTheWayOutOnceTheDraftIsClean()
        {
            foreach (var choice in new[] { SignalPromptChoice.Apply, SignalPromptChoice.Discard })
            {
                var guard = new SignalLeaveGuard();
                var exit = new SignalExit { Kind = SignalExitKind.OtherTool };
                guard.Request(true, exit);
                Assert.AreSame(exit, guard.Answer(choice, false), choice.ToString());
                Assert.IsFalse(guard.Asking);
            }
        }

        [Test]
        public void AnApplyThatFailedKeepsTheQuestionOpenAndTheWayOutWaiting()
        {
            var guard = new SignalLeaveGuard();
            var exit = new SignalExit { Kind = SignalExitKind.OtherTool };
            guard.Request(true, exit);
            Assert.IsNull(guard.Answer(SignalPromptChoice.Apply, true));
            Assert.IsTrue(guard.Asking);
            Assert.AreSame(exit, guard.Pending);
        }

        [Test]
        public void AnAnswerWithoutAQuestionDoesNothing()
        {
            Assert.IsNull(new SignalLeaveGuard().Answer(SignalPromptChoice.Discard, false));
        }

        // ----- a change of view with changes not applied -----

        [Test]
        public void ChangingTheViewWithChangesAsksAndWaitsWithTheActionThatShowsTheView()
        {
            var guard = new SignalLeaveGuard();
            var shown = false;
            var exit = new SignalExit { Kind = SignalExitKind.ViewChange, Payload = (System.Action)(() => shown = true) };
            Assert.IsFalse(guard.Request(true, exit));
            Assert.IsTrue(guard.Asking);
            Assert.IsFalse(shown, "nothing is shown before the answer");
            var waiting = guard.Answer(SignalPromptChoice.Discard, false);
            Assert.AreSame(exit, waiting);
            ((System.Action)waiting.Payload)();
            Assert.IsTrue(shown);
        }

        [Test]
        public void ChangingTheViewWithoutChangesAsksNothing()
        {
            var guard = new SignalLeaveGuard();
            Assert.IsTrue(guard.Request(false, new SignalExit { Kind = SignalExitKind.ViewChange }));
            Assert.IsFalse(guard.Asking);
        }

        [Test]
        public void KeepEditingCancelsTheChangeOfViewAndLetsItRestoreWhatItAlreadyShowed()
        {
            var guard = new SignalLeaveGuard();
            var restored = false;
            var exit = new SignalExit { Kind = SignalExitKind.ViewChange, Cancelled = () => restored = true };
            guard.Request(true, exit);
            var waiting = guard.Pending;
            Assert.IsNull(guard.Answer(SignalPromptChoice.KeepEditing, true));
            Assert.IsFalse(guard.Asking);
            waiting.Cancelled();
            Assert.IsTrue(restored);
        }

        [Test]
        public void TheQuestionNamesWhatIsAboutToHappen()
        {
            Assert.AreEqual("signals.prompt.text.view", SignalWindowRules.PromptKey(SignalExitKind.ViewChange));
            Assert.AreEqual("signals.prompt.text", SignalWindowRules.PromptKey(SignalExitKind.OtherTool));
            Assert.AreEqual("signals.prompt.text", SignalWindowRules.PromptKey(SignalExitKind.Action));
            Assert.AreEqual("signals.prompt.text", SignalWindowRules.PromptKey(null));
        }

        // ----- a question that has become stale -----

        [Test]
        public void ADraftCleanedByAnotherWayDropsTheQuestionAndTheWayOutThatWaited()
        {
            var guard = new SignalLeaveGuard();
            var exit = new SignalExit { Kind = SignalExitKind.OtherTool };
            guard.Request(true, exit);
            Assert.IsNull(guard.DropIfStale(true), "a draft with changes keeps its question");
            Assert.IsTrue(guard.Asking);
            Assert.AreSame(exit, guard.DropIfStale(false), "footer Discard or Apply, or undo, made it clean");
            Assert.IsFalse(guard.Asking);
            Assert.IsNull(guard.DropIfStale(false), "nothing left to drop");
        }

        [Test]
        public void TheQuestionIsNotStaleWhileItsOwnAnswerCleansTheDraft()
        {
            var guard = new SignalLeaveGuard();
            var exit = new SignalExit { Kind = SignalExitKind.ViewChange };
            guard.Request(true, exit);
            guard.BeginAnswer();
            Assert.IsNull(guard.DropIfStale(false), "Apply or Discard refresh the rows before the answer is read");
            guard.EndAnswer();
            Assert.IsFalse(guard.Answering);
            Assert.IsNotNull(guard.Pending);
            Assert.AreSame(exit, guard.Answer(SignalPromptChoice.Apply, false), "the way out still runs after the answer");
        }

        [Test]
        public void ARedoWithADraftIsAskedAndRunsAfterTheAnswer()
        {
            var guard = new SignalLeaveGuard();
            var redone = 0;
            var exit = new SignalExit { Kind = SignalExitKind.Action, Payload = (System.Action)(() => redone++) };
            Assert.IsFalse(guard.Request(true, exit));
            Assert.IsTrue(guard.Asking, "the question is open, so it is shown again after a language change and cleaned up when stale");
            Assert.AreEqual(0, redone);
            ((System.Action)guard.Answer(SignalPromptChoice.Discard, false).Payload)();
            Assert.AreEqual(1, redone);
        }

        [Test]
        public void AnotherWayOutReplacingTheWaitingOneGivesTheFirstUp()
        {
            var guard = new SignalLeaveGuard();
            var restored = 0;
            var first = new SignalExit { Kind = SignalExitKind.ViewChange, Cancelled = () => restored++ };
            guard.Request(true, first);
            var second = new SignalExit { Kind = SignalExitKind.OtherTool };
            Assert.IsFalse(guard.Request(true, second));
            Assert.AreEqual(1, restored, "the chrono date the first change showed must be put back");
            Assert.AreSame(second, guard.Pending);
            guard.Request(true, second);
            Assert.AreEqual(1, restored, "asking again for the same way out gives nothing up");
        }

        [Test]
        public void AWayOutThatMayGoOnNowGivesUpTheOneThatWaited()
        {
            var guard = new SignalLeaveGuard();
            var restored = 0;
            guard.Request(true, new SignalExit { Kind = SignalExitKind.ViewChange, Cancelled = () => restored++ });
            Assert.IsTrue(guard.Request(false, new SignalExit { Kind = SignalExitKind.OtherTool }));
            Assert.AreEqual(1, restored);
            Assert.IsFalse(guard.Asking);
        }

        [Test]
        public void ClearingTheQuestionGivesUpTheWayOutThatWaited()
        {
            var guard = new SignalLeaveGuard();
            var restored = 0;
            guard.Request(true, new SignalExit { Kind = SignalExitKind.ViewChange, Cancelled = () => restored++ });
            guard.Clear();
            Assert.AreEqual(1, restored);
            Assert.IsFalse(guard.Asking);
            guard.Clear();
            Assert.AreEqual(1, restored, "nothing waits any more");
        }

        // ----- size of a floating window when the area changes -----

        [Test]
        public void TheMostAWindowMayTakeLeavesTheMarginsAndTheBottomBar()
        {
            Assert.AreEqual(new Vector2(984f, 588f), FloatingWindowRules.MaxSize(new Vector2(1000f, 700f), 8f, 104f));
            Assert.AreEqual(Vector2.zero, FloatingWindowRules.MaxSize(new Vector2(10f, 50f), 8f, 104f));
        }

        [Test]
        public void ALargeWindowIsMadeSmallerWhenTheAreaShrinks()
        {
            var fit = FloatingWindowRules.Fit(new Vector2(40f, 80f), new Vector2(1400f, 780f), 780f, new Vector2(560f, 400f), new Vector2(1100f, 700f), 8f, 104f);
            Assert.AreEqual(new Vector2(1084f, 588f), fit.Size);
            Assert.AreEqual(new Vector2(16f, 8f), fit.Position, "inside the area above the bottom bar");
        }

        [Test]
        public void TheLeastSizeGivesWayToAnAreaSmallerThanIt()
        {
            var fit = FloatingWindowRules.Fit(new Vector2(0f, 0f), new Vector2(800f, 600f), 600f, new Vector2(560f, 400f), new Vector2(500f, 450f), 8f, 104f);
            Assert.AreEqual(484f, fit.Size.x, 0.01f);
            Assert.AreEqual(338f, fit.Size.y, 0.01f);
        }

        [Test]
        public void AWindowThatFitsKeepsItsSizeAndPlace()
        {
            var fit = FloatingWindowRules.Fit(new Vector2(100f, 60f), new Vector2(700f, 500f), 500f, new Vector2(560f, 400f), new Vector2(1600f, 900f), 8f, 104f);
            Assert.AreEqual(new Vector2(700f, 500f), fit.Size);
            Assert.AreEqual(new Vector2(100f, 60f), fit.Position);
        }

        [Test]
        public void ACollapsedWindowIsPlacedByItsTitleBarAndKeepsTheHeightItWillHave()
        {
            var fit = FloatingWindowRules.Fit(new Vector2(100f, 640f), new Vector2(700f, 900f), 40f, new Vector2(560f, 400f), new Vector2(1000f, 700f), 8f, 104f);
            Assert.AreEqual(588f, fit.Size.y, 0.01f, "the height it opens with is made fit");
            Assert.AreEqual(556f, fit.Position.y, 0.01f, "the title bar stays above the bottom bar");
        }

        // ----- the colours after a change that was not written -----

        [Test]
        public void AFailedAddLeavesTheColoursAsTheyWere()
        {
            var identities = new SignalGroupIdentities();
            identities.Reset(3);
            identities.Removed(0);
            var before = identities.Snapshot();
            var colours = new[] { identities.ColourOf(0), identities.ColourOf(1) };
            identities.Added();
            identities.Restore(before);
            Assert.AreEqual(2, identities.Count);
            Assert.AreEqual(colours[0], identities.ColourOf(0));
            Assert.AreEqual(colours[1], identities.ColourOf(1));
        }

        [Test]
        public void AFailedRemoveLeavesTheColoursAsTheyWere()
        {
            var identities = new SignalGroupIdentities();
            identities.Reset(4);
            var before = identities.Snapshot();
            var colours = Enumerable.Range(0, 4).Select(identities.ColourOf).ToArray();
            identities.Removed(1);
            identities.Sync(4); // a refresh that read the unchanged file starts the ids again
            identities.Restore(before);
            Assert.AreEqual(4, identities.Count);
            for (var group = 0; group < 4; group++)
                Assert.AreEqual(colours[group], identities.ColourOf(group), "group " + group);
        }

        // ----- keeping the crossing in sight beside the window -----

        private static readonly Rect Map = Rect.MinMaxRect(0f, 0f, 1600f, 800f);
        private static readonly Rect Covering = Rect.MinMaxRect(700f, 40f, 1500f, 700f);

        [Test]
        public void ACrossingUnderTheWindowMovesIntoTheLargestFreePart()
        {
            var shift = SignalWindowRules.ShiftAroundWindow(new Vector2(1000f, 300f), Covering, Map, 90f);
            var moved = new Vector2(1000f, 300f) + shift;
            Assert.LessOrEqual(moved.x, Covering.xMin - 90f + 0.01f, "the free part left of the window is the largest");
            Assert.AreEqual(300f, moved.y, 0.01f, "only as far as needed");
        }

        [Test]
        public void ACrossingThatIsClearOfTheWindowStaysWhereItIs()
        {
            Assert.AreEqual(Vector2.zero, SignalWindowRules.ShiftAroundWindow(new Vector2(200f, 300f), Covering, Map, 90f));
            Assert.AreEqual(Vector2.zero, SignalWindowRules.ShiftAroundWindow(new Vector2(1000f, 300f), Rect.zero, Map, 90f), "a window without a place");
            Assert.AreEqual(Vector2.zero, SignalWindowRules.ShiftAroundWindow(new Vector2(float.NaN, 300f), Covering, Map, 90f));
        }

        [Test]
        public void ACrossingTooCloseToTheWindowMovesAwayFromIt()
        {
            var shift = SignalWindowRules.ShiftAroundWindow(new Vector2(650f, 300f), Covering, Map, 90f);
            Assert.Less(shift.x, 0f);
        }

        [Test]
        public void WhenTheWindowFillsTheAreaThereIsNowhereToMoveTo()
        {
            var filling = Rect.MinMaxRect(20f, 20f, 1580f, 780f);
            Assert.AreEqual(Vector2.zero, SignalWindowRules.ShiftAroundWindow(new Vector2(800f, 400f), filling, Map, 90f));
        }

        [Test]
        public void TheFreePartAboveIsUsedWhenItIsTheLargest()
        {
            var low = Rect.MinMaxRect(40f, 500f, 1560f, 780f);
            var shift = SignalWindowRules.ShiftAroundWindow(new Vector2(800f, 600f), low, Map, 90f);
            var moved = new Vector2(800f, 600f) + shift;
            Assert.LessOrEqual(moved.y, low.yMin - 90f + 0.01f);
            Assert.AreEqual(800f, moved.x, 0.01f);
        }

        // ----- what the window shows -----

        [Test]
        public void EveryPageBelongsToOneAreaAndAnAreaOpensItsLastPage()
        {
            Assert.AreEqual(SignalArea.Groups, SignalWindowRules.AreaOf(SignalTab.Movements));
            Assert.AreEqual(SignalArea.Phases, SignalWindowRules.AreaOf(SignalTab.Phases));
            Assert.AreEqual(SignalArea.Phases, SignalWindowRules.AreaOf(SignalTab.Timeline));
            Assert.AreEqual(SignalArea.Check, SignalWindowRules.AreaOf(SignalTab.Check));
            Assert.AreEqual(SignalArea.Assistant, SignalWindowRules.AreaOf(SignalTab.Wizard));
            Assert.AreEqual(SignalArea.Assistant, SignalWindowRules.AreaOf(SignalTab.Templates));
            Assert.AreEqual(SignalTab.Timeline, SignalWindowRules.PageOf(SignalArea.Phases, SignalTab.Timeline, SignalTab.Wizard));
            Assert.AreEqual(SignalTab.Phases, SignalWindowRules.PageOf(SignalArea.Phases, SignalTab.Phases, SignalTab.Wizard));
            Assert.AreEqual(SignalTab.Templates, SignalWindowRules.PageOf(SignalArea.Assistant, SignalTab.Phases, SignalTab.Templates));
            Assert.AreEqual(SignalTab.Wizard, SignalWindowRules.PageOf(SignalArea.Assistant, SignalTab.Phases, SignalTab.Wizard));
            Assert.AreEqual(SignalTab.Movements, SignalWindowRules.PageOf(SignalArea.Groups, SignalTab.Timeline, SignalTab.Templates));
            Assert.AreEqual(SignalTab.Check, SignalWindowRules.PageOf(SignalArea.Check, SignalTab.Timeline, SignalTab.Templates));
        }

        [Test]
        public void OnlyPhasesAndTheAssistantHaveTwoPages()
        {
            Assert.IsTrue(SignalWindowRules.HasSubPages(SignalArea.Phases));
            Assert.IsTrue(SignalWindowRules.HasSubPages(SignalArea.Assistant));
            Assert.IsFalse(SignalWindowRules.HasSubPages(SignalArea.Groups));
            Assert.IsFalse(SignalWindowRules.HasSubPages(SignalArea.Check));
        }

        [Test]
        public void TheFooterSaysReadOnlyApplyingOrUnapplied()
        {
            Assert.AreEqual(SignalChangeState.ReadOnly, SignalWindowRules.ChangeState(false, true));
            Assert.AreEqual(SignalChangeState.ReadOnly, SignalWindowRules.ChangeState(false, false));
            Assert.AreEqual(SignalChangeState.Applied, SignalWindowRules.ChangeState(true, false));
            Assert.AreEqual(SignalChangeState.Unapplied, SignalWindowRules.ChangeState(true, true));
        }

        [Test]
        public void ApplyAndDiscardNeedAnEditableCrossingWithChanges()
        {
            Assert.IsTrue(SignalWindowRules.DraftActionsEnabled(true, true));
            Assert.IsFalse(SignalWindowRules.DraftActionsEnabled(true, false));
            Assert.IsFalse(SignalWindowRules.DraftActionsEnabled(false, true));
        }

        [Test]
        public void TheHintLeavesOutEmptyPartsAndKeepsTheOrder()
        {
            // arguments: crossing, reason, next click; the reason comes first, then the next click, the crossing last
            Assert.AreEqual("b\nc\na", SignalWindowRules.Hint("a", "b", "c"));
            Assert.AreEqual("c\na", SignalWindowRules.Hint("a", null, "c"));
            Assert.AreEqual("a", SignalWindowRules.Hint("a", " ", string.Empty));
            Assert.AreEqual(string.Empty, SignalWindowRules.Hint(null, null, null));
        }

        [Test]
        public void AProgramIsAPhasePlanOrFlaggedAndNeverConverted()
        {
            Assert.AreEqual(SignalPhaseView.Empty, SignalWindowRules.PhaseViewOf(null));
            Assert.AreEqual(SignalPhaseView.Empty, SignalWindowRules.PhaseViewOf(new OmsiSignalProgram()));

            // Phases written by the plan come back as a plan.
            var program = new OmsiSignalProgram();
            program.AddLight("A");
            program.AddLight("B");
            var plan = new OmsiSignalPhasePlan();
            plan.Phases.Add(new OmsiSignalPlanPhase { GreenSeconds = 20 });
            plan.Phases[0].Lights.Add(0);
            plan.Phases.Add(new OmsiSignalPlanPhase { GreenSeconds = 15 });
            plan.Phases[1].Lights.Add(1);
            plan.WriteTo(program);
            Assert.AreEqual(SignalPhaseView.Plan, SignalWindowRules.PhaseViewOf(program));

            // A group with a yellow of its own length is not representable: it is flagged and the program is left as it is.
            var before = program.Lights[1].Phases.Select(phase => phase.State + "/" + phase.Seconds).ToList();
            var yellow = program.Lights[1].Phases.FindIndex(phase => OmsiSignalAspects.Of(phase.State) == OmsiSignalAspect.Yellow);
            Assert.GreaterOrEqual(yellow, 0);
            program.Lights[1].Phases[yellow] = new OmsiSignalPhase(program.Lights[1].Phases[yellow].State, program.Lights[1].Phases[yellow].Seconds + 2);
            Assert.AreEqual(SignalPhaseView.NotRepresentable, SignalWindowRules.PhaseViewOf(program));
            Assert.AreNotEqual(string.Join(" ", before), string.Join(" ", program.Lights[1].Phases.Select(phase => phase.State + "/" + phase.Seconds)),
                "looking at the program does not change it, the test did");
        }

        [Test]
        public void FindingsOfTheTimingAreMendedOnPhasesAndThoseOfHeadsAndMovementsOnTheGroups()
        {
            Assert.AreEqual(SignalArea.Groups, SignalWindowRules.AreaOfFinding(OmsiSignalFindingKind.NoHeads));
            Assert.AreEqual(SignalArea.Groups, SignalWindowRules.AreaOfFinding(OmsiSignalFindingKind.UnboundMovements));
            Assert.AreEqual(SignalArea.Groups, SignalWindowRules.AreaOfFinding(OmsiSignalFindingKind.GroupWithoutHead));
            Assert.AreEqual(SignalArea.Phases, SignalWindowRules.AreaOfFinding(OmsiSignalFindingKind.ConflictingGreen));
            Assert.AreEqual(SignalArea.Phases, SignalWindowRules.AreaOfFinding(OmsiSignalFindingKind.ShortIntergreen));
        }

        [Test]
        public void ThePreviewTimeIsHeldWithinAChangedCycle()
        {
            Assert.AreEqual(40.0, SignalWindowRules.ClampPreviewTime(40.0, 60.0), 1e-9);
            Assert.AreEqual(30.0, SignalWindowRules.ClampPreviewTime(45.0, 30.0), 1e-9);
            Assert.AreEqual(0.0, SignalWindowRules.ClampPreviewTime(-3.0, 30.0), 1e-9);
            Assert.AreEqual(0.0, SignalWindowRules.ClampPreviewTime(12.0, 0.0), 1e-9);
            Assert.AreEqual(0.0, SignalWindowRules.ClampPreviewTime(double.NaN, 30.0), 1e-9);
        }

        // ----- the frame, built without a panel -----

        [Test]
        public void TheWindowHasFiveAreasInOrderAndMarksTheOpenOne()
        {
            var closed = false;
            var area = SignalArea.Groups;
            var sub = SignalTab.Movements;
            var window = new SignalWindow(() => closed = true, chosen => area = chosen, page => sub = page);
            Assert.IsTrue(window.ClassListContains("floating-panel"));
            Assert.AreSame(window.Header, window.Children().First(), "the title bar is first");
            var content = window.Content.Children().ToList();
            Assert.Less(content.IndexOf(window.Body), content.IndexOf(window.PreviewHost), "the preview stands under the work area");
            Assert.Less(content.IndexOf(window.PreviewHost), content.IndexOf(window.Footer), "the footer is last");
            Assert.AreEqual(4, window.Content.Query<Button>(className: "signal-nav-button").ToList().Count);

            var nav = window.Content.Query<Button>(className: "signal-nav-button").ToList();
            window.ShowArea(SignalTab.Timeline);
            Assert.AreEqual(new[] { false, true, false, false }, nav.Select(button => button.ClassListContains("panel-tab-active")).ToArray());
            Assert.AreEqual(DisplayStyle.Flex, window.SubBar.style.display.value, "phases and times have two pages");
            window.ShowArea(SignalTab.Check);
            Assert.AreEqual(DisplayStyle.None, window.SubBar.style.display.value);
            window.ShowArea(SignalTab.Templates);
            Assert.IsTrue(nav[3].ClassListContains("panel-tab-active"), "the templates belong to the assistant");
            Assert.IsFalse(closed);
            Assert.AreEqual(SignalArea.Groups, area);
            Assert.AreEqual(SignalTab.Movements, sub);
        }

        [Test]
        public void TheAssistantIsReachedByItsNameInTheNavigation()
        {
            var window = new SignalWindow(() => { }, _ => { }, _ => { });
            var assistant = window.Content.Query<Button>(className: "signal-nav-assistant").ToList();
            Assert.AreEqual(1, assistant.Count);
            Assert.IsNotNull(assistant[0].Q<Label>(), "a button with its text, not an icon alone");
        }

        [Test]
        public void CollapsingHidesTheContentAndKeepsTheTitleBar()
        {
            var window = new SignalWindow(() => { }, _ => { }, _ => { });
            window.Collapsed = true;
            Assert.IsTrue(window.ClassListContains("floating-panel-collapsed"));
            Assert.IsTrue(window.Collapsed);
            window.Collapsed = false;
            Assert.IsFalse(window.ClassListContains("floating-panel-collapsed"));
        }

        [Test]
        public void TheQuestionAboutChangesShowsAndHides()
        {
            var window = new SignalWindow(() => { }, _ => { }, _ => { });
            Assert.IsFalse(window.PromptShown);
            window.ShowPrompt("text", true);
            Assert.IsTrue(window.PromptShown);
            window.ShowPrompt(null, false);
            Assert.IsFalse(window.PromptShown);
        }

        [Test]
        public void TheCrossingIsWrittenBesideTheTitleAndHiddenWithout()
        {
            var window = new SignalWindow(() => { }, _ => { }, _ => { });
            var detail = window.Header.Q<Label>(className: "signal-title-detail");
            window.SetCrossingText("Crossing #5");
            Assert.AreEqual("Crossing #5", detail.text);
            Assert.AreEqual(DisplayStyle.Flex, detail.style.display.value);
            window.SetCrossingText(null);
            Assert.AreEqual(DisplayStyle.None, detail.style.display.value);
        }
    }
}
