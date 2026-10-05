using System;
using SmartHostMEP.Core;
using Xunit;

namespace SmartHostMEP.Core.Tests
{
    public class VerticalPlacementTests
    {
        const double Mm = 1 / 304.8;   // feet per mm
        static double Rad(double deg) => deg * Math.PI / 180;

        static void Near(double expected, double actual, double tolFt = 1e-9) =>
            Assert.True(Math.Abs(expected - actual) <= tolFt, $"expected {expected}, got {actual}");

        static void Near(V3 expected, V3 actual, double tolFt = 1e-9) =>
            Assert.True((expected - actual).Length <= tolFt, $"expected {expected}, got {actual}");

        // Block rotation -> plane facing: north, west, south, east and an angle.
        public static TheoryData<double, double, double> Directions => new TheoryData<double, double, double>
        {
            { 0, 0, 1 },                                              // north
            { 180, 0, -1 },                                           // south
            { 270, 1, 0 },                                            // east
            { 90, -1, 0 },                                            // west
            { 30, -Math.Sin(Rad(30)), Math.Cos(Rad(30)) },           // at an angle
            { 217.5, -Math.Sin(Rad(217.5)), Math.Cos(Rad(217.5)) },  // at an angle, other quadrant
        };

        // The same directions as block rotations only.
        public static TheoryData<double> Angles => new TheoryData<double> { 0, 180, 270, 90, 30, 217.5 };

        [Theory]
        [MemberData(nameof(Directions))]
        public void FacingFollowsBlockRotation(double deg, double fx, double fy)
        {
            var f = VerticalPlacement.Facing(Rad(deg));
            Near(new V3(fx, fy, 0), f);
            Near(1, f.Length);
        }

        [Theory]
        [MemberData(nameof(Angles))]
        public void ReferenceDirectionIsHorizontalAlongThePlane(double deg)
        {
            var n = VerticalPlacement.Facing(Rad(deg));
            var a = VerticalPlacement.Along(n);
            Near(0, a.Dot(n));        // in the plane
            Near(0, a.Z);             // horizontal: family stands upright
            Near(1, a.Length);
            // Z x n (right-handed): not mirrored, same as Revit's XYZ.BasisZ.CrossProduct(normal)
            Near(new V3(-n.Y, n.X, 0), a);
        }

        [Theory]
        [MemberData(nameof(Angles))]
        public void CadPointOnThePlaneStaysExactly(double deg)
        {
            // Plane created through the CAD point itself: target = CAD point at the elevation.
            var n = VerticalPlacement.Facing(Rad(deg));
            var cad = new V3(1234.567, -89.012, 0);
            var t = VerticalPlacement.Target(cad.X, cad.Y, 10, 1200 * Mm, cad, n);
            Near(new V3(cad.X, cad.Y, 10 + 1200 * Mm), t);
        }

        [Theory]
        [MemberData(nameof(Angles))]
        public void CadPointOffAPlaneIsProjectedPerpendicularly(double deg)
        {
            var n = VerticalPlacement.Facing(Rad(deg));
            var a = VerticalPlacement.Along(n);
            var planePoint = new V3(500, 300, 0);
            // CAD point 3 m along the plane and 40 mm in front of it.
            var cad = planePoint + a * (3000 * Mm) + n * (40 * Mm);
            var t = VerticalPlacement.Target(cad.X, cad.Y, 0, 450 * Mm, planePoint, n);
            Near(0, VerticalPlacement.Distance(t, planePoint, n));                   // on the plane
            Near(450 * Mm, t.Z);                                                     // at the elevation
            Near(3000 * Mm, (t - planePoint).Dot(a));                               // no shift along the plane
            Near(40, VerticalPlacement.DeviationMm(new V3(cad.X, cad.Y, t.Z), t), 1e-6);   // moved only perpendicular
        }

        [Theory]
        [MemberData(nameof(Angles))]
        public void FamilyOriginOffCentreIsCorrectedAlongThePlane(double deg)
        {
            var n = VerticalPlacement.Facing(Rad(deg));
            var a = VerticalPlacement.Along(n);
            var point = new V3(100, 200, 4);
            // Family origin at its left edge: centre 60 mm along the plane, 25 mm out of the plane.
            var centre = point + a * (60 * Mm) + n * (25 * Mm);
            double s = VerticalPlacement.CenterOffset(point, centre, a);
            Near(60 * Mm, s);
            // Moving by (point - anchor) puts the family centre (along the plane) on the point.
            var move = point - VerticalPlacement.Anchor(point, a, s);
            var newCentre = centre + move;
            Near(0, (newCentre - point).Dot(a));
            Near(0, move.Dot(n));     // stays on the plane
            Near(0, move.Z);          // stays at the elevation
        }

        [Fact]
        public void CentredFamilyIsNotMoved()
        {
            var a = VerticalPlacement.Along(VerticalPlacement.Facing(0));
            var loc = new V3(0, 0, 0);
            Assert.Equal(0, VerticalPlacement.CenterOffset(loc, loc + a * (8 * Mm), a));   // within 10 mm
            Assert.Equal(0, VerticalPlacement.CenterOffset(loc, new V3(0, 0, 0.5), a));    // only vertical: not along
        }

        [Theory]
        [MemberData(nameof(Angles))]
        public void OffsetOver10mmIsDetected(double deg)
        {
            var n = VerticalPlacement.Facing(Rad(deg));
            var a = VerticalPlacement.Along(n);
            var p = new V3(10, 20, 3);
            Assert.False(VerticalPlacement.IsOff(p + a * (9 * Mm), p));
            Assert.True(VerticalPlacement.IsOff(p + a * (11 * Mm), p));
            Assert.True(VerticalPlacement.IsOff(p + n * (-150 * Mm), p));   // e.g. on the back of the plane
            Near(150, VerticalPlacement.DeviationMm(p + n * (-150 * Mm), p), 1e-6);
        }
    }
}
