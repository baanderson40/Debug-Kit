# Architecture and safety contract

## Data flow

```text
UI command/input -> validated bounded request queue
framework update -> lifetime/readiness tracking -> one capture step or start/observe explicit bounded task
public API / explicit sheet readers / gated native readers / audited IPC -> detached typed payloads
consented fixed HTTPS GET -> bounded off-thread JSON projection -> detached typed payload
detached typed payloads -> immutable result history
immutable result -> formatted display / frozen report preview -> explicit local export
```

`DalamudGameSource` coordinates live reads on framework updates, delegating explicit sheet reads to `SheetReader` and gated native reads to `Readers/Native/NativeReader`. Indexed object-table access advances an integer cursor instead of retaining an enumerator or rescanning a prefix. Wrappers are copied immediately and discarded. Nearby distance uses capture-start origin; each scan lists its observation interval and step count. Selected-target distance is unavailable if no valid player origin exists.

Catalog lookup uses explicit descriptor IDs rather than enum positions. Phase 2 jobs share typed `IToolSource` steps through an explicit `ToolJobs` registry; comparison uses retained snapshots only. The existing context/nearby capture paths and legacy JSON fields remain to preserve tested behavior. New payload types are a closed polymorphic set, sanitized/bounded before merging into history; they never serialize arbitrary runtime objects. Byte trimming applies to both legacy rows and typed payloads. A no-progress/step cap prevents broken readers from running forever.

`DiagnosticEngine` serializes queue/control/tick operations behind a short-lived lock. UI reads immutable result snapshots. A transient Content ID participates only in context comparison and is discarded on invalidation/disposal. Login/logout, territory, instance, and zone-init events invalidate captures; sampled context also detects transitions/readiness/identity replacement. A failed source read invalidates prior context and cannot masquerade as successful empty data.

Outcomes are separate from `Partial`/`NonAtomic` and context generation. A cancelled or failed attempt does not erase a last successful retained result. Result eviction can remove historical successes; UI says none retained rather than resurrecting hidden unbounded copies. Clear removes local history, not active work; cancellation is separate.

## Privacy by schema

Context fields remain numeric/boolean/enumerated/position data. Nearby rows now include in-game display names for all kinds, including players/retainers/companions, at the user's explicit request. The framework reader copies names through `ObjectNames.For`; the engine validates them again before retention. Names are capped at 128 UTF-16 code units with bounded input traversal, normalized to one line, and stripped of control/format characters and text-report separators. Unavailable names stay null; those game-object inspectors retain no raw addresses/owner/object IDs or exception text. Exceptions become fixed reason codes; version strings are reconstructed only after `Version.TryParse`.

0.5.1 adds the user's explicit exception for unredacted integer callback arguments, which may contain IDs. This is not a new identity API read or a change to other inspectors. Separate numeric consent is frozen per observation, default-off/reset after capture/navigation, checked again by the engine and disclosed in UI/text/JSON manifest/export. Signed/unsigned values are retained only under matching integer type tags and width bounds. Pointer/string/float/vector arguments remain excluded. No anonymity/ID-redaction guarantee applies to numeric callback logs.

This is a narrow field policy, not a general string-redaction library. Names are intentionally not anonymized. Future inspectors must not introduce raw strings first and assume export-time regexes will make them safe. Numeric addon fields are not automatically safe either. Any new field must have explicit evidence and privacy classification.

Data includes display names, job/level, coordinates, territory, UTC times and conditions, which can identify people and reveal activity. UI warns before copy/export. Captures are not anonymous.

## Export lifecycle

UI freezes one selected result and a versioned manifest into immutable UTF-8 bytes. Preview never recaptures state. Export file work runs on a background task, separately from explicitly requested network work. The exporter admits one operation, writes a unique create-new temporary file inside its fixed reports directory, flushes/closes it, checks cancellation, and atomically renames without overwrite. It deletes only its own temporary file on failure/cancellation. A cleanup failure may leave that narrowly named `.tmp` file; it is never reported as a successful final report.

Disposal cancels local work and export lifetime. UI has no completion continuation that can update a disposed window. Final rename is serialized with exporter disposal. Cancellation received after a completed rename does not retroactively remove a successfully created report.

## Explicit exclusions

