# Debug Kit

Standalone Dalamud diagnostics. Version **0.5.3** contains **39 tools**: read-only game/domain inspectors, typed sheet lookups, public plugin inventory, explicit network summaries, and bounded manual observers. Project license: [AGPL-3.0-or-later](LICENSE.md). No required companion plugins, adapters, provider controls, callback replay, uploads or GBR-derived code. Callback observation uses one experimental native hook; other tools do not install it.

## Use

Open `/debugkit` or `/dk` (or Debug Kit's main/config UI through Dalamud if those commands are occupied). Each command registers only when available and is removed only if still owned by Debug Kit.

See the [tool-by-tool guides](docs/tools/README.md) for existing tools and the [Useful Scripts migration guide](docs/useful-scripts.md) for the ten added ports, usage, and limits.

Left pane contains Find tool search and scrolling section/tool navigation, with collapsible Debug Kit details pinned at the bottom. Expanded details scroll separately if space is tight. Right pane contains the selected tool's description, inputs, actions, status, result history, and copy/export controls. Resize the divider as needed.

1. Select a tool and enter its inputs. Native tools require an exact matching compatibility profile. Experimental native reads default enabled for the session; uncheck the session checkbox to disable them. It does not reset after capture; no capture runs automatically.
2. Press **Capture** to run only the selected snapshot tool. Game/provider reads execute on framework updates, never during drawing. Network tools require separate consent for each capture and run HTTP/JSON work off-thread. Callback logging and the three observers use dedicated **Start / Stop** controls with bounded rolling histories. Capture, Cancel, Clear results, and (for Nearby Objects) Save radius share one toolbar row; no multi-tool batch action is exposed.
3. Inspect timestamped retained results. Context changes cancel work and mark historical results stale. Loading/missing reads are not successful zero values.
4. Copy a result, or preview selected JSON/text and explicitly **Export EXACT preview**. **Open reports folder** beside export opens the fixed local destination (creating it if absent). Display names remain included.

Captures do not control movement, targets, actions, inventory, duties, other plugins, or their settings. Cancellation stops only Debug Kit's diagnostic work. Saving the radius affects only Debug Kit preferences.

### Privacy

Nearby Objects includes in-game display names for **all object kinds**, including players, retainers and companions, as requested. Names are sanitized to one line and capped at 128 UTF-16 code units. Missing/unreadable names show as unavailable without discarding the object. Names appear in displayed, copied and exported results.

Chat, pointer-typed arguments/address fields, arbitrary exception payloads, and filesystem paths are not captured. Character/object/owner IDs remain omitted by other inspectors. **Callback logging is the exception:** unredacted integer arguments may contain those or other IDs, included in display/copy/export; the UI warns before capture. This does not authorize new identity API reads. Character Content ID API is still read transiently only to detect context replacement; that API value never enters results or logs. Player/unknown-category base IDs remain omitted.

**Names, positions, territory, decoded SelectYesno prompt/button text, and raw numeric callback arguments (possibly IDs) remain in reports. Review before sharing.** Export filtering is not anonymity or name/ID redaction. Game-build provenance uses the bounded `ffxivgame.ver` file beside the running executable (an installation stamp, not independent proof of the running binary). Unavailable provenance stays unavailable; plugin/Dalamud/FFXIVClientStructs version and hash metadata are included.

Reports go only to Debug Kit's Dalamud configuration directory under `reports/`. Files are uniquely named, never overwritten, and never uploaded. Preview and exported content are the same frozen bytes. A stale-context preview remains a historical report, not a fresh capture.

### Limits

- At most 16 queued requests; same-input duplicates rejected while pending/active.
- Nearby scan: 1–200y radius (default 60), 32 table entries/update, 1,024 examined entries/request, 128 retained rows.
- Cooperative 2ms read-step budget, checked between entries; cannot preempt a slow getter. Serialization, UI rendering, and context checks are not covered by a hard real-time guarantee.
- 100 retained results, 64 KiB serialized payload/result, 1 MiB retained session payload. Collection caps also limit intermediate allocations; these numbers are not a bound on total CLR process memory.
- Export max 2 MiB; one export in flight. UI exports the selected result, not an unbounded whole-session dump.
- Multi-update scans are explicitly **non-atomic**. Object slot reuse/movement can produce omissions or duplicates. Row/entry/byte caps produce visible partial results, not completeness claims.
- Nearby report schema v3 adds display names and retains distinct **empty**, **invalid**, **filtered**, and **unreadable** counts. Empty means no object returned; invalid covers invalid wrappers, coordinates or categories; filtered means a valid object failed the radius/kind/base-ID filter. Their sum plus matched equals examined. JSON retains `Skipped` as the empty + invalid + filtered aggregate.
- Rows retain the original SDK object-kind numeric value and an allowlisted enum label separately from Debug Kit's broad category. `Other` means the broad category isn't mapped; `object kind GatheringPoint (6)`, for example, identifies the underlying kind. Unrecognized future SDK labels become `Unknown` while preserving their numeric value. Base IDs for `Other`/player rows remain omitted even when display names are included.
- No automatic capture/refresh loop. Only minimal lifetime/readiness tracking runs between requested captures.

## Phase 2 tools and limits

| Tool | Inputs / behavior |
| --- | --- |
| Selected Target | Current selected object, name/kind/base ID, position/distance, targetability, rotation/hitbox, supported HP/job/level, and up to 80 status IDs/localized names/reported remaining times. Status reads distinguish empty, unsupported, and failed; source-object IDs excluded. No target is `NoTarget`, not an empty success. |
| Addon List | Loaded/visible/focused scope, optional addon-name filter. Up to 256 rows, 32 examined entries/step. Native addon ID is not the `GetAddonByName` instance index. Structured-row shortcuts navigate to Values/Nodes but do not capture. |
| Addon Values | Addon name, instance index, start/count up to 128 values. Types reported; booleans, reviewed ContextMenu count, and `SelectYesno` prompt/button strings at slots 0–3 decoded. Dynamic text is included in reports; review before sharing. **Other unclassified numbers/strings/pointers are omitted.** |
| Addon Nodes | Addon/instance, optional node ID, parent depth 1–8. One bounded step, max 256 root ULD list entries, cycle/depth/allocation checks. Component internals/arbitrary node text omitted. Partial output is explicit. |
| Open Context Menu | An already-open ContextMenu, max 64 entry labels. Never opens/clicks the menu. Enabled state and action mapping remain explicitly unavailable/unresolved. No permanent capture-next-menu observer. |
| Sheet Row | One row from Item, Action, Mount, TerritoryType, BNpcName, ENpcResident or EObj; optional language. Explicit field readers and actual language provenance; references shown as IDs, never automatically followed. EObj exposes data/pop type, not the distinct EObjName sheet. |
| Inventory Slots | Normal bags 1–4, optional item/HQ filter and empty-slot display. 32 operations/step, max 256 retained rows. Unloaded containers remain unavailable and invalidate completeness; no retainer/saddlebag traversal. |
| Snapshot Comparison | Two compatible retained captures; selector labels show captured search inputs, UTC time, outcome and short request ID. Optional changed-only view. Schema/input/context/build/language/UI-lifetime mismatch blocks comparison. Duplicate keys are ambiguous; partial/non-atomic observations cannot establish removals. Max 256 diff keys. No copied hidden baseline history. |

See [Phase 2 details and validation](docs/phase-2.md). Reports use envelope format 2 with explicitly typed payloads; legacy phase-1 fields remain for compatibility, while human-readable output is tool-specific.

### Experimental native readers

Addon and inventory tools are **not live-certified by the coding agent**. User-provided successful checks are recorded in validation docs, not treated as general memory-safety certification. The session native-read checkbox defaults enabled per user request; it can be disabled and does not reset after queueing. Every request freezes its authorization and must still match this exact source-reviewed baseline:

- Installation stamp `2026.10.16.0000.0000`.
- Dalamud `15.0.3.9`.
- FFXIVClientStructs SHA-256 `28488f18ad05fdcd12373a83756eab275c291b09c6af061ef7f846aa06a36e29`.

Missing/mismatched evidence disables native reads with `UnsupportedBuild`; there is no config bypass. Matching only permits explicit experimental captures—it is not a native-memory safety certification. Disabling the session checkbox blocks new native captures with `NativeOptInRequired`; use Cancel to stop already-queued work. Game Context, Nearby Objects, Selected Target and Sheet Row do not require native authorization. Never change the profile simply to silence a mismatch; re-review and validate layouts first.

## Performance profiling

Expand **Performance profiling** in the right pane and enable **Collect timings and managed allocations (session)**. Off by default; does not trigger captures or exports. **Clear profiling samples** resets the baseline; **Copy profiling summary** copies bounded session statistics. Samples remain separate from diagnostic reports.

Per-tool measurements separate context reads, reader/job calls, framework steps, JSON serialization, text formatting and preview preparation; window drawing is aggregate. They report first measured operation plus warmed median/p95/max time and allocation bytes over the latest 256 later operations. These are synchronous wall-clock measurements, not CPU time or guaranteed cold-load measurements. Nested scopes overlap; never add them. Managed allocation counters exclude native allocations and other threads. See [profiling procedure and unverified measurements](docs/performance.md).

## Phase 3 domain evidence

| Tool | Evidence / limits |
| --- | --- |
| Active FATEs | Public FATE table, optional ID/name/state/radius filters, observed progress/position/timing. |
| Observed Casts | Public character cast state; optional action/name/radius and selected-target filter. No cast-target identity retained. |
| Bozjan / Occult Event Evidence | Territory-scoped FATE evidence for Bozja/Zadnor (920/975) and Occult Crescent (1252). **Not CE runtime state.** |
| Static World Placements | Installed planevent/planmap LGB1 common placements; event objects/ranges, NPCs and treasure. Not runtime instances. |
| Static Collision Bounds | Installed collision-box transforms/raw shapes, not proven world AABBs, active collision or navigation. Radius checks placement origin distance, not bounds overlap. |
| Familiar Sheet Evidence | XBMPet, XBMItem or XBMContent row with explicit fields; raw `ColumnNN`/`UnknownN` labels, not guessed stats. XBMPet uses reviewed schema `ED3C4185`; no familiar/job required. |
| Familiar / Housing UI Metadata | Loaded XBM/Housing-prefixed addon metadata through the existing gated native reader. **No roster/card/stage/bid semantic decoding.** |
| Housing Schedule Calculator | User-supplied UTC entry-start epoch and entry/results-day rules. No default epoch or authoritative lottery state assumed. |
| Duty Requirements | ContentFinderCondition raw requirement fields. `QueueMaxPlayers` is raw data, not interpreted party size; interpreted party size/unlock/reward mapping/launch readiness unavailable. |
| Observed Currency | Gated normal Currency container scan for one item ID (default Gil/1). Special currencies/caps unavailable; missing row is not zero. |
| Retainer Task & Timer | RetainerTask sheet requirements/maximum duration plus optional user-supplied remaining seconds. **Not a live venture/deployable timer.** |
| Process Memory | Current-process working set/private bytes, managed heap estimate and GC counts. No paths, handles or process controls. |

Most domain tools retain up to 128 rows; world tools up to 256. Scans examine at most 32 entries per step and 1,024 per request. Spatial filters freeze capture-start origin; comparing different origins is rejected. Static LGB decoding is capped at 4 MiB, 128 layers and 1,024 decoded common placements; no names/asset paths/nested payload traversal. Initial game-data loading is not preemptible.

Sheet Row now also supports Fate, DynamicEvent, ContentFinderCondition, XBMPet, XBMItem, XBMContent, HousingPlacement, HousingUnplacement and RetainerTask. Current sheet/domain/comparison schemas are **v2**, within unchanged report envelope format 2; context v1/nearby v3 and other inspector schemas remain unchanged.

0.3.1 fixes the installed SDK's stale XBMPet type by using a local, explicitly hash-validated current row profile. No checksum bypass or global SDK changes. Sheet schema/language/missing-sheet/variant/field failures now have distinct sanitized reason codes rather than a generic `SourceUnavailable`.

0.3.2 makes sheet/evidence field shortening explicit. Retained values remain capped at 128 UTF-16 units; JSON records `TextShortened` and, when known, `OriginalTextLength`. Text/table/clipboard output adds `[text shortened; source N UTF-16 units]`. `Text-shortened fields` is separate from omitted rows: capture `Partial` still describes row/read completeness, not text completeness. Display-name policy is unchanged. Comparisons classify rows/fields with shortened source text as **Indeterminate**, never definitive unchanged full text; before/after summary shortening is also marked. See [cleanup details](docs/phase-3.md#reporting-cleanup--032).

## Phase 4 optional diagnostics

| Tool | Supported scope |
| --- | --- |
| Provider Metadata | Nine fixed public internal-name families. Installed/loaded/version/contract support are separate from unknown endpoint/responding/ready states. Metadata capture never invokes IPC. Rotation Solver variants share `RotationSolver`; variant identity is not inferred. |
| Provider State Query | One fixed zero-argument `bool` endpoint per admitted provider: AutoRetainer suppressed; Lifestream busy; vnavmesh mesh ready; Wrath Combo IPC ready. Metadata version differences/dev flags do **not** block. Reports separately show reviewed source version and advisory match/difference/unknown evidence; runtime presence and typed invocation determine whether a response is available. Busy/suppressed are not readiness; successful bool does not certify installed code semantics. |
| Universalis Summary | One user-supplied item and public world ID or fixed DC choice. Cached NQ/HQ price/velocity summaries and qualified upload-age evidence; missing fields remain unavailable. No individual listings or seller identities. |
| Housing Service Summary | User-supplied public world ID/district; PaissaDB reported open-plot count and oldest/newest observation ages. Open does **not** mean currently purchasable/enterable; no plot controls or authoritative lottery claims. |

Network tools disclose exact service and sent public inputs before consent. **Services receive your IP address.** No game-derived world/location, names, character IDs, credentials or reports are sent. One GET per explicit capture; no redirects, retries, automatic refresh, subscriptions, authentication or local cache. Global spacing is 10 seconds; timeout 10 seconds plus a 15-second engine watchdog; body cap 128 KiB. HTTP 429 imposes at least 60 seconds of cooldown and honors a longer Retry-After. Cancel/context replacement/disposal discards late results.

Provider/network snapshots support display/copy/frozen export but **not comparison**; provider reload continuity and network freshness are not proven. No retainer/deployable timer endpoint met the reviewed current-scope/privacy/read-only requirements. AutoRetainer's additional-data getter can create provider configuration entries, so it is excluded. AutoDuty, BossMod variants, Rotation Solver and WahTools remain metadata-only, not implied ready or unsupported forever. See [audit, staged scope and live checklist](docs/phase-4.md).

0.4.1 replaces provider version blocking with advisory provenance at the user's request. Provider metadata/query schemas are v2 (`ReviewedEndpoint`, `ReviewedSourceVersion`, `VersionEvidence`); report envelope stays 2. Fixed endpoint allowlist, loaded/unambiguous/complete-metadata checks, sanitized errors and no-controls boundary remain. This does not broaden the endpoint set or change experimental native compatibility gates.

0.4.2 fixes network report context: displayed/copied/text-exported results always show frozen item/world/DC or world/district inputs, including failures without a payload. Numeric `HTTP status` is shown when a response was received; no response status retained stays unavailable, not a guessed code. JSON adds optional result-level `HttpStatusCode` (100–599), omitted when unknown. Network tool schemas advance to v2; provider schemas stay v2 and envelope 2. No raw error body/header text, new request capability or automatic retry is introduced.

See [Phase 3 implementation, deferrals and live checklist](docs/phase-3.md). CE runtime/layout readers, semantic familiar/housing decoders, duty reward/eligibility inference, live retainer/deployable timers and window/render introspection are **explicitly deferred**, not silently replaced by claims of full semantic coverage. New tools still require live validation. Existing first-use serialization/UI timing limitations remain accepted/deferred pending user-reported problems.

## Addon callback observation

**Addon Callback Log** (`addons.callbacks` v4): leave **Addon filter** blank to capture all addons, or enter one exact addon name. Press **Start capture**, interact normally, then **Stop capture**. No timer or consent checkboxes. Newest **64 events** remain in a rolling buffer; older events are dropped and counted, and capture continues until Stop. Each event names its addon in the **Addon | Values | Raw flag | Return** table; integers always show typed values (`0 [Int]: 23`, `1 [Int]: -1`, `1 [UInt]: 0`). This is a non-atomic observation, not a live unbounded console.

Boolean values/types/timestamps/counts and raw boolean flag/return are retained. Int/UInt/Int64/UInt64 values retain exact signedness and full width; **unredacted integers may contain IDs and are included in display/copy/export**. A warning remains visible before and after capture. Other float/string/vector/pointer-typed values stay omitted. Flag/return display as 1/0; flag meaning is not guessed as “update visibility.” No replay or setup/list-handler/addon-text hooks. An original implementation inspired by the requested [SimpleTweaks AddonDebug reference](https://github.com/Caraxi/SimpleTweaksPlugin/blob/main/Debugging/AddonDebug.cs), not a code port.

**Native hooks change executable routing and can conflict with other hooks or crash the game.** This observer requires the existing exact game/Dalamud/ClientStructs profile and native session authorization. Matching is not a safety certification. No hook is created on startup; Stop ends observation, while context change/disposal cancels it. See [callback boundary and live test](docs/addon-callbacks.md).

## Phase 5 readiness

Repeatable verification from WSL:

```bash
python3 -B tools/verify.py
```

Uses existing Windows .NET only: verifier self-tests, locked restore, Debug/x64 build treating warnings as errors, console tests, safety tripwires, manifest/version/PE-artifact fingerprints, unchanged lockfiles and local documentation file links. Restore may contact configured package feeds; no game/IPC/public-data-service requests, installation, publishing or release approval. See [capability matrix, remaining live checks and distribution gates](docs/phase-5.md). Known accepted performance limitation remains deferred.

Selected read-only diagnostics ported from the Useful Scripts set are mapped in [Useful Scripts migration](docs/useful-scripts.md); live-game validation and third-party attribution/compatibility review remain pending.

## Build

Requires Windows .NET 10 SDK, x64, and Dalamud API 15 development assemblies. Tested compilation baseline: SDK `10.0.403`, Dalamud `15.0.3.9`; this is not a live compatibility guarantee.

From this directory in WSL:

```bash
"/mnt/c/Program Files/dotnet/dotnet.exe" build DebugKit.slnx -c Debug -p:Platform=x64 --nologo
"/mnt/c/Program Files/dotnet/dotnet.exe" run --project DebugKit.Tests/DebugKit.Tests.csproj -c Debug --no-build
python3 -B tools/check_safety.py
```

From Windows use `dotnet` instead of the absolute executable. The SDK defaults to `%APPDATA%\XIVLauncher\addon\Hooks\dev`; use `DALAMUD_HOME` to override its assembly directory. Preserve generated `packages.lock.json` files. Reproducible verification after initial restore can use `dotnet restore DebugKit.slnx --locked-mode` followed by build with `--no-restore`.

Development output: `DebugKit/bin/Debug/DebugKit.dll`, `DebugKit.Core.dll`, and `DebugKit.json`. Load the development DLL through Dalamud's developer-plugin tooling. Dalamud supplies FFXIVClientStructs; no companion plugin installation is required.

Full development DLL path: `D:\Coding\C#\Debug Kit\DebugKit\bin\Debug\DebugKit.dll`. Keep Core/manifest beside it; reload through Dalamud developer-plugin tooling after a build. No release installation is performed by the verifier.

Tests are a **console assertion runner** referencing production `DebugKit.Core` and the detached menu decoder against installed Lumina, not a `dotnet test` project and not live game/UI tests. See [validation](docs/validation.md) for boundaries and pending in-game checks.

## Structure

- `DebugKit`: composition/lifecycle, public API/typed sheet readers, gated native readers, preferences, ImGui window.
- `DebugKit.Core`: typed schemas, explicit tool registry, coordinator, bounds, comparison, reports/export; no Dalamud dependency.
- `DebugKit.Tests`: fake reader/context/object/clock tests of production Core; no game-memory dereferences.
- `tools/check_safety.py`: source-level tripwires; not a formal proof of read-only behavior.
- `tools/verify.py`, `tools/test_verify.py`: repeatable development verification and failure-gate self-tests; not live/release certification.

Existing plugin debug windows remain untouched. Unreviewed native semantic decoders and unaudited provider contracts remain deferred; automation remains prohibited. See [architecture](docs/architecture.md).

## Source and distribution

Debug Kit is licensed under AGPL-3.0-or-later; see [LICENSE.md](LICENSE.md). These native implementations use public API contracts and do not copy code/assets from the reviewed plugin repositories. This project license does not grant rights to third-party code; verify attribution and compatibility before importing or distributing it. No release or publication is performed by this task.
