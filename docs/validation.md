# Validation history and outstanding live checks

## Recorded environment

- Windows SDK: .NET `10.0.403`; Windows/x64, invoked from WSL.
- Dalamud: assembly/file version `15.0.3.9` in existing XIVLauncher development directory.
- Plugin SDK: `Dalamud.NET.Sdk/15.0.0`.
- Live FFXIV/Dalamud execution: **not performed by the coding agent**.

## Automated checks

Recorded 2026-11-05: Debug solution build passed with **0 warnings / 0 errors**; **43/43** console tests passed; source safety tripwires passed. Verified `DebugKit.dll`, `DebugKit.Core.dll`, and API-15 `DebugKit.json` in `DebugKit/bin/Debug`. These results do not complete the pending in-game checklist below.

Patch 0.1.1: Debug build passed with **0 warnings / 0 errors**, **48/48** console tests passed, and source safety tripwires passed. Added regression coverage for distinct empty/invalid/filtered/unreadable counts, accounting through partial/cancelled captures, original SDK kind labels/numbers with safe unknown fallback, and nearby report schema v2 JSON/text. These patches have not yet been revalidated in-game.

UI 0.1.2: left pane is section/tool navigation only; all inputs, search, actions, status, results and export controls moved into the right pane. Source checks guard navigation-only structure. No game-capture logic changed. Visual layout, scrolling, resizing and navigation during an active export still require in-game checks.

0.1.3 adds nearby schema v3 in-game display names for all object kinds, including player/retainer/companion names, per explicit user request. Names are bounded/single-line and included in copy/JSON/text export; raw identity IDs remain omitted. Debug build passed with 0 warnings/errors; 52/52 offline tests and safety tripwires passed. Development DLL/Core dependency/API-15 manifest 0.1.3.0 verified. In-game name matching remains pending.

User-provided 0.1.x reports subsequently confirmed successful context/nearby captures, original-kind labels, reconciled counters, player/NPC display names and stale-history labeling. Those reports do not verify cancellation, export, native decoding or performance budgets.

0.2.0 implements Phase 2 shared inspectors and typed comparison/report infrastructure. Native readers are exact-build gated and remain experimental, not live-certified. Original per-capture opt-in was subsequently replaced at the user's request: session authorization defaults enabled, is manually disableable, and no longer resets after capture. Queued requests freeze authorization. See [Phase 2 details and live checklist](phase-2.md). Build/offline/source/output verification must be recorded separately from live validation.

Final 0.2.0 checks on 2026-11-05: Windows .NET Debug/x64 solution build passed with **0 warnings / 0 errors**; **90/90 offline production-Core tests passed**; source safety tripwires passed across 16 C# files. Locked-mode solution restore passed. Verified nonempty `DebugKit.dll`, `DebugKit.Core.dll`, and manifest together in `D:\Coding\C#\Debug Kit\DebugKit\bin\Debug`; source/output manifests both report `0.2.0.0`, Dalamud API 15. README/docs local links and manifest JSON parsed successfully. These checks include the final addon-ID, node-allocation, comparison-selection and FFXIVClientStructs-provenance changes. No agent-run live game/UI/native validation was performed; Phase 2 live checklist remains pending.

Build the solution, run console tests, then run the source safety tripwires using commands in `README.md`. Test runner reports the actual assertion count and exits nonzero on failure. Tests reference production Core code and compile the production detached menu decoder against installed Lumina; no copied policy implementation and no live game/Dalamud loading required.

Covered: validated queue/deduplication/overflow, context unknowns/loading/player absence, bounded indexed capture, radius/kind/base filtering, finite coordinates, row/entry/time budgets, disappeared/unreadable objects, character/territory/context invalidation, mid-step changes, cancellation, last-good retention, count/byte eviction, strict schemas, exception/version privacy, UTC clock jumps versus monotonic duration, JSON/text, exact-preview export, nonoverwrite, pre-cancelled/failed/oversized exports, and disposed execution/export.

