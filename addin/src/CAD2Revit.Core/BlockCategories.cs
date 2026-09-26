using System;
using System.Collections.Generic;
using System.Linq;

namespace CAD2Revit.Core
{
    /// <summary>
    /// Sorts CAD blocks into disciplines (Electrical, Architectural, ...) from their
    /// names, so the mapping window can group them and electrical devices are not
    /// lost among doors, furniture and grid heads from the background drawing.
    /// The result is only a suggestion; the user can change it per row.
    /// </summary>
    public static class BlockCategories
    {
        public const string Electrical = "Electrical";
        public const string Mechanical = "Mechanical";
        public const string Plumbing = "Plumbing";
        public const string Architectural = "Architectural";
        public const string Structural = "Structural";
        public const string Annotation = "Annotation";
        public const string Other = "Other";

        /// <summary>Display / group order.</summary>
        public static readonly string[] All = { Electrical, Mechanical, Plumbing, Architectural, Structural, Annotation, Other };

        public static int Order(string category)
        {
            int i = Array.FindIndex(All, c => c.Equals(category ?? "", StringComparison.OrdinalIgnoreCase));
            return i < 0 ? All.Length : i;
        }

        public static string Normalize(string category)
        {
            var c = All.FirstOrDefault(x => x.Equals((category ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
            return c ?? (string.IsNullOrWhiteSpace(category) ? null : Other);
        }

        // Checked in this order: the first discipline with a matching keyword wins.
        // Keywords of 3 letters or fewer, and keywords starting with "=", must equal a whole
        // word ("db", "=heat"); longer keywords also match inside a word ("downlight"
        // contains "light"). Keywords with a space need all their words ("fire alarm").
        static readonly (string Category, string[] Keywords)[] Rules =
        {
            (Electrical, new[]
            {
                "light", "lighting", "luminaire", "lum", "lamp", "downlight", "spotlight", "chandelier", "chandlier",
                "led", "emergency", "exit sign", "exitsign", "socket", "skt", "outlet", "receptacle", "rcpt", "switch", "sw",
                "dimmer", "isolator", "db", "panelboard", "distribution", "mdb", "smdb", "smoke",
                "=heat", "detector", "det", "mcp", "callpoint", "sounder", "strobe", "beacon", "bell", "horn", "speaker",
                "spk", "fire alarm", "firealarm", "alarm", "fa", "cctv", "camera", "cam", "data", "tel", "telephone", "phone", "wap",
                "wifi", "access control", "accesscontrol", "card reader", "cardreader", "reader", "intercom", "nurse", "pir", "sensor", "motion", "occupancy", "tv",
                "satellite", "junction", "jb", "power", "elec", "electrical", "cable", "tray", "trunking", "busbar",
                "ups", "generator", "transformer", "=meter", "em", "floor box", "floorbox",
            }),
            (Annotation, new[]
            {
                "grid", "tag", "text", "label", "title", "north", "arrow", "section", "callout", "legend", "keynote",
                "revision", "cloud", "matchline", "dimension", "dim", "symbol", "mark", "note", "logo", "border",
                "viewport", "scale",
            }),
            (Plumbing, new[]
            {
                "toilet", "wc", "urinal", "basin", "sink", "lavatory", "shower", "bath", "bathtub", "drain", "fd",
                "gully", "trap", "tap", "faucet", "valve", "pipe", "plumbing", "water", "heater", "sanitary", "bidet",
                "cleanout", "manhole",
            }),
            (Mechanical, new[]
            {
                "diffuser", "grille", "grill", "duct", "ahu", "fcu", "vav", "fan", "damper", "hvac", "ac", "split",
                "vent", "exhaust", "extract", "supply", "return", "chiller", "boiler", "pump", "thermostat",
                "mechanical", "mech", "cooling", "heating", "radiator",
            }),
            (Structural, new[]
            {
                "column", "beam", "footing", "foundation", "pile", "brace", "truss", "slab", "rebar", "structural",
                "struct", "joist",
            }),
            (Architectural, new[]
            {
                "door", "window", "wall", "casework", "cabinet", "counter", "furniture", "chair", "table", "desk",
                "bed", "sofa", "wardrobe", "shelf", "shelving", "elevator", "lift", "escalator", "stair", "ramp",
                "railing", "handrail", "louver", "louvre", "louvers", "curtain", "partition", "opening", "ceiling",
                "floor", "room", "kitchen", "plant", "tree", "car", "parking", "inclined", "=arch", "architectural",
                "glazing", "facade", "roof", "skylight", "mirror", "locker", "bench",
            }),
        };

        /// <summary>Discipline suggested for a block name.</summary>
        public static string Classify(string blockName)
        {
            var tokens = FamilyMatcher.Tokens(blockName);
            if (tokens.Count == 0) return Other;
            foreach (var (category, keywords) in Rules)
                foreach (var k in keywords)
                {
                    if (k.Contains(' '))
                    {
                        if (k.Split(' ').All(w => tokens.Contains(w))) return category;
                    }
                    else if (k.StartsWith("="))
                    {
                        if (tokens.Contains(k.Substring(1))) return category;
                    }
                    else if (k.Length <= 3 ? tokens.Contains(k) : tokens.Any(t => t.Contains(k)))
                        return category;
                }
            return Other;
        }

        /// <summary>Discipline of a Revit family category name ("Lighting Fixtures" -> Electrical),
        /// or null if unknown.</summary>
        public static string FromRevitCategory(string revitCategory)
        {
            var c = (revitCategory ?? "").ToLowerInvariant();
            if (c.Length == 0) return null;
            string[] elec = { "lighting", "electrical", "fire alarm", "communication", "data", "security", "nurse", "telephone" };
            if (elec.Any(c.Contains)) return Electrical;
            if (c.Contains("plumbing") || c.Contains("pipe")) return Plumbing;
            if (c.Contains("mechanical") || c.Contains("duct") || c.Contains("air terminal")) return Mechanical;
            if (c.Contains("structural")) return Structural;
            return null;
        }
    }
}
