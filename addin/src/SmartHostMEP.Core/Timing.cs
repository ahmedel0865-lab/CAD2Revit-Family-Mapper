using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace SmartHostMEP.Core
{
    /// <summary>Phase names used in the timings table (kept in one place so the log and the
    /// result window use the same words).</summary>
    public static class Phases
    {
        public const string ReadDwg = "Read DWG blocks";
        public const string Setup = "Mapping window setup (families, saved mapping, symbols)";
        public const string LoadMapping = "Load mapping (resolve family types)";
        public const string Activate = "Activate family types + regenerate (once)";
        public const string HostIndex = "Host detection - build face/wall indexes";
        public const string HostQuery = "Host detection - per block";
        public const string HostRay = "Host detection - ray-cast fallback (part of per block)";
        public const string DupIndex = "Duplicate check - build grid";
        public const string DupQuery = "Duplicate check - per block";
        public const string Planes = "Reference/vertical planes";
        public const string Create = "Family creation";
        public const string Verify = "Position check + snap (vertical planes)";
        public const string CreateBatch = "Family creation - batched (level-based)";
        public const string Rotate = "Rotation";
        public const string Params = "Parameter setting";
        public const string Commit = "Transaction commit";
        public const string Rollback = "Transaction rollback (Preview)";
        public const string WriteLog = "Write log";
        public const string Total = "Total";
    }

    /// <summary>
    /// Accumulates wall-clock time per named phase (Stopwatch based). Phases are listed in
    /// the order they were first used. Use <c>using (timer.Time(Phases.X)) { ... }</c>.
    /// </summary>
    public class PhaseTimer
    {
        class Entry
        {
            public string Name;
            public long Ticks;
            public int Count;
        }

        readonly List<Entry> _entries = new List<Entry>();
        readonly Dictionary<string, Entry> _byName = new Dictionary<string, Entry>();

        public IDisposable Time(string phase) => new Scope(this, phase);

        public T Measure<T>(string phase, Func<T> f)
        {
            using (Time(phase)) return f();
        }

        public void Add(string phase, long stopwatchTicks, int count = 1)
        {
            if (!_byName.TryGetValue(phase, out var e))
            {
                e = new Entry { Name = phase };
                _byName[phase] = e;
                _entries.Add(e);
            }
            e.Ticks += stopwatchTicks;
            e.Count += count;
        }

        public void Add(string phase, TimeSpan time, int count = 1) =>
            Add(phase, (long)(time.TotalSeconds * Stopwatch.Frequency), count);

        public double Ms(string phase) =>
            _byName.TryGetValue(phase, out var e) ? e.Ticks * 1000.0 / Stopwatch.Frequency : 0;

        public int Count(string phase) => _byName.TryGetValue(phase, out var e) ? e.Count : 0;

        public IList<(string Phase, double Ms, int Count)> Entries =>
            _entries.Select(e => (e.Name, e.Ticks * 1000.0 / Stopwatch.Frequency, e.Count)).ToList();

        static string FormatMs(double ms) =>
            ms >= 10000 ? (ms / 1000).ToString("0.0", CultureInfo.InvariantCulture) + " s"
                        : ms.ToString("0", CultureInfo.InvariantCulture) + " ms";

        /// <summary>Timings table for the result window. The Total row (if recorded) is last
        /// and the share column is relative to it.</summary>
        public string Format()
        {
            var list = Entries.Where(e => e.Phase != Phases.Total).ToList();
            double total = Ms(Phases.Total);
            if (total <= 0) total = list.Sum(e => e.Ms);
            var rows = list.Select(e => new object[]
            {
                e.Phase, FormatMs(e.Ms), e.Count,
                total > 0 ? (e.Ms / total * 100).ToString("0", CultureInfo.InvariantCulture) + " %" : "",
            }).ToList();
            if (_byName.ContainsKey(Phases.Total)) rows.Add(new object[] { Phases.Total, FormatMs(total), "", "100 %" });
            return TextTable.Format(new[] { "Phase", "Time", "Calls", "Share" }, rows);
        }

        /// <summary>Rows for the CSV log (same columns as Report.LogHeader, Status = "timing").</summary>
        public List<IList<object>> LogRows()
        {
            var rows = new List<IList<object>>();
            foreach (var e in Entries)
            {
                var row = new object[Report.LogHeader.Length];
                for (int i = 0; i < row.Length; i++) row[i] = "";
                row[0] = "timing";
                row[1] = e.Phase;
                row[row.Length - 1] = e.Ms.ToString("0.0", CultureInfo.InvariantCulture) + " ms; " + e.Count + " call(s)";
                rows.Add(row);
            }
            return rows;
        }

        sealed class Scope : IDisposable
        {
            readonly PhaseTimer _timer;
            readonly string _phase;
            readonly long _start = Stopwatch.GetTimestamp();
            bool _done;

            public Scope(PhaseTimer timer, string phase)
            {
                _timer = timer;
                _phase = phase;
            }

            public void Dispose()
            {
                if (_done) return;
                _done = true;
                _timer.Add(_phase, Stopwatch.GetTimestamp() - _start);
            }
        }
    }
}
