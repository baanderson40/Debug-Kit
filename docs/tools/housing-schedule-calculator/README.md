# Housing Schedule Calculator

**Tool ID:** `housing.schedule` · **Section:** Housing

## Purpose

Calculate entry/results phases from a user-supplied epoch and durations. This is a calculator, not an observation of authoritative lottery state.

## Inputs

- **Entry-start epoch:** optional ISO-8601 UTC (`Z` or `+00:00`).
- **Entry days** and **Results days:** each 1–30.

## Use

Enter an epoch and durations, then press **Capture**. If no epoch is supplied or it is in the future, the phase may be undefined.

## Limits

No default epoch is guessed. No bids, plots, results, or game lottery state are queried.
