# Addon Values

**Tool ID:** `addons.values` · **Section:** Addons & UI

## Purpose

Inspect a bounded range of values for one named addon instance. Reports types and only reviewed/approved scalar fields.

## Inputs

- **Addon name** and **Instance index:** 1–8.
- **Start value index:** nonnegative.
- **Count:** 1–128, within the supported range.

## Use

Enter the addon and range, then press **Capture**. Use Addon List to discover loaded addon names and available counts.

## Limits

Requires exact native compatibility and session authorization. Booleans and reviewed ContextMenu count fields may be decoded; unclassified numbers, strings, and pointers are omitted. Values can change between captures.
