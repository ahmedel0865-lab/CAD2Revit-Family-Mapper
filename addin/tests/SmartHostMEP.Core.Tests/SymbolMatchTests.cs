using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SmartHostMEP.Core;
using Xunit;

namespace SmartHostMEP.Core.Tests
{
    /// <summary>Auto-match by symbol geometry: signatures, scores, match rules, cache, learned matches.</summary>
    public class SymbolMatchTests
    {
        // ---- test symbols ----------------------------------------------------------------------

        static double[] Circle(double cx, double cy, double r, int n = 48)
        {
            var p = new double[(n + 1) * 2];
            for (int i = 0; i <= n; i++)
            {
                double a = 2 * Math.PI * i / n;
                p[2 * i] = cx + r * Math.Cos(a);
                p[2 * i + 1] = cy + r * Math.Sin(a);
            }
            return p;
        }

        static double[] Rect(double x0, double y0, double x1, double y1) => new[] { x0, y0, x1, y0, x1, y1, x0, y1, x0, y0 };
        static double[] Seg(double x0, double y0, double x1, double y1) => new[] { x0, y0, x1, y1 };

        /// <summary>Light fitting: circle with a cross through it.</summary>
        static BlockSymbol Light(double size = 600, double rotDeg = 0, double dx = 0, double dy = 0, bool mirror = false)
        {
            var paths = new List<double[]> { Circle(0, 0, size / 2), Seg(-size / 2, 0, size / 2, 0), Seg(0, -size / 2, 0, size / 2) };
            var s = Build(paths, rotDeg, dx, dy, mirror);
            s.Circles = 1; s.Lines = 2;
            return s;
        }

        /// <summary>Socket: a half circle on a base line with two short legs (no symmetry).</summary>
        static BlockSymbol Socket(double size = 300, double rotDeg = 0, double dx = 0, double dy = 0, bool mirror = false)
        {
            int n = 24;
            var arc = new double[(n + 1) * 2];
            for (int i = 0; i <= n; i++)
            {
                double a = Math.PI * i / n;
                arc[2 * i] = size / 2 * Math.Cos(a);
                arc[2 * i + 1] = size / 2 * Math.Sin(a);
            }
            var paths = new List<double[]> { arc, Seg(-size / 2, 0, size / 2, 0), Seg(-size / 4, 0, -size / 4, -size / 3), Seg(size / 6, 0, size / 6, -size / 5) };
            var s = Build(paths, rotDeg, dx, dy, mirror);
            s.Arcs = 1; s.Lines = 3;
            return s;
        }

        /// <summary>Panel board: long rectangle with a diagonal.</summary>
        static BlockSymbol Panel(double w = 900, double h = 300)
        {
            var s = Build(new List<double[]> { Rect(0, 0, w, h), Seg(0, 0, w, h) }, 0, 0, 0, false);
            s.Lines = 5;
            return s;
        }

        static BlockSymbol Build(List<double[]> paths, double rotDeg, double dx, double dy, bool mirror)
        {
            double a = rotDeg * Math.PI / 180, c = Math.Cos(a), sn = Math.Sin(a);
            var s = new BlockSymbol();
            foreach (var p in paths)
            {
                var q = new double[p.Length];
                for (int i = 0; i + 1 < p.Length; i += 2)
                {
                    double x = mirror ? -p[i] : p[i], y = p[i + 1];
                    q[i] = x * c - y * sn + dx;
                    q[i + 1] = x * sn + y * c + dy;
                }
                s.AddPath(q);
            }
            return s;
        }

        static SymbolSignature Sig(BlockSymbol s, string view = "") => SymbolSignature.From(s, view);

        // ---- signature -------------------------------------------------------------------------

        [Fact]
        public void TheSignatureIsCentredAndScaledToUnitSize()
        {
            var sig = Sig(Panel(900, 300));
            var xs = Enumerable.Range(0, sig.PointCount).Select(i => sig.Points[2 * i]).ToList();
            var ys = Enumerable.Range(0, sig.PointCount).Select(i => sig.Points[2 * i + 1]).ToList();
            Assert.Equal(SymbolSignature.SampleCount, sig.PointCount);
            Assert.InRange(xs.Max() - xs.Min(), 0.97, 1.0001);
            Assert.InRange(ys.Max() - ys.Min(), 0.30, 0.3334);
            Assert.InRange(Math.Abs(xs.Max() + xs.Min()), 0, 0.02);
            Assert.Equal(1 / 3.0, sig.Aspect, 2);
        }

