# Addon Nodes

**Tool ID:** `addons.nodes` · **Section:** Addons & UI

## Purpose

Inspect bounded root ULD node-list geometry for one addon instance.

## Inputs

- **Addon name**.
- **Instance index:** 1–8.
- **Max parent depth:** 1–8.
- **Node ID:** optional nonzero numeric filter.

## Use

Enter the addon and filters, then press **Capture**. Use Addon List to identify loaded addon names.

## Limits

Requires exact native compatibility and session authorization. At most 256 root-list entries are examined. No component internals, arbitrary node text, persistent pointer traversal, or UI mutation.
