using System;
using System.Collections.Generic;
using System.Linq;
using DMCRemapper.Core;
using Xunit;

namespace DMCRemapper.Core.Tests
{
    /// <summary>Which Revit wall/column face a vertical plane follows.</summary>
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
        public void TheClosestRevitFaceWinsWhateverTheBlockRotation()
        {
            // 250 mm from wall A, 150 mm from wall B: B is used (no preference from the block or symbol direction).
            var block = P(150, 250);
            var hit = EdgeSnap.Choose(Corner().Within(block.X, block.Y, 600 * Mm), block);
            Assert.Equal("B along Y", hit.Payload);
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
            var hit = EdgeSnap.Choose(idx.Within(block.X, block.Y, 600 * Mm), block);
            Assert.Equal("D continues", hit.Payload);
        }

        [Fact]
        public void NothingAlongTheDeviceGivesNull()
        {
            var idx = new EdgeIndex<string>(600 * Mm);
            idx.Add(0, 0, 1000 * Mm, 0, "C ends");
            var block = P(1400, 120);
            Assert.Null(EdgeSnap.Choose(idx.Within(block.X, block.Y, 600 * Mm), block));
        }
    }
}
