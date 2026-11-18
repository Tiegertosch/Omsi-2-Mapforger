using System.Linq;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiRoadTypeDraftTests
    {
        private const string TwoLanes = "walk 3 | drive 3.25 back | drive 3.25 | walk 3";

        private static OmsiRoadTypeDraft Draft(string description = TwoLanes, string path = null)
        {
            return new OmsiRoadTypeDraft(OmsiRoadType.Parse(description), path);
        }

        private static string Kinds(OmsiRoadTypeDraft draft)
        {
            return string.Join(",", draft.ToRoadType().Lanes.Select(lane => OmsiRoadLane.Word(lane.Kind)));
        }

        [Test]
        public void ElementsAreAddedBeforeOrAfterTheSelectionAndSelected()
        {
            var draft = Draft();
            var drive = draft.IdAt(1);
            draft.SelectElement(drive);

            var after = draft.Add(OmsiRoadLaneKind.Bus, OmsiRoadDraftSide.After);
            Assert.AreEqual("walk,drive,bus,drive,walk", Kinds(draft));
            Assert.AreEqual(after, draft.SelectedId);
            Assert.AreEqual(OmsiRoadLaneDirection.Backward, draft.LaneOf(after).Direction, "takes the selected lane's direction");

            draft.SelectElement(drive);
            draft.Add(OmsiRoadLaneKind.Median, OmsiRoadDraftSide.Before);
            Assert.AreEqual("walk,median,drive,bus,drive,walk", Kinds(draft));

            draft.ClearSelection();
            draft.Add(OmsiRoadLaneKind.Shoulder, OmsiRoadDraftSide.Before);
            draft.ClearSelection();
            draft.Add(OmsiRoadLaneKind.Parking, OmsiRoadDraftSide.After);
            Assert.AreEqual("shoulder,walk,median,drive,bus,drive,walk,parking", Kinds(draft));
        }

        [Test]
        public void NoElementIsAddedPastTheLimit()
        {
            var draft = Draft("walk 3");
            while (draft.Count < OmsiRoadType.MaximumLanes)
                Assert.Greater(draft.Add(OmsiRoadLaneKind.Driving, OmsiRoadDraftSide.After), 0);

            Assert.AreEqual(-1, draft.Add(OmsiRoadLaneKind.Driving, OmsiRoadDraftSide.After));
            Assert.AreEqual(-1, draft.Duplicate(draft.IdAt(0)));
            Assert.AreEqual(OmsiRoadType.MaximumLanes, draft.Count);
        }

        [Test]
        public void IdentitiesStayWithTheirElementsAndAreNeverReused()
        {
            var draft = Draft();
            var ids = draft.ElementIds.ToList();
            Assert.AreEqual(4, ids.Distinct().Count());

            Assert.IsTrue(draft.Move(ids[1], 1));
            Assert.AreEqual(new[] { ids[0], ids[2], ids[1], ids[3] }, draft.ElementIds.ToArray());
            Assert.AreEqual(OmsiRoadLaneDirection.Backward, draft.LaneOf(ids[1]).Direction);

            draft.Remove(ids[3]);
            var added = draft.Add(OmsiRoadLaneKind.Sidewalk, OmsiRoadDraftSide.After);
            CollectionAssert.DoesNotContain(ids, added);
            draft.Undo();
            draft.Undo();
            Assert.AreEqual(3, draft.IndexOf(ids[3]), "undo brings the element back under its own identity");
        }

        [Test]
        public void MovingStopsAtTheEdges()
        {
            var draft = Draft();
            Assert.IsFalse(draft.CanMove(draft.IdAt(0), -1));
            Assert.IsFalse(draft.Move(draft.IdAt(3), 1));
            Assert.IsTrue(draft.CanMove(draft.IdAt(0), 1));
            Assert.IsFalse(draft.CanUndo);
            Assert.IsTrue(draft.MoveTo(draft.IdAt(0), 3));
            Assert.AreEqual("drive,drive,walk,walk", Kinds(draft));
        }

        [Test]
        public void ADuplicateKeepsEverySettingUnderANewIdentity()
        {
            var draft = Draft("walk 3 row=\"Sceneryobjects\\x\\tree.sco\" every=15 at=outer turn=90 tex=\"Splines\\x\\t.bmp\" right=solid | drive 3.25");
            var walk = draft.IdAt(0);
            var copy = draft.Duplicate(walk);

            Assert.AreNotEqual(walk, copy);
            Assert.AreEqual(1, draft.IndexOf(copy));
            Assert.AreEqual(copy, draft.SelectedId);
            var description = draft.ToRoadType().Describe().Split('|').Select(part => part.Trim()).ToArray();
            Assert.AreEqual(description[0], description[1]);
        }

        [Test]
        public void ChangingTheKindKeepsWhatFits()
        {
            var draft = Draft("drive 3.5 back tex=\"Splines\\x\\t.bmp\" right=solid | walk 3 row=\"Sceneryobjects\\x\\tree.sco\" every=12");
            var drive = draft.IdAt(0);
            var walk = draft.IdAt(1);

            draft.SetKind(drive, OmsiRoadLaneKind.Bus);
            var bus = draft.LaneOf(drive);
            Assert.AreEqual(3.5, bus.Width, 1e-9, "3.5 m is a bus lane's width too");
            Assert.AreEqual(OmsiRoadLaneDirection.Backward, bus.Direction);
            Assert.AreEqual(@"Splines\x\t.bmp", bus.Texture);
            Assert.AreEqual(OmsiRoadMarking.Solid, bus.RightMarking);

            draft.SetKind(drive, OmsiRoadLaneKind.Bike);
            Assert.AreEqual(OmsiRoadLane.DefaultWidth(OmsiRoadLaneKind.Bike), draft.LaneOf(drive).Width, 1e-9, "3.5 m is too wide for a bike lane");

            draft.SetKind(drive, OmsiRoadLaneKind.Shoulder);
            Assert.AreEqual(OmsiRoadLaneDirection.Forward, draft.LaneOf(drive).Direction);

            draft.SetKind(walk, OmsiRoadLaneKind.Median);
            Assert.AreEqual(@"Sceneryobjects\x\tree.sco", draft.LaneOf(walk).RowObject);
            Assert.AreEqual(12.0, draft.LaneOf(walk).RowInterval, 1e-9);
            draft.SetKind(walk, OmsiRoadLaneKind.Tram);
            Assert.IsNull(draft.LaneOf(walk).RowObject, "no row on a track");
            draft.SetShared(walk, true);
            draft.SetKind(walk, OmsiRoadLaneKind.Driving);
            Assert.IsFalse(draft.LaneOf(walk).Shared);
        }

        [Test]
        public void ARaisedLaneBetweenKerbsMayBeTwiceAsWideAfterAKindChange()
        {
            var draft = Draft("drive 3.25 | shoulder 6 | drive 3.25");
            var middle = draft.IdAt(1);
            draft.SetKind(middle, OmsiRoadLaneKind.Median);
            Assert.AreEqual(6.0, draft.LaneOf(middle).Width, 1e-9);
            Assert.IsTrue(draft.WidthRange(middle, out var minimum, out var maximum));
            Assert.AreEqual(0.5, minimum, 1e-9);
            Assert.AreEqual(13.8, maximum, 1e-9);
        }

        [Test]
        public void WidthsAreRoundedSteppedAndCheckedButNotReset()
        {
            var draft = Draft();
            var drive = draft.IdAt(1);

            Assert.IsTrue(draft.SetWidth(drive, 3.256));
            Assert.AreEqual(3.26, draft.LaneOf(drive).Width, 1e-12);
            Assert.IsFalse(draft.SetWidth(drive, 0.0));
            Assert.IsFalse(draft.SetWidth(drive, double.NaN));

            Assert.IsTrue(draft.SetWidth(drive, 9.0));
            Assert.AreEqual(9.0, draft.LaneOf(drive).Width, 1e-12, "an out-of-range width stays as typed");
            Assert.AreEqual(OmsiRoadTypeProblemCode.WidthOutOfRange, draft.Problems().Single().Code);

            draft.StepWidth(drive, 1);
            Assert.AreEqual(5.0, draft.LaneOf(drive).Width, 1e-12, "a step clamps to the range");
            draft.StepWidth(drive, -3);
            Assert.AreEqual(4.85, draft.LaneOf(drive).Width, 1e-12);
            draft.SetWidth(drive, 2.52);
            draft.StepWidth(drive, -1);
            Assert.AreEqual(2.5, draft.LaneOf(drive).Width, 1e-12);
        }

        [Test]
        public void DirectionTramAndRowsChangeOnlyWhereTheyApply()
        {
            var draft = Draft("walk 3 | drive 3.25 | tram 3");
            Assert.IsFalse(draft.SetDirection(draft.IdAt(0), OmsiRoadLaneDirection.Backward));
            Assert.IsTrue(draft.SetDirection(draft.IdAt(1), OmsiRoadLaneDirection.Backward));
            Assert.IsFalse(draft.SetShared(draft.IdAt(1), true));
            Assert.IsTrue(draft.SetShared(draft.IdAt(2), true));
            Assert.IsFalse(draft.SetRowObject(draft.IdAt(1), @"Sceneryobjects\x\tree.sco"));
            Assert.IsTrue(draft.SetRowObject(draft.IdAt(0), @"Sceneryobjects\x\tree.sco", 90.0));
            draft.SetRowInterval(draft.IdAt(0), 15);
            draft.SetRowPlace(draft.IdAt(0), OmsiRoadRowPlace.Middle);
            draft.SetRowTurn(draft.IdAt(0), -90);
            draft.SetTexture(draft.IdAt(1), @"Splines\x\t.bmp");

            Assert.AreEqual("walk 3.00 row=\"Sceneryobjects\\x\\tree.sco\" every=15 at=middle turn=270 | drive 3.25 back tex=\"Splines\\x\\t.bmp\" | tram 3.00 cars",
                draft.ToRoadType().Describe());
            draft.SetRowObject(draft.IdAt(0), "");
            Assert.IsNull(draft.LaneOf(draft.IdAt(0)).RowObject);
        }

        [Test]
        public void LinesAreSetOnBoundariesWithAuto()
        {
            var draft = Draft();
            var left = draft.IdAt(1);
            Assert.AreEqual(OmsiRoadMarking.Dashed, draft.MarkingAfter(left));
            Assert.IsTrue(draft.SetMarking(left, OmsiRoadMarking.Double));
            Assert.AreEqual(OmsiRoadMarking.Double, draft.MarkingAfter(left));
            Assert.IsTrue(draft.SetMarking(left, null));
            Assert.AreEqual(OmsiRoadMarking.Dashed, draft.MarkingAfter(left));
            Assert.IsFalse(draft.SetMarking(draft.IdAt(3), OmsiRoadMarking.Solid), "no boundary right of the last element");
            Assert.IsFalse(draft.SelectBoundary(draft.IdAt(3)));
            Assert.IsTrue(draft.SelectBoundary(left));
            Assert.AreEqual(OmsiRoadDraftSelectionKind.Boundary, draft.SelectionKind);
        }

        [Test]
        public void UndoAndRedoRestoreElementsAndSelection()
        {
            var draft = Draft();
            var drive = draft.IdAt(1);
            draft.SelectElement(drive);
            var bus = draft.Add(OmsiRoadLaneKind.Bus, OmsiRoadDraftSide.After);
            draft.Remove(drive);
            Assert.AreEqual(bus, draft.SelectedId);

            Assert.IsTrue(draft.Undo());
            Assert.AreEqual(bus, draft.SelectedId, "selection as before the removal");
            Assert.AreEqual(1, draft.IndexOf(drive));
            Assert.IsTrue(draft.Undo());
            Assert.AreEqual(drive, draft.SelectedId);
            Assert.AreEqual(OmsiRoadType.Parse(TwoLanes).Describe(), draft.ToRoadType().Describe());
            Assert.IsFalse(draft.CanUndo);

            Assert.IsTrue(draft.Redo());
            Assert.AreEqual(bus, draft.SelectedId);
            Assert.IsTrue(draft.Redo());
            Assert.AreEqual(-1, draft.IndexOf(drive));
            Assert.IsFalse(draft.Redo());

            draft.Undo();
            draft.SetWidth(bus, 3.0);
            Assert.IsFalse(draft.CanRedo, "a new change drops the redo steps");
        }

        [Test]
        public void OneGestureIsOneUndoStep()
        {
            var draft = Draft();
            var drive = draft.IdAt(1);
            draft.SetWidth(drive, 3.3, "width");
            draft.SetWidth(drive, 3.35, "width");
            draft.StepWidth(drive, 1, "width");
            Assert.AreEqual(3.4, draft.LaneOf(drive).Width, 1e-12);
            draft.EndGesture();
            draft.SetWidth(drive, 3.5, "width");

            draft.Undo();
            Assert.AreEqual(3.4, draft.LaneOf(drive).Width, 1e-12);
            draft.Undo();
            Assert.AreEqual(3.25, draft.LaneOf(drive).Width, 1e-12);
            Assert.IsFalse(draft.CanUndo);

            draft.SetWidth(drive, 3.3, "width");
            draft.SetWidth(draft.IdAt(2), 3.3, "width");
            draft.Undo();
            Assert.AreEqual(3.3, draft.LaneOf(drive).Width, 1e-12, "another element is another step");
        }

        [Test]
        public void ASettingThatChangesNothingMakesNoStep()
        {
            var draft = Draft();
            Assert.IsTrue(draft.SetWidth(draft.IdAt(1), 3.25));
            Assert.IsFalse(draft.CanUndo);
            Assert.IsFalse(draft.IsDirty);
        }

        [Test]
        public void DirtyComparesWithTheSavedStateAndDiscardGoesBack()
        {
            var draft = Draft(TwoLanes, @"Splines\Mapforge\Test\Road.sli");
            Assert.IsFalse(draft.IsDirty);
            Assert.IsFalse(draft.IsNew);
            var drive = draft.IdAt(1);
            draft.SetWidth(drive, 3.5);
            Assert.IsTrue(draft.IsDirty);
            draft.SetWidth(drive, 3.25);
            Assert.IsFalse(draft.IsDirty, "back to the saved widths is not a change");

            draft.SelectElement(draft.IdAt(2));
            draft.Remove(draft.IdAt(0));
            Assert.IsTrue(draft.IsDirty);
            draft.Discard();
            Assert.IsFalse(draft.IsDirty);
            Assert.AreEqual(4, draft.Count);
            Assert.AreEqual(OmsiRoadDraftSelectionKind.Element, draft.SelectionKind);
            Assert.AreEqual(1, draft.SelectedIndex, "the selection stays at its place");
        }

        [Test]
        public void DiscardIsOneUndoableStepAndKeepsIdsByPosition()
        {
            var draft = Draft(TwoLanes, @"Splines\Mapforge\Test\Road.sli");
            var version = draft.Version;
            draft.Discard();
            Assert.IsFalse(draft.CanUndo, "discarding a clean draft makes no step");
            Assert.AreEqual(version, draft.Version);

            var ids = new[] { draft.IdAt(0), draft.IdAt(1), draft.IdAt(2), draft.IdAt(3) };
            draft.SetWidth(ids[1], 3.5);
            draft.EndGesture();
            draft.SelectElement(ids[2]);
            draft.Remove(ids[0]);
            var beforeIds = new[] { draft.IdAt(0), draft.IdAt(1), draft.IdAt(2) };
            var selectedBefore = draft.SelectedId;

            draft.Discard();
            Assert.IsFalse(draft.IsDirty);
            Assert.AreEqual(4, draft.Count);
            Assert.AreEqual(beforeIds[0], draft.IdAt(0), "ids stay by position");
            Assert.AreEqual(beforeIds[2], draft.IdAt(2));
            Assert.AreEqual(3.25, draft.LaneOf(draft.IdAt(1)).Width, 1e-12);

            Assert.IsTrue(draft.Undo());
            Assert.IsTrue(draft.IsDirty);
            Assert.AreEqual(3, draft.Count);
            Assert.AreEqual(beforeIds[0], draft.IdAt(0));
            Assert.AreEqual(beforeIds[2], draft.IdAt(2));
            Assert.AreEqual(selectedBefore, draft.SelectedId, "undo restores the selection");

            Assert.IsTrue(draft.Redo());
            Assert.IsFalse(draft.IsDirty);
            Assert.AreEqual(4, draft.Count);
        }

        [Test]
        public void SavingMakesTheDraftTheNewBaseline()
        {
            var draft = Draft();
            Assert.IsTrue(draft.IsNew);
            var drive = draft.IdAt(1);
            draft.SelectElement(drive);
            draft.SetWidth(drive, 3.5);
            draft.Rebase(@"Splines\Mapforge\Test\Road_2.sli");

            Assert.IsFalse(draft.IsDirty);
            Assert.IsFalse(draft.CanUndo);
            Assert.AreEqual(@"Splines\Mapforge\Test\Road_2.sli", draft.AssetPath);
            Assert.AreEqual(drive, draft.SelectedId);
            Assert.AreEqual(3.5, draft.Baseline.Lanes[1].Width, 1e-12);

            draft.SetWidth(drive, 4.0);
            draft.Discard();
            Assert.AreEqual(3.5, draft.LaneOf(draft.IdAt(1)).Width, 1e-12);
        }

        [Test]
        public void AChangedFileMarksTheDraftStale()
        {
            var draft = Draft(TwoLanes, @"Splines\Mapforge\Test\Road.sli");
            Assert.IsFalse(draft.CheckSaved(OmsiRoadType.Parse(TwoLanes)));
            Assert.IsTrue(draft.CheckSaved(OmsiRoadType.Parse("walk 3")));
            Assert.IsTrue(draft.IsStale);
            draft.Rebase(draft.AssetPath);
            Assert.IsFalse(draft.IsStale);
        }

        [Test]
        public void ATemplateOrATakenRoadReplacesTheElementsAsOneStep()
        {
            var draft = Draft();
            draft.SelectElement(draft.IdAt(1));
            var before = draft.ElementIds.ToList();

            Assert.IsTrue(draft.ApplyTemplate("tram-in-the-street"));
            Assert.AreEqual(OmsiRoadTypeTemplate.Find("tram-in-the-street").Type().Describe(), draft.ToRoadType().Describe());
            Assert.AreEqual(1, draft.SelectedIndex);
            CollectionAssert.IsEmpty(draft.ElementIds.Intersect(before));
            Assert.IsFalse(draft.ApplyTemplate("no-such-template"));

            draft.Apply(OmsiRoadType.Parse("walk 3"));
            Assert.AreEqual(0, draft.SelectedIndex);
            draft.Undo();
            draft.Undo();
            CollectionAssert.AreEqual(before, draft.ElementIds);
            Assert.IsFalse(draft.IsDirty);
        }

        [Test]
        public void TemplatesHaveStableIdsAndFigures()
        {
            var templates = OmsiRoadTypeTemplate.All;
            Assert.AreEqual(OmsiRoadType.Templates.Count, templates.Count);
            Assert.AreEqual(templates.Count, templates.Select(template => template.Id).Distinct().Count());
            Assert.AreEqual("2-lanes", templates[0].Id);
            Assert.AreEqual("2-plus-2-with-median", OmsiRoadTypeTemplate.IdOf("2+2 with median"));
            var street = OmsiRoadTypeTemplate.Find("tram-in-the-street");
            Assert.AreEqual(6, street.Elements);
            Assert.AreEqual(4, street.TrafficLanes, "shared tracks carry cars");
            Assert.AreEqual(19.5, street.Width, 1e-9);
            Assert.AreEqual(2, OmsiRoadTypeTemplate.Find("tram-in-the-middle").TrafficLanes);
            Assert.AreEqual(0, OmsiRoadTypeTemplate.Find("footpath").TrafficLanes);
        }

        [Test]
        public void TheLastElementMayGoButAnEmptyDraftCannotBeSaved()
        {
            var draft = Draft("walk 3");
            Assert.IsTrue(draft.CanSave);
            Assert.IsTrue(draft.Remove(draft.IdAt(0)));
            Assert.AreEqual(0, draft.Count);
            Assert.AreEqual(OmsiRoadDraftSelectionKind.None, draft.SelectionKind);
            Assert.IsFalse(draft.CanSave);
            Assert.AreEqual(OmsiRoadTypeProblemCode.NoLanes, draft.Problems().Single().Code);
            Assert.IsEmpty(draft.Problems().Single().Arguments());
            Assert.AreEqual("", draft.ToRoadType().Describe());
        }

        [Test]
        public void ProblemsCarryCodesAndArgumentsLikeTheEnglishTexts()
        {
            var draft = Draft("drive 7 | walk 3 row=\"x.sco\" every=1 | median 1");
            var id = draft.IdAt(0);
            var problems = draft.Problems();
            Assert.AreEqual(draft.ToRoadType().Problems().Count, problems.Count);
            CollectionAssert.AreEqual(new[] { OmsiRoadTypeProblemCode.WidthOutOfRange, OmsiRoadTypeProblemCode.RowTooDense }, problems.Select(problem => problem.Code));
            Assert.AreEqual(id, problems[0].LaneId);
            Assert.AreEqual(0, problems[0].LaneIndex);
            var arguments = problems[0].Arguments().ToDictionary(pair => pair.Key, pair => pair.Value);
            Assert.AreEqual("1", arguments["lane"]);
            Assert.AreEqual("drive", arguments["kind"]);
            Assert.AreEqual("2.50", arguments["min"]);
            Assert.AreEqual("5.00", arguments["max"]);
            Assert.AreEqual("2.00", problems[1].Arguments().Single(pair => pair.Key == "min").Value);

            var crowded = OmsiRoadType.Parse("drive 3.25 row=\"x.sco\"");
            for (var each = 0; each < OmsiRoadType.MaximumLanes; each++)
                crowded.Lanes.Add(new OmsiRoadLane { Kind = OmsiRoadLaneKind.Driving, Width = 3.25 });
            var crowdedProblems = OmsiRoadTypeProblem.Of(crowded);
            Assert.AreEqual(crowded.Problems().Count, crowdedProblems.Count);
            Assert.AreEqual(OmsiRoadTypeProblemCode.TooManyLanes, crowdedProblems[0].Code);
            Assert.AreEqual("16", crowdedProblems[0].Arguments().Single().Value);
            Assert.AreEqual(OmsiRoadTypeProblemCode.RowNotAllowed, crowdedProblems[1].Code);
            Assert.AreEqual(-1, crowdedProblems[1].LaneId);
        }

        [Test]
        public void OnlyRoadTypesInTheProjectsFolderMayBeOverwritten()
        {
            Assert.AreEqual(@"Splines\Mapforge\My Map", OmsiRoadTypeOwnership.ProjectFolder("My Map"));
            Assert.IsTrue(OmsiRoadTypeOwnership.InProjectFolder(@"Splines\Mapforge\My Map\Road.sli", "My Map"));
            Assert.IsTrue(OmsiRoadTypeOwnership.InProjectFolder("splines/mapforge/my map/Road.SLI", "My Map"));
            Assert.IsTrue(OmsiRoadTypeOwnership.InProjectFolder(@"Splines\Mapforge\My_Map_\Road.sli", "My/Map?"), "the folder name as the store makes it");
            Assert.IsFalse(OmsiRoadTypeOwnership.InProjectFolder(@"Splines\Mapforge\Other\Road.sli", "My Map"));
            Assert.IsFalse(OmsiRoadTypeOwnership.InProjectFolder(@"Splines\Mapforge\My Map 2\Road.sli", "My Map"));
            Assert.IsFalse(OmsiRoadTypeOwnership.InProjectFolder(@"Splines\Mapforge\My Map\texture\Road.sli", "My Map"));
            Assert.IsFalse(OmsiRoadTypeOwnership.InProjectFolder(@"Splines\Marcel\Road.sli", "My Map"));
            Assert.IsFalse(OmsiRoadTypeOwnership.InProjectFolder(null, "My Map"));
            Assert.IsFalse(OmsiRoadTypeOwnership.InProjectFolder(@"Splines\Mapforge\My Map\", "My Map"));
        }
    }
}
