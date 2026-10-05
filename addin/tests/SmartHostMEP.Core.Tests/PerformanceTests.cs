using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SmartHostMEP.Core;
using Xunit;

namespace SmartHostMEP.Core.Tests
{
    public class PerformanceTests
    {
        static double[] Rect(double x0, double y0, double x1, double y1) => new[] { x0, y0, x1, y0, x1, y1, x0, y1 };

        [Fact]
        public void PointGridFindsNeighboursInBandOnly()
        {
            var g = new PointGrid(0.5);
            g.Add(1, 10, 10, 0);
            Assert.True(g.Contains(1, 10.3, 10.2, -1, 5));      // within 0.5 in plan
            Assert.False(g.Contains(1, 10.6, 10, -1, 5));       // too far
            Assert.False(g.Contains(2, 10, 10, -1, 5));         // other family
            Assert.False(g.Contains(1, 10, 10, 1, 5));          // other level band
            Assert.True(g.Contains(1, 9.51, 10, -1, 5));        // across a cell border
        }

        [Fact]
        public void FaceIndexFindsSoffitAboveAndSkipsOpenings()
        {
            var idx = new FaceIndex<string>();
            // Slab of the level above: underside at z = 12 (normal down), 20 x 20 with a 2 x 2 opening at (10..12).
            idx.Add(new List<double[]> { Rect(0, 0, 20, 20), Rect(10, 10, 12, 12) }, 0, 0, -1, 0, 0, 12, "L2 soffit");
            idx.Add(new List<double[]> { Rect(0, 0, 20, 20), Rect(10, 10, 12, 12) }, 0, 0, 1, 0, 0, 13, "L2 top");
            // Slab two floors up.
            idx.Add(new List<double[]> { Rect(0, 0, 20, 20) }, 0, 0, -1, 0, 0, 24, "L3 soffit");

            var f = idx.Nearest(5, 5, 0, true, 14, x => x.Nz < -0.5, out var z);
            Assert.Equal("L2 soffit", f.Payload);
            Assert.Equal(12, z, 6);
            // Inside the opening: nothing within the level-to-level window (never L3).
            Assert.Null(idx.Nearest(11, 11, 0, true, 14, x => x.Nz < -0.5, out _));
            // Down from above the slab top: the top face.
            Assert.Equal("L2 top", idx.Nearest(5, 5, 14, false, 3, x => x.Nz > 0.5, out _).Payload);
        }

        [Fact]
        public void FaceIndexUsesPlaneForSlopedFaces()
        {
            var idx = new FaceIndex<int>();
            var n = Math.Sqrt(0.5);
            // Plane through (0,0,10) rising 1:1 in X (normal down-and-back).
            idx.Add(new List<double[]> { Rect(0, 0, 10, 10) }, n, 0, -n, 0, 0, 10, 1);
            idx.Nearest(4, 5, 0, true, 100, null, out var z);
            Assert.Equal(14, z, 6);
        }

        [Fact]
        public void UnindexedAreasAreReported()
        {
            var idx = new FaceIndex<int>();
            idx.AddUnindexed(0, 0, 5, 5);
            Assert.True(idx.IsUnindexedAt(1, 1));
            Assert.False(idx.IsUnindexedAt(6, 1));
        }

        [Fact]
        public void SegmentIndexReturnsNearestWallsFirst()
        {
            var idx = new SegmentIndex<string>();
            idx.Add(0, 0, 10, 0, 0, 10, "south");
            idx.Add(0, 3, 10, 3, 0, 10, "north");
            idx.Add(0, 1, 10, 1, 20, 30, "upper floor");
            var near = idx.Near(5, 1, 1, 2.5);
            Assert.Equal(new[] { "south", "north" }, near);
            Assert.Empty(idx.Near(5, 10, 1, 2));
            Assert.Equal(2.0, SegmentIndex<int>.Distance(12, 0, 0, 0, 10, 0), 6);
        }

        [Fact]
        public void TimerAccumulatesAndFormats()
        {
            var t = new PhaseTimer();
            t.Add(Phases.HostQuery, TimeSpan.FromMilliseconds(120), 1000);
            t.Add(Phases.Create, TimeSpan.FromMilliseconds(80));
            t.Add(Phases.Create, TimeSpan.FromMilliseconds(20));
            t.Add(Phases.Total, TimeSpan.FromMilliseconds(250));
            Assert.Equal(100, t.Ms(Phases.Create), 3);
            Assert.Equal(2, t.Count(Phases.Create));
            var text = t.Format();
            Assert.Contains(Phases.HostQuery, text);
            Assert.Contains("48 %", text);      // 120 of 250 ms
            var rows = t.LogRows();
            Assert.Equal(3, rows.Count);
            Assert.All(rows, r => Assert.Equal(Report.LogHeader.Length, r.Count));
            Assert.Equal("timing", rows[0][0]);
            using (t.Time("scoped")) { }
            Assert.Equal(1, t.Count("scoped"));
        }

        /// <summary>1,000 block points against a floor plate of 400 slab bays with openings:
        /// the index answers in milliseconds (this replaces 1,000 ray casts in Revit).</summary>
        [Fact]
        public void ThousandLookupsAreFast()
        {
            var idx = new FaceIndex<int>();
            int id = 0;
            for (int i = 0; i < 20; i++)
                for (int j = 0; j < 20; j++)
                    idx.Add(new List<double[]> { Rect(i * 30, j * 30, i * 30 + 30, j * 30 + 30), Rect(i * 30 + 1, j * 30 + 1, i * 30 + 3, j * 30 + 3) },
                            0, 0, -1, 0, 0, 12, id++);
            var rnd = new Random(1);
            var pts = Enumerable.Range(0, 1000).Select(_ => (rnd.NextDouble() * 600, rnd.NextDouble() * 600)).ToList();
            var sw = Stopwatch.StartNew();
            int hits = pts.Count(p => idx.Nearest(p.Item1, p.Item2, 0, true, 14, f => f.Nz < -0.5, out _) != null);
            sw.Stop();
            Assert.True(hits > 950);
            Assert.True(sw.ElapsedMilliseconds < 500, $"took {sw.ElapsedMilliseconds} ms");
        }
    }
}
