# Selected Target

**Tool ID:** `objects.target` · **Section:** Objects

## Purpose

Read the currently selected target's supported name, kind, position, distance, targetability, rotation, hitbox, HP, job, level, and up to 80 status entries (status ID, localized name, and reported remaining time). Source-object IDs are excluded.

## Use

Select **Selected Target** and press **Capture**. No inputs required. Select a target in-game yourself if you want one observed.

## Limits

Debug Kit never selects or changes a target. No selected target returns a no-target result; unsupported fields remain unavailable rather than inferred. Status availability distinguishes `Empty`, `Unsupported`, and `Failed`; partial status reads and lists over 80 are marked. Names are included; review before sharing.
