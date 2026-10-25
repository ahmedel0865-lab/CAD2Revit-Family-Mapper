using System;
using System.Globalization;

namespace DMCRemapper.Core
{
    /// <summary>Names of the horizontal reference planes created for Host Type
    /// "Reference Plane (auto-create)", e.g. "DMC_Level 1_+2800mm".</summary>
    public static class RefPlaneNames
    {
        public const string Prefix = "DMC_";
        /// <summary>Prefixes of planes made under the tool's former names (SmartHost MEP, CAD2Revit):
        /// still found and reused, so no duplicates.</summary>
        public static readonly string[] OldPrefixes = { "SmartHost_", "CAD2Revit_" };

        /// <summary>Down-facing planes (ceiling devices) use the plain name; Up-facing planes
        /// (floor devices) get "_Up", because one plane has one normal direction.</summary>
        public static string For(string levelName, double elevationMm, Facing facing) => Name(Prefix, levelName, elevationMm, facing);

        /// <summary>The names older versions gave the same plane (newest former name first).</summary>
        public static string[] OldFor(string levelName, double elevationMm, Facing facing) =>
            Array.ConvertAll(OldPrefixes, p => Name(p, levelName, elevationMm, facing));

        /// <summary>True for a plane name made by this tool under its current or a former name.</summary>
        public static bool IsOurs(string name) =>
            name != null && (name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) ||
                             Array.Exists(OldPrefixes, p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase)));

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
