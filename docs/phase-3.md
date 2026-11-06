# Phase 3 — bounded domain evidence, version 0.3.2

## Delivery status

The supported implementation slice adds **14 tools** to the existing ten. Every planned domain has an implemented evidence surface or an explicit capability deferral below. This is **not** a claim that all originally contemplated native decoders exist, that unavailable semantics are decoded, or that live validation is complete. Existing plugin repositories were not changed; GBR was not inspected/imported. No source-plugin code/assets were ported and no companion plugin/provider is required.

New domain requests share the existing queue/lifecycle/history/preview/export/profiling pipeline, canonical captured inputs, descriptor lookup and generated JSON graph. Domain/sheet/comparison schemas are now v2 (`$type: domain` remains unchanged); report envelope remains format 2, context v1 and nearby v3 unchanged. Initial delivery was 0.3.0; Phase 3 cleanup delivery was 0.3.2 / 0.3.2.0, API 15. Latest plugin version/scope is recorded in [README](../README.md) and [Phase 4](phase-4.md).

## Implemented modules and evidence

| Milestone | Tool IDs / source | Support boundary |
| --- | --- | --- |
| 3A | `content.fates`: public IFateTable/IFate | Bounded indexed observations; ID/name/state/radius filtering, state/progress/level/position/radius and supported time fields. Missing/unreadable entries stay unknown/partial. |
| 3A | `objects.casts`: public IBattleChara | Bounded indexed object scan or selected-target observation. Action-type ID is retained separately; only action type 1 resolves against Action sheet. No cast-target/owner/entity IDs read. |
| 3B | `content.bozja.events`, `content.occult.events` | FATE observations in territories 920/975 and 1252 respectively; other contexts return UnsupportedContext. The absence of FATE rows does not imply absence of CEs. |
| 3C | `world.placements`: installed territory planevent/planmap assets | Static EventObject/EventRange/EventNPC/BattleNPC/Treasure placement evidence only. Uses a bounded common LGB1 decoder, not native layout traversal. |
| 3C | `world.collision` | Static CollisionBox transform, raw shape and enabled flag. Scale/rotation are not converted into an unproven AABB; no active collision/navigation assertions. |
| 3D | `content.familiars.data`: typed XBMPet/XBMItem/XBMContent | Explicit fields retain installed schema names, including UnknownN; no inferred ownership, roster, stat or stage meaning. |
| 3D | `content.familiars.ui` | Loaded XBM-prefixed addon metadata (name/UI ID/visibility/readiness/counts), not roster/card/stage internals. |
| 3E | `housing.schedule` | UTC calculator from a user-supplied entry-start epoch and 1–30 entry/results days each. Missing/future epoch is ScheduleUndefined. Zero-based cycle and exclusive phase end are labeled calculated, not observed. |
| 3E | `housing.ui` | Loaded Housing-prefixed addon metadata only; does not infer plot/bid/lottery state. HousingPlacement/HousingUnplacement template text is available through Sheet Row with actual language. |
| 3F | `content.duty`: typed ContentFinderCondition | Static level/item-level/queue requirements and content references. Unlock and launch-readiness fields explicitly unavailable. |
| 3F | `content.currency`: normal Currency inventory container | Exact-profile/session-authorized native container reads using the reviewed inventory structure. One required item ID; separate item/quantity/name, cap unavailable. Missing item is no observed row, not fabricated zero. |
| 3G | `content.retainer.task`: typed RetainerTask | Static task cost/level/item-level/gathering/maximum time. Optional supplied remaining seconds (0–604800) produce a labeled calculated completion UTC; no live retainer identity/timer inferred. |
| 3G | `system.process`: public Process/GC APIs | Working set/private bytes/managed heap estimate/GC counts for current process only. No IDs, handles, titles, paths, working-set trim, window moves or render controls. |

Sheet Row's allowlist additionally includes Fate, DynamicEvent, ContentFinderCondition, XBMPet, XBMItem, XBMContent, HousingPlacement, HousingUnplacement and RetainerTask. There is no broad sheet reflection or unbounded row-range/all-items dump in the live plugin.

## XBMPet schema correction — 0.3.1

User-reported XBMPet row-1 failure was reproduced against installed game data: bundled SDK type expects column hash `0BCA1C16`, installed sheet is `ED3C4185`. The error occurs during typed sheet loading, independently of job/familiar state or character login. XBMItem and XBMContent loaded successfully through their existing SDK types.

`XbmPetCurrentRow` is an original local typed reader pinned via `Sheet("XBMPet", 0xED3C4185)`. Its explicit column types/offsets were reviewed against the installed 27-column header. It reads columns 0–10 only (plus a schema-profile field), labels them `ColumnNN (type)`, and does not infer stats or ownership. Lumina continues to validate the entire sheet column hash before row access. Unknown future layouts fail closed; no global checksum settings are disabled and no SDK files are changed.

The production row reader was exercised offline on installed English/Japanese row 1 (51 rows in each sheet), including all 12 output fields and a missing-row lookup. This does not validate live familiar roster or native game state. In-game retest of Familiar Sheet Evidence and Sheet Row remains pending.

