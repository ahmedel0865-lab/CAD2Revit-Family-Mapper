using System;
using System.Collections.Generic;
using System.Linq;
using SmartHostMEP.Core;
using Xunit;

namespace SmartHostMEP.Core.Tests
{
    /// <summary>Which wall/column face a vertical plane follows, and the symbol's back line.</summary>
    public class EdgeChoiceTests
    {
        const double Mm = 1 / 304.8;
        static V3 P(double xMm, double yMm) => new V3(xMm * Mm, yMm * Mm, 0);

        static EdgeIndex<string> Corner()
        {
            // A room corner at the origin: wall A along X (y = 0), wall B along Y (x = 0).
            var idx = new EdgeIndex<string>(600 * Mm);
            idx.Add(0, 0, 5000 * Mm, 0, "A along X");
            idx.Add(0, 0, 0, 5000 * Mm, "B along Y");
            return idx;
        }

        [Fact]
        public void AtACornerTheWallParallelToTheDeviceWinsOverACloserPerpendicularOne()
        {
            // Socket rotated to wall A (its wall direction is X), drawn 250 mm from A and only 150 mm from B.
            var block = P(150, 250);
            var hits = Corner().Within(block.X, block.Y, 600 * Mm);
            var hit = EdgeSnap.Choose(hits, block, new V3(1, 0, 0), out var off);
            Assert.Equal("A along X", hit.Payload);
            Assert.Equal(0, off, 6);
            // The old rule (closest edge) would have taken B.
            Assert.Equal("B along Y", Corner().Nearest(block.X, block.Y, 600 * Mm).Payload);
        }

        [Fact]
        public void WithNoParallelEdgeTheClosestOneIsUsedAndTheAngleReported()
        {
            var idx = new EdgeIndex<string>(600 * Mm);
            idx.Add(0, 0, 0, 5000 * Mm, "B along Y");
            var block = P(200, 1000);
            var hit = EdgeSnap.Choose(idx.Within(block.X, block.Y, 600 * Mm), block, new V3(1, 0, 0), out var off);
            Assert.Equal("B along Y", hit.Payload);
            Assert.Equal(90, off, 6);
            Assert.True(off > EdgeSnap.AlignDeg);
            Assert.Equal("Wall is 90° off the block/symbol direction - check the orientation", EdgeSnap.OffAngleReason(off));
        }

        [Theory]
        [InlineData(10, true)]      // within 15 deg: still the device's wall
        [InlineData(20, false)]
        public void ParallelMeansWithin15Degrees(double deg, bool aligned)
        {
            double a = deg * Math.PI / 180;
            Assert.Equal(aligned, EdgeSnap.AngleOffDeg(new V3(0, 0, 0), new V3(Math.Cos(a), Math.Sin(a), 0), new V3(1, 0, 0)) <= EdgeSnap.AlignDeg);
            // Direction sense does not matter.
            Assert.Equal(deg, EdgeSnap.AngleOffDeg(new V3(0, 0, 0), new V3(-Math.Cos(a), -Math.Sin(a), 0), new V3(1, 0, 0)), 6);
        }

        [Theory]
        [InlineData(2500, true)]    // along the wall
        [InlineData(5080, true)]    // just past the end (within 100 mm)
        [InlineData(5300, false)]   // beyond the end of the wall / in a door opening
        [InlineData(-300, false)]
        public void TheDeviceMustSitAlongTheWall(double xMm, bool covers) =>
            Assert.Equal(covers, EdgeSnap.Covers(P(xMm, 200), P(0, 0), P(5000, 0)));

        [Fact]
        public void AWallEndingBeforeTheDeviceIsSkippedForTheOneItSitsOn()
        {
            // Wall C ends at x = 1000 (door opening after it), 150 mm from the device; wall D runs past it 400 mm away.
            var idx = new EdgeIndex<string>(600 * Mm);
            idx.Add(0, 0, 1000 * Mm, 0, "C ends");
            idx.Add(0, 400 * Mm + 0, 4000 * Mm, 400 * Mm, "D continues");
            var block = P(1400, 120);
            var hit = EdgeSnap.Choose(idx.Within(block.X, block.Y, 600 * Mm), block, new V3(1, 0, 0), out _);
            Assert.Equal("D continues", hit.Payload);
        }

        [Fact]
        public void NothingAlongTheDeviceGivesNull()
        {
            var idx = new EdgeIndex<string>(600 * Mm);
            idx.Add(0, 0, 1000 * Mm, 0, "C ends");
            var block = P(1400, 120);
            Assert.Null(EdgeSnap.Choose(idx.Within(block.X, block.Y, 600 * Mm), block, new V3(1, 0, 0), out _));
        }

        // ---- symbol back line -------------------------------------------------------------

        static double[] Arc(double r, double from, double to, int n = 12) =>
            Enumerable.Range(0, n + 1).SelectMany(k =>
            {
                double a = from + (to - from) * k / n;
                return new[] { r * Math.Cos(a), r * Math.Sin(a) };
            }).ToArray();

        [Fact]
        public void SocketHalfCircleBackLineFacesTheRoom()
        {
            // Half circle above a flat back on y = 0: the room is +Y.
            var back = SymbolBackLine.Find(new[] { Arc(100, 0, Math.PI), new double[] { -100, 0, 100, 0 } });
            Assert.NotNull(back);
            Assert.Equal(1, Math.Abs(back.Dir.X), 6);
            Assert.Equal(1, back.Inward.Y, 6);
        }

        [Fact]
        public void BackLineWorksWhateverWayTheSymbolWasDrawn()
        {
            // Same socket drawn facing -X (back on x = 0, half circle on the left).
            var back = SymbolBackLine.Find(new[] { Arc(100, Math.PI / 2, 1.5 * Math.PI), new double[] { 0, -100, 0, 100 } });
            Assert.NotNull(back);
            Assert.Equal(-1, back.Inward.X, 6);
        }

        [Fact]
        public void SymbolsWithoutASingleFlatBackGiveNull()
        {
            Assert.Null(SymbolBackLine.Find(new[] { Arc(100, 0, 2 * Math.PI, 24) }));                       // circle
            Assert.Null(SymbolBackLine.Find(new[] { Arc(100, 0, 2 * Math.PI, 24),
                                                    new double[] { -70, -70, 70, 70 }, new double[] { -70, 70, 70, -70 } }));   // light: circle + cross
            Assert.Null(SymbolBackLine.Find(new[] { new double[] { 0, 0, 300, 0, 300, 150, 0, 150, 0, 0 } }));   // plain rectangle: two equal long sides
            Assert.Null(SymbolBackLine.Find(new double[][] { }));
        }

        [Fact]
        public void SwitchBaseLineWithAShortLever()
        {
            // Base line 200 long, small circle and a lever above it.
            var back = SymbolBackLine.Find(new[]
            {
                new double[] { -100, 0, 100, 0 },
                Arc(40, 0, 2 * Math.PI, 16).Select((v, i) => i % 2 == 1 ? v + 60 : v).ToArray(),
                new double[] { 0, 100, 70, 160 },
            });
            Assert.NotNull(back);
            Assert.Equal(1, back.Inward.Y, 6);
        }
    }
}
