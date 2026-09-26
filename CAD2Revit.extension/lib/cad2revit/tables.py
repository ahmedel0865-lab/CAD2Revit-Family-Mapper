# -*- coding: utf-8 -*-
"""Read/write CSV and XLSX tables.

Pure Python (no Revit imports) so it runs in IronPython 2.7, CPython 3 and in
the unit tests. The XLSX support is a small reader/writer built on zipfile +
ElementTree, so Excel does not need to be installed on the Revit machine.
"""
import codecs
import os
import re
import zipfile
import xml.etree.ElementTree as ET

_NS_MAIN = "http://schemas.openxmlformats.org/spreadsheetml/2006/main"
_NS_REL = "http://schemas.openxmlformats.org/officeDocument/2006/relationships"
_NS_PKG = "http://schemas.openxmlformats.org/package/2006/relationships"

try:
    text_type = unicode  # IronPython 2.7
except NameError:
    text_type = str      # CPython 3


def _text(value):
    if value is None:
        return u""
    if isinstance(value, float) and value == int(value):
        value = int(value)
    return text_type(value)


# --------------------------------------------------------------------- common

def rows_to_dicts(rows):
    """First row = header. Returns list of dicts keyed by stripped header text.
    Blank rows are dropped; short rows are padded with empty strings."""
    rows = [r for r in rows if any(_text(v).strip() for v in r)]
    if not rows:
        return []
    header = [_text(h).strip() for h in rows[0]]
    out = []
    for r in rows[1:]:
        r = list(r) + [u""] * (len(header) - len(r))
        out.append(dict((header[k], _text(r[k]).strip())
                        for k in range(len(header)) if header[k]))
    return out


def read_table(path):
    """Read a .csv or .xlsx file into a list of dicts (see rows_to_dicts)."""
    ext = os.path.splitext(path)[1].lower()
    if ext in (".xlsx", ".xlsm"):
        return rows_to_dicts(read_xlsx_rows(path))
    return rows_to_dicts(read_csv_rows(path))


def write_table(path, header, rows):
    """Write .xlsx if the path ends in .xlsx, otherwise CSV."""
    if os.path.splitext(path)[1].lower() == ".xlsx":
        write_xlsx(path, header, rows)
    else:
        write_csv(path, header, rows)


# ------------------------------------------------------------------------ CSV

def _csv_escape(value):
    text = _text(value)
    if any(c in text for c in (u",", u'"', u"\n", u"\r")):
        text = u'"' + text.replace(u'"', u'""') + u'"'
    return text


def write_csv(path, header, rows):
    """Write UTF-8 CSV with BOM so Excel opens Arabic/English text correctly."""
    with codecs.open(path, "w", "utf-8-sig") as f:
        f.write(u",".join(_csv_escape(h) for h in header) + u"\r\n")
        for row in rows:
            f.write(u",".join(_csv_escape(v) for v in row) + u"\r\n")


def _sniff_delimiter(first_line):
    # Excel in many European / Middle-East locales saves "CSV" with semicolons.
    return u";" if first_line.count(u";") > first_line.count(u",") else u","


def read_csv_rows(path):
    """Return a list of rows (lists of strings). Minimal RFC-4180 parser."""
    with codecs.open(path, "r", "utf-8-sig") as f:
        text = f.read()
    delim = _sniff_delimiter(text.split(u"\n", 1)[0])
    rows, field, row, in_q, i, n = [], u"", [], False, 0, len(text)
    while i < n:
        c = text[i]
        if in_q:
            if c == u'"':
                if i + 1 < n and text[i + 1] == u'"':
                    field += u'"'
                    i += 1
                else:
                    in_q = False
            else:
                field += c
        elif c == u'"':
            in_q = True
        elif c == delim:
            row.append(field)
            field = u""
        elif c in (u"\n", u"\r"):
            if c == u"\r" and i + 1 < n and text[i + 1] == u"\n":
                i += 1
            row.append(field)
            rows.append(row)
            field, row = u"", []
        else:
            field += c
        i += 1
    if field or row:
        row.append(field)
        rows.append(row)
    return rows


