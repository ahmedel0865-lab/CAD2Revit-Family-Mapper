using System;
using CAD2Revit.Core;
using Xunit;

namespace CAD2Revit.Core.Tests
{
    /// <summary>
    /// Wall hosting (Host Type "Wall"). Test cases from the spec:
    /// - block on the right face -> family on the right face, facing right;
    /// - block drawn at an angle -> family still flat on the wall (reference direction = wall
    ///   direction, the CAD rotation is not used).
    /// </summary>
    public class WallPlacementTests
    {
        const double Mm = 1 / 304.8;
        const double Half = 100 * Mm;   // 200 mm wall, location line at its centre

        static void Near(double e, double a, double tol = 1e-9) => Assert.True(Math.Abs(e - a) <= tol, $"expected {e}, got {a}");
        static void Near(V3 e, V3 a, double tol = 1e-9) => Assert.True((e - a).Length <= tol, $"expected {e}, got {a}");

        // Wall drawn from south to north along X = 0.
        static readonly V3 A = new V3(0, 0, 0), B = new V3(0, 10, 0);

        [Fact]
        public void BlockOnRightFaceGoesOnRightFaceFacingRight()
        {
            var cad = new V3(300 * Mm, 4, 0);             // 300 mm to the right (east) of the wall centre
            var s = WallPlacement.NearestOnLine(A, B, cad);
            var p = WallPlacement.Plan(s, -Half, Half, cad, null, 1.5);
            Near(new V3(Half, 4, 1.5), p.Point);          // on the east face, CAD point projected, at the height
            Near(new V3(1, 0, 0), p.Normal);              // faces east, towards the block
            Near(0, p.ReferenceDirection.Dot(p.Normal));  // along the wall
            Near(1, Math.Abs(p.ReferenceDirection.Y));
            Near(200 * Mm, p.MovedFt);
            Assert.False(p.InsideWall);
            Assert.False(WallPlacement.NeedsMoveReview(p));
        }

        [Fact]
        public void BlockOnLeftFaceGoesOnLeftFace()
        {
            var cad = new V3(-250 * Mm, 7, 0);
            var p = WallPlacement.Plan(WallPlacement.NearestOnLine(A, B, cad), -Half, Half, cad, null, 0);
            Near(new V3(-Half, 7, 0), p.Point);
            Near(new V3(-1, 0, 0), p.Normal);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(37)]
        [InlineData(90)]
        [InlineData(215)]
        public void BlockDrawnAtAnAngleStaysFlatOnTheWall(double blockDeg)
        {
            // The symbol is drawn at an angle: its centre of geometry is off the insertion point in
            // that direction. The CAD rotation itself is not an input: the family lies flat on the face.
            var cad = new V3(400 * Mm, 5, 0);
            double a = blockDeg * Math.PI / 180;
            var centre = cad + new V3(Math.Cos(a), Math.Sin(a), 0) * (80 * Mm);
            var p = WallPlacement.Plan(WallPlacement.NearestOnLine(A, B, cad), -Half, Half, cad, centre, 1);
            Near(new V3(1, 0, 0), p.Normal);                       // still faces the block side
            Near(0, p.ReferenceDirection.Z);                       // horizontal
            Near(1, Math.Abs(p.ReferenceDirection.Dot(new V3(0, 1, 0))));   // = wall direction
            Near(new V3(Half, 5, 1), p.Point);
        }

        [Fact]
        public void ReferenceDirectionKeepsTheFamilyUpright()
        {
            // Z x normal: family X = reference direction, family Y = normal x X = up.
            var cad = new V3(300 * Mm, 4, 0);
            var p = WallPlacement.Plan(WallPlacement.NearestOnLine(A, B, cad), -Half, Half, cad, null, 0);
            var n = p.Normal; var x = p.ReferenceDirection;
            var y = new V3(n.Y * x.Z - n.Z * x.Y, n.Z * x.X - n.X * x.Z, n.X * x.Y - n.Y * x.X);
            Near(new V3(0, 0, 1), y);
        }

