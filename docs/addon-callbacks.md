# Addon callback observation — 0.5.3

User-requested addition to Phase 5 readiness work. `addons.callbacks` v4 observes all addons by default, or one exact optional addon filter, until the user presses Stop. Newest 64 events remain in a rolling buffer; older events are dropped and counted. Integer values are always included and may contain IDs. No callback generation/replay or game/provider control. This is a narrow exception to the previous no-own-hooks/identity-data boundary, not authorization for other hooks or identity API reads.

## Use / first live check

1. Reload `D:\Coding\C#\Debug Kit\DebugKit\bin\Debug\DebugKit.dll` with Core/manifest beside it.
2. Select **Addon Callback Log**. Leave **Addon filter (optional)** blank to observe all addon names, or enter one exact known name (ASCII letters/digits/underscore, 1–31 characters). Wildcards are not supported.
3. Exact native profile must match; session native authorization must be enabled. The UI warns that raw integers may contain IDs. No per-capture checkbox is required.
4. Press **Start capture**, then interact normally with a benign UI. Press **Stop capture** to disable the hook and retain the result. There is no timer or event-count auto-stop. Do not trigger purchases/actions or enable automation for testing.
5. Inspect the always-visible **Addon | Values | Raw flag | Return** table. Each event shows actual addon name; each argument uses a typed line (`0 [Int]: 23`, `1 [Int]: -1`, `1 [UInt]: 0`); flag/return are 1/0. Hover addon for UTC/counts and values for unclipped text. Newest 64 events are retained across all addons; the report states how many older events were dropped. Integers may include IDs and appear in display/copy/export. String/float/vector/pointer-typed values remain excluded. Callback results are **non-atomic**; comparisons are unsupported.
6. Context change or plugin unload cancels and discards active capture. Clear results alone does not stop observation. Closing/navigating the UI does not stop it; return and press Stop. Stop if crashes, hook conflicts or behavior changes occur.
7. Verify copied text/JSON/exact exported bytes agree. Start/stop again and confirm no old events leak into the next result.

User supplied positive 0.5.0 and 0.5.2 reports: six Character callbacks in a five-second types-only capture, then one `ItemDetail` callback with `Int -1` in all-addon mode. These demonstrate the earlier paths, not indefinite observation, rolling overflow, manual stop or general native safety/game-behavior preservation. New 0.5.3 Start/Stop behavior remains **pending live validation**. Accepted first-use serialization/performance limitation is unchanged; logging is not performance-certified.

## Source and ABI review

