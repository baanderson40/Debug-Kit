# Performance profiling — procedure and evidence

## Current status

Instrumentation implemented; **broad live frame-impact and worst-case profiling remains unverified**. User-provided report spans (for example ~100ms addon listing across four updates, and ~10ms first menu capture) are observations of capture span, not isolated reader CPU cost.

User supplied profiler measurements for Character/node 1 before the generated-JSON patch (11 captures: first plus 10 later operations):

| Area | First ms / bytes | Warm median / p95 / max ms | Warm median bytes |
| --- | --- | --- | --- |
| FrameworkStep | 308.207 / 1,474,784 | 0.301 / 0.424 / 0.424 | 8,720 |
| ReaderJob | 3.207 / 6,224 | 0.055 / 0.067 / 0.067 | 4,040 |
| JsonSerialization | 298.381 / 1,462,968 | 0.162 / 0.268 / 0.268 | 1,696 |

This localizes the observed first-measured stall predominantly to synchronous serialization. Reflection metadata construction/JIT is a hypothesis, not a measured stack trace. The serialization path now uses a closed source-generated JSON context with string enums and no implicit reflection fallback; report bytes and size checks are regression-tested against the former serializer. This removes reflection-based contract discovery from production report serialization, but does not eliminate runtime initialization/JIT or prove the stall fixed. **Reload, enable profiling before any capture, and repeat first/warm measurements to assess this patch.** No initialization work was moved to another framework callback.

Idle/window samples showed low medians but occasional draw outliers (up to 18.528ms in one sample); observer/UI-command overhead and actual frame impact remain unresolved. No hard real-time guarantee is inferred from these measurements.

## Collecting measurements

1. Expand **Performance profiling** in the selected tool's right pane. Enable **Collect timings and managed allocations (session)**. This does not enqueue captures or run native readers on its own.
2. Clear profiling samples immediately before a case. Capture once and record the first measured sample; then run at least 30 repeated explicit captures with identical inputs/state to collect warm statistics. Large multi-step tools generate multiple reader/step samples per capture; sample counts are operations, not captures.
3. Use **Copy profiling summary**. Record tool inputs, actual row/entry counts, game/Dalamud/structs versions, profiler on/off, frame cap, scene and first/warm state separately. The copied summary includes only tool names/areas and metrics, not character names or native data.
4. For true first-load investigation, enable collection before the first requested sheet/menu operation after plugin reload. A plugin reload may not cold-start shared Dalamud/Lumina caches. Record that limitation; first measured does not prove cold process/data caches.
5. Compare in-game frame-time observations with profiling disabled and enabled in otherwise unchanged scenes. Use an external runtime profiler when CPU attribution, GC pauses, native/retained memory or GPU/frame impact is needed. Those measurements are not supplied by this UI.
6. Tune limits only after isolating a repeated expensive phase, then rerun correctness/partial/cancellation checks. Keep exact compatibility and read-only boundaries unchanged.

## Metric meanings

| Area | Synchronous region |
| --- | --- |
| FrameworkStep | One engine Tick, excluding initial engine-lock acquisition; includes context checks, reader/job work, result sanitization/merge/retention and any result-size serialization. Idle ticks have an aggregate/idle key. |
| ContextRead | One context read and normalization; separated from tool job timing so first sheet/menu measurements are not polluted by initial context reads. |
| ReaderJob | One dispatched Phase 2 step, including comparison computation; for Nearby Objects, one individual object getter. Does not include all later sanitization/merge/retention. |
| JsonSerialization | One result-size serialization attempt (including byte-trimming retries), or JSON report serialization. Same tool/area series combines these operations; isolate preview-only runs with a reset when necessary. |
| TextFormatting | One displayed-result formatting call, or text report assembly/UTF-8 encoding. Display formatting is cached, not rerun on every frame. |
| PreviewPreparation | Complete synchronous report creation, including its nested JSON/text work and immutable bytes; excludes disk write and the UI's later preview-text decoding. |
| WindowDraw | Aggregate plugin window Draw callback, including any user-triggered preview/report commands and profiling panel overhead; excludes Dalamud renderer/GPU processing and engine work outside this callback. |

All timings are monotonic **wall milliseconds, not CPU milliseconds**. Nested measurements overlap; do not sum them. Capture report **Capture span** (`DurationMs` in JSON) additionally includes update gaps; it is not a sum of framework-step durations or queued wait time.

Each series retains the first measured sample plus latest 256 later samples. Warm median/p95 use nearest-rank percentiles; warm maximum is over the retained window, not the entire session. With no later samples, warm statistics display unavailable. `Samples` is cumulative since reset. First allocation of recorder storage is excluded from that area's own allocation count but may appear in enclosing scopes. Sampling, locking, sorting, formatting and copy operations add observer overhead.

Allocation bytes are the difference in `GC.GetAllocatedBytesForCurrentThread` across a synchronous scope. They are not live heap size, native allocations, GPU memory, process working set, other-thread allocations or GC pause measurements. No scope crosses an await boundary. Clear/enable-state changes invalidate in-flight samples. No sample history is exported automatically or included in capture schemas.

## Live matrix — pending except the narrow pre-patch node-1 sample above

| Case | Inputs / exercise | Evidence to record |
| --- | --- | --- |
| Idle and UI | Idle engine; window hidden/visible; profiling panel collapsed/expanded; longest bounded results visible | Frame time with profiler off/on; ContextRead, idle FrameworkStep, WindowDraw |
| Nearby | Largest available table, radius 200, broad filter; then restrictive filter; cancellation while queued/multi-step | Getter and step distributions, allocations, examined/retained/partial status |
| Addon List | Loaded scope, empty name filter; visible/focused and restrictive-name variants | ReaderJob/FrameworkStep distributions, actual available entries; never manufacture native data to hit cap |
| Addon Nodes | Largest ready visible addon available, all node IDs, depth 8; then node 1 | Reader/retention/serialization cost, actual node count, partial limits; do not arbitrarily dereference components |
| Inventory | All four normal bags, include empty; item/HQ filters | Slots/rows, read-step distributions and byte-trimming cost; unavailable bags remain unavailable |
| Sheet Row | Known rows on each supported sheet/language; first requested lookup and 30+ repeats | ReaderJob first/warm and shared-cache caveat |
| Context Menu | User-opened plain and formatted/plugin-added labels, first and repeated captures | Decoder-inclusive ReaderJob, correctness/partial flags; never open/click from plugin |
| Comparison | Compatible largest retained snapshots, all rows versus changes-only; incompatible pair | ReaderJob computation and output formatting/serialization |
| Reports | Largest retained result; JSON and text previews; separate reset for each | PreviewPreparation, JsonSerialization/TextFormatting; compare exported frozen bytes separately |

The cooperative 2ms reader budget checks between operations; it cannot preempt a slow getter, native call, sheet load, serialization or rendering. A sample over 2ms is evidence to investigate, not grounds to relabel it successful budget compliance.
