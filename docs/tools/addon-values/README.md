# Addon Values

**Tool ID:** `addons.values` · **Section:** Addons & UI

## Purpose

Inspect a bounded range of values for one named addon instance. Reports types and only reviewed/approved scalar fields.

## Inputs

- **Addon:** select a name from the latest successful Addon List capture, or choose **Manual entry**. **Instance index:** 1–8.
- **Start value index:** nonnegative.
- **Count:** 1–128, within the supported range.

## Use

Select a captured addon (or manually enter its name) and range, then press **Capture**. Use Addon List to discover loaded addon names and available counts.

## Limits

Requires exact native compatibility and session authorization. Booleans, reviewed ContextMenu count, and `SelectYesno` prompt/button strings at slots 0–3 may be decoded. SelectYesno text is dynamic and included in reports; review before sharing. Other unclassified numbers, strings, and pointers stay omitted. Values can change between captures.
