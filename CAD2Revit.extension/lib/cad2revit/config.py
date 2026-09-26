# -*- coding: utf-8 -*-
"""User-adjustable settings. Edit, then click pyRevit > Reload."""

# --- Duplicate protection ------------------------------------------------------
# An existing instance closer than this (mm, in plan) to a new placement point,
# on the same level band, counts as "already placed" and is skipped.
DUPLICATE_TOLERANCE_MM = 50.0
# "family": any type of the same family counts as a duplicate (safe when you
#           change the type in the mapping and re-run).
# "type":   only the exact same family type counts.
DUPLICATE_SCOPE = "family"

# --- DWG reading -------------------------------------------------------------
# Also read blocks nested inside other blocks.
INCLUDE_NESTED_BLOCKS = False

# --- Hosting -----------------------------------------------------------------
# Max distance (mm) above the level to look for a ceiling / slab face. The search
# also never goes past the next level up.
HOST_SEARCH_DISTANCE_MM = 6000.0
# Max distance (mm) from the CAD point to a wall face for Host_Type = wall.
WALL_SEARCH_DISTANCE_MM = 500.0
# Also look for hosts inside linked Revit models (architectural link).
SEARCH_REVIT_LINKS = True
# If a hosted row finds no host: True = place it non-hosted (on the level at the
# row's offset) and note it in the log; False = skip it and log it as failed.
FALLBACK_TO_UNHOSTED = True

# --- Output ------------------------------------------------------------------
# Write the source CAD block name into each placed element's "Comments".
WRITE_BLOCK_NAME_TO_COMMENTS = True

MM_PER_FOOT = 304.8


def mm_to_ft(value_mm):
    return float(value_mm) / MM_PER_FOOT


def ft_to_mm(value_ft):
    return float(value_ft) * MM_PER_FOOT
