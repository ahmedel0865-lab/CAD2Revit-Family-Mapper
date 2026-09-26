using System;
using System.Collections.Generic;
using System.IO;
using CAD2Revit.Core;
using Xunit;

namespace CAD2Revit.Core.Tests
{
    public class RefPlaneTests
    {
        [Theory]
        [InlineData("Level 1", 2800, Facing.Down, "CAD2Revit_Level 1_+2800mm")]
        [InlineData("Level 1", 2800.04, Facing.Down, "CAD2Revit_Level 1_+2800mm")]
        [InlineData("Level 1", 2750.5, Facing.Down, "CAD2Revit_Level 1_+2750.5mm")]
        [InlineData("Level 1", 0, Facing.Up, "CAD2Revit_Level 1_+0mm_Up")]
        [InlineData("B1", -300, Facing.Down, "CAD2Revit_B1_-300mm")]
        [InlineData("L01: Ground", 3000, Facing.Down, "CAD2Revit_L01_ Ground_+3000mm")]
        public void Names(string level, double elev, Facing facing, string expected)
        {
            Assert.Equal(expected, RefPlaneNames.For(level, elev, facing));
        }

        [Theory]
        [InlineData("Reference Plane (auto-create)", HostMode.RefPlane)]
        [InlineData("reference plane", HostMode.RefPlane)]
        [InlineData("RefPlane", HostMode.RefPlane)]
        [InlineData("None (level-based)", HostMode.None)]
        [InlineData("Ceiling", HostMode.Ceiling)]
        [InlineData("Wall", HostMode.Wall)]
        [InlineData("Face (ceiling/slab/roof)", HostMode.Face)]
        [InlineData("Vertical plane (no wall)", HostMode.Vertical)]
        [InlineData("vertical", HostMode.Vertical)]
        public void HostLabelsParse(string text, HostMode expected)
        {
            Assert.Equal(expected, Mapping.ParseHost(text));
        }

        [Fact]
        public void EveryDisplayLabelRoundTrips()
        {
            foreach (var kv in Mapping.HostDisplay)
                Assert.Equal(kv.Key, Mapping.ParseHost(kv.Value));
            foreach (HostMode h in Enum.GetValues(typeof(HostMode)))
                Assert.True(Mapping.HostDisplay.ContainsKey(h), h + " has no dropdown label");
        }

        [Fact]
        public void FacingIsSavedAndLoaded()
        {
            var path = Path.Combine(Path.GetTempPath(), "c2r_" + Guid.NewGuid().ToString("N") + ".xlsx");
            Mapping.Save(path, new List<MapRow>
            {
                new MapRow { Block = "LIGHT", Family = "L", TypeName = "T", OffsetMm = 2800, Host = HostMode.RefPlane, Facing = Facing.Down },
                new MapRow { Block = "FLOOR-BOX", Family = "F", TypeName = "T", OffsetMm = 0, Host = HostMode.RefPlane, Facing = Facing.Up },
            });
            var m = Mapping.Load(path);
            File.Delete(path);
            Assert.Empty(m.Errors);
            Assert.Equal(HostMode.RefPlane, m.Rows["LIGHT"].Host);
            Assert.Equal(Facing.Down, m.Rows["LIGHT"].Facing);
            Assert.Equal(Facing.Up, m.Rows["FLOOR-BOX"].Facing);
        }

        [Fact]
        public void OldFilesWithoutFacingDefaultToDown()
        {
            var m = Mapping.Parse(new List<Dictionary<string, string>>
            {
                new Dictionary<string, string> { ["CAD_Block_Name"] = "A", ["Revit_Family_Name"] = "F", ["Revit_Type_Name"] = "T", ["Host_Type"] = "ceiling" },
            });
            Assert.Empty(m.Errors);
            Assert.Equal(Facing.Down, m.Rows["A"].Facing);
            Assert.Null(Mapping.ParseFacing("sideways"));
        }
    }
}