Source tripwires detect forbidden APIs/identity reads outside the designated boundary and unsafe dependency additions. They are regression aids, not complete static analysis or proof that game wrappers are safe.

No claim of live plugin construction, command ownership behavior, ImGui correctness, export cancellation under real disk stalls, or measured frame/memory performance is made by these offline tests.

Menu-label correction on 2026-11-05: user-provided menu capture exposed SeString formatting bytes being decoded as plain UTF-8. Production decoder now validates bounded detached SeString payloads, strips reviewed presentation macros, rejects invalid UTF-8/unsupported dynamic macros, and marks undecoded labels partial. Debug/x64 build passed with **0 warnings / 0 errors**, **93/93 offline Core/menu-decoder tests passed**, and source tripwires passed across 18 C# files. DLL/Core/manifest outputs verified together (0.2.0.0, API 15). Subsequent user-provided corrected-label capture is recorded below; broad native correctness is not inferred.

## User-provided Phase 2 live evidence — 2026-11-05

These are pasted reports/screenshots from the user, not agent-run tests or independent field-by-field game-memory verification.

| Area | Demonstrated | Still unverified |
| --- | --- | --- |
| Target | Aetheryte selected-target capture, native kind 5, position/distance and targetability reported | Player/NPC/absent target variants; numeric field accuracy; new unavailable-field formatting in game |
| Addon lists | Loaded: 124 examined/retained; Visible: 124 examined = 38 retained + 86 filtered; one-entry capture consistent with focused scope | Inputs were missing from pasted list text; full focused/name-filter coverage and worst-case lists |
| Values | Character instance 1, indices 0–63 retained; bool decoded, other fields explicitly withheld | Range edge cases, missing addon, broader layouts |
| Nodes | Character 83 nodes; node-1 filter: 83 examined = 1 retained + 82 filtered; closed Character returns AddonUnavailable | Allocation/cycle/depth limit stress in game |
| Lifetimes | Same-generation, same-node-filter captures showed epoch 2 then 6 after reported close/reopen; subsequent comparison rejected as incompatible | Epoch is global; report omits rejected comparison source IDs, so rejection attribution depends on user selection |
| Menu | Corrected capture: 13 readable labels, no UTF-8 payload garbage; retained private-use icon characters; closed menu returns AddonUnready and no rows | Independent label-by-label agreement, malformed/dynamic macro cases and widest menu performance |
| Sheet | Item row 1 English Gil and Japanese ギル, other six fields matching; missing-row request returns RowNotFound | Other six sheets, no-language data and independent field verification |
| Inventory | All normal bags: 140 examined = 61 occupied + 79 empty; Bag1/item 10373: 35 examined = 1 retained + 34 filtered, quantity 87; LoggedOut blocks with no rows | HQ/NQ, include-empty, unavailable-container and changing-inventory stress cases |
| Comparison | Cross-language pair rejected; same Japanese Gil data: 7 unchanged; root-node comparison: 1 Changed, coordinates 1113,350 to 590,178 with same dimensions | Full unchanged-row view, eviction/ambiguity/partial evidence in game |
| Export UI | Screenshot shows Export: Success and frozen JSON preview of 1,954 bytes | Saved bytes versus frozen preview; nonoverwrite/cancellation under disk stalls; Open reports folder button |
| Native control | NativeOptInRequired blocks showed zero rows; later screenshot shows session native-read checkbox enabled | Version-mismatch live tests, disable/queued-request behavior after session-default change |

Performance figures in pasted reports are capture spans, not isolated reader/CPU/frame costs. Native profile remains **ExperimentalMatched**; successful checks are not memory-safety certification. Actual live [performance profiling](performance.md), capture/export cancellation and exported-byte agreement remain outstanding.

## Phase 2 closeout implementation

