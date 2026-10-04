using System;
using System.Collections.Generic;
using System.Linq;
using CAD2Revit.Core;
using Xunit;

namespace CAD2Revit.Core.Tests
{
    /// <summary>Walls and columns detected from DWG shapes (no layer names).</summary>
    public class CadDetectTests
    {
        const double Mm = 1 / 304.8;
        static V3 P(double xMm, double yMm) => new V3(xMm * Mm, yMm * Mm, 0);
        static void Near(double e, double a, double tol = 1e-6) => Assert.True(Math.Abs(e - a) <= tol, $"expected {e}, got {a}");
        static void Dir(V3 e, V3 a) => Assert.True((e - a).Length < 1e-6, $"expected {e}, got {a}");

        static CadFace Find(CadDetector d, double xMm, double yMm) => d.Find(xMm * Mm, yMm * Mm);

        /// <summary>A 200 mm wall along X: faces at y = 0 and y = 200, from x = 0 to 5000.</summary>
        static CadDetector Wall(double thickMm = 200)
        {
            var d = new CadDetector();
            d.AddLine(P(0, 0), P(5000, 0));
            d.AddLine(P(0, thickMm), P(5000, thickMm));
            return d;
        }

        [Fact]
        public void ParallelPairIsAWallAndTheBlockSideFaceIsUsed()
        {
            var f = Find(Wall(), 2500, 500);
            Assert.NotNull(f);
            Assert.Equal(CadEdgeKind.WallPair, f.Kind);
            Assert.Equal("CAD wall pair (thk 200 mm)", f.Label);
            Near(200 * Mm, f.A.Y);
            Near(200 * Mm, f.B.Y);
            Dir(new V3(0, 1, 0), f.Outward);       // away from the wall, toward the block
            Near(300 * Mm, f.DistanceFt);
            Assert.Equal(2, f.Outline.Count);
        }

        [Fact]
        public void NeverJumpsAcrossTheWall()
        {
            var f = Find(Wall(), 2500, -400);       // block south of the wall
            Near(0, f.A.Y);
            Dir(new V3(0, -1, 0), f.Outward);
        }

        [Fact]
        public void BlockInsideTheWallUsesTheNearerFace()
        {
            var f = Find(Wall(), 2500, 40);
            Near(0, f.A.Y);
            Dir(new V3(0, -1, 0), f.Outward);
        }

        [Theory]
        [InlineData(50, false)]
        [InlineData(100, true)]
        [InlineData(600, true)]
        [InlineData(700, false)]
        public void WallThicknessRange(double thickMm, bool wall)
        {
            var f = Find(Wall(thickMm), 2500, thickMm + 100);
            Assert.Equal(wall, f != null);
        }

        [Fact]
        public void ThicknessRangeIsEditable()
        {
            var d = new CadDetector(new CadDetectOptions { WallMinMm = 50, WallMaxMm = 800 });
            d.AddLine(P(0, 0), P(5000, 0));
            d.AddLine(P(0, 700), P(5000, 700));
            Assert.Equal("CAD wall pair (thk 700 mm)", Find(d, 2500, 900).Label);
        }

        [Theory]
        [InlineData(0.5, true)]
        [InlineData(2, false)]
        public void LinesMustBeParallelWithin1Degree(double deg, bool wall)
        {
            var d = new CadDetector();
            d.AddLine(P(0, 0), P(3000, 0));
            double a = deg * Math.PI / 180;
            d.AddLine(P(0, 200), P(3000 * Math.Cos(a), 200 + 3000 * Math.Sin(a)));
            Assert.Equal(wall, Find(d, 500, -200) != null);
        }

        [Theory]
        [InlineData(250, false)]
        [InlineData(350, true)]
        public void LinesMustOverlap300mm(double overlapMm, bool wall)
        {
            var d = new CadDetector();
            d.AddLine(P(0, 0), P(2000, 0));
            d.AddLine(P(2000 - overlapMm, 200), P(4000 - overlapMm, 200));
            Assert.Equal(wall, Find(d, 1900, -100) != null);
        }

        [Fact]
        public void RotatedWall()
        {
            var d = new CadDetector();
            double a = 30 * Math.PI / 180;
            var u = new V3(Math.Cos(a), Math.Sin(a), 0);
            var n = new V3(-Math.Sin(a), Math.Cos(a), 0);
            d.AddLine(new V3(0, 0, 0), u * (4000 * Mm));
            d.AddLine(n * (150 * Mm), n * (150 * Mm) + u * (4000 * Mm));
            var block = u * (2000 * Mm) + n * (400 * Mm);
            var f = d.Find(block.X, block.Y);
            Assert.Equal("CAD wall pair (thk 150 mm)", f.Label);
            Dir(n, f.Outward);
        }

        [Fact]
        public void ClosedRectangleIsAColumnNotAWall()
        {
            var d = new CadDetector();
            d.AddPolyline(new[] { P(0, 0), P(400, 0), P(400, 400), P(0, 400), P(0, 0) });
            var f = Find(d, 200, 600);
            Assert.Equal(CadEdgeKind.RectColumn, f.Kind);
            Assert.Equal("CAD column 400x400", f.Label);
            Near(400 * Mm, f.A.Y);
            Dir(new V3(0, 1, 0), f.Outward);
            Near(200 * Mm, f.DistanceFt);
            Assert.Equal(4, f.Outline.Count);
        }

