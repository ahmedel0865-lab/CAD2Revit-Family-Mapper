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

        const double Mm = 304.8;

        /// <summary>Square 10 x 10 ft underside, z = baseMm + x * risePerFt (mm), normal pointing down.</summary>
        static void AddUnderside(FaceIndex<string> idx, string name, double baseMm, double risePerFtMm)
        {
            double b = risePerFtMm / Mm, len = System.Math.Sqrt(b * b + 1);
            var loop = new[] { 0.0, 0, 10, 0, 10, 10, 0, 10 };
            idx.Add(new List<double[]> { loop }, b / len, 0, -1 / len, 0, 0, baseMm / Mm, name);
        }

        [Fact]
        public void SlabAboveFollowsASlopedUndersideAboveTheNextLevel()
        {
            // Level 0, next level 3500: the normal window is 3500 + 500 = 4000 mm.
            double window = SlabSearch.AboveDistanceFt(0, 3500 / Mm, 500, 6000);
            double sloped = SlabSearch.SlopedAboveDistanceFt(window, 6000);
            var idx = new FaceIndex<string>();
            AddUnderside(idx, "sloped", 3000, 160);   // 3000 mm at x=0 up to 4600 mm at x=10 ft

            var low = SlabSearch.Nearest(idx, 1, 5, 0, true, window, sloped, out var zLow);
            Assert.Equal("sloped", low.Payload);
            Assert.Equal(3160, zLow * Mm, 3);

            // High end is above the 4000 mm window: still found, at the sloped height.
            var high = SlabSearch.Nearest(idx, 9, 5, 0, true, window, sloped, out var zHigh);
            Assert.Equal("sloped", high?.Payload);
            Assert.Equal(4440, zHigh * Mm, 3);
            Assert.True(SlabSearch.IsSloped(high.Nz));

            // Without the sloped window (old behaviour) the high end was missed.
            Assert.Null(SlabSearch.Nearest(idx, 9, 5, 0, true, window, 0, out _));
        }

        [Fact]
        public void FlatSlabTwoFloorsUpIsNeverUsed()
        {
            double window = SlabSearch.AboveDistanceFt(0, 3500 / Mm, 500, 6000);
            double sloped = SlabSearch.SlopedAboveDistanceFt(window, 6000);
            var idx = new FaceIndex<string>();
            AddUnderside(idx, "flat two floors up", 5500, 0);
            Assert.False(SlabSearch.IsSloped(-1));
            Assert.Null(SlabSearch.Nearest(idx, 5, 5, 0, true, window, sloped, out _));

            // A flat slab in front of a higher sloped one hides it (nearest face wins).
            AddUnderside(idx, "sloped above it", 5800, 20);
            Assert.Null(SlabSearch.Nearest(idx, 5, 5, 0, true, window, sloped, out _));
        }
    }
}