Optional session profiling separates context/reader jobs/framework steps/report formatting/serialization/preview preparation/window draw, with first measured and bounded warm median/p95/max wall-time and managed-allocation statistics. It defaults off, never starts captures and never enters diagnostic report payloads. Target text now uses explicit `unavailable` for unsupported numeric fields and preserves real zeros. Regression tests cover defaults, bounds, reset/disable behavior, integration and target formatting.

Final closeout checks on 2026-11-05: Windows .NET Debug/x64 build passed with **0 warnings / 0 errors**; **97/97 offline tests passed**; source tripwires passed across 19 C# files; locked-mode restore passed. Documentation local links and manifest JSON validated. Nonempty DLL/Core/manifest outputs verified together in `D:\Coding\C#\Debug Kit\DebugKit\bin\Debug`, both manifests `0.2.0.0` / API 15. These are correctness/build checks, not live profiling measurements. Profiler UI/copy behavior, first/warm frame costs and new unavailable-field rendering still need in-game verification; no live performance numbers are fabricated.

## General in-game acceptance checklist — pending unless explicitly reported above

Generated-JSON patch on 2026-11-05: user's Character/node-1 profiler sample showed first serialization 298.381ms versus ten later samples with median 0.162ms / max 0.268ms. Production report/result-size serialization now uses generated JSON metadata and string-enum converters; no framework-callback warmup was added. **98/98 offline tests passed** with default serializer reflection disabled, including legacy-byte parity for context/nearby and all eight typed payloads. Debug/x64 build: **0 warnings / 0 errors**; source tripwires passed across 20 C# files. Post-patch live first/warm measurements remain pending; this is not a claim that the measured stall is eliminated.

- [ ] Load `DebugKit.dll` with `DebugKit.Core.dll` and manifest beside it; no companion plugins needed.
- [ ] `/debugkit`, main UI and config UI callbacks open the window.
- [ ] A command conflict leaves the existing owner intact; Debug Kit opens via its plugin UI.
- [ ] Reload/dispose removes only owned command/subscriptions; no duplicate callbacks or updates to disposed UI.
- [ ] Logged-out context shows unavailable player values; object scan is blocked.
- [ ] Logged-in context agrees with visible territory/job/level/position and selected conditions.
- [ ] Loading, zoning, same-territory instance changes and character replacement invalidate work/history properly.
- [ ] Object filters/limits are honored; disappearing objects don't crash; non-atomic/partial labels remain visible.
- [ ] Cancel diagnostic work does not control any game/provider work; clear-history does not silently cancel.
- [ ] Clipped/collapsed/minimized windows, resizing and UI scaling preserve ImGui stack balance.
- [ ] UI never queries game state during drawing; framework reads behave safely through transitions.
- [ ] Clipboard/report data includes requested in-game display names but no character/object/owner IDs, addresses, captured secrets, error payloads or paths; warnings explicitly disclose names/positions/territory.
- [ ] Preview matches exported bytes; no upload; existing file preserved; explicit export cancellation under load behaves correctly.
- [ ] Worst-case captures measured for per-update duration and retained/transient memory; adjust proposed limits if needed.
- [ ] Exceptions/unavailable services generate honest fixed-code failures, not fabricated successful zeros.

## Phase 3 implementation verification — 2026-11-05

Version 0.3.0 adds 14 supported domain-evidence tools (24 total) and nine typed sheet cases. See [capability matrix, explicit deferrals and pending live tests](phase-3.md). This is supported-scope implementation delivery, not approval of unimplemented CE/native semantic decoders or completion of live acceptance.