- No controllers from existing plugins and no runtime dependency on them.
- No source-plugin logs/configuration/private state imported.
- No action/target setters, item use, menu callbacks, teleport/navigation, duty launch, provider toggles, arbitrary IPC/URLs, identity uploads or automatic external requests. Only the narrow Phase 4 reviewed boolean/fixed-GET boundaries are permitted.
- No arbitrary address input, retained raw native pointers, game-data writes or own signature scans. Unsafe reads are confined to the two reviewed native boundary files. The sole own-hook exception is the user-requested, separately consented callback observer; hook infrastructure changes executable routing but callback arguments/results are forwarded unchanged. Other tools install no own hook. Exact compatibility and request-frozen native session authorization remain required; session authorization defaults enabled per user request and remains disableable.
- No arbitrary sheet/property reflection or script/IPC console.
- No automatic capture, refresh, export, telemetry, or upload.

## Remaining limits

The 2ms budget is cooperative and applies between object reads; it cannot stop a slow getter or bound all render/serialization costs. Collection and serialized-payload caps constrain retained data, not all CLR allocations. Multi-step scans can contain slot-reuse duplicates or moving-object omissions. Compile-time public API compatibility does not establish live native-wrapper safety. In-game testing/profiling remains mandatory.

Native addon lifetime tracking begins only after an authorized matching-profile capture. It retains a global epoch counter, not addon pointers/names; any setup/finalize event conservatively invalidates multi-update addon captures and value/node/menu comparisons across that lifetime. Inventory re-resolves normal bag containers each step. Native nodes are a bounded single-step root-list observation with parent-depth/cycle guards, not recursive component introspection.

`PerformanceRecorder` is disabled by default and stores only allowlisted inspector/area keys, a first measured sample, and a bounded ring of 256 later samples per key. Synchronous scopes measure monotonic wall time and current-thread managed allocations; nested scopes overlap. Clear/toggle epochs reject in-flight stale samples. UI statistics refresh at most once per second while expanded. This instrumentation adds overhead and is not CPU/GPU/native-memory profiling or a hard-budget enforcement mechanism. Samples are session-only and never enter result payloads or reports. See [performance procedure](performance.md).

Generic addon scalar/string fields are unclassified and omitted (except bool and a reviewed small ContextMenu count). Menu text is decoded only from the reviewed bounded menu-label layout. Sheet text is public installed data from explicit typed fields. Comparison is observational: ambiguous keys are flagged, and name/base-ID equality never proves the same entity.

Report JSON and result-byte sizing use the closed `ReportJsonContext` source-generated graph, including polymorphic payloads and string enums. No implicit reflection resolver is used in these production paths. Tests disable default serializer reflection and compare generated report bytes to an explicit test-only legacy reflection oracle. Runtime initialization/JIT still requires first-use profiling; no startup warmup callback hides that cost.

Phase 3 adds a closed `DomainPayload`/`EvidenceRow` graph and explicit per-tool policies. `DomainReader` coordinates public FATE/cast observations, typed sheet/calculator/process readers and reviewed metadata/currency reads; `WorldReader` uses raw installed resources with the bounded Core `StaticLayoutParser`, not Lumina's full count-allocating LGB parser or native runtime hierarchies. Only a detached scalar capture origin and one bounded static-layout cache survive reader steps. UI still renders snapshots only. See [Phase 3 boundaries/deferrals](phase-3.md).

An installed SDK schema mismatch is not a reason to bypass column checks. XBMPet uses the reviewed local `XbmPetCurrentRow` profile (`ED3C4185`); other sheet types retain their SDK contracts. Sheet load and field-read failures map to fixed sanitized reason codes, never arbitrary exception messages. Static sheet lookup is independent of current job/familiar state.

Sheet/domain schema v2 field values use independent `FieldText` normalization and shortening metadata, not the display-name sanitizer. Limits remain 128 retained/512 examined UTF-16 units and are rune-boundary safe. Sanitization preserves shortening facts through detached source steps, display and frozen export. Capture row omissions/partial status are distinct from field shortening. Comparison v2 uses Indeterminate for shortened source text and separately flags presentation-shortened before/after summaries; old/new schemas cannot be compared. No hidden full-text baseline or tail hash is retained.

## Phase 4 boundaries

