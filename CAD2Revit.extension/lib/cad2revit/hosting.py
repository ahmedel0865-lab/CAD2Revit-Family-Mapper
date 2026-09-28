# -*- coding: utf-8 -*-
"""Revit-free geometry helpers for face hosting (sloped ceilings and slabs).

Vectors are plain (x, y, z) tuples so this module can be unit tested outside
Revit; placer.py converts to and from XYZ. IronPython 2.7 compatible.
"""
import math

# Host_Type value -> host categories searched ("face" uses config.HOST_CATEGORIES).
CATEGORY_KEYS = ("ceiling", "floor")

# A face counts as an underside when its normal points down at least this much
# (cos of ~84 deg from vertical), so steep ceilings still qualify but the TOP face
# of the slab the level sits on (normal pointing up) is never used.
MIN_DOWNWARD_NZ = 0.1

EPS = 1e-6


def _dot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def _cross(a, b):
    return (a[1] * b[2] - a[2] * b[1],
            a[2] * b[0] - a[0] * b[2],
            a[0] * b[1] - a[1] * b[0])


def _length(a):
    return math.sqrt(_dot(a, a))


def normalize(a):
    n = _length(a)
    if n < EPS:
        return None
    return (a[0] / n, a[1] / n, a[2] / n)


def categories_for(host, host_categories):
    """Host_Type -> list of category keys to search, e.g. "face" -> ["ceiling", "floor"]."""
    if host in CATEGORY_KEYS:
        return [host]
    if host == "face":
        return [c for c in (k.strip().lower() for k in host_categories) if c in CATEGORY_KEYS]
    return []


def is_underside(normal):
    """True for faces whose normal points downward (ceiling/slab soffits, sloped or flat)."""
    return normal is not None and normal[2] <= -MIN_DOWNWARD_NZ


def pick_nearest_underside(candidates, max_distance):
    """candidates: list of (proximity, normal, payload). Returns the payload of the nearest
    candidate within max_distance whose face is an underside, or None."""
    best = None
    for proximity, normal, payload in candidates:
        if proximity < 0 or proximity > max_distance or not is_underside(normal):
            continue
        if best is None or proximity < best[0]:
            best = (proximity, payload)
    return best[1] if best else None


def slope_deg(normal):
    """Angle between the face and the horizontal plane, in degrees (0 = flat)."""
    n = normalize(normal)
    if n is None:
        return 0.0
    return math.degrees(math.acos(min(1.0, abs(n[2]))))


def ref_direction(angle_rad, normal):
    """Reference direction for NewFamilyInstance(face, point, refDir, symbol).

    The CAD rotation d = (cos a, sin a, 0) is projected onto the face plane:
    ref_dir = d - n * (d . n), normalized. If d is (almost) parallel to the normal,
    n x Z is used instead (and X if the face is exactly horizontal).
    """
    d = (math.cos(angle_rad), math.sin(angle_rad), 0.0)
    n = normalize(normal)
    if n is None:
        return d
    k = _dot(d, n)
    r = normalize((d[0] - n[0] * k, d[1] - n[1] * k, d[2] - n[2] * k))
    if r is None:
        r = normalize(_cross(n, (0.0, 0.0, 1.0))) or (1.0, 0.0, 0.0)
    return r


def host_label(kind, linked):
    """e.g. "ceiling", "linked floor", "none"."""
    if not kind:
        return u"none"
    return (u"linked " if linked else u"") + kind


def describe(kind, linked, normal):
    """Text for the result message / CSV log, e.g. "host: linked ceiling, slope 12.5 deg"."""
    if not kind:
        return u"host: none"
    return u"host: {}, slope {:.1f} deg".format(host_label(kind, linked), slope_deg(normal))
