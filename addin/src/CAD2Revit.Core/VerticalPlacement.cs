using System;

namespace CAD2Revit.Core
{
    /// <summary>A plain 3D vector (feet), so the placement math can be tested without Revit.</summary>
    public struct V3
    {
        public readonly double X, Y, Z;
        public V3(double x, double y, double z) { X = x; Y = y; Z = z; }
        public static V3 operator +(V3 a, V3 b) => new V3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static V3 operator -(V3 a, V3 b) => new V3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static V3 operator *(V3 a, double k) => new V3(a.X * k, a.Y * k, a.Z * k);
        public double Dot(V3 b) => X * b.X + Y * b.Y + Z * b.Z;
        public double Length => Math.Sqrt(Dot(this));
        public V3 Normalize() { var l = Length; return l < 1e-12 ? this : this * (1 / l); }
        public override string ToString() => $"({X:0.####}, {Y:0.####}, {Z:0.####})";
    }

    /// <summary>
    /// Placement of a face-based family on a vertical work plane ("vertical" rows, and
    /// "wall" rows with no wall found):
    /// - the plane faces the CAD block's local +Y (block rotation + row rotation);
    /// - the family goes on the CAD insertion point projected onto the plane, at the row's
    ///   elevation, with its reference direction along the plane (horizontal);
    /// - after placing, the family is centred on that point along the plane if its origin
    ///   is not at its geometric centre, and moved back if it landed more than 10 mm off.
    /// </summary>
    public static class VerticalPlacement
    {
        public const double MmPerFoot = 304.8;
        /// <summary>Allowed distance (mm) between the placed family and the CAD point.</summary>
        public const double ToleranceMm = 10.0;
        /// <summary>A shared vertical plane is reused only if the point is this close to it (mm).</summary>
        public const double PlaneReuseMm = 0.5;

        /// <summary>Horizontal facing (plane normal) for a block rotation in radians:
        /// 0 = north (+Y), 90 deg = west, 180 deg = south, 270 deg = east.</summary>
        public static V3 Facing(double angleRad) => new V3(-Math.Sin(angleRad), Math.Cos(angleRad), 0);

        /// <summary>Reference direction of the family: horizontal, along the plane
        /// (Z x normal), so the family stands upright and is not mirrored.</summary>
        public static V3 Along(V3 normal) => new V3(-normal.Y, normal.X, 0).Normalize();

        /// <summary>Signed distance (feet) of <paramref name="p"/> from the plane through
        /// <paramref name="planePoint"/> with the given normal.</summary>
        public static double Distance(V3 p, V3 planePoint, V3 normal) => normal.Normalize().Dot(p - planePoint);

        /// <summary>The CAD point moved perpendicularly onto the plane (at its own Z).</summary>
        public static V3 Project(V3 p, V3 planePoint, V3 normal)
        {
            var n = normal.Normalize();
            return p - n * n.Dot(p - planePoint);
        }

        /// <summary>Target point: CAD insertion point (x, y) at levelZ + elevation, on the plane.</summary>
        public static V3 Target(double cadX, double cadY, double levelZ, double elevationFt, V3 planePoint, V3 normal) =>
            Project(new V3(cadX, cadY, levelZ + elevationFt), planePoint, normal);

        /// <summary>
        /// How far (feet, signed, along <paramref name="along"/>) the family's geometric centre
        /// is from its origin in the horizontal direction of the plane. 0 when that is within
        /// the tolerance (family origin already at its centre).
        /// </summary>
        public static double CenterOffset(V3 location, V3 center, V3 along)
        {
            double s = (center - location).Dot(along.Normalize());
            return Math.Abs(s) * MmPerFoot > ToleranceMm ? s : 0.0;
        }

        /// <summary>The point of the family that must sit on the CAD point: its origin,
        /// shifted to its centre along the plane when <paramref name="centerOffsetFt"/> is not 0.</summary>
        public static V3 Anchor(V3 location, V3 along, double centerOffsetFt) => location + along.Normalize() * centerOffsetFt;

        /// <summary>Distance in mm between two points.</summary>
        public static double DeviationMm(V3 a, V3 b) => (a - b).Length * MmPerFoot;

        public static bool IsOff(V3 actual, V3 expected) => DeviationMm(actual, expected) > ToleranceMm;
    }
}
