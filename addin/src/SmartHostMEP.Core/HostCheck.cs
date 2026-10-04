using System.Globalization;

namespace SmartHostMEP.Core
{
    /// <summary>What a placed instance actually ended up hosted on (FamilyInstance.Host).</summary>
    public enum HostKind { None, ReferencePlane, Level, LinkInstance, Element }

    /// <summary>
    /// Checks made after a face-hosted placement: the instance must really sit on the face
    /// that was found. Revit can accept a placement and still host the instance on a
    /// reference plane or level (e.g. with a link face reference it cannot resolve); such
    /// an instance is reported as failed, never as placed.
    /// </summary>
    public static class HostCheck
    {
        /// <summary>"slab", "ceiling", "wall" or "face" for messages.</summary>
        public static string What(HostMode mode) =>
            mode == HostMode.SlabAbove || mode == HostMode.SlabBelow ? "slab"
            : mode == HostMode.Ceiling ? "ceiling"
            : mode == HostMode.Wall ? "wall"
            : "face";

        public static string KindText(HostKind kind) =>
            kind == HostKind.ReferencePlane ? "Reference Plane"
            : kind == HostKind.Level ? "Level"
            : kind == HostKind.LinkInstance ? "Revit link"
            : kind == HostKind.Element ? "element of this model"
            : "none";

        /// <summary>Null when the instance is hosted as expected, else the failure message,
        /// e.g. "failed - not hosted on linked slab: Host is Reference Plane".</summary>
        public static string Problem(HostKind actual, bool expectLinked, bool hasHostFace, string what)
        {
            string target = (expectLinked ? "linked " : "") + what;
            string prefix = "failed - not hosted on " + target + ": ";
            if (actual == HostKind.None || actual == HostKind.ReferencePlane || actual == HostKind.Level)
                return prefix + "Host is " + KindText(actual);
            if (expectLinked && actual != HostKind.LinkInstance)
                return prefix + "Host is an element of this model, not the Revit link";
            if (!expectLinked && actual == HostKind.LinkInstance)
                return prefix + "Host is a Revit link, not the element found in this model";
            if (!hasHostFace)
                return prefix + "the instance has no host face";
            return null;
        }

        /// <summary>Message for a family that cannot be hosted on a face.</summary>
        public static string NotFaceBased(string placementType, string what) =>
            $"family is not face-based (placement type {placementType}) - it cannot be hosted on a {what}; " +
            "use a family made from a face-based template";

        /// <summary>One DebugHosting line, e.g. "DEBUG linked=yes link=STR.rvt element=123 (Floors)
        /// normal=(0.000,0.000,-1.000) host=Revit link STR.rvt, host face ok".</summary>
        public static string DebugLine(bool found, bool linked, string linkName, long? elementId, string category,
                                       double[] normal, string finalHost)
        {
            var inv = CultureInfo.InvariantCulture;
            string host = "host=" + (string.IsNullOrEmpty(finalHost) ? "none" : finalHost);
            if (!found) return "DEBUG no host face found; " + host;
            string n = normal != null && normal.Length == 3
                ? string.Format(inv, "({0:0.000},{1:0.000},{2:0.000})", normal[0], normal[1], normal[2])
                : "?";
            return "DEBUG linked=" + (linked ? "yes" : "no")
                 + (linked ? " link=" + (string.IsNullOrEmpty(linkName) ? "?" : linkName) : "")
                 + " element=" + (elementId.HasValue ? elementId.Value.ToString(inv) : "?")
                 + " (" + (string.IsNullOrEmpty(category) ? "?" : category) + ")"
                 + " normal=" + n + "; " + host;
        }
    }
}