def read_csv(path):
    """Backwards-compatible helper: CSV -> list of dicts."""
    return rows_to_dicts(read_csv_rows(path))


# ----------------------------------------------------------------------- XLSX

def _q(tag, ns=_NS_MAIN):
    return "{%s}%s" % (ns, tag)


def _col_index(cell_ref):
    """'C12' -> 2 (zero based)."""
    letters = re.match(r"[A-Za-z]+", cell_ref).group(0).upper()
    idx = 0
    for ch in letters:
        idx = idx * 26 + (ord(ch) - 64)
    return idx - 1


def _col_letter(idx):
    s = ""
    idx += 1
    while idx:
        idx, rem = divmod(idx - 1, 26)
        s = chr(65 + rem) + s
    return s


def _all_text(elem):
    """Concatenate every <t> below elem (handles rich-text runs)."""
    return u"".join(t.text or u"" for t in elem.iter(_q("t")))


def _sheet_path(zf, sheet_name):
    wb = ET.fromstring(zf.read("xl/workbook.xml"))
    sheets = [(s.get("name") or u"", s.get(_q("id", _NS_REL)))
              for s in wb.iter(_q("sheet"))]
    if not sheets:
        raise ValueError("Workbook has no sheets")
    chosen = sheets[0]
    for name, rid in sheets:
        if name.strip().lower() == (sheet_name or u"").lower():
            chosen = (name, rid)
            break
    rels = ET.fromstring(zf.read("xl/_rels/workbook.xml.rels"))
    for rel in rels.iter(_q("Relationship", _NS_PKG)):
        if rel.get("Id") == chosen[1]:
            target = rel.get("Target").replace("\\", "/")
            if target.startswith("/"):
                return target.lstrip("/")
            return "xl/" + target
    return "xl/worksheets/sheet1.xml"


def read_xlsx_rows(path, sheet_name=u"Mapping"):
    """Return rows (lists of strings) from the sheet named `sheet_name`, or the
    first sheet if there is none with that name. Formulas return their cached
    value; whole-number numeric cells are returned without '.0'."""
    zf = zipfile.ZipFile(path, "r")
    try:
        shared = []
        if "xl/sharedStrings.xml" in zf.namelist():
            sst = ET.fromstring(zf.read("xl/sharedStrings.xml"))
            shared = [_all_text(si) for si in sst.findall(_q("si"))]

        sheet = ET.fromstring(zf.read(_sheet_path(zf, sheet_name)))
        rows = []
        for r in sheet.iter(_q("row")):
            vals = {}
            for pos, c in enumerate(r.findall(_q("c"))):
                ref = c.get("r")
                col = _col_index(ref) if ref else pos
                typ = c.get("t", "n")
                v = c.find(_q("v"))
                if typ == "inlineStr":
                    is_ = c.find(_q("is"))
                    val = _all_text(is_) if is_ is not None else u""
                elif v is None or v.text is None:
                    val = u""
                elif typ == "s":
                    val = shared[int(v.text)]
                elif typ == "b":
                    val = u"TRUE" if v.text == "1" else u"FALSE"
                elif typ in ("str", "e"):
                    val = v.text
                else:
                    try:
                        val = _text(float(v.text))
                    except ValueError:
                        val = v.text
                vals[col] = val
            if vals:
                row_idx = int(r.get("r")) - 1 if r.get("r") else len(rows)
                while len(rows) < row_idx:
                    rows.append([])
                rows.append([vals.get(k, u"") for k in range(max(vals) + 1)])
        return rows
    finally:
        zf.close()


def _xml_escape(text):
    return (text.replace(u"&", u"&amp;").replace(u"<", u"&lt;")
            .replace(u">", u"&gt;").replace(u'"', u"&quot;"))


