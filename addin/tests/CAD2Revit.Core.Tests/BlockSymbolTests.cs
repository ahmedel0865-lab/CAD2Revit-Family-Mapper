using System.Linq;
using CAD2Revit.Core;
using Xunit;

namespace CAD2Revit.Core.Tests
{
    public class BlockSymbolTests
    {
        static BlockSymbol Square(double size)
        {
            var s = new BlockSymbol();
            s.AddPath(new[] { 0, 0, size, 0, size, size, 0, size, 0, 0 });
            return s;
        }

        [Fact]
        public void BoundsAndSize()
        {
            var s = Square(600 / 304.8);
            Assert.Equal(new[] { 0.0, 0, 600 / 304.8, 600 / 304.8 }, s.Bounds());
            Assert.Equal("600 x 600 mm", s.SizeText());
            Assert.Equal(5, s.PointCount);
        }

        [Fact]
        public void FitToCentresAndFlipsY()
        {
            var s = new BlockSymbol();
            s.AddPath(new[] { 0.0, 0, 2, 0 });       // wide line at the bottom
            s.AddPath(new[] { 0.0, 1, 2, 1 });       // top line
            var fit = s.FitTo(100, 100, 10);
            // Scale = 80 / 2 = 40 -> width 80, height 40, centred vertically (30..70)
            Assert.Equal(new[] { 10.0, 70, 90, 70 }, fit[0]);   // bottom line drawn lower on screen
            Assert.Equal(new[] { 10.0, 30, 90, 30 }, fit[1]);
        }

        [Fact]
        public void DegenerateAndEmpty()
        {
            var empty = new BlockSymbol();
            Assert.True(empty.IsEmpty);
            Assert.Null(empty.Bounds());
            Assert.Empty(empty.FitTo(100, 100, 5));
            empty.AddPath(new[] { 1.0, 1 });          // single point: ignored
            Assert.True(empty.IsEmpty);

            var vertical = new BlockSymbol();
            vertical.AddPath(new[] { 0.0, 0, 0, 1 });
            var fit = vertical.FitTo(100, 100, 10);
            Assert.Equal(50, fit[0][0], 6);           // zero width -> centred horizontally
        }

        [Fact]
        public void PointBudgetTruncates()
        {
            var s = new BlockSymbol();
            var big = Enumerable.Range(0, 2 * (BlockSymbol.MaxPoints - 1)).Select(i => (double)i).ToArray();
            Assert.True(s.AddPath(big));
            Assert.False(s.AddPath(new[] { 0.0, 0, 1, 1, 2, 2 }));
            Assert.True(s.Truncated);
            Assert.Single(s.Paths);
        }
    }
}
