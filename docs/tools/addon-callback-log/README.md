# Addon Callback Log

**Tool ID:** `addons.callbacks` · **Section:** Addons & UI

## Purpose

Observe normal addon callback traffic while you interact with the UI. Intended for bounded debugging, not automation or replay.

## Inputs and use

- **Addon filter:** leave blank for all addons, or enter one exact name.
- Confirm exact native compatibility and session authorization.
- Press **Start capture**, interact normally, then **Stop capture**. Stop retains the result.

## Output and limits

The rolling buffer retains newest 64 events and reports older events dropped. The table shows addon, typed argument values, raw flag, and return. Integers are unredacted and may contain IDs; the warning is included in display/copy/export. Strings, floats, vectors, and pointer-typed values remain excluded. Context changes and plugin unload cancel active capture. No callback replay, argument writes, or other-plugin controls. Native hooks can conflict or crash the game; compatibility is not a safety certification.