- Windows .NET Debug/x64 solution build: **0 warnings / 0 errors**.
- **131/131 offline tests passed**, including domain validation/dispatch/bounds/source changes/cancellation/provenance/comparison, static LGB count/offset/float/cap checks, calculator boundaries and generated JSON. Default serializer reflection remains disabled in the test runner.
- Source tripwires: **24 C# production files passed**; checks now also guard cast-target/FATE-address reads and prohibit the full allocating LGB parser in the live world reader.
- Locked-mode solution restore passed. Source/output manifests report **0.3.0.0 / API 15**; DLL/Core/manifest verified together in `D:\Coding\C#\Debug Kit\DebugKit\bin\Debug`. README/docs local links validated.
- Maintainer-only installed-data probe compared the bounded static decoder to Lumina on Bozja planevent (12,864 bytes, 101 placements, 89 base-ID rows), Bozja planmap (64,060 bytes, 394 placements, five collision records, 115 base-ID rows) and Occult planevent (8,963 bytes, 46 placements, 29 base-ID rows). All decoded transforms and reviewed accessible base/collision fields matched; no truncation in these three samples. This is **offline installed asset validation**, not live native/game/UI validation.
- No agent-run live Phase 3 validation, new native currency-container count verification, semantic CE/roster/bid decoding or frame-cost certification was performed. Native reads remain experimental/exact-profile gated. The known first-use serialization and window-draw timing limitations remain documented and deferred per user decision.

## XBMPet compatibility correction — 0.3.1, 2026-11-05

User reported Familiar Sheet Evidence/XBMPet row 1 failing with generic SourceUnavailable, including on a familiar-capable job. Offline installed-data reproduction located the failure in typed sheet loading: SDK XBMPet hash `0BCA1C16` does not match installed `ED3C4185`. It is independent of job, familiar presence and character state. XBMItem and XBMContent typed reads succeeded.

The local production `XbmPetCurrentRow` now pins the reviewed `ED3C4185` schema via Lumina's normal Sheet attribute/hash validation. It reads explicit reviewed columns 0–10 with raw ColumnNN/type labels and a schema-profile field; no checksum bypass, global SDK setting or native layout changes. Offline maintainer assertions loaded 51-row English/Japanese sheets, decoded row 1's 12 output fields, and preserved missing-row lookup behavior. Expected unsupported-language and original SDK mismatch cases remain visible in the maintainer probe, not silently bypassed.

All typed sheet readers now return specific sanitized schema/language/missing-sheet/variant/field reason codes. Build passed with **0 warnings / 0 errors**; **134/134 offline tests passed**; source tripwires passed across **26 production files** and now prohibit raw-sheet access/checksum disabling in production. DLL/Core/manifest verification and locked restore follow the same documented commands. Actual in-game retest of 0.3.1 remains pending; static sheet success does not establish roster ownership or native familiar state.

## User-provided Phase 3 live evidence — 2026-11-05

Reports below came from the user on 0.3.0/0.3.1, before the 0.3.2 reporting cleanup. They are example observations, not independent field-by-field verification or broad native safety certification.

