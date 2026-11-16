using System.IO;
using System.Linq;
using NUnit.Framework;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using OmsiMapEditor.UI;

namespace OmsiMapEditor.Tests
{
    /// <summary>A trip that cannot be written throws with the trip and the coded problems, so the editor can say so in its own language.</summary>
    public sealed class OmsiTripPlanExceptionTests
    {
        private static string RepositoryRoot()
        {
            var folder = Directory.GetCurrentDirectory();
            for (var depth = 0; depth < 6 && folder != null; depth++, folder = Path.GetDirectoryName(folder))
                if (Directory.Exists(Path.Combine(folder, "locales")) && Directory.Exists(Path.Combine(folder, "Assets", "_Project", "Scripts")))
                    return folder;
            Assert.Fail("The repository root with locales/ was not found from " + Directory.GetCurrentDirectory());
            return null;
        }

        [Test]
        public void TheExceptionCarriesTheTripAndTheProblemsAndKeepsTheEnglishMessage()
        {
            var problems = new[]
            {
                OmsiPlanProblem.Between(OmsiPlanProblemCode.NoWay, "A", "B"),
                OmsiPlanProblem.Of(OmsiPlanProblemCode.TooFewStops)
            };
            var exception = new OmsiTripPlanException("Trip 1", problems);
            Assert.AreEqual("Trip 1", exception.TripName);
            CollectionAssert.AreEqual(problems, exception.Problems);
            Assert.AreEqual("The trip Trip 1 has problems: No way leads from A to B. A trip needs at least two stops.", exception.Message);
            Assert.IsInstanceOf<System.InvalidOperationException>(exception);
        }

        [Test]
        public void ANullProblemListAndAMissingNameGiveAnEmptyListAndAShortMessage()
        {
            var exception = new OmsiTripPlanException(null, null);
            Assert.AreEqual(string.Empty, exception.TripName);
            Assert.AreEqual(0, exception.Problems.Count);
            Assert.AreEqual("The trip has problems: ", exception.Message);
        }

        [Test]
        public void ReplacingTheStopsOfATripWithAnInvalidPlanThrowsTheCodedException()
        {
            var root = Path.Combine(Path.GetTempPath(), "mapforge-tripplanexc-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var map = Path.Combine(root, "maps", "Exctown");
                Directory.CreateDirectory(map);
                File.WriteAllBytes(Path.Combine(map, "global.cfg"), System.Text.Encoding.Unicode.GetPreamble()
                    .Concat(System.Text.Encoding.Unicode.GetBytes("[NextIDCode]\r\n100\r\n\r\n[map]\r\n0\r\n0\r\ntile_0_0.map\r\n\r\n")).ToArray());
                File.WriteAllBytes(Path.Combine(map, "tile_0_0.map"), System.Text.Encoding.Unicode.GetPreamble()
                    .Concat(System.Text.Encoding.Unicode.GetBytes("[version]\r\n14\r\n\r\n")).ToArray());
                var session = OmsiMapSession.OpenProject(Path.Combine(map, "global.cfg"), Path.Combine(root, "Projects", "Exc.omproj"));
                var plan = new OmsiTripPlan();
                plan.Problems.Add(OmsiPlanProblem.OfStop(OmsiPlanProblemCode.StopHasNoLane, "Gaussdorf"));
                var exception = Assert.Throws<OmsiTripPlanException>(() => session.ReplaceTripStops("T1", plan, null));
                Assert.AreEqual("T1", exception.TripName);
                Assert.AreEqual(OmsiPlanProblemCode.StopHasNoLane, exception.Problems.Single().Code);
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
        }

        [Test]
        public void EveryLanguageHasTheTripProblemsTextsWithOnlyTheirArguments()
        {
            var locales = LocaleLoader.List(Path.Combine(RepositoryRoot(), "locales"), null);
            Assert.GreaterOrEqual(locales.Count, 8);
            foreach (var info in locales)
            {
                var messages = LocaleLoader.Load(info).Messages;
                foreach (var key in new[] { "line.status.trip_problems", "line.status.trip_problems_unnamed" })
                {
                    Assert.IsTrue(messages.ContainsKey(key), info.Code + " lacks " + key);
                    var names = MessageFormat.Parse(messages[key]).Arguments.Select(argument => argument.Name).ToList();
                    CollectionAssert.IsSubsetOf(names, new[] { "trip", "problems" }, info.Code + " " + key);
                    Assert.Contains("problems", names, info.Code + " " + key);
                }
            }
        }

        [Test]
        public void AnOtherExceptionKeepsItsMessageAsTheReason()
        {
            Assert.AreEqual("disk full", LinePlanProblems.Reason(new IOException("disk full")));
        }
    }
}