        [Fact]
        public void RotationIsRemoved()
        {
            // A long panel drawn at 30 deg comes out along X with the same aspect ratio.
            var straight = Sig(Panel(900, 300));
            var s = Build(new List<double[]> { Rect(0, 0, 900, 300), Seg(0, 0, 900, 300) }, 30, 500, -200, false);
            s.Lines = 5;
            var turned = Sig(s);
            Assert.Equal(straight.Aspect, turned.Aspect, 1);
            Assert.True(new SymbolMatcher().Geometry(straight, turned) >= 95);
        }

        [Fact]
        public void EmptySymbolsHaveNoScore()
        {
            var empty = SymbolSignature.From(new BlockSymbol());
            Assert.True(empty.IsEmpty);
            Assert.Equal("", empty.Hash());
            Assert.Equal(0, new SymbolMatcher().Geometry(empty, Sig(Light())));
        }

        // ---- scores ----------------------------------------------------------------------------

        [Fact]
        public void TheSameSymbolScoresNearly100AtAnySizePositionAndRightAngle()
        {
            var m = new SymbolMatcher();
            var block = Sig(Socket(300));
            Assert.True(m.Geometry(block, Sig(Socket(300))) > 99);
            Assert.True(m.Geometry(block, Sig(Socket(1, 90, 50, 50))) > 97, "rotated 90 + scaled + moved");
            Assert.True(m.Geometry(block, Sig(Socket(300, 180))) > 97, "rotated 180");
            Assert.True(m.Geometry(block, Sig(Socket(300, 0, 0, 0, mirror: true))) > 97, "mirrored");
        }

        [Fact]
        public void DifferentSymbolsScoreLow()
        {
            var m = new SymbolMatcher();
            var light = Sig(Light());
            Assert.True(m.Geometry(light, Sig(Socket())) < 50);
            Assert.True(m.Geometry(light, Sig(Panel())) < 50);
            // Circle alone vs the circle with a cross: related, but not an automatic match.
            var circle = new BlockSymbol();
            circle.AddPath(Circle(0, 0, 300), SymbolKind.Circle);
            Assert.True(m.Geometry(light, Sig(circle)) < 80);
        }

        [Fact]
        public void PreFilterDropsClearlyDifferentSymbols()
        {
            var light = Sig(Light());
            Assert.False(SymbolMatcher.PreFilter(light, Sig(Panel(900, 100))));             // aspect 1 vs 0.11
            Assert.True(SymbolMatcher.PreFilter(light, Sig(Light(300))));
            // Category: a Mechanical block is never compared with a lighting family.
            Assert.False(SymbolMatcher.PreFilter(light, Sig(Light()), BlockCategories.Mechanical, "Lighting Fixtures"));
            Assert.True(SymbolMatcher.PreFilter(light, Sig(Light()), BlockCategories.Other, "Lighting Fixtures"));
            Assert.True(SymbolMatcher.PreFilter(light, Sig(Light()), BlockCategories.Electrical, "Lighting Fixtures"));
            // Element counts 2 vs 40.
            var many = Light();
            many.Lines = 40;
            Assert.False(SymbolMatcher.PreFilter(Sig(Light()), Sig(many)));
        }

        static FamilySymbolEntry Fam(string name, BlockSymbol s, string category = "Lighting Fixtures", string view = "Ref. Level", params string[] types) =>
            new FamilySymbolEntry
            {
                Family = name, Category = category, Signature = Sig(s, view),
                Types = (types.Length == 0 ? new[] { "Standard" } : types).Select(t => name + " : " + t).ToList(),
            };

        [Fact]
        public void RankKeepsTheTop3FamiliesAboveTheMinimum()
        {
            var m = new SymbolMatcher();
            var fams = new[]
            {
                Fam("Ceiling Light", Light(450), types: new[] { "600x600", "Round" }),
                Fam("Duplex Receptacle", Socket(), "Electrical Fixtures", "Front"),
                Fam("Panelboard", Panel(), "Electrical Equipment"),
            };
            var ranked = m.Rank(Sig(Socket(250, 90)), "SKT-DBL", BlockCategories.Other, fams);
            Assert.Single(ranked);   // only the socket reaches 50
            Assert.Equal("Duplex Receptacle", ranked[0].Family);
            Assert.Equal("Front", ranked[0].View);
            Assert.True(ranked[0].Score >= 95);
        }

