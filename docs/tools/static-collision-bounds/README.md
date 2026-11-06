# Static Collision Bounds

**Tool ID:** `world.collision` · **Section:** World

## Purpose

Inspect static collision shapes/transforms from installed LGB data near the player.

## Inputs

- **Radius:** 1–200 yalms.
- **Installed LGB:** Planevent or Planmap.

## Use

Choose the radius and asset, then press **Capture**.

## Limits

Static file evidence only—not runtime collision, navigation, line-of-sight, or walkability. Radius filters placement origin, not shape overlap. Initial asset loading is not preemptible; output is bounded.
