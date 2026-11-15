using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using OmsiMapEditor.UI;

namespace OmsiMapEditor.Tests
{
    /// <summary>The problems of the line and track planners: codes with arguments, an English text for the log, and a text in every language.</summary>
    public sealed class LinePlanProblemsTests
    {
        private static readonly string[] Arguments = { "stop", "from", "to", "metres" };

        private static OmsiMapPath Path(int id, int kind, double x0, double y0, double x1, double y1)
        {
            return new OmsiMapPath
            {
                TileX = 0, TileY = 0, ElementId = id, PathIndex = 0, OnSpline = true, Kind = kind, Direction = kind == 2 ? 2 : 0,
                LengthMeters = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)),
                Points = new[] { new OmsiMapPoint(x0, y0), new OmsiMapPoint((x0 + x1) / 2.0, (y0 + y1) / 2.0), new OmsiMapPoint(x1, y1) }
            };
        }

        private static OmsiTripStopChoice Stop(string name, int id, double x, double y)
        {
            return new OmsiTripStopChoice { Name = name, TileIndex = 0, ObjectId = id, World = new OmsiMapPoint(x, y) };
        }

        private static OmsiTripStopChoice Waypoint(double x, double y)
        {
            return new OmsiTripStopChoice { Name = "waypoint", IsWaypoint = true, World = new OmsiMapPoint(x, y) };
        }

        private static string RepositoryRoot()
        {
            var folder = Directory.GetCurrentDirectory();
            for (var depth = 0; depth < 6 && folder != null; depth++, folder = System.IO.Path.GetDirectoryName(folder))
                if (Directory.Exists(System.IO.Path.Combine(folder, "locales")) && Directory.Exists(System.IO.Path.Combine(folder, "Assets", "_Project", "Scripts")))
                    return folder;
            Assert.Fail("The repository root with locales/ was not found from " + Directory.GetCurrentDirectory());
            return null;
        }

        [Test]
        public void EveryCodeHasAKeyEveryLanguageHasWithOnlyTheProblemsArguments()
        {
            var locales = LocaleLoader.List(System.IO.Path.Combine(RepositoryRoot(), "locales"), null);
            Assert.GreaterOrEqual(locales.Count, 8);
            var keys = Enum.GetValues(typeof(OmsiPlanProblemCode)).Cast<OmsiPlanProblemCode>().Select(code => LinePlanProblems.Key(code)).ToList();
            CollectionAssert.AllItemsAreNotNull(keys);
            CollectionAssert.AllItemsAreUnique(keys);
            keys.Add("line.problem.a_waypoint");
            foreach (var info in locales)
            {
                var messages = LocaleLoader.Load(info).Messages;
                foreach (var key in keys)
                {
                    Assert.IsTrue(messages.ContainsKey(key), info.Code + " lacks " + key);
                    var names = MessageFormat.Parse(messages[key]).Arguments.Select(argument => argument.Name).ToList();
                    CollectionAssert.IsSubsetOf(names, Arguments, info.Code + " " + key);
                }
            }
        }

        [Test]
        public void TheEnglishTextsMatchTheLogTextOfEveryCode()
        {
            var english = LocaleLoader.Load(LocaleLoader.List(System.IO.Path.Combine(RepositoryRoot(), "locales"), null).Find(item => item.Code == "en-US")).Messages;
            var samples = new[]
            {
                OmsiPlanProblem.Of(OmsiPlanProblemCode.TooFewStops),
                OmsiPlanProblem.OfStop(OmsiPlanProblemCode.StopHasNoLane, "Gaussdorf"),
                OmsiPlanProblem.Between(OmsiPlanProblemCode.NoWay, "A", "B"),
                OmsiPlanProblem.Between(OmsiPlanProblemCode.NoWayOverWaypoints, "A", "B"),
                OmsiPlanProblem.Between(OmsiPlanProblemCode.WaypointNotNearLane, "A", "B"),
                OmsiPlanProblem.Between(OmsiPlanProblemCode.LaneOnUnlistedTile, "A", "B"),
                OmsiPlanProblem.OfStop(OmsiPlanProblemCode.StopHasNoRail, "Gaussdorf", 8),
                OmsiPlanProblem.OfStop(OmsiPlanProblemCode.WaypointHasNoRail, string.Empty, 8),
                OmsiPlanProblem.Between(OmsiPlanProblemCode.NoRailWay, "A", "B"),
                OmsiPlanProblem.Of(OmsiPlanProblemCode.NoRailWayFirstToLast),
            };
            CollectionAssert.AreEquivalent(Enum.GetValues(typeof(OmsiPlanProblemCode)), samples.Select(sample => sample.Code));
            foreach (var problem in samples)
            {
                var arguments = new List<(string Name, object Value)> { ("stop", problem.Stop), ("from", problem.From), ("to", problem.To), ("metres", problem.Meters) };
                var text = MessageFormat.Parse(english[LinePlanProblems.Key(problem.Code)]).Format(CultureInfo.GetCultureInfo("en-US"), "en", arguments);
                Assert.AreEqual(problem.ToString(), text, problem.Code.ToString());
            }
            Assert.AreEqual("The stop Gaussdorf has no rail within 8 m.", OmsiPlanProblem.OfStop(OmsiPlanProblemCode.StopHasNoRail, "Gaussdorf", 8).ToString());
            Assert.AreEqual("No way leads over the rails from a waypoint to B.",
                OmsiPlanProblem.Between(OmsiPlanProblemCode.NoRailWay, string.Empty, "B", true, false).ToString());
        }

        [Test]
        public void MetresRoundHalvesAwayFromZeroAndShowWithoutGrouping()
        {
            Assert.AreEqual(9, OmsiPlanProblem.WholeMetres(8.5));
            Assert.AreEqual(3, OmsiPlanProblem.WholeMetres(2.5));
            Assert.AreEqual(8, OmsiPlanProblem.WholeMetres(8.4));
            Assert.AreEqual(2.5.ToString("0", CultureInfo.InvariantCulture), OmsiPlanProblem.WholeMetres(2.5).ToString(CultureInfo.InvariantCulture));
            var problem = OmsiPlanProblem.OfStop(OmsiPlanProblemCode.StopHasNoRail, "A", OmsiPlanProblem.WholeMetres(1234.5));
            Assert.AreEqual("The stop A has no rail within 1235 m.", problem.ToString());
            var english = LocaleLoader.Load(LocaleLoader.List(System.IO.Path.Combine(RepositoryRoot(), "locales"), null).Find(item => item.Code == "en-US")).Messages;
            var arguments = new List<(string Name, object Value)> { ("stop", problem.Stop), ("from", string.Empty), ("to", string.Empty), ("metres", problem.Meters) };
            Assert.AreEqual("The stop A has no rail within 1235 m.",
                MessageFormat.Parse(english[LinePlanProblems.Key(problem.Code)]).Format(CultureInfo.GetCultureInfo("en-US"), "en", arguments), "no thousands separator in the interface text");
        }

        [Test]
        public void TheTrackPlannerNamesItsProblemsByCode()
        {
            var network = OmsiPathNetwork.Build(new[] { Path(1, 2, 0, 0, 100, 0), Path(2, 2, 300, 0, 400, 0) }, 2);
            var few = OmsiTrackPlanner.Plan(new[] { Stop("A", 10, 30, -3) }, network, (x, y) => 0);
            Assert.AreEqual(OmsiPlanProblemCode.TooFewStops, few.Problems.Single().Code);

            var far = OmsiTrackPlanner.Plan(new[] { Stop("A", 10, 30, -3), Stop("Gaussdorf", 11, 50, 80) }, network, (x, y) => 0);
            var problem = far.Problems.Single();
            Assert.AreEqual(OmsiPlanProblemCode.StopHasNoRail, problem.Code);
            Assert.AreEqual("Gaussdorf", problem.Stop);
            Assert.AreEqual(8, problem.Meters);
            Assert.AreEqual("The stop Gaussdorf has no rail within 8 m.", problem.ToString());

            var waypoint = OmsiTrackPlanner.Plan(new[] { Stop("A", 10, 30, -3), Waypoint(50, 80), Stop("B", 11, 60, -3) }, network, (x, y) => 0);
            Assert.AreEqual(OmsiPlanProblemCode.WaypointHasNoRail, waypoint.Problems.Single().Code);

            var apart = OmsiTrackPlanner.Plan(new[] { Stop("A", 10, 30, -3), Stop("B", 11, 350, -3) }, network, (x, y) => 0);
            problem = apart.Problems.Single();
            Assert.AreEqual(OmsiPlanProblemCode.NoRailWay, problem.Code);
            Assert.AreEqual("A", problem.From);
            Assert.AreEqual("B", problem.To);
            Assert.IsFalse(problem.FromIsWaypoint || problem.ToIsWaypoint);

            var viaWaypoint = OmsiTrackPlanner.Plan(new[] { Stop("A", 10, 30, -3), Waypoint(350, 1), Stop("B", 11, 360, -3) }, network, (x, y) => 0);
            problem = viaWaypoint.Problems.Single();
            Assert.AreEqual(OmsiPlanProblemCode.NoRailWay, problem.Code);
            Assert.AreEqual("A", problem.From);
            Assert.IsTrue(problem.ToIsWaypoint);
            Assert.AreEqual("No way leads over the rails from A to a waypoint.", problem.ToString());
        }

        [Test]
        public void TheTripPlannerNamesItsProblemsByCode()
        {
            var network = OmsiPathNetwork.Build(new[] { Path(1, 0, 0, 0, 100, 0), Path(2, 0, 300, 0, 400, 0) });
            var few = OmsiTripPlanner.Plan(new[] { Stop("A", 10, 30, -3) }, new OmsiTimetable(), network, (x, y) => 0);
            Assert.AreEqual(OmsiPlanProblemCode.TooFewStops, few.Problems.Single().Code);

            var far = OmsiTripPlanner.Plan(new[] { Stop("A", 10, 30, -3), Stop("Far", 11, 50, 500) }, new OmsiTimetable(), network, (x, y) => 0);
            var problem = far.Problems.Single();
            Assert.AreEqual(OmsiPlanProblemCode.StopHasNoLane, problem.Code);
            Assert.AreEqual("Far", problem.Stop);

            var apart = OmsiTripPlanner.Plan(new[] { Stop("A", 10, 30, -3), Stop("B", 11, 350, -3) }, new OmsiTimetable(), network, (x, y) => 0);
            problem = apart.Problems.Single();
            Assert.AreEqual(OmsiPlanProblemCode.NoWay, problem.Code);
            Assert.AreEqual("No way leads from A to B.", problem.ToString());

            var offLane = OmsiTripPlanner.Plan(new[] { Stop("A", 10, 30, -3), Waypoint(50, 500), Stop("B", 11, 80, -3) }, new OmsiTimetable(), network, (x, y) => 0);
            Assert.AreEqual(OmsiPlanProblemCode.WaypointNotNearLane, offLane.Problems.Single().Code);

            var overWaypoints = OmsiTripPlanner.Plan(new[] { Stop("A", 10, 30, -3), Waypoint(350, 1), Stop("B", 11, 80, -3) }, new OmsiTimetable(), network, (x, y) => 0);
            Assert.AreEqual(OmsiPlanProblemCode.NoWayOverWaypoints, overWaypoints.Problems.Single().Code);

            var unlisted = OmsiTripPlanner.Plan(new[] { Stop("A", 10, 30, -3), Stop("B", 11, 80, -3) }, new OmsiTimetable(), network, (x, y) => -1);
            problem = unlisted.Problems.Single();
            Assert.AreEqual(OmsiPlanProblemCode.LaneOnUnlistedTile, problem.Code);
            Assert.AreEqual("B", problem.To);
        }
    }
}
