# -*- coding: utf-8 -*-
"""Small helpers that work in both IronPython and CPython engines."""
import codecs
from Autodesk.Revit.DB import Element


def elem_name(element):
    """Element.Name is unreliable on some types in IronPython; use the getter."""
    try:
        return Element.Name.GetValue(element)
    except Exception:
        return element.Name


def _csv_escape(value):
    text = u"" if value is None else u"{}".format(value)
    if any(c in text for c in (u",", u'"', u"\n", u"\r")):
        text = u'"' + text.replace(u'"', u'""') + u'"'
    return text


def write_csv(path, header, rows):
    """Write UTF-8 CSV with BOM so Excel opens Arabic/English text correctly."""
    with codecs.open(path, "w", "utf-8-sig") as f:
        f.write(u",".join(_csv_escape(h) for h in header) + u"\r\n")
        for row in rows:
            f.write(u",".join(_csv_escape(v) for v in row) + u"\r\n")


def read_csv(path):
    """Return list of dicts (keys stripped). Minimal parser supporting quotes."""
    with codecs.open(path, "r", "utf-8-sig") as f:
        text = f.read()
    rows, field, row, in_q, i = [], u"", [], False, 0
    while i < len(text):
        c = text[i]
        if in_q:
            if c == u'"':
                if i + 1 < len(text) and text[i + 1] == u'"':
                    field += u'"'
                    i += 1
                else:
                    in_q = False
            else:
                field += c
        else:
            if c == u'"':
                in_q = True
            elif c == u",":
                row.append(field)
                field = u""
            elif c in (u"\n", u"\r"):
                if c == u"\r" and i + 1 < len(text) and text[i + 1] == u"\n":
                    i += 1
                row.append(field)
                field = u""
                if any(v.strip() for v in row):
                    rows.append(row)
                row = []
            else:
                field += c
        i += 1
    if field or row:
        row.append(field)
        if any(v.strip() for v in row):
            rows.append(row)
    if not rows:
        return []
    header = [h.strip() for h in rows[0]]
    out = []
    for r in rows[1:]:
        r = r + [u""] * (len(header) - len(r))
        out.append(dict((header[k], r[k].strip()) for k in range(len(header))))
    return out