| Area | Demonstrated evidence | Remaining caveat |
| --- | --- | --- |
| FATEs (0.3.2) | Territory 814: 2 examined = 1 matched + 1 filtered; Not Today #1476, level 78, 28.252y, state 4/progress 0, 153 seconds remaining consistent with start/duration. User confirmed game UI agreement | One positive example; broader state/transition/stress cases remain pending |
| Casts | Territory 961 scan: 819 examined = 762 empty + 57 filtered, zero matches, 26 steps, no reported unreadable entries | No positive cast row yet; short casts may be missed between observations |
| Currency | Gil/item 1: 11 examined = 1 matched + 2 empty + 8 filtered; reported quantity 572,494,365. Item 10373: 11 = 2 empty + 9 filtered, no fabricated zero | User did not independently confirm count against Gil UI; special currencies/caps remain unsupported |
| Duty | ContentFinderCondition row 1: Toto-Rak, level 24/sync 27, English actual language, unlock/launch unavailable | Raw QueueMaxPlayers=0 is not interpreted party size; 0.3.2 labels/caveat need live retest |
| World placements | Territory 961 planevent: 731 examined = 132 matched + 599 filtered; filter base 2012130: 1 matched + 730 filtered, same hash/key/position, distance 25.095y | Static layers/quest variants, not spawned objects; all byte/text counts were pre-cleanup schema v1 |
| Collision | Planmap: 184 examined/all filtered; planevent: 731 examined/all filtered; both completed without errors | No positive collision record; origin-radius matching does not test bounds overlap or prove area collision absent |
| Familiar sheets | 0.3.1 XBMPet row 1/profile ED3C4185 succeeded with offline-matching raw fields; missing row 4294967295 gave RowNotFound. XBMItem row 1 matched Belt of Constitution data; XBMContent row 1 matched numeric data and actual language None | XBMPet description was silently cut in v1; new shortening metadata still requires live retest. No roster/stage/card inference |
| Housing calculator | Blank epoch: ScheduleUndefined. Supplied Nov 1 UTC, 5/4 days: Entry cycle 0 ending Nov 6 UTC. Supplied Oct 31 UTC: Results cycle 0, Nov 5–9 UTC | Synthetic calculator rules, not authoritative lottery observations; exact boundary cases covered offline, not by these timed live examples |
| Retainer task/timer | Row 1: static 60-minute maximum, cost 1, level 1, actual language None; no timer invented when blank. Supplied 60 seconds produced completion exactly one minute after calculation | User-supplied estimate, not a live venture/deployable timer |
| Process | Current-process working-set/private/managed estimate and cumulative GC counts returned | Not Debug Kit attribution, a leak diagnosis, collection rate or frame-cost guarantee |
| Housing metadata | SourceChanged cancellation after 64 examined entries; later settled scan: 124 examined/all filtered, lifetime 5 | Global lifecycle invalidation can include unrelated addons; no positive housing UI/bid semantics tested |
| Familiar metadata | 124 examined = 2 matched + 122 filtered; XBMItemDetail and XBMBattleMonsterDetail loaded/ready but hidden, lifetime 7 | Loaded metadata does not establish owned familiar or active roster; semantic decoder still deferred |
| Territory guards | Bozja and Occult tools both returned Blocked/UnsupportedContext in territory 1189, zero examined | Positive supported-zone event/FATE captures remain pending |

Still pending: positive cast/collision and supported-zone domain event examples, remaining stress/transition scenarios, live export-content agreement/cancellation and tooltip/JSON details. Deferred native semantic capabilities remain explicitly absent. Known first-use serialization/draw timing limitations remain accepted and deferred per user request.

## Reporting cleanup — 0.3.2

Field text now records shortening separately from omitted rows, preserves metadata across sanitization, and displays consistent markers in tables/copied text/text previews/exports. JSON retains bounded values plus structured flags/source lengths. Comparison marks shortened source evidence Indeterminate, not definitive unchanged; diff summary shortening is separately flagged. Display-name policy and capture limits remain unchanged. Duty raw-party-field/collision-origin/context-applicability labels were clarified. Sheet/domain/comparison schemas are v2; report envelope remains 2, context v1/nearby v3 unchanged.

Final 0.3.2 verification on 2026-11-05: Windows .NET Debug/x64 build passed with **0 warnings / 0 errors**; **143/143 offline tests passed**; source tripwires passed across **27 production files**. Locked-mode restore passed. DLL/Core/manifest outputs verified together in `D:\Coding\C#\Debug Kit\DebugKit\bin\Debug`; source/output manifests both `0.3.2.0` / API 15. Documentation local links validated.

New exact-limit/Unicode/metadata/uncertainty/export-byte tests exercise production code, not live game/UI. Frozen JSON/text test exports matched their preview bytes. Installed XBMPet production-reader probing confirmed English row 1 Column08 now carries a shortening marker and original source length **275 UTF-16 units**; Japanese/missing-row checks still passed. New UI marker/tooltips, clipboard behavior and schema-v2 comparisons remain pending in-game retest. Reader capabilities/native gates/performance behavior were not expanded or certified by this cleanup.

