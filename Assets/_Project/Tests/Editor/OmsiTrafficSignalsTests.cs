using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiTrafficSignalsTests
    {
        // The program of Spandau's Einm_CharlCh_Polizei as OMSI's editor wrote it (long decimals, a comment line, an
        // unknown section), cut down to three of its lights, with five paths: a [path_2] among them and three bound to lights.
        private static readonly string Crossing =
            "comment line kept as it is\r\n" +
            "[friendlyname]\r\nTest crossing\r\n\r\n" +
            "[traffic_lights_group]\r\n81.9999966515692\r\n\r\n" +
            "[traffic_light]\r\nMain\r\n\r\n" +
            "[phase]\r\n3\r\n1.9999999686135\r\n\r\n[phase]\r\n6\r\n51.0000016048259\r\n\r\n[phase]\r\n9\r\n2.99999995292025\r\n\r\n[phase]\r\n0\r\n0\r\n\r\n" +
            "[traffic_light]\r\nSide\r\n\r\n" +
            "[phase]\r\n0\r\n63.0000027908964\r\n\r\n[phase]\r\n3\r\n1.9999999686135\r\n\r\n[phase]\r\n6\r\n11.000000514569\r\n\r\n[phase]\r\n9\r\n2.99999995292025\r\n\r\n[phase]\r\n0\r\n0\r\n\r\n" +
            "[traffic_light]\r\nMain_Ped\r\n\r\n" +
            "[phase]\r\n6\r\n35.0000011687231\r\n\r\n[phase]\r\n0\r\n0\r\n\r\n[approachdist]\r\n0\r\n\r\n" +
            "[traffic_light_stop]\r\n1\r\n34.0\r\n1\r\n\r\n" +
            "[some_unknown_section]\r\nwhatever\r\n\r\n" +
            Path(0) + "[use_traffic_light]\r\n0\r\n\r\n" +
            Path(1) +
            Path2(2) + "[use_traffic_light]\r\n1\r\n\r\n[crossingproblem]\r\n\r\n" +
            Path(3) +
            Path(4) + "[use_traffic_light]\r\n2\r\n\r\n";

        private static string Path(int index)
        {
            return "[path]\r\n" + index + "\r\n0\r\n0\r\n0\r\n0\r\n10\r\n0\r\n0\r\n0\r\n3\r\n0\r\n0\r\n\r\n";
        }

        private static string Path2(int index)
        {
            return "[path_2]\r\n" + index + "\r\n0\r\n0\r\n0\r\n0\r\n10\r\n0\r\n0\r\n0\r\n3\r\n0\r\n0\r\n0\r\n0\r\n\r\n";
        }

        private static byte[] Bytes(OmsiTextEncoding encoding = OmsiTextEncoding.Windows1252)
        {
            return OmsiTextFile.Encode(Crossing, encoding);
        }

        private static OmsiRecordFile Parse(byte[] bytes)
        {
            return OmsiRecordFile.Parse(bytes, OmsiSceneryRecordLayouts.Instance);
        }

        private static List<string> Lines(byte[] bytes)
        {
            return OmsiTextFile.Decode(bytes).Split('\n').ToList();
        }

        [Test]
        public void TheProgramIsReadAsOmsiStoresIt()
        {
            var program = OmsiSignalProgram.Read(Bytes());

            Assert.AreEqual(81.9999966515692, program.CycleSeconds.Value, 1e-9);
            CollectionAssert.AreEqual(new[] { "Main", "Side", "Main_Ped" }, program.Lights.Select(light => light.Name).ToArray());
            CollectionAssert.AreEqual(new[] { 3, 6, 9, 0 }, program.Lights[0].Phases.Select(phase => phase.State).ToArray());
            Assert.AreEqual(51.0000016048259, program.Lights[0].Phases[1].Seconds, 1e-9);
            Assert.AreEqual(0, program.Lights[2].ApproachMeters);
            Assert.IsNull(program.Lights[0].ApproachMeters);
            Assert.AreEqual(1, program.Holds.Count);
            Assert.AreEqual(1, program.Holds[0].Light);
            Assert.AreEqual(34.0, program.Holds[0].Seconds);
            Assert.IsTrue(program.Holds[0].IfNoRequest);
            Assert.IsNull(program.Holds[0].JumpTo);
            // [path] and [path_2] are counted alike, as openOMSI does.
            CollectionAssert.AreEqual(new[] { 0, -1, 1, -1, 2 }, program.PathLights);
        }

        [Test]
        public void TheStateFollowsThePhasesAndTheLastHoldsUntilTheCycleEnds()
        {
            var program = OmsiSignalProgram.Read(Bytes());

            Assert.AreEqual(3, program.StateAt(0, 1.0));
            Assert.AreEqual(6, program.StateAt(0, 2.5));
            Assert.AreEqual(9, program.StateAt(0, 54.0));
            Assert.AreEqual(0, program.StateAt(0, 60.0));
            Assert.AreEqual(0, program.StateAt(1, 62.0));
            Assert.AreEqual(3, program.StateAt(1, 64.0));
            Assert.AreEqual(6, program.StateAt(1, 70.0));
            // The cycle wraps round; times before the start count back from its end.
            Assert.AreEqual(3, program.StateAt(0, 82.0 + 1.0));
            Assert.AreEqual(0, program.StateAt(0, -1.0));
            Assert.AreEqual(53.0000015734394, program.PhaseStart(0, 2), 1e-9);
            // A light without phases, or one that does not exist, lets traffic go, as openOMSI's lamps do.
            Assert.AreEqual(6, program.StateAt(7, 10));
        }

        [Test]
        public void WithoutAGroupLengthTheCycleIsTheLongestLight()
        {
            var program = new OmsiSignalProgram();
            program.Lights.Add(new OmsiSignalLight { Name = "A" });
            program.Lights[0].Phases.Add(new OmsiSignalPhase(6, 20));
            program.Lights[0].Phases.Add(new OmsiSignalPhase(0, 10));
            program.Lights.Add(new OmsiSignalLight { Name = "B" });
            program.Lights[1].Phases.Add(new OmsiSignalPhase(0, 45));

            Assert.AreEqual(45, program.CycleLength);
            program.CycleSeconds = 0;
            Assert.AreEqual(45, program.CycleLength);
            program.CycleSeconds = 60;
            Assert.AreEqual(60, program.CycleLength);
            Assert.AreEqual(1, new OmsiSignalProgram().CycleLength);
        }

        [Test]
        public void TheAspectsFollowTheStockCarHead()
        {
            Assert.AreEqual(OmsiSignalAspect.Red, OmsiSignalAspects.Of(0));
            Assert.AreEqual(OmsiSignalAspect.Red, OmsiSignalAspects.Of(2));
            Assert.AreEqual(OmsiSignalAspect.RedYellow, OmsiSignalAspects.Of(3));
            Assert.AreEqual(OmsiSignalAspect.Green, OmsiSignalAspects.Of(6));
            Assert.AreEqual(OmsiSignalAspect.GreenYellow, OmsiSignalAspects.Of(8));
            Assert.AreEqual(OmsiSignalAspect.Yellow, OmsiSignalAspects.Of(9));
            Assert.AreEqual(OmsiSignalAspect.Dark, OmsiSignalAspects.Of(12));
            Assert.AreEqual(OmsiSignalAspect.Dark, OmsiSignalAspects.Of(-1));
            Assert.IsTrue(OmsiSignalAspects.AllowsGo(6));
            Assert.IsTrue(OmsiSignalAspects.AllowsGo(12));
            Assert.IsFalse(OmsiSignalAspects.AllowsGo(9));
            foreach (OmsiSignalAspect aspect in Enum.GetValues(typeof(OmsiSignalAspect)))
                Assert.AreEqual(aspect, OmsiSignalAspects.Of(OmsiSignalAspects.StateOf(aspect)));
        }

        [TestCase(OmsiTextEncoding.Windows1252)]
        [TestCase(OmsiTextEncoding.Utf16LittleEndian)]
        [TestCase(OmsiTextEncoding.Utf8WithByteOrderMark)]
        public void WritingAnUnchangedProgramKeepsTheFileByteForByte(OmsiTextEncoding encoding)
        {
            var bytes = Bytes(encoding);
            var file = Parse(bytes);
            Assert.IsTrue(file.IsExactRoundTrip);

            OmsiSignalProgram.Read(file).WriteTo(file);

            CollectionAssert.AreEqual(bytes, file.ToBytes());
        }

        [Test]
        public void ChangingOnePhaseChangesOneLine()
        {
            var bytes = Bytes();
            var file = Parse(bytes);
            var program = OmsiSignalProgram.Read(file);
            program.Lights[1].Phases[2].Seconds = 15;

            program.WriteTo(file);

            var before = Lines(bytes);
            var after = Lines(file.ToBytes());
            Assert.AreEqual(before.Count, after.Count);
            var changed = Enumerable.Range(0, before.Count).Where(index => before[index] != after[index]).ToList();
            Assert.AreEqual(1, changed.Count);
            Assert.AreEqual("15\r", after[changed[0]]);
            Assert.AreEqual(15, OmsiSignalProgram.Read(file).Lights[1].Phases[2].Seconds);
        }

        [Test]
        public void BindingsAreAddedAfterTheirPathChangedAndTakenAway()
        {
            var file = Parse(Bytes());
            var program = OmsiSignalProgram.Read(file);
            program.PathLights[1] = 2;
            program.PathLights[2] = 0;
            program.PathLights[4] = -1;

            program.WriteTo(file);

            CollectionAssert.AreEqual(new[] { 0, 2, 0, -1, -1 }, OmsiSignalProgram.Read(file).PathLights);
            var names = file.Records.Select(record => record.Name.ToLowerInvariant()).ToList();
            var paths = names.Select((name, index) => (name, index)).Where(x => x.name == "path" || x.name == "path_2").Select(x => x.index).ToList();
            // The new binding stands straight after the second path; the [crossingproblem] of the third path stays after its binding.
            Assert.AreEqual("use_traffic_light", names[paths[1] + 1]);
            Assert.AreEqual("use_traffic_light", names[paths[2] + 1]);
            Assert.AreEqual("crossingproblem", names[paths[2] + 2]);
            Assert.AreEqual(3, names.Count(name => name == "use_traffic_light"));
            Assert.IsTrue(file.ToBytes().Length > 0);
        }

        [Test]
        public void AddingALightWritesTheProgramAnewAndKeepsTheRest()
        {
            var bytes = Bytes();
            var file = Parse(bytes);
            var program = OmsiSignalProgram.Read(file);
            var light = new OmsiSignalLight { Name = "Side_Ped" };
            light.Phases.Add(new OmsiSignalPhase(0, 65));
            light.Phases.Add(new OmsiSignalPhase(6, 8));
            light.Phases.Add(new OmsiSignalPhase(0, 0));
            program.Lights.Add(light);
            program.Holds.Add(new OmsiSignalHold { Light = 3, Seconds = 70, IfNoRequest = false, JumpTo = 80 });

            program.WriteTo(file);

            var again = OmsiSignalProgram.Read(file);
            CollectionAssert.AreEqual(new[] { "Main", "Side", "Main_Ped", "Side_Ped" }, again.Lights.Select(l => l.Name).ToArray());
            Assert.AreEqual(2, again.Holds.Count);
            Assert.AreEqual(80, again.Holds[1].JumpTo);
            CollectionAssert.AreEqual(new[] { 0, -1, 1, -1, 2 }, again.PathLights);
            var text = OmsiTextFile.Decode(file.ToBytes());
            StringAssert.StartsWith("comment line kept as it is\r\n[friendlyname]\r\nTest crossing\r\n", text);
            StringAssert.Contains("[some_unknown_section]\r\nwhatever\r\n", text);
            // The program stands where it stood: before the unknown section and the paths.
            Assert.Less(text.IndexOf("[traffic_light_jump]", StringComparison.Ordinal), text.IndexOf("[some_unknown_section]", StringComparison.Ordinal));
        }

        [Test]
        public void AProgramCanBeAddedToAnObjectWithoutOneAndTakenAwayAgain()
        {
            var plain = "[friendlyname]\r\nPlain\r\n\r\n" + Path(0) + Path(1);
            var bytes = OmsiTextFile.Encode(plain, OmsiTextEncoding.Windows1252);
            var file = Parse(bytes);
            var program = OmsiSignalProgram.Read(file);
            Assert.IsFalse(program.HasProgram);
            Assert.IsNull(program.CycleSeconds);

            program.CycleSeconds = 60;
            program.Lights.Add(new OmsiSignalLight { Name = "A" });
            program.Lights[0].Phases.Add(new OmsiSignalPhase(6, 30));
            program.Lights[0].Phases.Add(new OmsiSignalPhase(0, 0));
            program.PathLights[1] = 0;
            program.WriteTo(file);

            var text = OmsiTextFile.Decode(file.ToBytes());
            Assert.Less(text.IndexOf("[traffic_lights_group]", StringComparison.Ordinal), text.IndexOf("[path]", StringComparison.Ordinal));
            var again = OmsiSignalProgram.Read(file);
            Assert.AreEqual(60, again.CycleSeconds);
            CollectionAssert.AreEqual(new[] { -1, 0 }, again.PathLights);

            var none = new OmsiSignalProgram();
            none.PathLights.AddRange(new[] { -1, -1 });
            none.WriteTo(file);
            CollectionAssert.AreEqual(bytes, file.ToBytes());
        }

        [Test]
        public void WhatCannotBeWrittenIsRefused()
        {
            var file = Parse(Bytes());
            var program = OmsiSignalProgram.Read(file);
            program.PathLights[0] = 5;
            Assert.Throws<ArgumentException>(() => program.WriteTo(file));

            program = OmsiSignalProgram.Read(file);
            program.PathLights.Add(-1);
            Assert.Throws<ArgumentException>(() => program.WriteTo(file));

            program = OmsiSignalProgram.Read(file);
            program.Lights[0].Phases[0].Seconds = -2;
            Assert.Throws<ArgumentException>(() => program.WriteTo(file));

            program = OmsiSignalProgram.Read(file);
            program.Lights[0].Name = "[path]";
            Assert.Throws<ArgumentException>(() => program.WriteTo(file));

            program = OmsiSignalProgram.Read(file);
            program.Holds[0].Light = 9;
            Assert.Throws<ArgumentException>(() => program.WriteTo(file));
            // Nothing was written by the refused attempts.
            CollectionAssert.AreEqual(Bytes(), file.ToBytes());
        }

        [Test]
        public void ASceneryObjectKnowsWhenItIsASignalHead()
        {
            Assert.IsTrue(OmsiSceneryObject.Parse(OmsiTextFile.Encode("[trafficlight]\r\n\r\n[mesh]\r\nampel.o3d\r\n", OmsiTextEncoding.Windows1252)).IsTrafficLight);
            Assert.IsFalse(OmsiSceneryObject.Parse(Bytes()).IsTrafficLight);
        }

        [TestCase("0", 0)]
        [TestCase(" 3 ", 3)]
        [TestCase("-1", null)]
        [TestCase("Main", null)]
        [TestCase("", null)]
        public void AHeadNamesItsLightWithItsFirstLabel(string label, int? light)
        {
            Assert.AreEqual(light, OmsiSignalHead.LightOf(new[] { label, "other" }));
        }

        [Test]
        public void TheHeadsOfACrossingAreFoundByTheirVariableParent()
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mapforge-signals-" + Guid.NewGuid().ToString("N"));
            try
            {
                var map = System.IO.Path.Combine(root, "maps", "Signals");
                Directory.CreateDirectory(map);
                File.WriteAllBytes(System.IO.Path.Combine(map, "global.cfg"), OmsiTextFile.Encode(
                    "[name]\r\nSignals\r\n\r\n[map]\r\n0\r\n0\r\ntile_0_0.map\r\n\r\n[map]\r\n1\r\n0\r\ntile_1_0.map\r\n\r\n[map]\r\n3\r\n0\r\ntile_3_0.map\r\n",
                    OmsiTextEncoding.Utf16LittleEndian));
                // Tile 0, 0: the crossing (1), a head for light 1 (2), a head without a number (3), a lamp that is no head (4).
                File.WriteAllBytes(System.IO.Path.Combine(map, "tile_0_0.map"), OmsiTextFile.Encode(
                    "[version]\r\n14\r\n\r\n" +
                    Object("Sceneryobjects\\Test\\crossing.sco", 1, 0) +
                    Object("Sceneryobjects\\Test\\head.sco", 2, 1, "1") + "[varparent]\r\n1\r\n\r\n" +
                    Object("Sceneryobjects\\Test\\head.sco", 3, 1, "Main") + "[varparent]\r\n1\r\n\r\n" +
                    Object("Sceneryobjects\\Test\\lamp.sco", 4, 1, "0") + "[varparent]\r\n1\r\n\r\n", OmsiTextEncoding.Utf16LittleEndian));
                // Tile 1, 0 next to it holds a head for light 0; tile 3, 0 is too far away.
                File.WriteAllBytes(System.IO.Path.Combine(map, "tile_1_0.map"), OmsiTextFile.Encode(
                    "[version]\r\n14\r\n\r\n" + Object("Sceneryobjects\\Test\\head.sco", 5, 1, "0") + "[varparent]\r\n1\r\n\r\n", OmsiTextEncoding.Utf16LittleEndian));
                File.WriteAllBytes(System.IO.Path.Combine(map, "tile_3_0.map"), OmsiTextFile.Encode(
                    "[version]\r\n14\r\n\r\n" + Object("Sceneryobjects\\Test\\head.sco", 6, 1, "0") + "[varparent]\r\n1\r\n\r\n", OmsiTextEncoding.Utf16LittleEndian));
                var summary = OmsiMapReader.Read(System.IO.Path.Combine(map, "global.cfg"));
                var crossingTile = summary.Tiles.First(tile => tile.X == 0 && tile.Y == 0);

                var heads = OmsiSignalHead.Of(summary, crossingTile, 1, path => path.EndsWith("head.sco", StringComparison.OrdinalIgnoreCase));

                CollectionAssert.AreEquivalent(new[] { 2, 3, 5 }, heads.Select(head => head.Id).ToArray());
                Assert.AreEqual(1, heads.First(head => head.Id == 2).LightIndex);
                Assert.IsNull(heads.First(head => head.Id == 3).LightIndex);
                Assert.AreEqual(0, heads.First(head => head.Id == 5).LightIndex);
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
        }

        /// <summary>An [object] with labels: x, y, height, heading, then the label count and lines (tile version 14).</summary>
        private static string Object(string asset, int id, int x, params string[] labels)
        {
            return "[object]\r\n0\r\n" + asset + "\r\n" + id + "\r\n" + (100 + x * 10) + "\r\n100\r\n0\r\n0\r\n0\r\n0\r\n" + labels.Length + "\r\n" +
                   string.Concat(labels.Select(label => label + "\r\n")) + "\r\n";
        }
    }
}
