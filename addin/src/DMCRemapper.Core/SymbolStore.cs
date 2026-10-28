using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DMCRemapper.Core
{
    /// <summary>
    /// Family symbol signatures kept between runs in %TEMP%\DMCRemapper\SymbolCache (never next to
    /// the project), so a family is only opened again when it is new or changed. The key holds the
    /// family name and a change stamp (Revit's version id of the family). Families without a symbol
    /// are remembered too (empty signature). Deleting the folder is safe: it is rebuilt on the next
    /// Auto-match.
    /// </summary>
    public class SignatureCache
    {
        public const string FileName = "families.txt";

        public static string DefaultFolder =>
            Path.Combine(Path.GetTempPath(), AppStorage.FolderName, "SymbolCache");

        readonly string _path;
        readonly Dictionary<string, string> _lines = new Dictionary<string, string>(StringComparer.Ordinal);
        bool _dirty;

        public SignatureCache(string folder = null)
        {
            _path = Path.Combine(folder ?? DefaultFolder, FileName);
            try
            {
                if (!File.Exists(_path)) return;
                foreach (var line in File.ReadAllLines(_path, Encoding.UTF8))
                {
                    var f = line.Split(new[] { '\t' }, 3);
                    if (f.Length == 3 && f[0] == "v" + SymbolSignature.Version) _lines[f[1]] = f[2];
                }
            }
            catch (Exception) { /* unreadable cache = empty cache */ }
        }

        public string FilePath => _path;
        public int Count => _lines.Count;

        public static string Key(string family, string stamp) =>
            ((family ?? "").Trim() + "|" + (stamp ?? "")).Replace('\t', ' ');

        /// <summary>True when the family is cached; <paramref name="signature"/> is empty when it has no symbol.</summary>
        public bool TryGet(string key, out SymbolSignature signature)
        {
            signature = null;
            if (!_lines.TryGetValue(key, out var text)) return false;
            signature = text.Length == 0 ? new SymbolSignature() : SymbolSignature.FromText(text);
            if (signature != null) return true;
            _lines.Remove(key);   // damaged line: compute again
            return false;
        }

        public void Put(string key, SymbolSignature signature)
        {
            _lines[key] = signature == null || signature.IsEmpty ? "" : signature.ToText();
            _dirty = true;
        }

        /// <summary>Writes the file (creating the folder). Returns false when it cannot be written.</summary>
        public bool Save()
        {
            if (!_dirty) return true;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                var tmp = _path + ".tmp";
                File.WriteAllLines(tmp, _lines.Select(kv => "v" + SymbolSignature.Version + "\t" + kv.Key + "\t" + kv.Value), new UTF8Encoding(false));
                if (File.Exists(_path)) File.Delete(_path);
                File.Move(tmp, _path);
                _dirty = false;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>One remembered manual match.</summary>
    public class LearnedMatch
    {
        public string Block = "";
        public string Hash = "";      // SymbolSignature.Hash() of the block ("" when it had no symbol)
        public string Family = "";
        public string TypeName = "";
        public string Label => Family + " : " + TypeName;
    }

    /// <summary>
    /// Manual matches remembered for Auto-match (%AppData%\DMCRemapper\learned-matches.txt): block
    /// name + symbol signature -> family type. Every family the user picks or fixes is saved; Auto-match
    /// looks here first. Only manual choices are written, so an automatic match never replaces one.
    /// </summary>
    public class LearnedMatches
    {
        public static string DefaultPath => Path.Combine(AppStorage.Root, "learned-matches.txt");

        readonly string _path;
        readonly List<LearnedMatch> _items = new List<LearnedMatch>();
        bool _dirty;

        public LearnedMatches(string path = null)
        {
            _path = path ?? DefaultPath;
            try
            {
                if (!File.Exists(_path)) return;
                foreach (var line in File.ReadAllLines(_path, Encoding.UTF8))
                {
                    if (line.StartsWith("#")) continue;
                    var f = line.Split('\t');
                    if (f.Length >= 4 && f[0].Length > 0 && f[2].Length > 0)
                        _items.Add(new LearnedMatch { Block = f[0], Hash = f[1], Family = f[2], TypeName = f[3] });
                }
            }
            catch (Exception) { }
        }

        public int Count => _items.Count;
        public IReadOnlyList<LearnedMatch> Items => _items;

        static string Clean(string s) => (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();

        /// <summary>Remembers a manual choice (replaces the earlier one for the same block and symbol).</summary>
        public void Add(string block, string hash, string family, string typeName)
        {
            block = Clean(block); hash = Clean(hash); family = Clean(family); typeName = Clean(typeName);
            if (block.Length == 0 || family.Length == 0) return;
            var old = _items.FindIndex(m => m.Block.Equals(block, StringComparison.OrdinalIgnoreCase) && m.Hash == hash);
            if (old >= 0)
            {
                var o = _items[old];
                if (o.Family == family && o.TypeName == typeName) return;
                _items.RemoveAt(old);
            }
            _items.Add(new LearnedMatch { Block = block, Hash = hash, Family = family, TypeName = typeName });
            _dirty = true;
        }

        /// <summary>
        /// The remembered matches for a block, best first: the same name and symbol, then the same
        /// symbol under another name (latest first), then the same name when the block has no symbol.
        /// </summary>
        public List<LearnedMatch> Find(string block, string hash)
        {
            block = Clean(block);
            hash = hash ?? "";
            var result = new List<LearnedMatch>();
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                var m = _items[i];
                if (m.Hash == hash && m.Block.Equals(block, StringComparison.OrdinalIgnoreCase)) result.Add(m);
            }
            if (hash.Length > 0)
                for (int i = _items.Count - 1; i >= 0; i--)
                    if (_items[i].Hash == hash && !result.Contains(_items[i])) result.Add(_items[i]);
            return result;
        }

        public bool Save()
        {
            if (!_dirty) return true;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                var lines = new List<string> { "# DMC Remapper - manual Auto-match choices: block, symbol id, family, type" };
                lines.AddRange(_items.Select(m => string.Join("\t", m.Block, m.Hash, m.Family, m.TypeName)));
                File.WriteAllLines(_path, lines, new UTF8Encoding(false));
                _dirty = false;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