        [Fact]
        public void FourLooseLinesJoinedIntoAColumn()
        {
            var d = new CadDetector();
            // Drawn as separate lines, in any order and direction, ends within 5 mm.
            d.AddLine(P(0, 0), P(600, 0));
            d.AddLine(P(0, 403), P(0, 2));
            d.AddLine(P(600, 400), P(1, 401));
            d.AddLine(P(601, 1), P(600, 400));
            var f = Find(d, 900, 200);
            Assert.Equal(CadEdgeKind.RectColumn, f.Kind);
            Assert.Matches(@"^CAD column (599|600|601)x(399|400|401)$", f.Label);   // ends are up to 5 mm apart
            Dir(new V3(1, 0, 0), f.Outward);
        }

        [Fact]
        public void LShapedColumn()
        {
            var d = new CadDetector();
            d.AddPolyline(new[] { P(0, 0), P(600, 0), P(600, 200), P(200, 200), P(200, 600), P(0, 600), P(0, 0) });
            var f = Find(d, 400, 400);
            Assert.Equal(CadEdgeKind.PolygonColumn, f.Kind);
            Assert.Equal("CAD column 600x600 (polygon)", f.Label);
            Near(200 * Mm, f.DistanceFt);
        }

        [Fact]
        public void CircularColumnUsesTheTangent()
        {
            var d = new CadDetector();
            d.AddCircle(P(0, 0), 250 * Mm);
            var f = Find(d, 0, 500);
            Assert.Equal(CadEdgeKind.CircleColumn, f.Kind);
            Assert.Equal("CAD circular column D=500", f.Label);
            Dir(new V3(0, 1, 0), f.Outward);
            Near(250 * Mm, f.DistanceFt);
            Near(250 * Mm, f.A.Y);                  // tangent line at the nearest point
            Near(250 * Mm, f.B.Y);
        }

        [Theory]
        [InlineData(100, false)]
        [InlineData(200, true)]
        [InlineData(1500, true)]
        [InlineData(1600, false)]
        public void ColumnSizeRange(double sizeMm, bool column)
        {
            var d = new CadDetector();
            d.AddCircle(P(0, 0), sizeMm / 2 * Mm);
            Assert.Equal(column, Find(d, 0, sizeMm / 2 + 100) != null);
            var r = new CadDetector();
            r.AddPolyline(new[] { P(0, 0), P(sizeMm, 0), P(sizeMm, sizeMm), P(0, sizeMm), P(0, 0) });
            var f = Find(r, sizeMm / 2, sizeMm + 100);
            Assert.Equal(column, f != null && f.Kind == CadEdgeKind.RectColumn);
        }

        [Theory]
        [InlineData(330, true)]     // column side 30 mm farther than the wall face: column wins
        [InlineData(400, false)]    // 100 mm farther: the wall face is used
        public void ColumnPreferredWhenAlmostAsClose(double columnDistMm, bool column)
        {
            var d = Wall();                                         // wall face at y = 200
            double top = 500 + columnDistMm;                        // block at y = 500
            d.AddPolyline(new[] { P(2400, top), P(2800, top), P(2800, top + 400), P(2400, top + 400), P(2400, top) });
            var f = Find(d, 2600, 500);
            Assert.Equal(column, f.Kind == CadEdgeKind.RectColumn);
        }

        [Fact]
        public void DoorSwingsAndShortLinesAreIgnored()
        {
            Assert.True(CadDetector.IsDoorSwing(900 * Mm, Math.PI / 2));
            Assert.False(CadDetector.IsDoorSwing(900 * Mm, Math.PI));
            Assert.False(CadDetector.IsDoorSwing(2000 * Mm, Math.PI / 2));
            var d = new CadDetector();
            var arc = Enumerable.Range(0, 10).Select(k => P(900 * Math.Cos(k * Math.PI / 18), 900 * Math.Sin(k * Math.PI / 18))).ToList();
            d.AddArc(arc, 900 * Mm, Math.PI / 2);
            d.AddLine(P(0, 0), P(90, 0));
            Assert.Equal(1, d.IgnoredDoorArcs);
            Assert.Equal(1, d.IgnoredShort);
            Assert.Equal(0, d.Segments);
        }

        [Fact]
        public void ClosedArcIsACircle()
        {
            var d = new CadDetector();
            var pts = Enumerable.Range(0, 36).Select(k => P(300 * Math.Cos(k * Math.PI / 18), 300 * Math.Sin(k * Math.PI / 18))).ToList();
            d.AddArc(pts, 300 * Mm, 2 * Math.PI);
            Assert.Equal("CAD circular column D=600", Find(d, 0, 500).Label);
        }

        [Fact]
        public void NothingWithinTheSearchRadius()
        {
            Assert.Null(Find(Wall(), 2500, 900));                  // 700 mm from the nearest face
            var d = new CadDetector(new CadDetectOptions { SearchMm = 800 });
            d.AddLine(P(0, 0), P(5000, 0));
            d.AddLine(P(0, 200), P(5000, 200));
            Assert.NotNull(Find(d, 2500, 900));
        }

        [Fact]
        public void LoneLineIsNotAWall()
        {
            var d = new CadDetector();
            d.AddLine(P(0, 0), P(5000, 0));
            Assert.Null(Find(d, 2500, 300));
        }

        [Fact]
        public void PlanOutwardFacesOutEvenFromInsideTheWall()
        {
            var face = Find(Wall(), 2500, 40);
            var p = EdgeSnap.PlanOutward(P(2500, 40), face.A, face.B, face.Outward, PlanePosition.SnapToFace, 1);
            Dir(new V3(0, -1, 0), p.Facing);
            Near(0, p.Point.Y);
            Near(2500 * Mm, p.Point.X);
            Near(40 * Mm, p.MovedFt);
            var through = EdgeSnap.PlanOutward(P(2500, 40), face.A, face.B, face.Outward, PlanePosition.ThroughBlockPoint, 1);
            Near(40 * Mm, through.Point.Y);
        }
    }
}
