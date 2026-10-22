using System;
using System.Collections.Generic;
using System.Linq;

namespace SmartHostMEP.Core
{
    /// <summary>Auto-match by symbol geometry: weights and limits (settings.ini, SymbolScore* keys).</summary>
    public class SymbolScoreWeights
    {
        /// <summary>Shape overlap (chamfer distance of the sampled points, best of 8 orientations).</summary>
        public double Shape = 55;
        /// <summary>Element mix: share of the line work that is curved (arcs, circles) vs straight.</summary>
        public double Counts = 15;
        public double Aspect = 15;
        /// <summary>64 x 64 raster overlap (backup for symbols the sampled points describe poorly).</summary>
        public double Raster = 15;
        /// <summary>Name similarity: adds at most this many points, so it only breaks ties.</summary>
        public double Name = 2;
        /// <summary>A family scoring at least this much is selected automatically.</summary>
        public double AutoMatch = 80;
        /// <summary>Below this the block stays unmatched (no candidate is offered).</summary>
        public double Minimum = 50;
        /// <summary>The best two closer than this (percent of the best): Needs Review "Ambiguous symbol match".</summary>
        public double AmbiguousPercent = 10;
    }

    /// <summary>A loaded family with its symbol (one per family: all its types share it).</summary>
    public class FamilySymbolEntry
    {
        public string Family = "";
        /// <summary>Revit category name, e.g. "Lighting Fixtures".</summary>
        public string Category = "";
        /// <summary>"Family : Type" labels of its types.</summary>
        public List<string> Types = new List<string>();
        public SymbolSignature Signature;
    }

    /// <summary>One ranked family for a CAD block.</summary>
    public class SymbolCandidate
    {
        public string Family = "";
        /// <summary>The type picked for the block ("Family : Type"): the one whose name is closest.</summary>
        public string Label = "";
        public double Score;          // 0..100, with the name tie-breaker
        public double Geometry;       // 0..100, geometry only
        public string View = "";      // family view the symbol came from
        public bool Learned;          // from a previous manual match

        public string Text => $"{Score:0}  {Label}";
        public override string ToString() => Text;
    }

    /// <summary>What Auto-match does with a block.</summary>
    public class SymbolDecision
    {
        public List<SymbolCandidate> Candidates = new List<SymbolCandidate>();
        /// <summary>The selected candidate (score at least AutoMatch, or learned), else null.</summary>
        public SymbolCandidate Chosen;
        public bool Ambiguous;
        public SymbolCandidate Best => Candidates.Count > 0 ? Candidates[0] : null;
    }

    /// <summary>
    /// Scores CAD block symbols against family symbols (0..100). Cheap checks first (category,
    /// curved share, aspect ratio), then the shape: the mean distance between the two point sets
    /// both ways (chamfer distance), for each of the 4 right-angle rotations and their mirror images;
    /// the best orientation counts.
    /// </summary>
    public class SymbolMatcher
    {
        public const string AmbiguousReason = "Ambiguous symbol match";
        /// <summary>Chamfer distance (normalized frame, larger side = 1) at which the shape score reaches 0.</summary>
        public const double ShapeZero = 0.15;
        /// <summary>Chamfer distance up to this counts as a perfect fit (sampling and distance-grid noise).</summary>
        public const double ShapeExact = 0.02;
        /// <summary>Pre-filter: aspect ratios farther apart than this are never compared.</summary>
        public const double MaxAspectGap = 0.5;
        /// <summary>Pre-filter: curved shares farther apart than this are never compared (e.g. a circle and a square).</summary>
        public const double MaxCurvedGap = 0.7;
        public const int TopCount = 3;

        readonly SymbolScoreWeights _w;

        public SymbolMatcher(SymbolScoreWeights weights = null) { _w = weights ?? new SymbolScoreWeights(); }

        public SymbolScoreWeights Weights => _w;

        /// <summary>False when the pair is clearly different without comparing shapes.</summary>
        public static bool PreFilter(SymbolSignature block, SymbolSignature family, string blockCategory = null, string familyCategory = null)
        {
            if (block == null || family == null || block.IsEmpty || family.IsEmpty) return false;
            var bc = BlockCategories.Normalize(blockCategory);
            var fc = BlockCategories.FromRevitCategory(familyCategory);
            if (bc != null && bc != BlockCategories.Other && bc != BlockCategories.Annotation && fc != null && fc != bc) return false;
            if (Math.Abs(block.Aspect - family.Aspect) > MaxAspectGap) return false;
            if (Math.Abs(block.Curved - family.Curved) > MaxCurvedGap) return false;
            return true;
        }

        /// <summary>Symmetric chamfer distance in orientation <paramref name="t"/> of <paramref name="a"/>.</summary>
        public static double Chamfer(SymbolSignature a, SymbolSignature b, int t)
        {
            double sa = 0, sb = 0;
            var pa = a.Points;
            for (int k = 0; k + 1 < pa.Length; k += 2)
            {
                SymbolSignature.Orient(t, pa[k], pa[k + 1], out var x, out var y);
                sa += b.DistanceTo(x, y);
            }
            int inv = SymbolSignature.Inverse(t);
            var pb = b.Points;
            for (int k = 0; k + 1 < pb.Length; k += 2)
            {
                SymbolSignature.Orient(inv, pb[k], pb[k + 1], out var x, out var y);
                sb += a.DistanceTo(x, y);
            }
            return 0.5 * (sa / a.PointCount + sb / b.PointCount);
        }

