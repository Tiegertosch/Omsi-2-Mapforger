using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiParkListTests
    {
        [Test]
        public void AListFileGivesItsTrimmedNonEmptyLines()
        {
            var entries = OmsiParkList.Parse(Encoding.ASCII.GetBytes("Vehicles\\A\\parked_a.sco\r\n\r\n  Vehicles\\B\\b_parked.sco  \r\nVehicles\\A\\parked_a.sco"));
            CollectionAssert.AreEqual(new[] { "Vehicles\\A\\parked_a.sco", "Vehicles\\B\\b_parked.sco", "Vehicles\\A\\parked_a.sco" }, entries);
            Assert.AreEqual(0, OmsiParkList.Parse(null).Count);
        }

        [Test]
        public void ChoosingCarsKeepsTheWeightOfThoseAlreadyListedAndDropsTheOthers()
        {
            var current = new[] { "V\\A.sco", "V\\A.sco", "V\\B.sco", "V\\C.sco" };
            var merged = OmsiParkList.Merge(current, new[] { "V\\C.sco", "v\\a.sco", "V\\D.sco" });
            // C once, A twice (its weight, matched without regard to case), the new D once; B is gone.
            CollectionAssert.AreEqual(new[] { "V\\C.sco", "v\\a.sco", "v\\a.sco", "V\\D.sco" }, merged);
            Assert.AreEqual(0, OmsiParkList.Merge(current, new string[0]).Count);
        }

        [Test]
        public void TheCarsOfferedAreThoseWithAnAiCarBesideThemAndThoseAlreadyListed()
        {
            var root = Path.Combine(Path.GetTempPath(), "mapforge-parklist-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Vehicles", "Golf"));
                Directory.CreateDirectory(Path.Combine(root, "Vehicles", "Lonely"));
                File.WriteAllText(Path.Combine(root, "Vehicles", "Golf", "ai_golf.bus"), "x");
                File.WriteAllText(Path.Combine(root, "Vehicles", "Golf", "parked_golf.sco"), "x");
                File.WriteAllText(Path.Combine(root, "Vehicles", "Golf", "golf.sco"), "x");
                File.WriteAllText(Path.Combine(root, "Vehicles", "Lonely", "parked_lonely.sco"), "x");
                var offered = OmsiParkList.Candidates(root, new[] { "Vehicles\\Addon\\parked_addon.sco", "vehicles\\golf\\PARKED_GOLF.sco" });
                CollectionAssert.AreEqual(new[] { "Vehicles\\Golf\\parked_golf.sco", "Vehicles\\Addon\\parked_addon.sco" }, offered);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }
    }
}
