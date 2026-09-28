# -*- coding: utf-8 -*-
"""User-adjustable settings."""

# Two families closer than this (mm) with the same type are treated as duplicates.
DUPLICATE_TOLERANCE_MM = 50.0

# Also read blocks nested inside other blocks.
INCLUDE_NESTED_BLOCKS = False

# Write the source CAD block name into each placed element's "Comments" parameter.
WRITE_BLOCK_NAME_TO_COMMENTS = True

# Max search distance (mm) above the level when looking for a face to host on.
HOST_SEARCH_DISTANCE_MM = 6000.0

# Categories searched for Host_Type = "face" (nearest underside above wins).
# Allowed values: "ceiling", "floor" (floors = slabs, structural or architectural).
HOST_CATEGORIES = ["ceiling", "floor"]

# The upward ray starts this far (mm) above the level. Top faces (normal pointing up)
# are always skipped, so the slab the level sits on is never used as the host.
HOST_RAY_START_MM = 10.0

MM_PER_FOOT = 304.8


def mm_to_ft(value_mm):
    return float(value_mm) / MM_PER_FOOT


def ft_to_mm(value_ft):
    return float(value_ft) * MM_PER_FOOT
