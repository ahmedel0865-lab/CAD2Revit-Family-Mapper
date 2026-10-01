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
        /// <summary>An existing CAD2Revit vertical plane is reused only if the block point lies on it
        /// (closer than this, mm) and it faces the same way (within <see cref="PlaneReuseDeg"/>).</summary>
        public const double PlaneReuseMm = 5.0;
        public const double PlaneReuseDeg = 0.5;
        /// <summary>Drawn height of a new vertical plane above the level (mm).</summary>
        public const double PlaneHeightMm = 3000.0;
        /// <summary>Default for the final check (all host types): an element farther than this
        /// (mm, in plan) from its CAD block goes to Needs Review.</summary>
        public const double DefaultReviewDistanceMm = 50.0;
        public const string PlaneNamePrefix = "CAD2Revit_V_";

        /// <summary>"CAD2Revit_V_Level 1_7".</summary>
        public static string PlaneName(string levelName, int n) => PlaneNamePrefix + levelName + "_" + n;

        /// <summary>n of "CAD2Revit_V_&lt;level&gt;_&lt;n&gt;" for that level, or 0.</summary>
        public static int PlaneNumber(string planeName, string levelName)
        {
            var prefix = PlaneNamePrefix + levelName + "_";
            if (planeName == null || !planeName.StartsWith(prefix, StringComparison.Ordinal)) return 0;
            return int.TryParse(planeName.Substring(prefix.Length), out var n) && n > 0 ? n : 0;
        }

        /// <summary>
        /// Reuse an existing vertical plane only if it is colinear with the block: the block point
        /// lies on it (distance &lt; 5 mm) and its normal is within 0.5 deg of the block's facing.
        /// Never because it is the nearest plane, or on the same level or elevation.
        /// </summary>
        public static bool CanReuse(V3 blockPoint, V3 planePoint, V3 planeNormal, V3 facing)
        {
            var n = new V3(planeNormal.X, planeNormal.Y, 0).Normalize();
            var f = new V3(facing.X, facing.Y, 0).Normalize();
            if (n.Length < 0.5 || f.Length < 0.5) return false;
            if (n.Dot(f) < Math.Cos(PlaneReuseDeg * Math.PI / 180)) return false;
            var p = new V3(blockPoint.X, blockPoint.Y, 0);
            var o = new V3(planePoint.X, planePoint.Y, 0);
            return Math.Abs(Distance(p, o, n)) * MmPerFoot < PlaneReuseMm;
        }

        /// <summary>Distance in plan (XY only), mm.</summary>
        public static double PlanDistanceMm(V3 a, V3 b) => new V3(a.X - b.X, a.Y - b.Y, 0).Length * MmPerFoot;

        /// <summary>The Needs Review reason: "Placed 123 mm away from CAD block".</summary>
        public static string PlacedAwayReason(double mm) => $"Placed {mm:0} mm away from CAD block";

        /// <summary>"Placed 75 mm away from its intended point" (the snapped face point, wall face, ...).</summary>
        public static string PlacedAwayFromTargetReason(double mm) => $"Placed {mm:0} mm away from its intended point";

        public static bool IsPlacedAwayReason(string text) =>
            text != null && text.StartsWith("Placed ", StringComparison.Ordinal) &&
            (text.EndsWith(" mm away from CAD block", StringComparison.Ordinal) || text.EndsWith(" mm away from its intended point", StringComparison.Ordinal));

        /// <summary>Horizontal facing (plane normal) for a block rotation in radians:
        /// 0 = north (+Y), 90 deg = west, 180 deg = south, 270 deg = east.</summary>
        public static V3 Facing(double angleRad) => new V3(-Math.Sin(angleRad), Math.Cos(angleRad), 0);

        /// <summary>The angle whose <see cref="Facing"/> is the block's +Y axis (yx, yy) in plan:
        /// equals the block rotation for a normal block, and is 180 deg off for a mirrored block
        /// (its Y axis is flipped), so the family faces the side the symbol is drawn on.</summary>
        public static double FacingAngleFromYAxis(double yx, double yy) => Math.Atan2(-yx, yy);

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
