# Phase 2 — implementation and live validation

## Implemented

Version 0.2.0 adds eight tools to the two existing inspectors. All implementations are inside Debug Kit. Existing plugin trees and GBR remain untouched. No plugin is installed, published, or automatically enabled by this work.

- Stable explicit catalog lookup and tool dispatch, immutable canonical tool inputs, duplicate/queue protection.
- Closed typed payloads, per-tool schemas, bounded steps/rows/bytes, text policy and report envelope format 2.
- Selected Target via public API; no targeting changes or entity-continuity claims.
- Loaded/visible/focused addon listing with metadata and navigation-only drill-down.
- Addon value types/reviewed scalar decoding; unclassified values explicitly omitted.
- Bounded root addon ULD-node metadata with parent-depth/cycle checks and no component/text decoding.
- One-shot capture of a currently open ContextMenu; labels only, no action mapping/callbacks.
- Seven typed sheet readers with row/language provenance and no reference recursion.
- Normal player-bag inventory capture, readiness/quality/empty-slot filtering.
- Managed retained-snapshot comparison with compatibility checks and uncertainty handling.
- Existing left search/navigation/pinned details and right per-tool controls/results preserved. No batch-capture button. Experimental native authorization defaults enabled for the session per user request; it can be disabled and no longer resets after queueing. Each request freezes authorization, and exact compatibility remains mandatory.

## Compatibility evidence and limitations

Native reads require installation stamp `2026.10.16.0000.0000`, Dalamud `15.0.3.9`, and FFXIVClientStructs SHA-256 `f5a2c68ccecd3e199bda572050e98902b2e7594066c95dc7826c2d4297cd0564`.

The installed version file and development assembly hash were inspected locally during implementation. The stamp is read from the fixed `ffxivgame.ver` file beside the running executable, using bounded input; paths are never exported. It is installation evidence, **not independent proof of running code/layout correctness**. Missing/read-failed/mismatched evidence yields `UnsupportedBuild`, with no bypass.

Matching is deliberately labeled **ExperimentalMatched**. Every native capture additionally checks authorization frozen when queued from the session checkbox (default enabled per user request). Disabling the checkbox blocks new native requests, not requests already authorized; Cancel stops existing Debug Kit work. No in-game safety/performance certification is claimed. A changed profile requires code/layout review and live testing, not a looser match or manual checkbox override.

Native source uses only the installed FFXIVClientStructs contract. No unmanaged pointers, iterators or wrappers survive a framework call. Source pointers in visited sets exist only during one bounded node step; none enter Core results/export. Native exceptions cannot reliably contain invalid-memory faults: readiness checks and exact compatibility are safeguards, not proof.

## Tool details

### Selected Target

Captures name, broad/native kind, permitted base ID, position/distance, targetability, finite rotation/hitbox and supported character HP/job/level. Battle-character targets also capture up to 80 status IDs, localized names, and reported remaining times; source-object IDs are excluded. Status availability distinguishes empty lists, unsupported target types, and failed reads. Per-entry read failures, truncation, and partial output remain explicit. Unsupported numeric fields display `unavailable`, never empty interpolations; genuine zero values remain zero. No target, loading, invalid position and missing player-distance origin have explicit unavailable representations. Raw identity/owner IDs and addresses remain excluded. Display names are included as requested.

### Addon List

Loaded/visible/focused unit lists, optional name filter, maximum 256 list entries and 32 examined entries per step under the cooperative budget. A native addon ID is not the numbered instance parameter accepted by `GetAddonByName`. Drill-down defaults to instance index 1; the user can select another reviewed index.

### Addon Values

Validated internal addon name, instance 1–8, bounded start/count within the actual value array. At most 128 values in one bounded capture. Reports types and whether fields were decoded. Booleans and ContextMenu index-0 count (UInt <=64) are decoded. **Other scalar/string/pointer values are omitted**, not treated as safe merely because numeric. This is a useful type/range inspector, not an arbitrary value dump.

### Addon Nodes

Ready/visible selected addon, root ULD list only, max 256 examined entries and parent depth 8. Checks allocation size, finite geometry, cycles/repeated pointers and time budget. Filtering by node ID does not remove the total examined-entry cap. Component internal lists and arbitrary text are not followed. A partial scan is not resumed using stale pointers or hidden unbounded rescans.

### Open Context Menu

User opens the menu before capture. Decoder validates the reviewed count/label layout, max 64 entries, max 512 native string bytes before termination, and bounded sanitized retained labels. Detached labels are parsed with Lumina's SeString parser, not plain UTF-8 decoding. Reviewed presentation payloads (colors/styles/icons) are stripped; text uses strict UTF-8 validation. Malformed, empty, over-bound, or unsupported dynamic-macro labels are unavailable/undecoded and make the capture partial. No game-dependent macro evaluation occurs. It never calls menu actions or invents enabled/action-ID mappings. A capture-next-menu watcher is not implemented; it was optional follow-up, not required for one-shot capture.

### Sheet Row

