using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SmartHostMEP.Core
{
    /// <summary>One Slab (above) / Ceiling block that found no host and went on the fallback plane/height.</summary>
    public class ReviewItem
    {
        public long? ElementId;
        public string FamilyType = "";
        public string Block = "";
        public double Xmm, Ymm;
        public string Reason = "";

        public string XY => Xmm.ToString("0", CultureInfo.InvariantCulture) + ", " + Ymm.ToString("0", CultureInfo.InvariantCulture);
    }

    /// <summary>The "Needs Review" list shown after a run: every Slab (above) / Ceiling fallback element,
    /// and wall-hosted elements to check (no wall in range, moved more than 200 mm, wall in a link).</summary>
    public static class NeedsReview
    {
        const double MmPerFoot = 304.8;

        /// <summary>Written in the Comments of every fallback element, to find them with a filter or schedule.</summary>
        public const string CommentText = "SmartHost: Host = Reference Plane";

        public static readonly string[] Header = { "Element ID", "Family : Type", "CAD Block", "X (mm)", "Y (mm)", "Reason" };

        public static List<ReviewItem> From(IEnumerable<PlacementResult> results) =>
            results.Where(r => r.Status == Status.Placed && (r.SlabFallback || !string.IsNullOrEmpty(r.Review)))
                .Select(r => new ReviewItem
                {
                    ElementId = r.ElementId,
                    FamilyType = r.Row?.Label ?? "",
                    Block = r.BlockName,
                    Xmm = r.Point != null ? r.Point[0] * MmPerFoot : 0,
                    Ymm = r.Point != null ? r.Point[1] * MmPerFoot : 0,
                    Reason = string.Join("; ", new[] { r.SlabFallback ? Reason(r.Message) : null, r.Review }
                                                .Where(t => !string.IsNullOrEmpty(t))),
                })
                .ToList();

        /// <summary>"No slab/beam within 5000 mm - placed on ..." -> "No slab/beam within 5000 mm".</summary>
        public static string Reason(string message)
        {
            var part = (message ?? "").Split(new[] { "; " }, StringSplitOptions.None)
                .FirstOrDefault(SlabSearch.IsFallbackText) ?? "No host in range";
            int i = part.IndexOf(" - ", StringComparison.Ordinal);
            return i > 0 ? part.Substring(0, i) : part;
        }

        /// <summary>"123,456,789" for Manage &gt; Select by ID.</summary>
        public static string CopyIds(IEnumerable<ReviewItem> items) =>
            string.Join(",", items.Where(i => i.ElementId.HasValue).Select(i => i.ElementId.Value.ToString(CultureInfo.InvariantCulture)));

        public static List<IList<object>> Rows(IEnumerable<ReviewItem> items) =>
            items.Select(i => (IList<object>)new object[]
            {
                i.ElementId.HasValue ? (object)i.ElementId.Value : "", i.FamilyType, i.Block,
                Math.Round(i.Xmm, 1), Math.Round(i.Ymm, 1), i.Reason,
            }).ToList();
    }
}