        [Fact]
        public void TheTypeWhoseNameIsClosestIsPicked()
        {
            var m = new SymbolMatcher();
            var fams = new[] { Fam("Downlight", Light(), types: new[] { "Square", "Round Emergency" }) };
            var c = m.Rank(Sig(Light()), "LT-EM-ROUND", null, fams).Single();
            Assert.Equal("Downlight : Round Emergency", c.Label);
            // Name adds at most SymbolScoreName points.
            Assert.InRange(c.Score - c.Geometry, 0, 2.0001);
        }

        [Fact]
        public void MatchRules()
        {
            var m = new SymbolMatcher(new SymbolScoreWeights());
            SymbolCandidate C(double s, string f) => new SymbolCandidate { Family = f, Label = f + " : A", Score = s };
            // >= 80: chosen; second within 10 %: ambiguous.
            var d = m.Decide(new List<SymbolCandidate> { C(90, "A"), C(82, "B") });
            Assert.Equal("A", d.Chosen.Family);
            Assert.True(d.Ambiguous);
            d = m.Decide(new List<SymbolCandidate> { C(90, "A"), C(80, "B") });
            Assert.False(d.Ambiguous);
            // 50..79: offered in the dropdown but not selected.
            d = m.Decide(new List<SymbolCandidate> { C(70, "A") });
            Assert.Null(d.Chosen);
            Assert.Equal("A", d.Best.Family);
            Assert.Equal("Ambiguous symbol match", SymbolMatcher.AmbiguousReason);
        }

        [Fact]
        public void TwoNearlyEqualFamiliesAreAmbiguous()
        {
            var m = new SymbolMatcher();
            var fams = new[] { Fam("Light A", Light(600)), Fam("Light B", Light(500)) };
            var ranked = m.Rank(Sig(Light()), "X", null, fams);
            Assert.Equal(2, ranked.Count);
            var d = m.Decide(ranked);
            Assert.NotNull(d.Chosen);
            Assert.True(d.Ambiguous);
        }

        [Fact]
        public void CandidateText()
        {
            var c = new SymbolCandidate { Label = "Downlight : Round", Score = 87.4 };
            Assert.Equal("87  Downlight : Round", c.Text);
        }

        [Fact]
        public void ManyFamiliesAreScoredQuickly()
        {
            var m = new SymbolMatcher();
            var fams = Enumerable.Range(0, 300).Select(i => Fam("F" + i, i % 3 == 0 ? Light(300 + i) : i % 3 == 1 ? Socket(200 + i) : Panel(900, 300 + i % 50))).ToList();
            var blocks = Enumerable.Range(0, 100).Select(i => Sig(i % 2 == 0 ? Light(500, i) : Socket(300, i * 7))).ToList();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var b in blocks) m.Rank(b, "B", null, fams);
            Assert.True(sw.ElapsedMilliseconds < 10000, $"took {sw.ElapsedMilliseconds} ms");
        }

        // ---- hash, text form, cache, learned ---------------------------------------------------

        [Fact]
        public void TheHashIgnoresSizePositionRotationAndMirror()
        {
            var h = Sig(Socket(300)).Hash();
            Assert.StartsWith("L3A1C0-", h);
            Assert.Equal(h, Sig(Socket(900, 0, 1000, 2000)).Hash());
            Assert.Equal(h, Sig(Socket(300, 90)).Hash());
            Assert.Equal(h, Sig(Socket(300, 0, 0, 0, mirror: true)).Hash());
            Assert.NotEqual(h, Sig(Light()).Hash());
        }

        [Fact]
        public void TextFormRoundTrips()
        {
            var a = Sig(Socket(), "Front");
            var b = SymbolSignature.FromText(a.ToText());
            Assert.Equal("Front", b.View);
            Assert.Equal(a.Lines, b.Lines);
            Assert.Equal(a.Arcs, b.Arcs);
            Assert.Equal(a.Aspect, b.Aspect, 4);
            Assert.Equal(a.Raster, b.Raster);
            Assert.True(new SymbolMatcher().Geometry(a, b) > 99);
            Assert.Null(SymbolSignature.FromText("broken"));
        }

        static string TempDir() => Path.Combine(Path.GetTempPath(), "sh_sym_" + Guid.NewGuid().ToString("N"));