The requested [SimpleTweaks AddonDebug.cs](https://github.com/Caraxi/SimpleTweaksPlugin/blob/main/Debugging/AddonDebug.cs) was read as a feature reference. Its custom callback delegate returns `void*`, reads generic integer/string values and retains native addresses. None of that code, signature scan, string decoding or pointer logging was copied.

This implementation uses installed `AtkUnitBase.Delegates.FireCallback` and the generated `MemberFunctionPointers` address, compiled against the exact hash-pinned ClientStructs baseline. The current signature returns **bool**, with `(AtkUnitBase*, uint, AtkValue*, bool)` arguments. The final bool is reported neutrally as a raw flag, not guessed to be the reference's older UpdateVisibility meaning.

Reviewed layouts: fixed 32-byte `AtkUnitBase.Name` span and `AtkValue` type/boolean/32-bit/64-bit integer union fields. Addon name is decoded only within that fixed span; no native C-string scan. Blank filter accepts all bounded ASCII addon names; nonblank filter matches exact ordinal name. Each row records only the validated bounded addon name, not pointer/identity data. At most 16 value slots are read; Bool and integer union members are read only on corresponding exact type. Missing buffers/excessive counts are not dereferenced; omitted argument counts are explicit. No string/float/vector/pointer-typed values, setup/list-handler callbacks, addon text, return addresses or addon pointers are retained. No source-plugin code/assets are copied. Project code is AGPL-3.0-or-later; third-party attribution/compatibility remains a separate gate.

### Numeric privacy exception — 0.5.1

The user explicitly authorized raw numeric callback arguments after a warning that they may include character/object/owner or other IDs. This inspector always includes integers; no per-capture checkbox. Integer values are not classified, masked, dereferenced or treated as ID-free. Other identity API reads and other inspectors' privacy policies remain unchanged. All display/copy/preview/export paths warn; report manifest does not claim ID-free data for callback logs. Values stay local unless the user explicitly shares an exported/copied report.

Signed/unsigned fields preserve exact width and negative sentinels, reject wrong-type/out-of-range values, and are omitted from JSON when absent. 64-bit JSON integers require a consumer preserving 64-bit precision. `ArgumentsOmitted` counts missing argument entries, not intentionally withheld values. The table uses neutral **Raw flag**, not the old reference's unverified “Update visibility” label. Callback schema is v4; envelope remains 2 and comparisons remain unsupported.

The filter is an exact **name**, not a native instance identity: callbacks from multiple same-name instances may appear in one capture. Only the normal callback entry point is observed; callbacks using other paths are outside scope. Observation continues until Stop, context cancellation or unload; it still cannot establish complete activity across other callback paths.

The existing native profile remains mandatory: installation stamp `2026.10.16.0000.0000`, Dalamud `15.0.3.9`, ClientStructs SHA-256 `f5a2c68ccecd3e199bda572050e98902b2e7594066c95dc7826c2d4297cd0564`. Generated types and matching installation stamp/hash do not independently certify the running binary or hook compatibility.

## Lifecycle / bounds

- Only `Readers/Native/AddonCallbackObserver.cs` may create this own hook; one fixed generated address, no scanner/arbitrary address input. It is lazy-created after compatibility/native-session-authorization checks, never on startup/draw or by other tools.
- Detour forwards the original **exactly once**, outside observer exception handling, with unchanged addon/count/value pointer/flag; original boolean return is unchanged. No argument writes or callback dispatch/retry. Original/native faults are not magically made safe by managed catches.
- Native pointers exist only as arguments/locals. Only the managed hook owner/session/detached bounded evidence persist. Addon filtering precedes argument inspection. Other-thread callbacks are forwarded without inspection.
- Bounded argument loops; rolling buffer holds at most 64 entries/16 arguments each and saturating dropped/read-failure counters. Existing 64KiB result/1MiB history limits still apply and may trim rows. Value omission is type-policy or unavailable evidence, not proof of decoded completeness.
- Start creates/enables hook; Stop sets a thread-safe request flag. The next framework update disables the hook and completes one task. Engine waits without blocking and has no callback timeout. Waiting frames are not read steps. Events are stored after original return using pre-call UTC timestamps; nested callback completion can affect row order.
- Cancel/context replacement stops retention immediately via the local token and detaches the engine from late completion; the next framework update disables the hook. Disposal disables/disposes the managed hook. No event can update a disposed UI or become an automatically rearmed capture. Hook disable failures attempt disposal and report unavailable; catastrophic native/hook failure cannot be certified away.
- Results/copy/preview/export use the closed generated `callbacks` payload and persistent numeric warning; no disk logging, telemetry or external upload. Callback comparisons are deliberately rejected. Native session authorization remains separate; no callback-specific consent is saved.

Offline coverage exercises production Core buffers/options/reporting and fake-source lifecycle behavior, not a native hook. Numeric tests cover typed values, negative/zero/32-bit/64-bit boundaries and wrong-type suppression. Lifecycle tests cover indefinite wait, explicit Stop preserving rows, cancellation isolation, and bounded newest-64 rollover/drop reporting. Export tests check exact JSON/text bytes with privacy warnings. Source tripwires require the narrow hook boundary, explicit stop path, rolling buffer, no pointer fields/excluded value reads, and exactly one unchanged original invocation; these are regression aids, not a formal proof.
