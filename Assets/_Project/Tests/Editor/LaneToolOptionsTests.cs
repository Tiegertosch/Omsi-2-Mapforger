using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using OmsiMapEditor.UI;
using UnityEngine.UIElements;

namespace OmsiMapEditor.Tests
{
    /// <summary>The lanes window (ADR 098, M4) built without a panel: which tiles each tab lists, the empty text, and that listing raises no request.</summary>
    public sealed class LaneToolOptionsTests
    {
        private static LaneArmEntry Arm(int arm, bool turnAvailable, OmsiTurnLane turn = null, OmsiCrossing crossing = null)
        {
            return new LaneArmEntry { Arm = arm, Direction = "north", TurnAvailable = turnAvailable, Turn = turn, Crossing = crossing };
        }

        private static List<Button> Tiles(LaneToolOptions options)
        {
            return options.Query<Button>(className: "picker-tile").ToList();
        }

        private static Label Empty(LaneToolOptions options)
        {
            return options.Query<Label>(className: "picker-empty").First();
        }

        private static List<LaneArmEntry> ThreeArms()
        {
            // Arm 0 has no median, arm 1 has one and no lane, arm 2 has a lane already.
            return new List<LaneArmEntry>
            {
                Arm(0, false),
                Arm(1, true),
                Arm(2, true, new OmsiTurnLane())
            };
        }

        [Test]
        public void TurnTabListsOnlyArmsWithAMedianAndAValuesTileOnlyForAnExistingLane()
        {
            var options = new LaneToolOptions();
            options.SetArms(ThreeArms());
            // Arm 1: toggle only; arm 2: toggle and values.
            Assert.AreEqual(3, Tiles(options).Count);
            Assert.AreEqual(DisplayStyle.None, Empty(options).style.display.value);
        }

        [Test]
        public void CrossingsTabListsEveryArmAndAValuesTileOnlyForAnExistingCrossing()
        {
            var options = new LaneToolOptions();
            var arms = ThreeArms();
            arms[0] = Arm(0, false, null, new OmsiCrossing());
            options.SetArms(arms);
            options.ShowTab(LaneTab.Crossings);
            // Three toggles and one values tile.
            Assert.AreEqual(4, Tiles(options).Count);
        }

        [Test]
        public void ArmWithoutMedianIsHiddenOnTheTurnTabEvenWithoutOtherArms()
        {
            var options = new LaneToolOptions();
            options.SetArms(new List<LaneArmEntry> { Arm(0, false) });
            Assert.AreEqual(0, Tiles(options).Count);
            Assert.AreEqual(DisplayStyle.Flex, Empty(options).style.display.value);
        }

        [Test]
        public void EmptyTextFollowsTheTabAndTheChoiceOfAJunction()
        {
            var options = new LaneToolOptions();
            options.SetArms(null);
            Assert.AreEqual(Loc.T("lanes.click_junction"), Empty(options).text);
            options.SetArms(new List<LaneArmEntry>());
            Assert.AreEqual(Loc.T("lanes.turn.none"), Empty(options).text);
            options.ShowTab(LaneTab.Crossings);
            Assert.AreEqual(Loc.T("lanes.crossing.none"), Empty(options).text);
            Assert.AreNotEqual(Loc.T("lanes.turn.none"), Empty(options).text);
            options.SetArms(null);
            Assert.AreEqual(Loc.T("lanes.click_junction"), Empty(options).text);
        }

        [Test]
        public void ListingAndSwitchingTabsRaiseNoRequests()
        {
            var options = new LaneToolOptions();
            var raised = 0;
            options.EveryRequested += () => raised++;
            options.NoneRequested += () => raised++;
            options.TurnToggled += _ => raised++;
            options.TurnLengthsRequested += _ => raised++;
            options.CrossingToggled += _ => raised++;
            options.CrossingValuesRequested += _ => raised++;
            options.ArmHovered += (_, __) => raised++;
            options.CloseRequested += () => raised++;
            options.SetArms(ThreeArms());
            options.ShowTab(LaneTab.Crossings);
            options.ShowTab(LaneTab.TurnLanes);
            options.RefreshTexts();
            options.ShowJunction("Junction");
            options.ShowPlan("plan", false);
            Assert.AreEqual(0, raised);
        }

        [Test]
        public void TooltipOfTheWaysRowStaysTheJunctionLineAfterRefreshingTexts()
        {
            var options = new LaneToolOptions();
            options.ShowJunction("Junction 7");
            options.RefreshTexts();
            var label = options.Query<Label>().ToList().First(item => item.tooltip.StartsWith("Junction 7"));
            StringAssert.StartsWith("Junction 7", label.tooltip);
        }

        [Test]
        public void JunctionWithoutAnyTurnLaneOpensTheCrossingsAndOthersKeepTheUsersTab()
        {
            Assert.AreEqual(LaneTab.Crossings, LaneToolOptions.TabForJunction(false, LaneTab.TurnLanes));
            Assert.AreEqual(LaneTab.Crossings, LaneToolOptions.TabForJunction(false, LaneTab.Crossings));
            Assert.AreEqual(LaneTab.TurnLanes, LaneToolOptions.TabForJunction(true, LaneTab.TurnLanes));
            Assert.AreEqual(LaneTab.Crossings, LaneToolOptions.TabForJunction(true, LaneTab.Crossings), "the user's own choice stays");
        }

        [Test]
        public void TheAutomaticCrossingsTabDoesNotReplaceTheUsersTab()
        {
            var options = new LaneToolOptions();
            Assert.AreEqual(LaneTab.TurnLanes, options.UserTab);
            // Junction A has no turn lanes: the crossings open, the user's tab stays.
            options.ShowTab(LaneToolOptions.TabForJunction(false, options.UserTab));
            Assert.AreEqual(LaneTab.Crossings, options.Tab);
            Assert.AreEqual(LaneTab.TurnLanes, options.UserTab);
            // Junction B has turn lanes: back to the user's tab.
            options.ShowTab(LaneToolOptions.TabForJunction(true, options.UserTab));
            Assert.AreEqual(LaneTab.TurnLanes, options.Tab);
            // An explicit choice sticks for later junctions.
            options.ChooseTab(LaneTab.Crossings);
            options.ShowTab(LaneToolOptions.TabForJunction(true, options.UserTab));
            Assert.AreEqual(LaneTab.Crossings, options.Tab);
        }

        [Test]
        public void OnlyTheNoWaysChoiceLooksLikeDelete()
        {
            var options = new LaneToolOptions();
            var danger = options.Query<Button>(className: "option-icon-danger").ToList();
            Assert.AreEqual(1, danger.Count, "the every-way choice stays neutral");
        }
    }
}
