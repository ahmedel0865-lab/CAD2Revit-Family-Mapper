using System;
using System.Globalization;

namespace SmartHostMEP.Core
{
    /// <summary>Names of the horizontal reference planes created for Host Type
    /// "Reference Plane (auto-create)", e.g. "SmartHost_Level 1_+2800mm".</summary>
    public static class RefPlaneNames
    {
        public const string Prefix = "SmartHost_";
        /// <summary>Planes made before the rename (still found and reused, so no duplicates).</summary>
        public const string OldPrefix = "CAD2Revit_";

        /// <summary>Down-facing planes (ceiling devices) use the plain name; Up-facing planes
        /// (floor devices) get "_Up", because one plane has one normal direction.</summary>
        public static string For(string levelName, double elevationMm, Facing facing) => Name(Prefix, levelName, elevationMm, facing);

        /// <summary>The name an older version gave the same plane.</summary>
        public static string OldFor(string levelName, double elevationMm, Facing facing) => Name(OldPrefix, levelName, elevationMm, facing);

        static string Name(string prefix, string levelName, double elevationMm, Facing facing)
        {
            double r = Math.Round(elevationMm, 1);
            var elev = (r < 0 ? "-" : "+") + Math.Abs(r).ToString("0.#", CultureInfo.InvariantCulture) + "mm";
            // Characters Revit does not allow in element names.
            var level = (levelName ?? "Level").Trim();
            foreach (var c in "{}[]|;<>?`~:\\") level = level.Replace(c, '_');
            return prefix + level + "_" + elev + (facing == Facing.Up ? "_Up" : "");
        }
    }
}
