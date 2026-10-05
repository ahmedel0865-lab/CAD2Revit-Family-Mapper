using System;
using System.Collections.Generic;
using System.Linq;

namespace SmartHostMEP.Core
{
    /// <summary>
    /// Existing instances for the "already placed" check, built once per run. A block counts as
    /// already in the model when, within the duplicate tolerance in plan and inside the level's
    /// height band, there is an instance of the same family (or type) OR an instance whose
    /// Comments say "SmartHost: &lt;this block&gt;" (placed by this tool, possibly with another family).
    /// Comments written by older versions ("CAD: &lt;block&gt;", "CAD2Revit: ...") are recognised too.
    /// </summary>
    public class ExistingIndex
    {
        /// <summary>Comments tag for the block name.</summary>
        public const string Tag = "SmartHost: ";
        /// <summary>Tags written by older versions, still recognised.</summary>
        static readonly string[] OldTags = { "CAD: ", "CAD2Revit: " };
        readonly PointGrid _byFamily, _byBlock;
        readonly Dictionary<string, long> _blockIds = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        public ExistingIndex(double tolerance)
        {
            _byFamily = new PointGrid(tolerance);
            _byBlock = new PointGrid(tolerance);
        }

        public int Count => _byFamily.Count;

        /// <summary>The block name in "SmartHost: A" or "SmartHost: Host = Reference Plane | SmartHost: A",
        /// or in the older "CAD: A" / "CAD2Revit: Host = Reference Plane | CAD: A"; else null.</summary>
        public static string BlockFromComment(string comments)
        {
            if (string.IsNullOrWhiteSpace(comments)) return null;
            var c = comments.Trim();
            int bar = c.LastIndexOf(" | ", StringComparison.Ordinal);
            var part = bar >= 0 ? c.Substring(bar + 3).Trim() : c;
            foreach (var tag in new[] { Tag }.Concat(OldTags))
            {
                if (!part.StartsWith(tag, StringComparison.OrdinalIgnoreCase)) continue;
                var name = part.Substring(tag.Length).Trim();
                // "SmartHost: Host = Reference Plane" on its own names no block.
                if (name.Length == 0 || name.StartsWith("Host = ", StringComparison.OrdinalIgnoreCase)) return null;
                return name;
            }
            return null;
        }

        /// <summary>Adds one existing instance. familyKey 0 = unknown family (only its comment counts).</summary>
        public void Add(long familyKey, string comments, double x, double y, double z)
        {
            if (familyKey != 0) _byFamily.Add(familyKey, x, y, z);
            var block = BlockFromComment(comments);
            if (block != null) _byBlock.Add(BlockId(block, create: true), x, y, z);
        }

        /// <summary>True when this block (of this family) already exists at (x, y) in [zMin, zMax).</summary>
        public bool Contains(long familyKey, string block, double x, double y, double zMin, double zMax)
        {
            if (familyKey != 0 && _byFamily.Contains(familyKey, x, y, zMin, zMax)) return true;
            long id = block == null ? 0 : BlockId(block, create: false);
            return id != 0 && _byBlock.Contains(id, x, y, zMin, zMax);
        }

        long BlockId(string block, bool create)
        {
            var key = block.Trim();
            if (_blockIds.TryGetValue(key, out var id)) return id;
            if (!create) return 0;
            id = _blockIds.Count + 1;
            _blockIds[key] = id;
            return id;
        }

        /// <summary>Text of the warning shown before Run.</summary>
        public static string Warning(int count) =>
            $"{count} element{(count == 1 ? "" : "s")} already exist{(count == 1 ? "s" : "")} at these locations.";
    }
}
