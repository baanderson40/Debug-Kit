# Snapshot Comparison

**Tool ID:** `results.compare` · **Section:** Results

## Purpose

Compare two compatible retained captures and summarize added, removed, changed, unchanged, ambiguous, or indeterminate evidence.

## Inputs

- **Baseline** and **Comparison:** select two different retained captures.
- **Changed/added/removed/uncertain only:** hide unchanged rows when enabled.

## Use

Capture the same supported tool twice, open Snapshot Comparison, select both results, then press **Capture**.

## Limits

Only matching tool/schema/inputs and compatible context/build/lifetime can be compared. Partial observations cannot prove removal; names or base IDs do not prove entity continuity. Provider, network, and callback captures do not support comparison.
