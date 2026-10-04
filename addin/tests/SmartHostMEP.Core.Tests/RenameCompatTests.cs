using System;
using System.IO;
using SmartHostMEP.Core;
using Xunit;

namespace SmartHostMEP.Core.Tests
{
    /// <summary>The rename to SmartHost MEP: new names, and what older versions created still recognised.</summary>
    public class RenameCompatTests
    {
        [Fact]
        public void NewAndOldReferencePlaneNames()
        {
            Assert.Equal("SmartHost_Level 1_+3000mm", RefPlaneNames.For("Level 1", 3000, Facing.Down));
            Assert.Equal("CAD2Revit_Level 1_+3000mm", RefPlaneNames.OldFor("Level 1", 3000, Facing.Down));
            Assert.Equal("CAD2Revit_Level 1_+0mm_Up", RefPlaneNames.OldFor("Level 1", 0, Facing.Up));
        }

        [Fact]
        public void NewVerticalPlaneNamesAndOldNumbersCount()
        {
            Assert.Equal("SmartHost_V_Level 1_3", VerticalPlacement.PlaneName("Level 1", 3));
            Assert.Equal(3, VerticalPlacement.PlaneNumber("SmartHost_V_Level 1_3", "Level 1"));
            // Old planes keep their numbers, so new planes never take a number already used.
            Assert.Equal(12, VerticalPlacement.PlaneNumber("CAD2Revit_V_Level 1_12", "Level 1"));
            Assert.Equal(0, VerticalPlacement.PlaneNumber("CAD2Revit_V_Level 2_12", "Level 1"));
        }

        [Theory]
        [InlineData("SmartHost: SOCKET", "SOCKET")]
        [InlineData("CAD: SOCKET", "SOCKET")]
        [InlineData("CAD2Revit: Host = Reference Plane | CAD: SOCKET", "SOCKET")]
        [InlineData("SmartHost: Host = Reference Plane | SmartHost: SOCKET", "SOCKET")]
        [InlineData("SmartHost: Host = Reference Plane", null)]
        [InlineData("CAD2Revit: Host = Reference Plane", null)]
        [InlineData("installed by electrician", null)]
        public void CommentsOfEveryVersionAreRecognised(string comments, string block) =>
            Assert.Equal(block, ExistingIndex.BlockFromComment(comments));

        [Fact]
        public void BlockPlacedByTheOldVersionIsADuplicate()
        {
            var ix = new ExistingIndex(0.1);
            ix.Add(0, "CAD: SOCKET", 10, 20, 3);
            Assert.True(ix.Contains(99, "SOCKET", 10, 20, 0, 10));
        }

        [Fact]
        public void CommentTextAndTag()
        {
            Assert.Equal("SmartHost: Host = Reference Plane", NeedsReview.CommentText);
            Assert.Equal("SmartHost: ", ExistingIndex.Tag);
        }

        [Fact]
        public void SettingsAndMappingsAreCopiedOnceFromTheOldFolder()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "shm_" + Guid.NewGuid().ToString("N"));
            var oldRoot = Path.Combine(tmp, "CAD2Revit");
            var newRoot = Path.Combine(tmp, "SmartHostMEP");
            try
            {
                Directory.CreateDirectory(Path.Combine(oldRoot, "projects"));
                File.WriteAllText(Path.Combine(oldRoot, "settings.ini"), "DuplicateToleranceMm = 75");
                File.WriteAllText(Path.Combine(oldRoot, "projects", "Tower_1234abcd.xlsx"), "x");

                Assert.True(AppStorage.Migrate(oldRoot, newRoot));
                Assert.Equal("DuplicateToleranceMm = 75", File.ReadAllText(Path.Combine(newRoot, "settings.ini")));
                Assert.True(File.Exists(Path.Combine(newRoot, "projects", "Tower_1234abcd.xlsx")));
                Assert.True(File.Exists(Path.Combine(oldRoot, "settings.ini")));      // old folder untouched

                // Once only: later changes in the new folder are never overwritten.
                File.WriteAllText(Path.Combine(newRoot, "settings.ini"), "DuplicateToleranceMm = 20");
                Assert.False(AppStorage.Migrate(oldRoot, newRoot));
                Assert.Equal("DuplicateToleranceMm = 20", File.ReadAllText(Path.Combine(newRoot, "settings.ini")));
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        [Fact]
        public void NoOldFolderNothingToDo()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "shm_" + Guid.NewGuid().ToString("N"));
            Assert.False(AppStorage.Migrate(Path.Combine(tmp, "CAD2Revit"), Path.Combine(tmp, "SmartHostMEP")));
            Assert.False(Directory.Exists(tmp));
        }
    }
}