        [Fact]
        public void TheCacheStoresSignaturesAndFamiliesWithoutSymbol()
        {
            var dir = TempDir();
            try
            {
                var cache = new SignatureCache(dir);
                Assert.False(cache.TryGet(SignatureCache.Key("Downlight", "g1"), out _));
                cache.Put(SignatureCache.Key("Downlight", "g1"), Sig(Light(), "Ref. Level"));
                cache.Put(SignatureCache.Key("Box", "g2"), new SymbolSignature());
                Assert.True(cache.Save());

                var again = new SignatureCache(dir);
                Assert.Equal(2, again.Count);
                Assert.True(again.TryGet(SignatureCache.Key("Downlight", "g1"), out var s));
                Assert.Equal("Ref. Level", s.View);
                Assert.True(again.TryGet(SignatureCache.Key("Box", "g2"), out var none));
                Assert.True(none.IsEmpty);
                // A changed family (new stamp) is not found: it is computed again.
                Assert.False(again.TryGet(SignatureCache.Key("Downlight", "g9"), out _));

                // Folder deleted: empty cache, rebuilt on save.
                Directory.Delete(dir, true);
                var fresh = new SignatureCache(dir);
                Assert.Equal(0, fresh.Count);
                fresh.Put(SignatureCache.Key("Downlight", "g1"), Sig(Light()));
                Assert.True(fresh.Save());
                Assert.True(File.Exists(fresh.FilePath));
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Fact]
        public void TheDefaultCacheIsInTemp()
        {
            Assert.StartsWith(Path.GetTempPath(), SignatureCache.DefaultFolder);
            Assert.EndsWith(Path.Combine("SmartHostMEP", "SymbolCache"), SignatureCache.DefaultFolder);
        }

        [Fact]
        public void LearnedMatchesAreFoundByNameAndSymbol()
        {
            var dir = TempDir();
            var path = Path.Combine(dir, "learned.txt");
            try
            {
                var l = new LearnedMatches(path);
                l.Add("SKT", "h1", "Duplex Receptacle", "Standard");
                l.Add("LT-01", "h2", "Downlight", "Round");
                Assert.True(l.Save());

                var again = new LearnedMatches(path);
                Assert.Equal(2, again.Count);
                Assert.Equal("Duplex Receptacle : Standard", again.Find("skt", "h1")[0].Label);
                // Same symbol under another block name.
                Assert.Equal("Downlight", again.Find("LIGHT-NEW", "h2")[0].Family);
                // Same name but another symbol: not reused.
                Assert.Empty(again.Find("SKT", "other"));
                // A later manual fix replaces the earlier choice for that block.
                again.Add("SKT", "h1", "Single Receptacle", "Standard");
                Assert.Equal("Single Receptacle", again.Find("SKT", "h1")[0].Family);
                Assert.Equal(2, again.Count);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Fact]
        public void SymbolWeightsAreReadFromSettings()
        {
            var path = Path.Combine(Path.GetTempPath(), "sh_sw_" + Guid.NewGuid().ToString("N") + ".ini");
            try
            {
                File.WriteAllText(path, "SymbolScoreShape = 70\nSymbolScoreCounts = 10\nSymbolScoreAspect = 10\nSymbolScoreRaster = 0\n" +
                                        "SymbolScoreName = 1\nSymbolAutoMatchScore = 85\nSymbolMinimumScore = 40\nSymbolAmbiguousPercent = 5\n");
                var st = Settings.Load(path);
                Assert.Equal(70, st.SymbolScore.Shape);
                Assert.Equal(10, st.SymbolScore.Counts);
                Assert.Equal(0, st.SymbolScore.Raster);
                Assert.Equal(1, st.SymbolScore.Name);
                Assert.Equal(85, st.SymbolScore.AutoMatch);
                Assert.Equal(40, st.SymbolScore.Minimum);
                Assert.Equal(5, st.SymbolScore.AmbiguousPercent);
                Assert.Contains("SymbolAutoMatchScore = 85", st.ToIni());
            }
            finally { try { File.Delete(path); } catch { } }
        }

        [Fact]
        public void BlockSymbolCountsElements()
        {
            var s = new BlockSymbol();
            s.AddPath(Seg(0, 0, 1, 0), SymbolKind.Line);
            s.AddPath(Rect(0, 0, 1, 1), SymbolKind.Polyline);
            s.AddPath(Circle(0, 0, 1), SymbolKind.Circle);
            s.AddPath(new[] { 0.0, 0, 1, 1, 2, 0 }, SymbolKind.Arc);
            Assert.Equal(5, s.Lines);
            Assert.Equal(1, s.Arcs);
            Assert.Equal(1, s.Circles);
        }
    }
}
