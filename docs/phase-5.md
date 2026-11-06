# Phase 5 — readiness and user-requested callback observation

## Status

Development tooling/regression coverage implemented; new callback observer implemented experimentally. **Full live acceptance and distribution approval are not complete.** Project license is AGPL-3.0-or-later; no release installed, package published or source-plugin repository changed. Third-party license/attribution compatibility remains pending. Accepted first-use serialization/draw limitation remains deferred; no new optimization work hidden in this phase.

## Repeatable verification

From `D:\Coding\C#\Debug Kit` in WSL:

```bash
python3 -B tools/verify.py
```

The verifier uses the existing Windows `dotnet.exe` (override only with `--dotnet` pointing to another already-installed Windows executable). It never installs an SDK or invokes Linux dotnet. Runs its own failure-gate unit tests, locked restore, Debug/x64 warning-as-error build, default console tests, source tripwires, project/source/output manifest checks, PE-header/nonempty DLL checks, SHA-256 fingerprints and local Markdown file targets. Package locks must stay byte-identical. SDK-added output manifest fields are allowed, source fields must agree. PE signatures/fingerprints are inventory checks, not binary correctness certification. Remote links/anchors are not validated. Restore can contact configured package feeds; no plugin load, native hook, IPC or public data-service call occurs.

Success means development checks passed only. Failures exit nonzero; no license/live acceptance is inferred. No staging, publishing, dev-plugin registration or release deployment is part of this command.

Verified 0.5.0: 195/195 C# offline tests, 6/6 verifier tests, warning-free Windows Debug/x64 build, source safety checks across 35 production files, locked restore/unchanged lockfiles, manifest/artifact fingerprints and documentation file links passed. These are automated results, not the live checklist below. See [validation history](validation.md#phase-5-readiness-and-callback-observation--050).

0.5.3 replaces callback timeout/cap and consent checkboxes with manual Start/Stop, always-on integer values, and rolling newest-64 retention with dropped-count reporting. Development verification passed: 205/205 C# tests plus 6/6 verifier tests; native callback/table/copy/export behavior needs live retest. See [callback scope and numeric policy](addon-callbacks.md).

## Capability / evidence matrix (29 tools)

| Supported tool group | Evidence already supplied | Still pending / explicit limits |
| --- | --- | --- |
| Context / Nearby / Target | Positive user examples with names/kinds/context and selected Aetheryte | Broad transition/target variants and independent field agreement; bounded display names intentionally included |
| Addon List / Values / Nodes / Open Menu | Positive user examples and unavailable/lifetime cases | Experimental exact-profile native reads; generic payloads withheld; broader lifecycle/stress/UI checks |
| Sheet Row / Familiar Sheet Evidence | Positive typed/English/Japanese and XBM examples; shortening marker/comparison behavior confirmed | Explicit installed schema fields, not familiar ownership/stats/roster semantics |
| Inventory / Currency | Normal bags and currency-container examples | HQ/special currency/cap/full count agreement not certified; no retainer inventory |
| Snapshot Comparison | Compatible static examples, changed rows and shortened-source Indeterminate | Full live partial/ambiguity/eviction matrix; no entity continuity claim |
| FATEs / Casts / Bozja / Occult | Positive FATE Not Today #1476 agreed with UI; zero-match cast and unsupported-zone guard examples | Positive casts/supported-zone events remain pending; CE runtime deferred |
| World Placements / Collision | Positive static placement filtering; offline collision records | Not runtime collision/navigation; positive live collision and remaining radius cases pending |
| Familiar UI / Housing UI / Housing Calculator | Loaded metadata and supplied UTC calculator examples | No semantic roster/card/bid/lottery decoder or authoritative epoch |
| Duty / Retainer Task & Timer / Process | Static raw duty/task, supplied timer and memory examples | No duty eligibility/launch, live venture/deployable timers or leak/frame-cost attribution |
| Provider Metadata / State Query | Metadata v2 and AutoRetainer GetSuppressed=false on 4.6.2.18; newer version advisory | Other admitted getters need natural positive tests; unaudited endpoints remain excluded; no provider-control calls |
| Universalis / Housing Service | Positive world/DC/PaissaDB summaries; 0.4.2 copied inputs and HTTP 200/400 demonstrated | Consent reset, cancellation, comparison blocking and exported-byte agreement still need live checks; no freshness/availability guarantee |
| Addon Callback Log | Positive 0.5.0 types-only capture and 0.5.2 all-addon `ItemDetail`/Int -1 capture | 0.5.3 manual Start/Stop, rolling overflow/drop count, UI and export need live retest; unredacted integers may contain IDs; no string/pointer-typed values or replay |

