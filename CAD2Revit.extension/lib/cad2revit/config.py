# -*- coding: utf-8 -*-
"""User-adjustable settings."""

# Two families closer than this (mm) with the same type are treated as duplicates.
DUPLICATE_TOLERANCE_MM = 50.0

# Also read blocks nested inside other blocks.
INCLUDE_NESTED_BLOCKS = False

# Write the source CAD block name into each placed element's "Comments" parameter.
WRITE_BLOCK_NAME_TO_COMMENTS = True

# Max search distance (mm) above the level when looking for a ceiling face to host on.
HOST_SEARCH_DISTANCE_MM = 6000.0

MM_PER_FOOT = 304.8


def mm_to_ft(value_mm):
    return float(value_mm) / MM_PER_FOOT


def ft_to_mm(value_ft):
    return float(value_ft) * MM_PER_FOOT
