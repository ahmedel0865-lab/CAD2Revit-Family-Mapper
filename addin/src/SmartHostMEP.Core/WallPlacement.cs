using System;

namespace SmartHostMEP.Core
{
    /// <summary>The nearest point of a wall's location line (in plan) to a CAD point.</summary>
    public struct WallStation
    {
        public V3 Point;        // on the location line (Z = 0)
        public V3 Tangent;      // unit, horizontal, along the wall at Point
        public double Overshoot; // feet the CAD point lies beyond the wall's end (0 = beside the wall)
    }

    /// <summary>Where and how a wall-hosted family goes (see <see cref="WallPlacement.Plan"/>).</summary>
    public struct WallPlan
    {
        /// <summary>+1: the face with the larger offset along <see cref="WallPlacement.Left"/>(tangent), -1: the other face.</summary>
        public int Side;
        public V3 Point;          // on the chosen face, at the placement height
        public V3 Normal;         // horizontal, out of the wall towards the CAD block
        public V3 ReferenceDirection; // along the wall (Z x Normal): family upright, flat on the face
        public double DistanceFt; // plan distance from the CAD point to the wall (0 inside the wall)
        public double MovedFt;    // plan distance from the CAD point to Point
        public bool InsideWall;   // the CAD point lies within the wall thickness
    }

    /// <summary>
    /// Wall hosting math (Host Type "Wall"), Revit-free so it can be tested:
    /// - the side of the wall is the side the CAD point is on (signed offset from the location
    ///   line along the wall normal); a point inside the wall thickness uses the side the block
    ///   symbol is drawn on (its centre of geometry), else the nearer face;
    /// - the family goes on the CAD point projected perpendicularly onto that face;
    /// - the CAD block rotation is ignored: the reference direction is the wall direction
    ///   (the tangent at the projected point, for curved walls), so the family sits flat on the
    ///   face, facing out towards the block.
    /// </summary>
    public static class WallPlacement
    {
        public const double MmPerFoot = 304.8;
        public const double DefaultSearchMm = 500;
        /// <summary>Moved more than this (mm) to reach the face: listed in Needs Review.</summary>
        public const double MoveReviewMm = 200;

        public static string NoWallReason(double searchMm) => $"No wall within {searchMm:0} mm";
        public const string MovedReason = "Moved more than 200 mm to reach the wall face";
        public const string LinkedReason = "Wall is in a linked model";

        /// <summary>Horizontal left normal of a direction: Z x t.</summary>
        public static V3 Left(V3 t) => new V3(-t.Y, t.X, 0).Normalize();

        static V3 Plan(V3 p) => new V3(p.X, p.Y, 0);

        /// <summary>Nearest point of a straight wall a-b to p (plan).</summary>
        public static WallStation NearestOnLine(V3 a, V3 b, V3 p)
        {
            a = Plan(a); b = Plan(b); p = Plan(p);
            var d = b - a;
            double len = d.Length;
            if (len < 1e-9) return new WallStation { Point = a, Tangent = new V3(1, 0, 0), Overshoot = (p - a).Length };
            var t = d * (1 / len);
            double u = (p - a).Dot(t);
            double c = Math.Max(0, Math.Min(len, u));
            return new WallStation { Point = a + t * c, Tangent = t, Overshoot = Math.Abs(u - c) };
        }

        /// <summary>Nearest point of a curved wall (arc: centre, radius, start angle, signed sweep in
        /// radians, positive = counter-clockwise) to p (plan). The tangent follows the arc direction.</summary>
        public static WallStation NearestOnArc(V3 centre, double radius, double startAngle, double sweep, V3 p)
        {
            centre = Plan(centre); p = Plan(p);
            var rel = p - centre;
            double ang = rel.Length < 1e-9 ? startAngle : Math.Atan2(rel.Y, rel.X);
            // angle travelled from the start, in the arc's direction, in [0, 2pi)
            double dir = sweep >= 0 ? 1 : -1;
            double along = Mod((ang - startAngle) * dir, 2 * Math.PI);
            double total = Math.Abs(sweep);
            double overshootAng = 0;
            if (along > total)
            {
                // beyond the end: clamp to the nearer end
                double pastEnd = along - total, beforeStart = 2 * Math.PI - along;
                if (pastEnd <= beforeStart) { overshootAng = pastEnd; along = total; }
                else { overshootAng = beforeStart; along = 0; }
            }
            double a = startAngle + dir * along;
            var radial = new V3(Math.Cos(a), Math.Sin(a), 0);
            var point = centre + radial * radius;
            var tangent = new V3(-radial.Y, radial.X, 0) * dir;
            double overshoot = overshootAng > 0 ? Math.Abs((p - point).Dot(tangent)) : 0;
            return new WallStation { Point = point, Tangent = tangent, Overshoot = overshoot };
        }

        static double Mod(double v, double m) { var r = v % m; return r < 0 ? r + m : r; }

        /// <summary>Signed plan offset of p from the location line at the station, along Left(tangent).</summary>
        public static double Offset(WallStation s, V3 p) => Left(s.Tangent).Dot(Plan(p) - s.Point);

        /// <summary>
        /// Which face: +1 = the face at <paramref name="high"/>, -1 = the face at <paramref name="low"/>
        /// (face offsets from the location line, low &lt; high). A point outside the wall takes its own
        /// side. Inside the thickness, the block symbol's centre decides; without one, the nearer face.
        /// </summary>
        public static int ChooseSide(double point, double low, double high, double? centre)
        {
            if (point >= high) return 1;
            if (point <= low) return -1;
            double mid = (low + high) / 2;
            if (centre.HasValue && Math.Abs(centre.Value - point) > 1e-9)
                return centre.Value >= point ? 1 : -1;   // the symbol is drawn towards that face
            return point >= mid ? 1 : -1;
        }

        /// <summary>Plan distance from the CAD point to the wall body (0 inside it).</summary>
        public static double DistanceToWall(double point, double low, double high, double overshoot)
        {
            double perp = point > high ? point - high : point < low ? low - point : 0;
            return Math.Sqrt(perp * perp + overshoot * overshoot);
        }

        /// <summary>
        /// The full plan for one block: <paramref name="low"/>/<paramref name="high"/> are the offsets
        /// of the two side faces from the location line (along Left(tangent)), <paramref name="centre"/>
        /// the block symbol's centre of geometry (optional), <paramref name="z"/> the placement height
        /// (level + Elevation From Level). The block rotation is deliberately not an input.
        /// </summary>
        public static WallPlan Plan(WallStation s, double low, double high, V3 cadPoint, V3? centre, double z)
        {
            if (low > high) { var t = low; low = high; high = t; }
            var n = Left(s.Tangent);
            double sp = Offset(s, cadPoint);
            double? sc = centre.HasValue ? Offset(s, centre.Value) : (double?)null;
            int side = ChooseSide(sp, low, high, sc);
            double face = side > 0 ? high : low;
            var onFace = s.Point + n * face;
            var point = new V3(onFace.X, onFace.Y, z);
            var normal = n * side;
            return new WallPlan
            {
                Side = side,
                Point = point,
                Normal = normal,
                ReferenceDirection = Left(normal),
                DistanceFt = DistanceToWall(sp, low, high, s.Overshoot),
                MovedFt = (Plan(cadPoint) - onFace).Length,
                InsideWall = sp > low && sp < high,
            };
        }

        public static bool NeedsMoveReview(WallPlan p) => p.MovedFt * MmPerFoot > MoveReviewMm;
    }
}
