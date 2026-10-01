using System;
using CAD2Revit.Core;
using Xunit;

namespace CAD2Revit.Core.Tests
{
    /// <summary>Vertical reference planes: one per block, reused only when colinear; and the final
    /// "placed too far from the CAD block" check.</summary>
    public class VerticalPlaneReuseTests
    {
        const double Mm = 1 / 304.8;
        static double Rad(double deg) => deg * Math.PI / 180;

        // A plane through (10, 20) facing north (block rotation 0): runs east-west.
        static readonly V3 PlanePoint = new V3(10, 20, 0);
        static readonly V3 North = VerticalPlacement.Facing(0);

        [Fact]
        public void PointOnThePlaneSameDirectionIsReused()
        {
            var p = PlanePoint + new V3(5, 0, 3);    // 5 ft along the plane, other height
            Assert.True(VerticalPlacement.CanReuse(p, PlanePoint, North, North));
        }

        [Theory]
        [InlineData(4.9, true)]
        [InlineData(5.1, false)]
        [InlineData(150, false)]     // a nearby but different (parallel) plane is never reused
        public void ReusedOnlyIfThePointIsWithin5mm(double offMm, bool reuse)
        {
            var p = PlanePoint + new V3(2, offMm * Mm, 0);
            Assert.Equal(reuse, VerticalPlacement.CanReuse(p, PlanePoint, North, North));
        }

        [Theory]
        [InlineData(0.4, true)]
        [InlineData(0.6, false)]
        [InlineData(180, false)]     // same line, facing the other way
        [InlineData(90, false)]
        public void ReusedOnlyIfTheDirectionIsWithinHalfADegree(double turnDeg, bool reuse)
        {
            var facing = VerticalPlacement.Facing(Rad(turnDeg));
            Assert.Equal(reuse, VerticalPlacement.CanReuse(PlanePoint, PlanePoint, North, facing));
        }

        [Fact]
        public void BlocksOnDifferentLinesGetTheirOwnPlanes()
        {
            // Two sockets on the same level and elevation, 2 m apart across the room, same facing:
            // the second must NOT reuse the first one's plane.
            var a = new V3(0, 0, 0);
            var b = new V3(0, 2000 * Mm, 0);
            Assert.False(VerticalPlacement.CanReuse(b, a, North, North));
            // ...but a socket further along the same wall line does.
            Assert.True(VerticalPlacement.CanReuse(new V3(3000 * Mm, 0, 0), a, North, North));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(90)]
        [InlineData(180)]
        [InlineData(270)]
        [InlineData(33)]
        public void ANewPlaneThroughTheBlockPutsTheFamilyExactlyOnIt(double deg)
        {
            var f = VerticalPlacement.Facing(Rad(deg));
            var block = new V3(123.456, -78.9, 0);
            var onPlane = VerticalPlacement.Target(block.X, block.Y, 10, 1200 * Mm, block, f);
            Assert.True(VerticalPlacement.PlanDistanceMm(onPlane, block) < 1e-6);
            Assert.True(Math.Abs(onPlane.Z - (10 + 1200 * Mm)) < 1e-9);
            // Plane direction = block X direction (cos, sin), normal = block +Y.
            var along = VerticalPlacement.Along(f);
            Assert.True(Math.Abs(Math.Abs(along.X * Math.Cos(Rad(deg)) + along.Y * Math.Sin(Rad(deg))) - 1) < 1e-9);
        }

        [Fact]
        public void PlaneNames()
        {
            Assert.Equal("CAD2Revit_V_Level 1_7", VerticalPlacement.PlaneName("Level 1", 7));
            Assert.Equal(7, VerticalPlacement.PlaneNumber("CAD2Revit_V_Level 1_7", "Level 1"));
            Assert.Equal(0, VerticalPlacement.PlaneNumber("CAD2Revit_V_Level 2_7", "Level 1"));
            Assert.Equal(0, VerticalPlacement.PlaneNumber("CAD2Revit vertical 123456", "Level 1"));
        }

        [Fact]
        public void PlacedAwayReasonAndPlanDistance()
        {
            Assert.Equal("Placed 123 mm away from CAD block", VerticalPlacement.PlacedAwayReason(123.4));
            Assert.True(VerticalPlacement.IsPlacedAwayReason("Placed 123 mm away from CAD block"));
            Assert.False(VerticalPlacement.IsPlacedAwayReason("Moved more than 200 mm to reach the wall face"));
            // Plan distance ignores height (elevation from level is expected).
            Assert.Equal(0, VerticalPlacement.PlanDistanceMm(new V3(1, 2, 0), new V3(1, 2, 9)), 9);
            Assert.Equal(60, VerticalPlacement.PlanDistanceMm(new V3(0, 0, 0), new V3(60 * Mm, 0, 0)), 6);
            Assert.Equal(50, VerticalPlacement.DefaultReviewDistanceMm);
            Assert.Equal(50, SlabOptions.DefaultReviewDistanceMm);
        }

        [Fact]
        public void ReviewDistanceIsSavedWithTheMapping()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "c2r_review_" + Guid.NewGuid().ToString("N") + ".csv");
            try
            {
                Mapping.Save(path, new[] { new MapRow { Block = "SOCKET", Family = "F", TypeName = "T", Host = HostMode.Vertical } },
                             new SlabOptions { ReviewDistanceMm = 75 });
                var m = Mapping.Load(path);
                Assert.True(m.HasSlabOptions);
                Assert.Equal(75, m.Slab.ReviewDistanceMm);
            }
            finally { try { System.IO.File.Delete(path); } catch { } }
        }
    }
}
