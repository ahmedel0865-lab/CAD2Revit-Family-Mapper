using System.Collections.Generic;
using System.Linq;
using SmartHostMEP.Core;
using Xunit;

namespace SmartHostMEP.Core.Tests
{
    /// <summary>DWG wall-line scoring for vertical planes with no Revit wall/column nearby.</summary>
    public class WallLineScorerTests
    {
        const double Mm = 1 / 304.8;
        static int _path;

        static LineSeg L(double x1, double y1, double x2, double y2, string layer = "A-WALL", int? path = null) =>
            new LineSeg { X1 = x1 * Mm, Y1 = y1 * Mm, X2 = x2 * Mm, Y2 = y2 * Mm, Layer = layer, Path = path ?? ++_path };

        /// <summary>A 200 x 100 mm symbol centred at (x, y), axis-aligned.</summary>
        static BlockOutline Box(double x, double y, double w = 200, double h = 100) =>
            BlockOutline.FromCorners(new[]
            {
                new V3((x - w / 2) * Mm, (y - h / 2) * Mm, 0), new V3((x + w / 2) * Mm, (y - h / 2) * Mm, 0),
                new V3((x + w / 2) * Mm, (y + h / 2) * Mm, 0), new V3((x - w / 2) * Mm, (y + h / 2) * Mm, 0),
            }, x * Mm, y * Mm);

        [Fact]
        public void ATouchingLineBeatsACloserLookingLongerOne()
        {
            // Socket box from y = 50 to 150. Line 1 touches its bottom edge (y = 50). Line 2 is 100 mm
            // above the top, longer. The touching one wins.
            var touching = L(-1000, 50, 1000, 50);
            var other = L(-3000, 250, 3000, 250);
            var s = new WallLineScorer(new[] { touching, other });
            var c = s.Choose(Box(0, 100));
            Assert.Same(touching, c.Seg);
            Assert.True(c.Touching);
            Assert.False(c.Ambiguous);
            Assert.Equal("Touching line (layer: A-WALL)", WallLineScorer.SourceText(c));
        }

        [Fact]
        public void TheOutlineCountsNotJustTheInsertionPoint()
        {
            // Insertion point 250 mm from the line, but the 400 mm deep symbol reaches 50 mm from it.
            var line = L(-2000, 0, 2000, 0);
            var s = new WallLineScorer(new[] { line });
            Assert.NotNull(s.Choose(Box(0, 250, 200, 400)));
            // The same point with a tiny symbol: 245 mm away, still inside the 300 mm search.
            Assert.NotNull(s.Choose(Box(0, 250, 10, 10)));
            // 400 mm away: outside the 300 mm search.
            Assert.Null(s.Choose(Box(0, 400, 10, 10)));
        }

        [Fact]
        public void ShortLinesAndWiresAreNotCandidates()
        {
            var shortLine = L(-140, 0, 140, 0);                 // 280 mm: not longer than 300 mm
            var wire = L(0, 50, 0, -1500);                      // starts at the symbol outline: a tail
            var s = new WallLineScorer(new[] { shortLine, wire });
            var c = s.Choose(Box(0, 100));
            Assert.Null(c);
        }

        [Fact]
        public void AWirePolylineIsATailOnAllItsSegments()
        {
            // Polyline leaving the block: down 200 mm then along. Its long segment passes near the block.
            int p = 900;
            var a = L(0, 50, 0, -100, "E-WIRE", p);
            var b = L(0, -100, 3000, -100, "E-WIRE", p);
            var s = new WallLineScorer(new[] { a, b });
            Assert.Null(s.Choose(Box(0, 100)));
        }

        [Fact]
        public void ClosedPolylinesHaveNoTails()
        {
            // A column drawn as a closed polyline; the block sits against it.
            int p = 901;
            var segs = new[] { L(0, 0, 400, 0, "S-COL", p), L(400, 0, 400, 400, "S-COL", p), L(400, 400, 0, 400, "S-COL", p), L(0, 400, 0, 0, "S-COL", p) };
            var s = new WallLineScorer(segs);
            var c = s.Choose(Box(200, 450));
            Assert.NotNull(c);
            Assert.Same(segs[2], c.Seg);
        }

        [Fact]
        public void TwoLinesScoringWithin10PercentAreAmbiguous()
        {
            // Block centred between two equal lines 100 mm above and below its outline.
            var below = L(-1500, -100, 1500, -100);
            var above = L(-1500, 200, 1500, 200);
            var s = new WallLineScorer(new[] { below, above });
            var c = s.Choose(Box(0, 50));
            Assert.True(c.Ambiguous);
            Assert.Equal("Ambiguous wall line", WallLineScorer.AmbiguousReason);
        }

        [Fact]
        public void PiecesOfTheSameLineAreNotAmbiguousWithEachOther()
        {
            // One wall line split in two next to the block (the split is 50 mm from its outline).
            var left = L(-2000, 50, -150, 50);
            var right = L(-150, 50.002, 2000, 50.002);
            Assert.True(WallLineScorer.SameLine(left, right));
            var s = new WallLineScorer(new[] { left, right });
            var c = s.Choose(Box(0, 100));
            Assert.False(c.Ambiguous);
            Assert.Equal(left.Group, right.Group);
        }