Subsequent user-provided 0.3.2 live text reports confirmed XBMPet v2 `Text-shortened fields 1`/source length 275 marker; comparison `Indeterminate 1`, `Unchanged 0`, `CompleteEvidence False`; duty raw QueueMaxPlayers/not-party-size label, unavailable interpreted party size and territory/lifetime not-applicable labels. This resolves those text-report checks, not tooltip/JSON/export-byte agreement.

## Phase 4 — 0.4.0

Four new inspectors (28 total) add fixed provider metadata, four source-reviewed boolean queries, and explicit Universalis/PaissaDB summaries. Initial 0.4.0 used exact-version gates; 0.4.1 replaces those with advisory provenance as described below. Retainer/deployable provider timers and remaining provider contracts are deferred with source-specific reasons in [Phase 4 audit](phase-4.md). Existing game/native gates and accepted first-use performance limitation are unchanged.

Default tests use fake HTTP handlers and production transport/policies/decoder/engine; no real network or IPC runs in offline tests. Separate development service-shape GETs returned public Universalis/PaissaDB responses and exposed documented-vs-live median/upload-scope differences handled by the decoder. No Phase 4 in-game/provider/UI behavior is live-certified; use the [live checklist](phase-4.md#live-acceptance--pending).

0.4.0 verification on 2026-11-05: Windows .NET Debug/x64 build **0 warnings / 0 errors**; **169/169 offline tests**; safety tripwires across **33 production files**; locked restore and local documentation links passed. `DebugKit.dll`, `DebugKit.Core.dll` and `DebugKit.json` verified together in `D:\Coding\C#\Debug Kit\DebugKit\bin\Debug`; source/output manifest **0.4.0.0 / API 15**. No release installation, staging, commit or publishing performed.

### Provider version policy — 0.4.1

At the user's request, metadata version differences and dev flags no longer block fixed provider queries. `ReviewedEndpoint` identifies only the admitted source-reviewed getter; `ReviewedSourceVersion` and `VersionEvidence` disclose advisory metadata match/difference/unknown. Provider metadata/query schemas advance to v2; other schemas/envelope/native compatibility gates remain unchanged. Complete metadata, unambiguous loaded provider, fixed endpoint, runtime presence/typed invocation and sanitized failure handling remain required. No additional endpoint was admitted; successful bool does not certify installed semantics.

User-pasted 0.4.0 reports demonstrated metadata for nine families and version blocking for AutoRetainer 4.6.2.18, plus positive Universalis world/DC and PaissaDB summaries. Those are historical examples, not evidence of positive provider getters. The new policy specifically permits the supplied AutoRetainer 4.6.2.18, Lifestream 2.6.0.12 and vnavmesh 1.2.3.15 metadata versions to attempt their fixed endpoints; absent/missing/incompatible endpoints still cannot fabricate a value. No new provider/UI live test is claimed.

0.4.1 automated checks: Windows Debug/x64 build **0 warnings / 0 errors**, **171/171 offline tests**, safety tripwires across **33 production files**. Tests cover observed newer versions, unknown version/dev metadata, unchanged fixed endpoint selection, loaded/absent/ambiguous/incomplete metadata gates, missing/throwing responses and v2 text/generated JSON advisory fields. Native and network policies are unchanged.

### Network reporting cleanup — 0.4.2

Network descriptions now show frozen captured inputs independently of payload presence: item/world, item/DC, or world/district. Numeric response status is retained as optional result-level `HttpStatusCode` through the async pipeline, bounded to 100–599 and omitted from JSON when unknown. Local cooldown/no response/late completion does not invent a status. HTTP errors never retain raw response bodies, reason phrases, headers or exception text. Network tool schemas advance to v2; provider schemas stay v2, report envelope stays 2, request/consent/cancellation/rate-limit/native policies unchanged.

Windows Debug/x64 build **0 warnings / 0 errors**; **175/175 offline tests**; safety checks across **33 production files**. New tests cover success/failure scope labels for world/DC/housing, production transport status retention (redirect/error/429/200), local cooldown unknown status, numeric bounds/network-only retention, ignored late responses, generated JSON and exact JSON/text export bytes with private error body excluded. No new live UI/copy/export validation is inferred.

Locked restore and local documentation links passed; Debug DLL/Core/manifest verified together at the development path, manifest **0.4.2.0 / API 15**. No release installation, commit, staging or publishing performed.

User-provided 0.4.1 live report demonstrated AutoRetainer 4.6.2.18 `GetSuppressed` responding with false despite advisory source-version difference; later metadata v2 remained IPC-free with unknown endpoint/responding/ready states. This is one positive typed-call example, not a general semantic/no-side-effect certification.

Subsequent 0.4.2 user text reports demonstrated frozen inputs/status on both outcomes: item 5333/world 74 success with HTTP 200 and three rows, then item 30737/world 74 failure with HTTP 400 and no payload. No raw error body appeared. The failure's cause is not established by the numeric status alone. This resolves those copied-text reporting examples, not exported-byte agreement, consent reset or cancellation/late-result suppression in game.

## Phase 5 readiness and callback observation — 0.5.0

Added repeatable `tools/verify.py`, its failure-gate tests, lifecycle regression coverage, [capability/live/release matrix](phase-5.md), and the separately requested [addon callback observer](addon-callbacks.md). Callback logging is an original bounded implementation, not a port of SimpleTweaks; no source-plugin repositories/configurations were changed. It observes all addons or one exact optional filter through installed generated callback delegate/address. Generic float/string/pointer values, addresses, callback replay and reference's other hooks remain excluded. Native profile plus session authorization required; no hook on startup.

Entire new callback native/ABI/UI behavior remains live-unverified. No new memory-safety, original-forwarding correctness under real hook interactions, UI/copy/export or frame-cost certification is inferred from offline tests. Existing accepted first-use performance limitation stays deferred. Third-party license/attribution compatibility and full live acceptance remain release blockers; no publication or installation performed.

0.5.0 automated verification on 2026-11-05: **195/195 C# offline tests**, **6/6 Python verifier tests**, Windows Debug/x64 build with warnings treated as errors (**0 warnings / 0 errors**), safety tripwires across **35 production files**, locked restore with byte-unchanged package locks, source/output manifest **0.5.0.0 / API 15**, nonempty PE DLL/Core artifacts with SHA-256 fingerprints, and local documentation file-link checks passed through `python3 -B tools/verify.py`. No real IPC/native hook/public-data-service requests run in the default tests. First live callback and remaining acceptance checks are still required.

### Callback argument table and numeric consent — 0.5.1

User-provided 0.5.0 report `c7f50941-ec70-40d8-ad78-1883b3f5a4dd` demonstrated six Character callbacks over approximately five seconds, Int/UInt type tags, true raw flag/false return, no entry omissions/read failures and non-atomic labeling. The user expected actual typed values in a table rather than masked values; a supplied reference screenshot illustrated Int 23/-1 and Int 20/UInt 0. It is not a new Debug Kit numeric capture or behavioral-safety certification.

After explicit authorization to relax the no-ID policy for numeric callback arguments, 0.5.1 adds separate per-capture numeric consent and a dedicated always-visible Addon/Values/Raw flag/Return table, with shared Core formatting for text and typed Int/UInt/Int64/UInt64 fields in JSON. Values preserve negative sentinels and full 64-bit precision. Native buffer and engine frozen-request checks independently strip numbers without consent. Both permissions reset on capture/navigation; no integer identity API reads or other inspector privacy changes. Strings/floats/vectors/pointer-typed values stay excluded; no replay. Numeric-mode reports explicitly warn that unredacted integers may contain IDs and are included in copy/export; manifest no longer claims ID-free data for them. Callback schema v2, envelope 2; other schemas/native gates unchanged.

Automated build/offline validation does not establish new native integer correctness, table rendering, UI consent resets or live exported-byte agreement. These remain pending retest; third-party license/compatibility gates and accepted first-use performance limitation remain unchanged.

0.5.1 verification: **201/201 C# offline tests**, **6/6 verifier tests**, warning-as-error Windows Debug/x64 build with **0 warnings / 0 errors**, safety checks across **35 production files**, locked restore/byte-unchanged locks, source/output manifest **0.5.1.0 / API 15**, DLL/Core PE signatures/fingerprints and local documentation file links passed via `python3 -B tools/verify.py`. New tests cover typed screenshot-format values, negative/zero/32-bit/64-bit boundaries, wrong-type/out-of-range stripping, buffer and engine numeric-consent enforcement, cancellation isolation, and exact JSON/text export bytes carrying privacy warnings. No release installation or publication performed.

### All-addon callback observation — 0.5.2

User asked that addon name not be required. Blank filter now accepts every callback with a valid bounded addon name; a supplied name remains an exact ordinal filter. Each retained event includes its cleaned addon name for table/text/JSON output. Five-second duration and 64-event cap remain global; all-addon mode does not grant 64 events per addon. Callback schema advances to v3; envelope remains 2. Callback name extraction is confined to generated `AtkUnitBase.Name`'s fixed 32-byte field; no C-string scan or pointer retention. Blank filter + unknown/invalid names are accepted as `unknown` rather than silently dropped. Numeric capture still requires its separate warning/consent; all other hook and privacy boundaries remain unchanged.

0.5.2 verification: **205/205 C# offline tests**, **6/6 verifier tests**, Windows Debug/x64 warning-as-error build with **0 warnings / 0 errors**, source safety tripwires across **35 production files**, locked restore/unchanged lockfiles, source/output manifest **0.5.2.0 / API 15**, DLL/Core artifacts and local documentation links passed via `python3 -B tools/verify.py`. New tests cover blank/all/exact filtering, fixed-span addon-name decoding, per-event names through JSON/text, and the shared global 64-event buffer limit. This does not live-validate broad capture, hook callback volume, table/UI, or game behavior. No release installation or publication performed.

### Manual callback Start/Stop — 0.5.3

User requested removal of time limit and consent checkboxes; capture should continue until stopped. Callback tool now has explicit **Start capture / Stop capture**, no timer/event-count auto-stop, and always retains signed/unsigned integer values with persistent ID warning. The buffer rolls over at 64 rows, retaining newest events and counting dropped older events. Stop disables the hook on the next framework update and saves the snapshot; context change/unload still cancel and discard. Schema advances to v4; native compatibility/session authorization remains mandatory; report envelope/API remain 2/15.

Automated coverage verifies no callback timeout after simulated elapsed time, explicit stop request preserving completed payload, cancellation isolation, default typed integer retention and newest-64 overflow/drop count. Live indefinite capture, stop UI, overflow behavior and report/export agreement remain pending. No release approval inferred.

0.5.3 verification: **205/205 C# offline tests**, **6/6 verifier tests**, Windows Debug/x64 warning-as-error build (**0 warnings / 0 errors**), source safety checks across **35 production files**, locked restore/unchanged lockfiles, manifest **0.5.3.0 / API 15**, PE artifacts/fingerprints and documentation links passed via `python3 -B tools/verify.py`. This is development verification only; no live game/UI or release approval.

## Distribution gate (unchanged)

Project license is AGPL-3.0-or-later; any third-party copying/attribution requires separate review before publication. Native feature ports do not copy source-plugin code/assets. Packaging artifacts generated by the development build are not published or installed automatically.