        /// <summary>Raster overlap 0..1 in orientation <paramref name="t"/> of <paramref name="a"/>:
        /// the share of each raster's cells lying on (or next to) the other's, averaged.</summary>
        public static double RasterOverlap(SymbolSignature a, SymbolSignature b, int t)
        {
            var ta = a.Thick();
            var tb = b.Thick();
            int na = 0, hitA = 0, nb = 0, hitB = 0;
            int inv = SymbolSignature.Inverse(t);
            for (int j = 0; j < SymbolSignature.RasterSize; j++)
                for (int i = 0; i < SymbolSignature.RasterSize; i++)
                {
                    if (SymbolSignature.GetCell(a.Raster, i, j))
                    {
                        na++;
                        SymbolSignature.OrientCell(t, i, j, out var oi, out var oj);
                        if (SymbolSignature.GetCell(tb, oi, oj)) hitA++;
                    }
                    if (SymbolSignature.GetCell(b.Raster, i, j))
                    {
                        nb++;
                        SymbolSignature.OrientCell(inv, i, j, out var oi, out var oj);
                        if (SymbolSignature.GetCell(ta, oi, oj)) hitB++;
                    }
                }
            if (na == 0 || nb == 0) return 0;
            return 0.5 * ((double)hitA / na + (double)hitB / nb);
        }

        /// <summary>Element mix 0..1: how alike the curved shares are. Told from the geometry, not from
        /// the element types, so lines vs polylines or a circle vs a 64-sided polyline do not matter.</summary>
        public static double CountSimilarity(SymbolSignature a, SymbolSignature b) =>
            Math.Max(0, 1 - Math.Abs(a.Curved - b.Curved));

        public static double AspectSimilarity(SymbolSignature a, SymbolSignature b) =>
            Math.Max(0, 1 - Math.Abs(a.Aspect - b.Aspect) / Math.Max(0.1, Math.Max(a.Aspect, b.Aspect)));

        /// <summary>Geometry score 0..100 (no name part); 0 when either symbol is empty.</summary>
        public double Geometry(SymbolSignature a, SymbolSignature b)
        {
            if (a == null || b == null || a.IsEmpty || b.IsEmpty) return 0;
            double best = double.MaxValue;
            int bestT = 0;
            for (int t = 0; t < 8; t++)
            {
                var d = Chamfer(a, b, t);
                if (d < best - 1e-12) { best = d; bestT = t; }
            }
            double shape = Math.Max(0, Math.Min(1, 1 - (best - ShapeExact) / (ShapeZero - ShapeExact)));
            double total = _w.Shape + _w.Counts + _w.Aspect + _w.Raster;
            if (total <= 0) return 0;
            double raster = _w.Raster > 0 ? RasterOverlap(a, b, bestT) : 0;
            double score = (_w.Shape * shape + _w.Counts * CountSimilarity(a, b) + _w.Aspect * AspectSimilarity(a, b) + _w.Raster * raster) / total;
            return Math.Max(0, Math.Min(100, score * 100));
        }

        /// <summary>The best families for a block, best first (at most <see cref="TopCount"/>, only
        /// those scoring at least Minimum). One entry per family; its type is the one whose name is
        /// closest to the block name.</summary>
        public List<SymbolCandidate> Rank(SymbolSignature block, string blockName, string blockCategory, IEnumerable<FamilySymbolEntry> families)
        {
            var result = new List<SymbolCandidate>();
            if (block == null || block.IsEmpty) return result;
            foreach (var f in families)
            {
                if (f.Types.Count == 0 || !PreFilter(block, f.Signature, blockCategory, f.Category)) continue;
                double geo = Geometry(block, f.Signature);
                if (geo + _w.Name < _w.Minimum) continue;
                string label = f.Types[0];
                double name = -1;
                foreach (var t in f.Types)
                {
                    double s = FamilyMatcher.Score(blockName, t);
                    if (s > name + 1e-9 || (Math.Abs(s - name) < 1e-9 && t.Length < label.Length)) { name = s; label = t; }
                }
                double score = Math.Min(100, geo + _w.Name * Math.Max(0, name));
                if (score < _w.Minimum) continue;
                result.Add(new SymbolCandidate { Family = f.Family, Label = label, Score = score, Geometry = geo, View = f.Signature.View ?? "" });
            }
            return result.OrderByDescending(c => c.Score).ThenBy(c => c.Label, StringComparer.OrdinalIgnoreCase).Take(TopCount).ToList();
        }

        /// <summary>Applies the match rules to ranked candidates: the best is chosen at AutoMatch or
        /// more, and is ambiguous when the second is within AmbiguousPercent of it.</summary>
        public SymbolDecision Decide(List<SymbolCandidate> ranked)
        {
            var d = new SymbolDecision { Candidates = ranked ?? new List<SymbolCandidate>() };
            var best = d.Best;
            if (best == null || best.Score < _w.AutoMatch) return d;
            d.Chosen = best;
            if (d.Candidates.Count > 1 && d.Candidates[1].Score >= best.Score * (1 - _w.AmbiguousPercent / 100.0))
                d.Ambiguous = true;
            return d;
        }
    }
}
