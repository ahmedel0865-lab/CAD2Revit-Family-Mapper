using System;
using System.Linq;
using SmartHostMEP.Core;
using Xunit;

namespace SmartHostMEP.Core.Tests
{
    /// <summary>Vertical planes parallel to the nearest wall/column face or DWG wall line.</summary>
    public class EdgeSnapTests
    {
        const double Mm = 1 / 304.8;
        static void Near(double e, double a, double tol = 1e-9) => Assert.True(Math.Abs(e - a) <= tol, $"expected {e}, got {a}");
        static void Near(V3 e, V3 a, double tol = 1e-9) => Assert.True((e - a).Length <= tol, $"expected {e}, got {a}");

        // A wall face running north-south along x = 0, from y = 0 to y = 10 ft.
        static readonly V3 A = new V3(0, 0, 0), B = new V3(0, 10, 0);

        [Fact]
        public void PlaneIsParallelToTheFaceNotToTheBlockRotation()
        {
            // Block drawn at 37 degrees, 150 mm east of the face.
            double rot = 37 * Math.PI / 180;
            var blockFacing = VerticalPlacement.Facing(rot);
            var block = new V3(150 * Mm, 4, 0);
            var p = EdgeSnap.Plan(block, A, B, blockFacing, PlanePosition.SnapToFace, 1.2);
            Near(new V3(1, 0, 0), p.Facing);                 // faces away from the wall, toward the block (east)
            Near(0, p.Along.X);                              // plane direction = wall direction (north-south)
            Near(1, Math.Abs(p.Along.Y));
            Near(new V3(0, 4, 1.2), p.Point);                // snapped onto the face, block projected
            Near(150 * Mm, p.MovedFt);
        }

        [Fact]
        public void BlockOnTheOtherSideFacesTheOtherWay()
        {
            var block = new V3(-200 * Mm, 6, 0);
            var p = EdgeSnap.Plan(block, A, B, VerticalPlacement.Facing(0), PlanePosition.SnapToFace, 0);
            Near(new V3(-1, 0, 0), p.Facing);
            Near(new V3(0, 6, 0), p.Point);
        }

        [Fact]
        public void ThroughBlockPointKeepsThePointButTakesTheFaceDirection()
        {
            var block = new V3(150 * Mm, 4, 0);
            var p = EdgeSnap.Plan(block, A, B, VerticalPlacement.Facing(0.6), PlanePosition.ThroughBlockPoint, 2);
            Near(new V3(150 * Mm, 4, 2), p.Point);
            Near(new V3(1, 0, 0), p.Facing);
            Near(0, p.MovedFt);
        }

        [Fact]
        public void BlockExactlyOnTheFaceUsesItsOwnFacingForTheSide()
        {
            var west = VerticalPlacement.Facing(Math.PI / 2);   // block faces -X
            var p = EdgeSnap.Plan(new V3(0, 3, 0), A, B, west, PlanePosition.SnapToFace, 0);
            Near(new V3(-1, 0, 0), p.Facing);
        }

        [Fact]
        public void AngledWall()
        {
            var a = new V3(0, 0, 0); var b = new V3(10, 10, 0);   // 45 degree wall
            var block = new V3(5, 4, 0);                           // south-east of it
            var p = EdgeSnap.Plan(block, a, b, VerticalPlacement.Facing(0), PlanePosition.SnapToFace, 0);
            var s = Math.Sqrt(0.5);
            Near(new V3(s, -s, 0), p.Facing);
            Near(new V3(4.5, 4.5, 0), p.Point);
            Near(0, p.Along.Dot(p.Facing));
        }

        [Fact]
        public void SnapReviewOver200mm()
        {
            var far = EdgeSnap.Plan(new V3(250 * Mm, 1, 0), A, B, VerticalPlacement.Facing(0), PlanePosition.SnapToFace, 0);
            var near = EdgeSnap.Plan(new V3(150 * Mm, 1, 0), A, B, VerticalPlacement.Facing(0), PlanePosition.SnapToFace, 0);
            Assert.True(EdgeSnap.NeedsSnapReview(far));
            Assert.False(EdgeSnap.NeedsSnapReview(near));
            Assert.Equal("Moved 250 mm to snap to the wall/column face", EdgeSnap.SnapMovedReason(250));
            Assert.Equal("No wall/column within 600 mm - used block rotation", EdgeSnap.NoEdgeReason(600));
        }

