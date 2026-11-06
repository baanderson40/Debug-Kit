# Useful Scripts migration

Native Debug Kit implementations. No Lua runtime or SND dependency. Project license: AGPL-3.0-or-later (`../LICENSE.md`). Captures are read-only; live in-game validation and third-party port/attribution compatibility remain pending.

## Port map

| Lua script | Debug Kit tool | Status / boundary |
|---|---|---|
| Big Dump | Game State → Big Dump | Reviewed public fields only; supported target slots shown separately; absent/unsupported/withheld/failed states retained. No reflection, arbitrary object serialization, IDs, or pointers. |
| Character Condition Testing | Game State → Condition Flags | Snapshot of installed SDK flag names, numeric values, and booleans. No chat or automated transition tracker. |
| Installed Dalamud Plugins | Providers → Installed Plugins | Public internal name, loaded state, sanitized version; bounded to 512. No IPC/configuration/path reads. Sharing reveals installed plugin inventory. |
| Job Name&Abbr from excel sheet | Game Data → Job Lookup | Explicit ClassJob row or bounded ID 1–128 scan; localized name/abbreviation. |
| NPC name by id and excel sheet | Game Data → NPC Name Lookup | Direct typed BNpcName or ENpcResident lookup. No guessed cross-sheet relationship. |
| PlaceName by TerritoryTypeID | Game Data → Territory Lookup | TerritoryType row with typed PlaceName/Map references and place name. |
| Player Current Status IDs | Game State → Player Statuses | Bounded local status ID/name/remaining-time snapshot. Source IDs withheld; stack-count semantics not claimed. |
| Player Movement Speed Tracker | Game State → Movement Speed Tracker | Manual Start/Stop; bounded rolling samples; elapsed time uses monotonic clock. Sampled displacement/time, not authoritative speed. Large jumps marked discontinuities. |
| Scan Entity By Name | Objects → Entity Name Watch | Manual Start/Stop; exact/substring; bounded table scan and match count. “Not observed” does not mean despawn; no map flags. |
| Target Distance Tracker | Objects → Target Distance Tracker | Manual Start/Stop; flat and 3D origin-to-origin distance; reacquires selected target per sample. No identity continuity claim. |

## Shared observation behavior

- One observation at a time; explicit Start/Stop. Navigating away does not stop it.
- Logout, territory/character context change, cancellation, or unload discards the active observation.
- Newest 128 samples retained; older samples counted as dropped.
- Sampling interval and filters are frozen at Start. No worker-thread/game-wrapper reads, automatic startup, catch-up bursts, or game actions.
- Position/name fields may appear in reports; inspect before sharing.

## Excluded

Addon Arg Probe, Go To Coordinates, IPCs Subscribers, and SND base script remain excluded. Debug Kit has no generic callback dispatch, movement/target control, arbitrary IPC console, or Lua helper runtime.

## Validation

Windows Debug-x64 build and offline console tests exercise schemas, bounds, rollover, cancellation, sanitization, and report formatting. They do not exercise live game state. Validate fields and behavior in-game before relying on results. Distribution remains gated on licensing/port clearance.