All typed sheet readers now distinguish `SheetSchemaMismatch`, `SheetLanguageUnavailable`, `SheetUnavailable`, `SheetVariantUnsupported`, and `SheetFieldReadFailed` from `RowNotFound`. Exception messages/paths are not copied into plugin results.

## Reporting cleanup — 0.3.2

- Evidence text uses `FieldText`, independent of unchanged display-name policy. A field retains up to 128 UTF-16 units after bounded single-line normalization; at most 512 source units are examined. Rune boundaries are preserved when cutting valid surrogate pairs. This does not expand report capture or retain hidden tails.
- `FieldRow.Value` is retained text, `TextShortened` records length-limit uncertainty, and optional `OriginalTextLength` records original source UTF-16 length before normalization. Unknown original lengths remain unknown. Idempotent sanitization preserves metadata.
- Text, structured rows and clipboard descriptions append a visible shortening marker. Frozen JSON carries the same facts as structured properties; text previews/exports contain the markers. `Text-shortened fields` counts retained shortened fields separately from row omissions. `Partial: False` therefore still means scan/read completion, not that every original field tail is present.
- Sheet fields and domain rows containing shortened source fields compare as **Indeterminate** (with summary count), even if retained prefixes match. Missing entries remain Unobserved when full evidence is unavailable. CompleteEvidence is false and comparison is partial; changes-only includes uncertain rows.
- Before/after diff summaries are independently capped/flagged (`BeforeShortened`/`AfterShortened`). Presentation shortening alone does not erase a known change computed from full retained source fields; inspect source captures for full retained evidence.
- Sheet/domain/comparison schemas advance to v2. Existing v1 exports cannot acquire lost tails or metadata retroactively; cross-schema comparison is rejected. Report envelope stays format 2 because changes are versioned inside existing inspector payload contracts.
- Duty QueueMaxPlayers is explicitly labeled raw/not party size; interpreted party size remains unavailable. Collision filters/labels state placement-origin radius rather than bounds overlap and omit irrelevant All/base-any inputs. Static/non-territory domain data and non-addon lifetimes show not applicable in text; genuinely missing scoped context still shows unavailable. JSON keeps nullable IDs and uses the domain kind to define applicability.
- No reader capability, native gate, integration or performance overhaul is introduced by this cleanup.

## Explicit capability deferrals

| Capability not implemented | Reason / implemented alternative | Reopening gate |
| --- | --- | --- |
| Native CE/dynamic-event runtime state and event-marker joins | Public IFateTable does not represent CE state; static DynamicEvent references cannot establish runtime associations. FATE and sheet evidence are provided separately. | Review exact native event structures, membership rules, lifetime behavior and installed layout; bounded tests plus in-game validation. |
| Loaded native LGB hierarchy/global collision traversal | The addon compatibility profile does not approve layout-manager hierarchies or continuation safety. Static installed placements/raw collision data are provided. | Separate native decoder review and safe re-resolution/cursor design; never retain pointers across updates. |
| Semantic familiar roster/party/stage/treasure interpretation | Installed XBM sheets expose mostly UnknownN fields; addon IDs/nodes alone do not prove semantic mappings. Raw typed sheet fields and addon metadata are provided. | Establish field meanings and per-addon layouts, representative captures and lifecycle validation. No roster configuration or card-selection controls. |
| Housing plot/bid/lottery observation, automatic authoritative schedule | No reviewed semantic decoder or authoritative epoch contract established. Calculator requires supplied rules; templates/metadata remain independently useful. | Verify game-owned clock/rules and field layouts; distinguish calculations from observed bid state. Native entry hooks remain excluded. |
| Duty reward/currency mapping and complete character eligibility | Reviewed ContentFinderCondition fields are requirements, not an authoritative reward/unlock/readiness contract. Currency observations remain a separate tool. | Review explicit reward relationships and supported character predicates; retain unknown factors. No duty entry. |
| Live retainer/deployable/other-character timers | RetainerTask duration is static and cannot establish a running venture. Private provider state is prohibited; public provider contracts belong to Phase 4. | Reviewed game-owned timer reader or Phase 4 allowlisted read-only provider contract. |
| Window/render introspection | No reviewed public window/render observation contract included in this slice; current-process memory metrics are implemented. | Read-only public evidence contract; no arbitrary native window handles/controls or other-process scans. |

Deferral means absent capability, not a successful result with fabricated empty/false data. UI tools are explicitly named Metadata/Calculator/Requirements where full semantic state is unavailable. No disabled "full decoder" buttons pretend these capabilities were implemented.

## Bounds, lifecycle and comparison