def _is_number(value):
    if isinstance(value, bool):
        return False
    if isinstance(value, (int, float)):
        return True
    try:
        long_type = long  # noqa: F821 (IronPython)
        return isinstance(value, long_type)
    except NameError:
        return False


def write_xlsx(path, header, rows, sheet_name=u"Mapping"):
    """Write a single-sheet workbook: bold, frozen header row, text cells as
    inline strings and numbers as numbers."""
    all_rows = [list(header)] + [list(r) for r in rows]
    widths = [10] * len(header)
    lines = []
    for ri, row in enumerate(all_rows):
        cells = []
        for ci, value in enumerate(row):
            ref = u"%s%d" % (_col_letter(ci), ri + 1)
            style = u' s="1"' if ri == 0 else u""
            if ci < len(widths):
                widths[ci] = min(60, max(widths[ci], len(_text(value)) + 2))
            if _is_number(value):
                cells.append(u'<c r="%s"%s><v>%s</v></c>' % (ref, style, _text(value)))
            else:
                cells.append(u'<c r="%s"%s t="inlineStr"><is><t xml:space="preserve">%s</t></is></c>'
                             % (ref, style, _xml_escape(_text(value))))
        lines.append(u'<row r="%d">%s</row>' % (ri + 1, u"".join(cells)))

    cols = u"".join(u'<col min="%d" max="%d" width="%d" customWidth="1"/>' % (i + 1, i + 1, w)
                    for i, w in enumerate(widths))
    sheet = (u'<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
             u'<worksheet xmlns="%s"><sheetViews><sheetView workbookViewId="0">'
             u'<pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/>'
             u'</sheetView></sheetViews><cols>%s</cols><sheetData>%s</sheetData></worksheet>'
             % (_NS_MAIN, cols, u"".join(lines)))
    files = {
        "[Content_Types].xml":
            u'<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
            u'<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
            u'<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>'
            u'<Default Extension="xml" ContentType="application/xml"/>'
            u'<Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>'
            u'<Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>'
            u'<Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>'
            u'</Types>',
        "_rels/.rels":
            u'<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
            u'<Relationships xmlns="%s"><Relationship Id="rId1" '
            u'Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" '
            u'Target="xl/workbook.xml"/></Relationships>' % _NS_PKG,
        "xl/workbook.xml":
            u'<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
            u'<workbook xmlns="%s" xmlns:r="%s"><sheets><sheet name="%s" sheetId="1" r:id="rId1"/>'
            u'</sheets></workbook>' % (_NS_MAIN, _NS_REL, _xml_escape(sheet_name)),
        "xl/_rels/workbook.xml.rels":
            u'<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
            u'<Relationships xmlns="%s">'
            u'<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>'
            u'<Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>'
            u'</Relationships>' % _NS_PKG,
        "xl/styles.xml":
            u'<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
            u'<styleSheet xmlns="%s">'
            u'<fonts count="2"><font><sz val="11"/><name val="Calibri"/></font>'
            u'<font><b/><sz val="11"/><name val="Calibri"/></font></fonts>'
            u'<fills count="2"><fill><patternFill patternType="none"/></fill>'
            u'<fill><patternFill patternType="gray125"/></fill></fills>'
            u'<borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>'
            u'<cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>'
            u'<cellXfs count="2"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>'
            u'<xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/></cellXfs>'
            u'<cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>'
            u'</styleSheet>' % _NS_MAIN,
        "xl/worksheets/sheet1.xml": sheet,
    }
    zf = zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED)
    try:
        for name in ("[Content_Types].xml", "_rels/.rels", "xl/workbook.xml",
                     "xl/_rels/workbook.xml.rels", "xl/styles.xml", "xl/worksheets/sheet1.xml"):
            zf.writestr(name, files[name].encode("utf-8"))
    finally:
        zf.close()