Detailed contracts/deferrals remain in [Phase 2](phase-2.md), [Phase 3](phase-3.md), [Phase 4](phase-4.md) and [callback observation](addon-callbacks.md). No deferred capability becomes supported merely because readiness tests pass.

## Remaining live acceptance — do once and report observations

Record version/tool/inputs, expected versus observed result and whether game/provider state stayed unchanged. Pasted text proves only that text, not screenshots/tooltips/export bytes. Do not enable automation or perform purchases/actions merely to test diagnostics.

- [ ] **Load/reload/commands:** 0.5.3 DLL/Core/manifest together; `/debugkit`, `/dk`, plugin UI; command conflicts preserve the other owner; repeated reload has no duplicate callbacks or updates after disposal. Check mixed/dependency mismatch fails safely rather than installing different files automatically.
- [ ] **Consent:** network consent remains unchecked initially/reset after capture. Callback observer has Start/Stop controls and no additional checkbox; native session authorization still required/frozen per request. Integers always captured; warning visible.
- [ ] **Cancel/late completion:** start one naturally slow explicit request then Cancel. Pending requests cancel too; no later success from the old request, no provider/game controls. Begin a new request manually after cooldown; old data must not overwrite it. Do not abuse services to create delays/errors.
- [ ] **Clear/history:** clear while work is active; it should continue unless Cancel was pressed. Completed history/copy selectors remain timestamped, byte/row-limited and honest about omissions.
- [ ] **Transitions/disposal:** naturally zone/logout/reload during pending work; local token cancels, no late result or disposed-UI update. Same-territory instance changes preserve lifecycle invalidation.
- [ ] **Comparison blocking:** select two provider or network captures; expect IncompatibleSnapshots, not an invented diff. Callback captures also refuse comparison.
- [ ] **Copy/preview/export:** display and copied scope/status agree; select JSON then text preview and Export EXACT preview. Open the fixed reports folder; compare saved content with the frozen preview, not a later recapture. Confirm no overwrite and review names/positions/territory plus raw callback integers (possibly IDs) before sharing. Numeric callback warnings must survive text/JSON/export. Cancellation/disk-stall checks remain separate if not naturally exercised.
- [ ] **Callback observer:** follow [first live check](addon-callbacks.md#use--first-live-check), verify all-addon capture and optional exact filter, benign UI behavior unchanged, native gates, manual Stop preserving rows, repeat start/stop, context-change/unload cancellation, rolling newest-64/drop count and four-column table. No strings/pointer-typed values; no broad hook safety claim from one successful example.
- [ ] **UI regression:** resize/scale/scroll/collapse/minimize; left Find/navigation/footer and right inputs/action row/results remain usable; controls do not migrate into navigation or start batch captures.

Offline regression tests include no automatic work from idle/profiling/reporting, invalid requests, active+queued cancellation, clear-versus-cancel, late replacement/reload isolation, failed-task recovery, transition matrix, network history/byte bounds and provider unload preserving unknowns rather than reusing an old boolean. These do not replace the live list.

## Distribution gates — blocked until explicit decisions/evidence

| Gate | Current status | Required before distribution |
| --- | --- | --- |
| Project license | AGPL-3.0-or-later; see [LICENSE.md](../LICENSE.md) | Preserve license and include it with distributions |
| Third-party clearance | No source-plugin code/assets copied; API/dependency obligations not distribution-cleared | Review shipped dependencies, attribution and compatibility; independent feature reference is not permission to copy later |
| Live acceptance | Partial user examples only; new hook unvalidated | Record supported-tool outcomes above or explicitly narrow release scope; do not label tests unrun as passed |
| Native/hook risk | Experimental, exact-profile gated | Keep warnings/limits; validate current binary/layout/interactions or withhold unsupported native features |
| Release packaging/install/publish | Not authorized/performed | Explicit separate approval; verify actual release files/manifest/dependencies without overwriting user work |

For development only, load `D:\Coding\C#\Debug Kit\DebugKit\bin\Debug\DebugKit.dll` through Dalamud developer tooling, with `DebugKit.Core.dll` and `DebugKit.json` alongside. Other plugin repositories/configuration remain untouched. A successful Debug build or generated packaging artifact is not distribution clearance.
