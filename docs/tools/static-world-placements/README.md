# Static World Placements

**Tool ID:** `world.placements` · **Section:** World

## Purpose

Inspect static installed LGB placements near the player. These are data-file placements, not proof of runtime-spawned objects.

## Inputs

- **Radius:** 1–200 yalms.
- **Installed LGB:** Planevent or Planmap.
- **Placement type:** All, Event Object, Event Range, Battle NPC, Event NPC, or Treasure.
- **Base ID:** optional nonzero filter.

## Use

Choose asset and filters, then press **Capture**. The scan is bounded and may be partial.

## Limits

At most 1,024 entries examined and 256 rows retained. Radius uses placement-origin distance, not runtime distance or bounds overlap. Asset paths are not exported.