- Existing queue 16, result 64 KiB, history 100/1 MiB and export 2 MiB remain enforced.
- Domain scans examine up to 32 entries per step and 1,024 per capture. Typical domain results retain up to 128 rows; world tools up to 256. Source/serialization trimming marks partial output and counts omitted rows.
- Evidence rows have bounded keys/names and up to 16 explicit single-line fields. Source/build/language/territory/lifetime/limitations are detached, sanitized values. Shortening metadata consumes the existing byte budget; no limits are raised, so additional rows/fields may be byte-trimmed with omission counts.
- FATE/object wrappers are not retained; table length is checked across steps. Same-length replacement/moving events still make multi-step captures non-atomic.
- Spatial scans retain only a detached capture-start origin and re-resolve live wrappers each step. Different filter origins cannot be compared as equivalent spatial scope.
- Native currency uses the existing exact game/Dalamud/FFXIVClientStructs gate and request-frozen session authorization. Familiar/housing metadata reuses only the reviewed addon-list reader; this does not authorize a semantic native decoder.
- Metadata captures use the global addon epoch; setup/finalize invalidates captures/comparisons conservatively. Currency readiness is checked every step; unavailable containers are not empty successes.
- Static LGB decoder accepts only LGB1/LGP1 common records, up to 4 MiB raw data, 128 layers, 1,024 detached placements. Offsets/counts/floats are checked before record reads; no strings, asset references, nested records or arbitrary paths are followed. Unknown common placement types do not gain guessed semantics. Treasure base-ID decoding remains unavailable because its parent-data interpretation was not established.
- One bounded detached static-layout cache is replaced on asset/territory change and released on logout/context reset. Static source provenance includes a SHA-256 of the installed raw asset; comparisons cannot silently reuse placement-index identity across different asset bytes. Initial public game-data resource loading, hashing and common-record decoding are not preemptible; profiling limits are cooperative, not hard real-time guarantees.
- Comparison requires matching canonical inputs, schema, evidence kind/source, actual language, known build, territory/lifetime and spatial origin. Static data/calculators may compare across context generations; live evidence may not. FATE/cast keys are observations and never prove entity/event-instance continuity; missing rows remain Unobserved. Duplicate keys are ambiguous.

## Source and offline verification

Production uses installed API contracts; no source-plugin implementations are imported. Installed typed-sheet fields were inspected with the maintainer-only `tools/SchemaProbe` utility; that reflection is not included in the plugin. The live plugin still uses explicit typed readers and generated report metadata.

The static common-record layout was checked against Lumina's LayerCommon definitions. Offline installed-asset comparisons matched Lumina transforms for Bozja planevent (101 placements), Bozja planmap (394 placements), and Occult planevent (46 placements). These are **installed asset tests**, not live native/game validation. The planmap sample includes five collision records; additional checks compare reviewed EventObject/EventNPC base fields and collision shape/enabled fields.

Pure tests exercise production validation/canonicalization/filters, dispatch, bounded merging, cancellation/source changes, schema/provenance, comparison uncertainty, housing boundaries, timer limits, path derivation and malformed/capped static LGB records. Fake tool steps do not instantiate live Dalamud readers. Build/test results are recorded in [validation](validation.md).

## Live acceptance checklist — full matrix, with reported examples tracked separately

See [user-provided Phase 3 evidence](validation.md#user-provided-phase-3-live-evidence--2026-11-05). Unchecked items below include subcases beyond those examples; they do not imply the reported checks were ignored. User subsequently confirmed cleanup text/comparison labels and a positive FATE against game UI; tooltip/JSON/export and broader matrix checks are not established by pasted text reports.

- [ ] Reload 0.3.2, verify all 24 tools, v2 shortening markers/comparisons and existing Phase 2 behavior/commands/search/footer/scrolling.
- [ ] FATEs: known present/absent observations, filters/states/timing, invalid/moving table, logged out/loading/missing player.
- [ ] Casts: player/NPC/selected/no target, noncasting entries, action-type resolution, malformed/nonfinite data and no target changes.
- [ ] Bozja/Zadnor/Occult scopes: correct territory gate, FATE observations agree with UI, no inferred CE claims.
- [ ] World: known nearby static placements, planevent/planmap filters, raw collision shape/transforms, missing assets, partial caps and territory/logout cache invalidation.
- [ ] Familiar data: known/missing rows and languages, UnknownN names preserved; XBM metadata agrees with loaded/closed addons.
- [ ] Housing calculator: supplied UTC rules at entry/results/cycle boundaries; blank/future epoch unavailable. Housing templates/metadata agree without reading bid semantics.
- [ ] Duty metadata agrees with installed rows; unlock/launch/rewards remain explicitly unknown.
- [ ] Currency: known item count, missing item, unavailable container, enabled/disabled native gate and context change; no changes to currency.
- [ ] Retainer task: known/missing row, supplied zero/nonzero timer estimate, no claim of live venture state.
- [ ] Process memory metrics plausible; no process/window/render controls or exported paths/identities.
- [ ] Domain comparison: same scope/origin, differing origins/builds/languages/lifetimes, duplicate keys, partial/non-atomic evidence and evicted inputs.
- [ ] JSON/text/clipboard/preview agreement, exact frozen export, reports-folder button, cancellation/reload and byte/history bounds.
- [ ] Profile actual workloads. Accepted first-use serialization/window-draw stalls remain documented and deferred unless revisited; no performance certification is implied.

Phase 4 provider/network work is not started by this implementation. Project license is AGPL-3.0-or-later; third-party copying/attribution compatibility remains a separate distribution gate.
