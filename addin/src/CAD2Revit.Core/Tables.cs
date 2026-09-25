using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace CAD2Revit.Core
{
    /// <summary>
    /// Reads and writes simple tables as CSV or XLSX. The XLSX support is a small
    /// reader/writer on top of ZipArchive + LINQ to XML, so Excel does not need to
    /// be installed on the Revit machine.
    /// </summary>
    public static class Tables
    {
        static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        static readonly XNamespace Pkg = "http://schemas.openxmlformats.org/package/2006/relationships";
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // ---------------------------------------------------------------- common

        /// <summary>First row = header. Returns one dictionary per data row keyed by the
        /// trimmed header text. Blank rows are dropped; short rows are padded.</summary>
        public static List<Dictionary<string, string>> RowsToDicts(IEnumerable<IList<string>> rows)
        {
            var list = rows.Where(r => r.Any(v => !string.IsNullOrWhiteSpace(v))).ToList();
            var result = new List<Dictionary<string, string>>();
            if (list.Count == 0) return result;
            var header = list[0].Select(h => (h ?? "").Trim()).ToList();
            foreach (var r in list.Skip(1))
            {
                var d = new Dictionary<string, string>();
                for (int k = 0; k < header.Count; k++)
                {
                    if (header[k].Length == 0 || d.ContainsKey(header[k])) continue;
                    d[header[k]] = k < r.Count ? (r[k] ?? "").Trim() : "";
                }
                result.Add(d);
            }
            return result;
        }

        /// <summary>Reads a .csv or .xlsx file into a list of row dictionaries.</summary>
        public static List<Dictionary<string, string>> ReadTable(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".xlsx" || ext == ".xlsm"
                ? RowsToDicts(ReadXlsxRows(path))
                : RowsToDicts(ReadCsvRows(path));
        }

        /// <summary>Writes .xlsx if the path ends in .xlsx, otherwise CSV.</summary>
        public static void WriteTable(string path, IList<string> header, IEnumerable<IList<object>> rows)
        {
            if (Path.GetExtension(path).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
                WriteXlsx(path, header, rows);
            else
                WriteCsv(path, header, rows);
        }

        public static string ToText(object value)
        {
            switch (value)
            {
                case null: return "";
                case double d: return d.ToString("0.###############", Inv);
                case float f: return ((double)f).ToString("0.#######", Inv);
                case IFormattable fm: return fm.ToString(null, Inv);
                default: return value.ToString();
            }
        }

        static bool IsNumber(object v) =>
            v is int || v is long || v is double || v is float || v is decimal || v is short;

        // ------------------------------------------------------------------ CSV

        static string CsvEscape(object value)
        {
            var text = ToText(value);
            if (text.IndexOfAny(new[] { ',', '"', '\n', '\r', ';' }) >= 0)
                text = "\"" + text.Replace("\"", "\"\"") + "\"";
            return text;
        }

        /// <summary>UTF-8 with BOM so Excel opens Arabic/English text correctly.</summary>
        public static void WriteCsv(string path, IList<string> header, IEnumerable<IList<object>> rows)
        {
            var sb = new StringBuilder();
            sb.Append(string.Join(",", header.Select(h => CsvEscape(h)))).Append("\r\n");
            foreach (var r in rows)
                sb.Append(string.Join(",", r.Select(CsvEscape))).Append("\r\n");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        /// <summary>Minimal RFC-4180 parser. Detects ';' as separator when the header
        /// line has more semicolons than commas (European / Middle-East Excel).</summary>
        public static List<IList<string>> ReadCsvRows(string path)
        {
            string text;
            using (var reader = new StreamReader(path, Encoding.UTF8, true))
                text = reader.ReadToEnd();
            return ParseCsv(text);
        }

        public static List<IList<string>> ParseCsv(string text)
        {
            var firstLine = text.Split('\n')[0];
            char delim = firstLine.Count(c => c == ';') > firstLine.Count(c => c == ',') ? ';' : ',';
            var rows = new List<IList<string>>();
            var row = new List<string>();
            var field = new StringBuilder();
            bool inQ = false, any = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                any = true;
                if (inQ)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                        else inQ = false;
                    }
                    else field.Append(c);
                }
                else if (c == '"') inQ = true;
                else if (c == delim) { row.Add(field.ToString()); field.Clear(); }
                else if (c == '\n' || c == '\r')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    row.Add(field.ToString());
                    rows.Add(row);
                    row = new List<string>();
                    field.Clear();
                    any = false;
                }
                else field.Append(c);
            }
            if (any || field.Length > 0 || row.Count > 0)
            {
                row.Add(field.ToString());
                rows.Add(row);
            }
            return rows;
        }

        // ----------------------------------------------------------------- XLSX

        static int ColIndex(string cellRef)
        {
            int idx = 0;
            foreach (char ch in cellRef)
            {
                if (!char.IsLetter(ch)) break;
                idx = idx * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
            }
            return idx - 1;
        }

        static string ColLetter(int idx)
        {
            var s = "";
            idx += 1;
            while (idx > 0)
            {
                int rem = (idx - 1) % 26;
                s = (char)('A' + rem) + s;
                idx = (idx - 1) / 26;
            }
            return s;
        }

        static string AllText(XElement e) =>
            e == null ? "" : string.Concat(e.Descendants(Main + "t").Select(t => t.Value));

        static XDocument LoadEntry(ZipArchive zip, string name)
        {
            var entry = zip.GetEntry(name);
            if (entry == null) return null;
            using (var s = entry.Open())
                return XDocument.Load(s);
        }

        static string SheetPath(ZipArchive zip, string sheetName)
        {
            var wb = LoadEntry(zip, "xl/workbook.xml") ?? throw new InvalidDataException("Not an Excel workbook");
            var sheets = wb.Descendants(Main + "sheet")
                .Select(s => new { Name = (string)s.Attribute("name") ?? "", Id = (string)s.Attribute(Rel + "id") })
                .ToList();
            if (sheets.Count == 0) throw new InvalidDataException("Workbook has no sheets");
            var chosen = sheets.FirstOrDefault(s => s.Name.Trim().Equals(sheetName ?? "", StringComparison.OrdinalIgnoreCase))
                         ?? sheets[0];
            var rels = LoadEntry(zip, "xl/_rels/workbook.xml.rels");
            var target = rels?.Descendants(Pkg + "Relationship")
                .FirstOrDefault(r => (string)r.Attribute("Id") == chosen.Id)?.Attribute("Target")?.Value;
            if (target == null) return "xl/worksheets/sheet1.xml";
            target = target.Replace('\\', '/');
            return target.StartsWith("/") ? target.TrimStart('/') : "xl/" + target;
        }

        /// <summary>Rows from the sheet named <paramref name="sheetName"/>, or the first sheet.
        /// Formulas return their cached value; whole numbers are returned without ".0".</summary>
        public static List<IList<string>> ReadXlsxRows(string path, string sheetName = "Mapping")
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                var shared = new List<string>();
                var sst = LoadEntry(zip, "xl/sharedStrings.xml");
                if (sst != null)
                    shared = sst.Root.Elements(Main + "si").Select(AllText).ToList();

                var sheet = LoadEntry(zip, SheetPath(zip, sheetName));
                var rows = new List<IList<string>>();
                foreach (var r in sheet.Descendants(Main + "row"))
                {
                    var vals = new SortedDictionary<int, string>();
                    int pos = 0;
                    foreach (var c in r.Elements(Main + "c"))
                    {
                        var cref = (string)c.Attribute("r");
                        int col = cref != null ? ColIndex(cref) : pos;
                        pos = col + 1;
                        var type = (string)c.Attribute("t") ?? "n";
                        var v = c.Element(Main + "v");
                        string val;
                        if (type == "inlineStr") val = AllText(c.Element(Main + "is"));
                        else if (v == null) val = "";
                        else if (type == "s") val = shared[int.Parse(v.Value, Inv)];
                        else if (type == "b") val = v.Value == "1" ? "TRUE" : "FALSE";
                        else if (type == "str" || type == "e") val = v.Value;
                        else val = double.TryParse(v.Value, NumberStyles.Float, Inv, out var d) ? ToText(d) : v.Value;
                        vals[col] = val;
                    }
                    if (vals.Count == 0) continue;
                    var rAttr = (string)r.Attribute("r");
                    int rowIdx = rAttr != null ? int.Parse(rAttr, Inv) - 1 : rows.Count;
                    while (rows.Count < rowIdx) rows.Add(new List<string>());
                    int width = vals.Keys.Max() + 1;
                    rows.Add(Enumerable.Range(0, width).Select(k => vals.TryGetValue(k, out var s) ? s : "").ToList());
                }
                return rows;
            }
        }

        static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

        /// <summary>Single-sheet workbook: bold, frozen header row, numbers as numbers.</summary>
        public static void WriteXlsx(string path, IList<string> header, IEnumerable<IList<object>> rows, string sheetName = "Mapping")
        {
            var all = new List<IList<object>> { header.Cast<object>().ToList() };
            all.AddRange(rows);
            var widths = header.Select(_ => 10).ToArray();
            var sheetData = new StringBuilder();
            for (int ri = 0; ri < all.Count; ri++)
            {
                sheetData.Append($"<row r=\"{ri + 1}\">");
                for (int ci = 0; ci < all[ri].Count; ci++)
                {
                    var value = all[ri][ci];
                    var cref = ColLetter(ci) + (ri + 1).ToString(Inv);
                    var style = ri == 0 ? " s=\"1\"" : "";
                    var text = ToText(value);
                    if (ci < widths.Length) widths[ci] = Math.Min(60, Math.Max(widths[ci], text.Length + 2));
                    if (IsNumber(value))
                        sheetData.Append($"<c r=\"{cref}\"{style}><v>{text}</v></c>");
                    else
                        sheetData.Append($"<c r=\"{cref}\"{style} t=\"inlineStr\"><is><t xml:space=\"preserve\">{Esc(text)}</t></is></c>");
                }
                sheetData.Append("</row>");
            }
            var cols = string.Concat(widths.Select((w, i) => $"<col min=\"{i + 1}\" max=\"{i + 1}\" width=\"{w}\" customWidth=\"1\"/>"));
            const string decl = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>";
            var files = new Dictionary<string, string>
            {
                ["[Content_Types].xml"] = decl +
                    "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                    "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                    "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                    "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                    "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                    "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
                    "</Types>",
                ["_rels/.rels"] = decl + $"<Relationships xmlns=\"{Pkg}\"><Relationship Id=\"rId1\" " +
                    "Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>",
                ["xl/workbook.xml"] = decl + $"<workbook xmlns=\"{Main}\" xmlns:r=\"{Rel}\"><sheets>" +
                    $"<sheet name=\"{Esc(sheetName)}\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>",
                ["xl/_rels/workbook.xml.rels"] = decl + $"<Relationships xmlns=\"{Pkg}\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                    "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
                    "</Relationships>",
                ["xl/styles.xml"] = decl + $"<styleSheet xmlns=\"{Main}\">" +
                    "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
                    "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>" +
                    "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
                    "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                    "<cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
                    "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/></cellXfs>" +
                    "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>",
                ["xl/worksheets/sheet1.xml"] = decl + $"<worksheet xmlns=\"{Main}\"><sheetViews><sheetView workbookViewId=\"0\">" +
                    "<pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>" +
                    $"<cols>{cols}</cols><sheetData>{sheetData}</sheetData></worksheet>",
            };
            if (File.Exists(path)) File.Delete(path);
            using (var fs = new FileStream(path, FileMode.CreateNew))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                foreach (var kv in files)
                {
                    var entry = zip.CreateEntry(kv.Key, CompressionLevel.Optimal);
                    using (var w = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
                        w.Write(kv.Value);
                }
            }
        }
    }
}