        [Fact]
        public void IndexFindsTheNearestSegmentWithinTheRadiusOnly()
        {
            var idx = new EdgeIndex<string>(600 * Mm);
            idx.Add(0, 0, 0, 10, "wall west");
            idx.Add(1, 0, 1, 10, "wall east");                     // 1 ft = 305 mm east
            idx.Add(0, 20, 100, 20, "long wall");                  // far, long (many cells)
            var hit = idx.Nearest(0.8, 5, 600 * Mm);
            Assert.Equal("wall east", hit.Payload);
            Near(0.2, hit.DistanceFt);
            Assert.Null(idx.Nearest(50, 5, 600 * Mm));             // nothing within 600 mm
            Assert.Equal("long wall", idx.Nearest(73.4, 20.5, 600 * Mm).Payload);
            Assert.Equal("wall west", idx.Nearest(0.8, 5, 600 * Mm, p => p != "wall east").Payload);
        }

        [Fact]
        public void SegmentDistanceIsClampedToTheEnds()
        {
            Near(1, EdgeIndex<int>.SegmentDistance(0, 11, 0, 0, 0, 10));
            Near(0.5, EdgeIndex<int>.SegmentDistance(0.5, 5, 0, 0, 0, 10));
        }

        [Theory]
        [InlineData("A-WALL", true)]
        [InlineData("a-wall-ext", true)]
        [InlineData("S-COLS", true)]
        [InlineData("E-POWER", false)]
        [InlineData("A-WALL-HATCH", false)]   // hatch layers never count
        [InlineData("COLUMN", true)]
        public void LayerFilterWildcards(string layer, bool expected) =>
            Assert.Equal(expected, LayerFilter.Matches(layer, EdgeSnap.DefaultLayers, false));

        [Fact]
        public void AllLayersButNeverHatch()
        {
            Assert.True(LayerFilter.Matches("E-POWER", "", true));
            Assert.False(LayerFilter.Matches("HATCH-01", "", true));
            Assert.False(LayerFilter.Matches("E-POWER", "", false));
            Assert.Equal(4, LayerFilter.Parse("*WALL*; *COL*, ,A-?").Count + 1);
        }

        [Theory]
        [InlineData(".csv")]
        [InlineData(".xlsx")]
        public void VerticalPlaneOptionsAreSavedWithTheMapping(string ext)
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "c2r_edge_" + Guid.NewGuid().ToString("N") + ext);
            try
            {
                Mapping.Save(path, new[] { new MapRow { Block = "SOCKET", Family = "F", TypeName = "T", Host = HostMode.Vertical } },
                             new SlabOptions { EdgeSearchMm = 750, PlanePosition = PlanePosition.ThroughBlockPoint,
                                               DwgLayers = "*WALL*, A-COL?", AllDwgLayers = true });
                var m = Mapping.Load(path);
                Assert.Empty(m.Errors);
                Assert.Equal(750, m.Slab.EdgeSearchMm);
                Assert.Equal(PlanePosition.ThroughBlockPoint, m.Slab.PlanePosition);
                Assert.Equal("*WALL*, A-COL?", m.Slab.DwgLayers);
                Assert.True(m.Slab.AllDwgLayers);
            }
            finally { try { System.IO.File.Delete(path); } catch { } }
        }

        [Fact]
        public void Defaults()
        {
            var o = new SlabOptions();
            Assert.Equal(600, o.EdgeSearchMm);
            Assert.Equal(PlanePosition.SnapToFace, o.PlanePosition);
            Assert.Equal("*WALL*, *COL*, *A-WALL*, *S-COLS*", o.DwgLayers);
            Assert.False(o.AllDwgLayers);
        }

        [Fact]
        public void PlacedAwayReasons()
        {
            Assert.Equal("Placed 75 mm away from its intended point", VerticalPlacement.PlacedAwayFromTargetReason(75));
            Assert.True(VerticalPlacement.IsPlacedAwayReason(VerticalPlacement.PlacedAwayFromTargetReason(75)));
            Assert.True(VerticalPlacement.IsPlacedAwayReason(VerticalPlacement.PlacedAwayReason(75)));
        }
    }
}
