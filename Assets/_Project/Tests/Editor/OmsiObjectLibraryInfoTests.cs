using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiObjectLibraryInfoTests
    {
        [Test]
        public void ReadsTheGroupsOfASceneryObject()
        {
            var text = "[friendlyname]\r\nRow house\r\n\r\n[groups]\r\n3\r\nBuildings\r\nResidential\r\nRow House\r\n\r\n[mesh]\r\nmodel\\house.o3d\r\n";
            var groups = OmsiSceneryObjectGroups.Read(Encoding.UTF8.GetBytes(text));

            CollectionAssert.AreEqual(new[] { "Buildings", "Residential", "Row House" }, groups);
            CollectionAssert.IsEmpty(OmsiSceneryObjectGroups.Read(Encoding.UTF8.GetBytes("[mesh]\r\nmodel\\a.o3d\r\n")));
            // A count larger than the list stops at the next section; a group named twice counts once.
            CollectionAssert.AreEqual(new[] { "Trees" },
                OmsiSceneryObjectGroups.Read(Encoding.UTF8.GetBytes("[groups]\n4\nTrees\ntrees\n[mesh]\nx.o3d\n")));
        }
    }
}
