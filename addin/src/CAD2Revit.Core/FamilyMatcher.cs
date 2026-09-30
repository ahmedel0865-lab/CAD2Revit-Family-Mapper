using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CAD2Revit.Core
{
    /// <summary>
    /// Suggests a Revit family type for a CAD block name, e.g. "SMOKE-DET" ->
    /// "Smoke Detector : Ceiling". Used to pre-select families in the mapping
    /// window; the user can always change the choice.
    /// </summary>
    public static class FamilyMatcher
    {
        /// <summary>Minimum score (0..1) for an automatic selection.</summary>
        public const double Threshold = 0.75;

        // Common CAD abbreviations in electrical / FA / ELV drawings. Each entry
        // lists alternatives; an alternative with several words matches only if
        // the candidate contains all of them.
        static readonly Dictionary<string, string[]> Synonyms = new Dictionary<string, string[]>
        {
            ["det"] = new[] { "detector" },
            ["smk"] = new[] { "smoke" },
            ["ht"] = new[] { "heat" },
            ["skt"] = new[] { "socket", "receptacle", "outlet" },
            ["sock"] = new[] { "socket", "receptacle", "outlet" },
            ["socket"] = new[] { "receptacle", "outlet" },
            ["rcpt"] = new[] { "receptacle" },
            ["recep"] = new[] { "receptacle" },
            ["double"] = new[] { "duplex", "twin" },
            ["dbl"] = new[] { "double", "duplex", "twin" },
            ["twin"] = new[] { "duplex", "double" },
            ["sw"] = new[] { "switch" },
            ["swt"] = new[] { "switch" },
            ["lt"] = new[] { "light", "lighting" },
            ["ltg"] = new[] { "light", "lighting" },
            ["lum"] = new[] { "luminaire", "light" },
            ["light"] = new[] { "luminaire", "lighting", "downlight" },
            ["em"] = new[] { "emergency" },
            ["emg"] = new[] { "emergency" },
            ["db"] = new[] { "panelboard", "distribution board", "switchboard" },
            ["dp"] = new[] { "data" },
            ["pnl"] = new[] { "panelboard", "panel" },
            ["mcp"] = new[] { "manual call point", "call point", "pull station" },
            ["fa"] = new[] { "fire alarm" },
            ["spk"] = new[] { "speaker" },
            ["cam"] = new[] { "camera" },
            ["cctv"] = new[] { "camera" },
            ["tel"] = new[] { "telephone", "phone" },
            ["wap"] = new[] { "wireless access point", "access point" },
        };

        public static List<string> Tokens(string text) =>
            Regex.Split((text ?? "").ToLowerInvariant(), "[^a-z0-9]+").Where(t => t.Length > 0).ToList();

        static bool TokenMatches(string t, List<string> cand)
        {
            bool Eq(string a, string b) => a == b || (a.Length >= 3 && b.StartsWith(a)) || (b.Length >= 3 && a.StartsWith(b));
            if (cand.Any(c => Eq(t, c))) return true;
            // "1g" / "2g" = 1 gang / 2 gang (switches, sockets).
            var gang = Regex.Match(t, "^([0-9]+)g$");
            if (gang.Success && cand.Contains(gang.Groups[1].Value) && cand.Any(c => c.StartsWith("gang"))) return true;
            if (Synonyms.TryGetValue(t, out var alts))
                foreach (var alt in alts)
                {
                    var words = alt.Split(' ');
                    // One direction only: the candidate word must start with the synonym
                    // ("panelboard" must not match a plain "panel").
                    if (words.All(w => cand.Any(c => c.StartsWith(w)))) return true;
                }
            return false;
        }

        /// <summary>Similarity 0..1 between a block name and a "Family : Type" label.</summary>
        public static double Score(string block, string candidate)
        {
            var bt = Tokens(block);
            var ct = Tokens(candidate);
            if (bt.Count == 0 || ct.Count == 0) return 0;
            int matched = bt.Count(t => TokenMatches(t, ct));
            if (matched == 0) return 0;
            double tokenScore = (double)matched / bt.Count;
            // How much of the candidate is explained by the block (tie-breaker).
            int covered = ct.Count(c => bt.Any(t => TokenMatches(t, new List<string> { c })));
            double coverage = (double)covered / ct.Count;
            return Math.Min(1.0, tokenScore * 0.9 + coverage * 0.1);
        }

        /// <summary>Index of the best candidate, or -1 if none reaches <see cref="Threshold"/>.
        /// Ties go to the shorter label (usually the more generic type).</summary>
        public static int BestMatch(string block, IList<string> candidates, out double bestScore)
        {
            bestScore = 0;
            int best = -1;
            for (int i = 0; i < candidates.Count; i++)
            {
                var s = Score(block, candidates[i]);
                if (s > bestScore + 1e-9 ||
                    (best >= 0 && Math.Abs(s - bestScore) < 1e-9 && candidates[i].Length < candidates[best].Length))
                {
                    bestScore = s;
                    best = i;
                }
            }
            return bestScore >= Threshold ? best : -1;
        }
    }
}