        [Fact]
        public void ContinuityPrefersTheLineOtherBlocksAlsoTouch()
        {
            // Two lines equally far from block 0 (100 mm below and above its outline). Blocks 1 and 2
            // farther along touch the lower line, so it wins for block 0.
            var lower = L(-3000, -100, 3000, -100);
            var upper = L(-3000, 200, 3000, 200);
            var s = new WallLineScorer(new[] { lower, upper });
            var blocks = new List<BlockOutline> { Box(0, 50), Box(1500, -50), Box(-1500, -50) };
            s.AddBlocks(blocks);
            var c = s.Choose(blocks[0], 0);
            Assert.Same(lower, c.Seg);
            Assert.Equal(2, c.Continuity);
            Assert.False(c.Ambiguous);
            // The block itself never counts for its own continuity.
            Assert.Equal(1, s.Choose(blocks[1], 1).Continuity);
        }

        [Fact]
        public void ScoreParts()
        {
            var w = new ScoreWeights { Touch = 100, Distance = 50, Length = 30, Continuity = 10 };
            var line = L(0, 0, 3000, 0);
            var s = new WallLineScorer(new[] { line }, 300, w);
            // Touching the outline, full length: 100 + 50 + 30 = 180.
            Assert.Equal(180, s.Score(Box(1500, 50), line).Score, 3);
            // 150 mm away (half the search), not touching: 25 + 30 = 55.
            var c = s.Score(Box(1500, 200), line);
            Assert.False(c.Touching);
            Assert.Equal(55, c.Score, 3);
            Assert.Equal("Nearest line (score 55)", WallLineScorer.SourceText(c));
        }

        [Fact]
        public void LengthIsCappedAt3000Mm()
        {
            var w = new ScoreWeights { Touch = 0, Distance = 0, Length = 30, Continuity = 0 };
            var l3 = L(0, 0, 3000, 0);
            var l9 = L(0, 1000, 9000, 1000);
            var l15 = L(0, 2000, 1500, 2000);
            var s = new WallLineScorer(new[] { l3, l9, l15 }, 300, w);
            Assert.Equal(30, s.Score(Box(500, 100), l3).Score, 3);
            Assert.Equal(30, s.Score(Box(500, 1100), l9).Score, 3);
            Assert.Equal(15, s.Score(Box(500, 2100), l15).Score, 3);
        }

        [Fact]
        public void TouchingTheInsertionPointCountsAsTouching()
        {
            // No outline (block without line work): the line passes 10 mm from the point.
            var line = L(-1000, 10, 1000, 10);
            var s = new WallLineScorer(new[] { line });
            var c = s.Choose(BlockOutline.AtPoint(0, 0));
            Assert.True(c.Touching);
        }

        [Fact]
        public void RotatedOutlines()
        {
            // A 45 deg rotated square around (0, 0), half diagonal 141 mm; a line at y = -150 is 9 mm away: touching.
            double h = 141.42;
            var o = BlockOutline.FromCorners(new[] { new V3(0, -h * Mm, 0), new V3(h * Mm, 0, 0), new V3(0, h * Mm, 0), new V3(-h * Mm, 0, 0) }, 0, 0);
            var line = L(-1000, -150, 1000, -150);
            Assert.Equal(8.58, WallLineScorer.SegmentToOutline(o, line) / Mm, 1);
            Assert.True(new WallLineScorer(new[] { line }).Choose(o).Touching);
        }

        [Fact]
        public void NearestForThePickedPoint()
        {
            var a = L(0, 0, 2000, 0);
            var b = L(0, 500, 2000, 500);
            var s = new WallLineScorer(new[] { a, b });
            Assert.Same(b, s.Nearest(1000 * Mm, 420 * Mm, 300 * Mm));
            Assert.Null(s.Nearest(1000 * Mm, 250 * Mm, 100 * Mm));
        }

        [Fact]
        public void ReviewTexts()
        {
            Assert.Equal("No wall line found", WallLineScorer.NoLineReason);
            Assert.Equal("Moved 250 mm to snap to the wall line", EdgeSnap.SnapMovedReason(250));
            Assert.Equal("SmartHost: A", NeedsReview.WithoutMark("SmartHost: Host = Reference Plane | SmartHost: A"));
            Assert.Equal("note", NeedsReview.WithoutMark("note"));
        }

        [Fact]
        public void ScoreWeightsAreReadFromSettings()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sh_w_" + System.Guid.NewGuid().ToString("N") + ".ini");
            try
            {
                System.IO.File.WriteAllText(path, "LineScoreTouch = 80\nLineScoreDistance = 10\nLineScoreLength = 5\n" +
                                                  "LineScoreContinuity = 2\nLineScoreContinuityMax = 3\nLineScoreAmbiguousPercent = 25\nShowDetection = true\n");
                var st = Settings.Load(path);
                Assert.Equal(80, st.LineScore.Touch);
                Assert.Equal(10, st.LineScore.Distance);
                Assert.Equal(5, st.LineScore.Length);
                Assert.Equal(2, st.LineScore.Continuity);
                Assert.Equal(3, st.LineScore.ContinuityMax);
                Assert.Equal(25, st.LineScore.AmbiguousPercent);
                Assert.True(st.ShowDetection);
                Assert.Contains("LineScoreTouch = 80", st.ToIni());
            }
            finally { try { System.IO.File.Delete(path); } catch { } }
        }

        [Fact]
        public void ManySegmentsAreGroupedQuickly()
        {
            // 20 000 segments: grouping and a choice per block stay fast (index, no pairwise scan).
            var segs = Enumerable.Range(0, 20000).Select(i => L(i * 10, (i % 200) * 400, i * 10 + 1000, (i % 200) * 400)).ToList();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var s = new WallLineScorer(segs);
            for (int k = 0; k < 500; k++) s.Choose(Box(k * 100, 50));
            Assert.True(sw.ElapsedMilliseconds < 5000, $"took {sw.ElapsedMilliseconds} ms");
        }
    }
}