`ProviderReader` runs only through `DalamudGameSource` on the framework thread. It copies fixed-family `InstalledPlugins` metadata without manifest/config/instance access. `ProviderQueries` requires a complete metadata scan, one unambiguous loaded match and a fixed registry endpoint before allowing one typed boolean invocation. Since 0.4.1, provider version/dev metadata is advisory, not an invocation gate. `ReviewedEndpoint` identifies the reviewed getter in the registry, not certification of the installed provider; separate reviewed-source-version and version-evidence fields disclose match/difference/unknown. Endpoint presence and responding/readiness remain distinct; `HasFunction` does not independently prove signature or ownership, and invocation exceptions are sanitized. Successful bool establishes only that this invocation returned bool, not unchanged semantics or no side effects in an altered installed implementation. Post-call metadata changes discard the value. Same-version reload continuity and binary/source correspondence cannot be certified. Native version/hash gating is unchanged.

`IAsyncToolSource` is a separate explicit path for the two network inspectors. Each engine work item owns one task/token; framework updates only inspect `IsCompleted` and consume an already-completed result. Waiting frames do not consume the synchronous 128-step/no-progress budget or imply multiple read steps. Context tracking continues while waiting. Finish/cancel/invalidation/disposal cancels only that local token and detaches from the task; no completion continuation can retain a late result. A monotonic 15-second watchdog ends even a nonresponsive source. `NetworkCaptureClient` applies its own 10-second request timeout, one-request gate, global monotonic spacing/cooldown and streamed size cap. HTTP/JSON code runs off-thread with frozen public inputs only, never game/provider wrappers.

The HTTP handler prohibits redirects, cookies, proxy/default credentials and compression; requests use only GET and the fixed `NetworkPolicy` hosts/routes. No request runs during startup, metadata capture, drawing, profiling or export. `NetworkDecoder` projects only reviewed numeric/null fields from a 128KiB/depth-16 response, then discards the document/body. Unknown server text/error bodies are never copied into reports. Payload discriminators remain `provider`, `providerQuery`, `network`; provider metadata/query tool schemas are v2 as of 0.4.1, network v2 as of 0.4.2, envelope 2. Other tool schemas remain unchanged. These observations deliberately refuse comparisons. Async network worker time/allocations are not measured by synchronous current-thread profiling. See [Phase 4 audit](phase-4.md).

Network captures carry an optional numeric `HttpStatusCode` through `ToolStep` -> bounded engine work -> `CaptureResult`, independent of whether a payload exists. The engine accepts only 100–599 for network inspectors, never other tools. HTTP response status is retained for successes, HTTP failures/429 and later body/decode failures; absent/unobserved status remains null and is omitted from JSON. Local cooldown, pre-response failure or detached late completion never fabricate a response status. Report text obtains request scope from frozen `CaptureResult.ToolOptions`, not current UI inputs or response payload, so failure/cancellation reports still identify the intended public query. Raw error body, reason phrase, headers and exception text remain excluded. Existing byte/row caps and frozen preview/export checks still apply.

## Bounded addon callback observer

0.5.0 introduced the observer; 0.5.1 added integer values; 0.5.2 added all-addon mode; 0.5.3 advances `addons.callbacks` to v4 for explicit Start/Stop. No duration or event-count auto-stop. A rolling buffer retains newest 64 events and counts dropped older rows. Stop requests are thread-safe signals; hook disable and result completion occur on framework updates. Context invalidation/disposal cancel and discard active capture. Each event retains its validated addon name from the fixed 32-byte native field. `ICallbackToolSource` starts the fixed native observer on the framework thread; its task completes after Stop, not through worker-thread game reads. The engine's per-work async task/token serves either explicit network work or one callback session; callback sessions have no watchdog timeout. Waiting frames are not reader steps. Callback results are explicitly non-atomic.

`AddonCallbackObserver` alone may use `IGameInteropProvider` to install the generated ClientStructs callback signature/address after exact compatibility and native session authorization. No scanner or arbitrary dispatch. The original is invoked once with unchanged parameters/result, outside observer exception handling. A rolling bounded snapshot retains types, booleans and unredacted integer arguments, never raw pointer fields or strings. Stop finalizes latest 64 entries; older entries and read failures are counted. The four-column table and copied text share Core formatting; boolean flag/return render 1/0 without guessing native flag semantics. Numeric privacy warnings persist in reports. Native hook correctness is not established by managed tests or source tripwires. See [callback scope/risks/live checks](addon-callbacks.md) and [Phase 5 readiness gates](phase-5.md).
