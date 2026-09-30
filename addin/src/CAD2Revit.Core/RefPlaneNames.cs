using System;
using System.Globalization;

namespace CAD2Revit.Core
{
    /// <summary>Names of the horizontal reference planes created for Host Type
    /// "Reference Plane (auto-create)", e.g. "CAD2Revit_Level 1_+2800mm".</summary>
    public static class RefPlaneNames
    {
        public const string Prefix = "CAD2Revit_";

        /// <summary>Down-facing planes (ceiling devices) use the plain name; Up-facing planes
        /// (floor devices) get "_Up", because one plane has one normal direction.</summary>
        public static string For(string levelName, double elevationMm, Facing facing)
        {
            double r = Math.Round(elevationMm, 1);
            var elev = (r < 0 ? "-" : "+") + Math.Abs(r).ToString("0.#", CultureInfo.InvariantCulture) + "mm";
            // Characters Revit does not allow in element names.
            var level = (levelName ?? "Level").Trim();
            foreach (var c in "{}[]|;<>?`~:\\") level = level.Replace(c, '_');
            return Prefix + level + "_" + elev + (facing == Facing.Up ? "_Up" : "");
        }
    }
}