Item, Action, Mount, TerritoryType, BNpcName, ENpcResident and EObj with explicit fields. Requested language is an allowlisted enum; actual sheet language is reported (including no-language sheets). Missing row/schema/language data is not fabricated. No arbitrary reflection or automatic row-reference traversal. EObj has data/pop fields; EObjName is a distinct sheet and is not silently substituted.

### Inventory Slots

Normal bags 1–4 only. Native container loaded/pointer/size readiness is checked on every step. No symbolic-item traversal, crafter IDs or private retainer data. Item ID, HQ flag, quantity, name and container/slot are separate. Include-empty captures deliberately record valid empty slots; unloaded containers never become empty successes. Multi-update snapshots and truncated observed counts are not authoritative inventory totals.

### Snapshot Comparison

Only retained snapshots are referenced by ID. Clearing/evicting sources yields `SnapshotUnavailable`; comparison does not keep extra hidden baseline copies. Requires different captures, same tool/schema/canonical inputs, compatible context (except static sheet context), matching build/language and appropriate UI lifetime.

Keys: sheet field, bag/slot, addon name/native ID, value index, menu entry index or node ID. Duplicate keys are ambiguous. Nearby keys are observations only; target metadata does not prove identity continuity. Missing entries in partial/non-atomic captures are **Unobserved**, not Removed. Comparison max 256 keys; changed-only filtering preserves complete summary counts for examined keys and visibly marks key-cap truncation.

## Formats and bounds

- Report envelope format 2; legacy context v1 and nearby v3 schemas remain unchanged.
- New tools start at schema v1; payload `$type` identifies the closed typed schema.
- Human-readable output is tool-specific. Legacy envelope fields remain for existing JSON consumers.
- Queue 16, result 64 KiB, history 100 / 1 MiB, export 2 MiB and one writer.
- Phase 2 retained rows max 256; strings bounded/single-line; source/byte omissions explicit.
- Cooperative step budgets do not preempt a slow managed/native getter or initial game-data load. Rendering/formatting/serialization also require profiling.
- Unknown values remain unavailable; captured names/positions/territory are intentionally not anonymous.

## Verification boundaries

Offline tests exercise production Core against detached fake reader steps: dispatch/validation/canonicalization, target/missing data, paged results, epoch/context cancellation, row/byte/no-progress bounds, payload schemas, exact profile matching, node guards, inventory filtering, and comparison compatibility/ambiguity/completeness/eviction.

Tests also compile the production detached menu-label decoder against the installed Lumina assembly, covering plain/Japanese text, color/icon payloads, malformed envelopes/expressions, invalid UTF-8, unsupported macros and byte/text bounds. They **do not dereference native game memory**, instantiate the live plugin, verify actual sheet contents, or establish native/UI correctness. Build and source tripwires are separate regression checks, not runtime proof.

## Live checklist — Phase 2 pending

- [ ] Reload 0.2.0 and verify all ten tools, `/debugkit` and `/dk`, navigation/search/footer, scrolling and resize behavior.
- [ ] Selected Target: player/NPC/object/no target; correct names/base kinds/HP; no target changes; missing distance remains unavailable.
- [ ] Sheet Row: known Item/Action/etc., missing row, alternate languages, no-language data, correct explicit fields.
- [ ] Compatibility: footer/report stamp/version/hash truthful; mismatched/unavailable profile blocks native reads. Session authorization defaults enabled, remains disableable and does not reset after queueing; existing queued requests retain frozen authorization.
- [ ] Addon List: loaded/visible/focused agreement; name filter, ready/unready states, metadata, navigation drill-down without automatic capture.
- [ ] Addon Values: range bounds/types; unclassified data omitted; closing/reopening addon cancels or invalidates appropriately.
- [ ] Addon Nodes: node/allocation/depth/cycle limits, clipping, partial output, no component text/private fields.
- [ ] Context Menu: user-opened labels correct, no automatic opening/clicking, closed/unready/changed menu handled honestly.
- [ ] Inventory: bag selection, HQ/NQ, item filter, empty slots, unavailable containers, quantities and item names; no changes to inventory.
- [ ] Compare: changes-only/all fields, incompatible tool/schema/inputs/build/language/context, ambiguous nodes/objects, partial/unobserved semantics and evicted sources.
- [ ] Cancel pending and multi-update captures; source addon/character/territory changes; last-good results remain visibly historical.
- [ ] Format-2 JSON/text/clipboard/preview agree; exact-preview export/nonoverwrite/cancel/reload; no IDs/addresses/exception payloads.
- [ ] Measure frame cost, first sheet-load cost, render cost and memory under bounded worst-case captures; tune from measurements.

User-reported Phase 2 checks are now recorded individually in [validation history](validation.md); the checklist above remains a full acceptance matrix, not a claim that every subcase was tested. Native tools stay experimental. Profiling instrumentation and the remaining [performance procedure](performance.md), export-content agreement/cancellation and stress cases remain unverified in-game until explicitly measured.
