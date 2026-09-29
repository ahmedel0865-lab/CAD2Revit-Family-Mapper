using System.Collections.Generic;
using CAD2Revit.Core;
using Xunit;

namespace CAD2Revit.Core.Tests
{
    public class SlabTests
    {
        [Theory]
        [InlineData("Slab (above)", HostMode.SlabAbove)]
        [InlineData("slab above", HostMode.SlabAbove)]
        [InlineData("slab", HostMode.SlabAbove)]
        [InlineData("Slab (below)", HostMode.SlabBelow)]
        [InlineData("slab below", HostMode.SlabBelow)]
        [InlineData("floor", HostMode.SlabBelow)]
        public void ParsesSlabHosts(string text, HostMode expected) =>
            Assert.Equal(expected, Mapping.ParseHost(text));

        [Fact]
        public void SlabRowsRoundTripThroughMappingFile()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cad2revit_slab_" + System.Guid.NewGuid() + ".csv");
            Mapping.Save(path, new[]
            {
                new MapRow { Block = "LIGHT", Family = "L", TypeName = "T", Host = HostMode.SlabAbove },
                new MapRow { Block = "FLOOR-BOX", Family = "F", TypeName = "T", Host = HostMode.SlabBelow },
            });
            var m = Mapping.Load(path);
            System.IO.File.Delete(path);
            Assert.Empty(m.Errors);
            Assert.Equal(HostMode.SlabAbove, m.Rows["LIGHT"].Host);
            Assert.Equal(HostMode.SlabBelow, m.Rows["FLOOR-BOX"].Host);
        }

        [Fact]
        public void AboveSearchIsLevelToLevelPlusTolerance()
        {
            // Level at 3000 mm, next level at 6500 mm, 500 mm tolerance -> 4000 mm.
            double ft = SlabSearch.AboveDistanceFt(3000 / 304.8, 6500 / 304.8, 500, 6000);
            Assert.Equal(4000, ft * 304.8, 3);
            // Top level: the fallback distance.
            Assert.Equal(6000, SlabSearch.AboveDistanceFt(0, null, 500, 6000) * 304.8, 3);
            Assert.Equal(800, SlabSearch.BelowDistanceFt(500) * 304.8, 3);
        }

        [Fact]
        public void FormatsHostLabels()
        {
            Assert.Equal("Floor: 250mm RC Slab - Third Floor (linked: STR.rvt)",
                HostLabels.Format("Floors", "250mm RC Slab", "Third Floor", "STR.rvt"));
            Assert.Equal("Ceiling: 600x600 Grid - Level 1", HostLabels.Format("Ceilings", "600x600 Grid", "Level 1", null));
            Assert.Equal("Wall (linked)", HostLabels.Format("Walls", "", null, ""));
        }

        static PlacementResult R(Status s, string host) => new PlacementResult { BlockName = "B", Status = s, Host = host };

        [Fact]
        public void SummarizesDetectedHostsPerBlock()
        {
            const string slab = "Floor: 250mm RC Slab - Third Floor (linked: STR.rvt)";
            Assert.Equal(slab, HostLabels.Summarize(new[] { R(Status.Placed, slab), R(Status.Placed, slab) }));
            var mixed = new List<PlacementResult>
            {
                R(Status.Placed, slab), R(Status.Placed, slab), R(Status.Placed, slab),
                R(Status.Placed, "Reference plane CAD2Revit_Second Floor_+3250mm"),
                R(Status.Duplicate, ""), R(Status.Failed, ""),
            };
            Assert.Equal(slab + " (3) · Reference plane CAD2Revit_Second Floor_+3250mm (1) · 1 duplicate(s) · 1 failed",
                HostLabels.Summarize(mixed));
            Assert.Equal("", HostLabels.Summarize(new PlacementResult[0]));
        }

        [Fact]
        public void SlabAboveRangeIgnoresTheLevelAbove()
        {
            Assert.Equal(5000 / 304.8, SlabSearch.RangeFt(5000), 9);
            // A slab at 5500 mm is outside the default 5000 mm range, even with the level above at 7000.
            var idx = new FaceIndex<string>();
            idx.Add(new List<double[]> { new[] { 0.0, 0, 10, 0, 10, 10, 0, 10 } }, 0, 0, -1, 0, 0, 5500 / 304.8, "high slab");
            Assert.Null(idx.Nearest(5, 5, 0.01, true, SlabSearch.RangeFt(5000), f => f.Nz < -0.5, out _));
            Assert.NotNull(idx.Nearest(5, 5, 0.01, true, SlabSearch.RangeFt(6000), f => f.Nz < -0.5, out _));
        }

        [Fact]
        public void FallbackMessageAndPlaneName()
        {
            Assert.Equal("No slab within 5000 mm - placed on reference plane at +3000 mm",
                         SlabSearch.FallbackMessage(5000, 3000, levelBased: false));
            Assert.Equal("No slab within 4500 mm - placed level-based at +2750.5 mm",
                         SlabSearch.FallbackMessage(4500, 2750.5, levelBased: true));
            Assert.Equal("CAD2Revit_Level 1_+3000mm", RefPlaneNames.For("Level 1", 3000, Facing.Down));
        }
    }
}