        [Fact]
        public void PointInsideTheWallUsesTheSideTheSymbolIsDrawnOn()
        {
            var cad = new V3(20 * Mm, 3, 0);                      // inside, nearer the east face
            var westSymbol = new V3(-150 * Mm, 3, 0);             // symbol drawn on the west side
            var s = WallPlacement.NearestOnLine(A, B, cad);
            var p = WallPlacement.Plan(s, -Half, Half, cad, westSymbol, 0);
            Assert.True(p.InsideWall);
            Near(new V3(-1, 0, 0), p.Normal);
            Near(new V3(-Half, 3, 0), p.Point);
            // Without a symbol centre: the nearer face.
            Near(new V3(1, 0, 0), WallPlacement.Plan(s, -Half, Half, cad, null, 0).Normal);
        }

        [Fact]
        public void LocationLineOnAFaceStillPicksTheBlockSide()
        {
            // Location line = the east finish face: faces at x = 0 and x = -200 mm. Offsets are measured
            // along Left(tangent) = -X for this wall, so they are 0 and +200 mm.
            var cad = new V3(-500 * Mm, 2, 0);
            var p = WallPlacement.Plan(WallPlacement.NearestOnLine(A, B, cad), 0, 200 * Mm, cad, null, 0);
            Near(new V3(-200 * Mm, 2, 0), p.Point);
            Near(new V3(-1, 0, 0), p.Normal);
            Near(300 * Mm, p.DistanceFt);
            Assert.True(WallPlacement.NeedsMoveReview(p));        // 500 mm to the face
        }

        [Fact]
        public void DistanceIncludesGoingPastTheWallEnd()
        {
            var cad = new V3(400 * Mm, 10 + 300 * Mm, 0);         // 300 mm past the north end, 400 mm east
            var s = WallPlacement.NearestOnLine(A, B, cad);
            Near(300 * Mm, s.Overshoot);
            var p = WallPlacement.Plan(s, -Half, Half, cad, null, 0);
            Near(Math.Sqrt(300 * 300 + 300 * 300) * Mm, p.DistanceFt, 1e-9);
            Near(new V3(Half, 10, 0), p.Point);
        }

        [Theory]
        [InlineData(1.0)]     // counter-clockwise arc
        [InlineData(-1.0)]    // clockwise arc
        public void CurvedWallUsesTheTangentAtTheProjectedPoint(double dir)
        {
            // Quarter circle, radius 5 ft, centred at the origin, between 0 and 90 degrees.
            double start = dir > 0 ? 0 : Math.PI / 2;
            var centre = new V3(0, 0, 0);
            double deg45 = Math.PI / 4;
            var outward = new V3(Math.Cos(deg45), Math.Sin(deg45), 0);
            var cad = outward * (5 + 400 * Mm);                  // outside the curve at 45 degrees
            var s = WallPlacement.NearestOnArc(centre, 5, start, dir * Math.PI / 2, cad);
            Near(outward * 5, s.Point);
            Near(0, s.Overshoot);
            var p = WallPlacement.Plan(s, -Half, Half, cad, null, 2);
            Near(outward, p.Normal);                              // faces out, towards the block
            Near(0, p.ReferenceDirection.Dot(outward));           // tangent at that point
            Near(0, p.ReferenceDirection.Z);
            var onFace = outward * (5 + 100 * Mm);
            Near(new V3(onFace.X, onFace.Y, 2), p.Point);

            // Inside the curve: the inner face, facing the centre.
            var cadIn = outward * (5 - 300 * Mm);
            var pin = WallPlacement.Plan(WallPlacement.NearestOnArc(centre, 5, start, dir * Math.PI / 2, cadIn), -Half, Half, cadIn, null, 0);
            Near(outward * -1, pin.Normal);
        }

        [Fact]
        public void CurvedWallBeyondItsEndClampsToTheEnd()
        {
            double a = -10 * Math.PI / 180;
            var cad = new V3(Math.Cos(a), Math.Sin(a), 0) * 5;
            var s = WallPlacement.NearestOnArc(new V3(0, 0, 0), 5, 0, Math.PI / 2, cad);
            Near(new V3(5, 0, 0), s.Point);
            Assert.True(s.Overshoot > 0.8 && s.Overshoot < 0.9);   // ~0.87 ft along the end tangent
        }

        [Fact]
        public void ReviewReasons()
        {
            Assert.Equal("No wall within 500 mm", WallPlacement.NoWallReason(500));
            Assert.Equal("Moved more than 200 mm to reach the wall face", WallPlacement.MovedReason);
            Assert.Equal("Wall is in a linked model", WallPlacement.LinkedReason);
        }
    }
}
