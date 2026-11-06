# Phase 4 — optional diagnostics, 0.4.2

## Staged outcome

- **4A implemented:** public metadata for nine fixed internal-name families; no IPC invocation during metadata capture. Up to 512 installed entries examined; incomplete scans preserve unknown absence and cannot authorize IPC. Duplicate internal names are ambiguous.
- **4B implemented for four source-reviewed endpoints:** fixed zero-argument boolean getters below. Since 0.4.1, version/dev metadata is advisory, never an invocation gate. Other candidates remain metadata-only; no arbitrary endpoint console, private reflection, provider startup or controls.
- **4C audit complete; timer capability deferred:** no reviewed current-scope timer endpoint met both privacy and no-side-effect requirements. No empty timer tool or misleading live-timer claim added; Phase 3 supplied/static timer remains unchanged.
- **4D implemented:** explicit Universalis aggregated market and PaissaDB district summaries, with bounded cancellable off-thread HTTP/JSON work. No uploads or authentication.

This completes the currently supported implementation, **not** every candidate integration or live certification. Third-party license/attribution compatibility and Phase 3 native deferrals remain separate distribution gates. Project license: AGPL-3.0-or-later.

## Public provider matrix

All admitted signatures are `() -> bool`, invoked once on the framework thread after a fresh complete metadata scan with one unambiguous loaded provider. `ReviewedEndpoint` means a fixed source-reviewed getter exists, **not** that the installed implementation was audited. Metadata versions/dev flags do not block; manifests and source project versions alone cannot establish runtime compatibility. Reports show `ReviewedSourceVersion` and advisory `VersionEvidence` (match/difference/unknown). Matching version likewise does not prove binary/source correspondence. A missing function is `IpcMissing`; a throwing/mismatched signature is `IpcFailed` without exception text. Neither becomes false. Successful typed invocation proves only that this call returned bool, not unchanged semantics/no side effects in an altered implementation. Metadata change after invocation cancels/discards the value; same-version reload continuity cannot be proven.

| Family / internal name | Reviewed source version (advisory) | Endpoint / output meaning | Source review |
| --- | --- | --- | --- |
| AutoRetainer / `AutoRetainer` | 4.6.1.26 | `AutoRetainer.GetSuppressed` / runtime suppressed flag; ready stays unknown | `AutoRetainer/Modules/IPC.cs` registers `GetSuppressed`, which only returns static `Suppressed`. No config getter, postprocess, venture or multi-mode call. |
| Lifestream / `Lifestream` | 2.5.4.18 | `Lifestream.IsBusy` / queued task or existing path; ready stays unknown | `Lifestream/IPC/IPCProvider.cs`: TaskManager.IsBusy or an existing followPath waypoint count. `Lifestream.cs` guards the lazy FollowPath getter with `followPath != null`; ECommons NeoTaskManager.IsBusy only reads Tasks.Count/CurrentTask. EzIPC defaults to plugin internal-name prefix. |
| vnavmesh / `vnavmesh` | 1.2.3.14 | `vnavmesh.Nav.IsReady` / mesh non-null, not navigation readiness/reachability | `vnavmesh/IPCProvider.cs` registration and auto-property `NavmeshManager.Navmesh`. No pathfinding/build/reload/config call. |
| Wrath Combo / `WrathCombo` | 1.0.4.27 | `WrathCombo.IPCReady` / IPC caches initialized, not combat/rotation readiness | `WrathCombo/Services/IPC/Provider.cs`: explicit prefix `WrathCombo`; getter only returns `_ipcReady`. No lease, control, configuration or rotation query. |
| AutoDuty / `AutoDuty` | none | Metadata only | Candidate scalar status getters exist in `AutoDuty/IPC/IPCProvider.cs`; their complete invocation boundary is not yet admitted to the reviewed registry. Placeholder project version alone is no longer a blocker. |
| BossMod / `BossMod` | none | Metadata only | Current checkout uses `BossMod/Services/IpcService.cs`, not older notes' Framework path; broad config/rotation/control surface not admitted. Transitive getter audit not completed. |
| BossMod Reborn / `BossModReborn` | none | Metadata only | `BossMod/Framework/IPCProvider.cs` includes active-module getters alongside controls/debug walks; full getter boundary audit/admission remains deferred. Placeholder project version alone is no longer a blocker. |
| Rotation Solver family / `RotationSolver` | none | Metadata only; variant unknown | Reborn manifest also uses `RotationSolver`, **not** `RotationSolverReborn`. Internal name alone cannot distinguish variants. Checked provider surface includes controls; no versioned read-only contract admitted. |
| WahTools / `WahTools` | none | Metadata only | `Core/Ipc/WahToolsIpc.cs` IsBusy traverses virtual module Busy getters; Feesh getter reaches `FreeForTrip()` and further runtime helpers. Full transitive audit not completed, so no invocation. |

No source plugin, adapters, configurations or existing debug windows were modified. No provider code was imported into Debug Kit.

### Reviewed checkout identities

Paths below are relative to the read-only `ffxiv-knowledge` reference corpus:

| Repository | Commit |
| --- | --- |
| reference-plugins/AutoRetainer | `cc020df0efee3ea65a45117e2b89999108c702e8` |
| reference-plugins/Lifestream | `b36320170faafc8315f7585edb45cfbcd34e7046` |
| runtime-providers/ffxiv_navmesh | `6fc80725eb8290472eee433fc4be7ee06ec79357` |
| runtime-providers/WrathCombo | `1e64c866c937d9c1d7d246824d80e9361fb626ce` |
| shared-libraries/ECommons | `6e3524d0c6d229b854d300d949b6a315aab97ab3` |
| reference-plugins/AutoDuty | `52023216e7f5108221885d296ddd5f326ad23dbf` |
| runtime-providers/ffxiv_bossmod | `3bbbdf5e0ba0b1f201c5f76465df63ba63dc96c4` |
| runtime-providers/BossmodReborn | `577675d83e66c8be7d41e7cd1ef004cffe0abd8d` |
| runtime-providers/RotationSolverReborn | `a560469db6f67d26cedee4373b38a0af7f3b3944` |
| reference-plugins/WahTools | `6503a4dfc2e861911b152be888570768e8980495` |

## 4C retainer/deployable review

AutoRetainer's public `GetRegisteredCIDs` expands to an offline account roster and is excluded. `GetOfflineCharacterData(ulong)` requires character identity and returns broad mutable offline state, not an independently scoped timer DTO. `GetAdditionalRetainerData(ulong,string)` calls `Utils.GetAdditionalData` -> `GetAdditionalDataKey(..., create: true)` -> inserts a new configuration dictionary entry when absent. It is **not** a side-effect-free getter and is prohibited. No control/write/postprocess endpoint is an alternative.

No current-character bounded retainer/deployable DTO was identified in the reviewed contract. Do not add Content ID use outside the existing transient game-lifetime boundary or import the offline API wholesale. Future support requires a separately reviewed contract, not relaxing this policy. Existing supplied timer estimates do not become live evidence.

## Fixed network contracts

Reviewed 2026-11-05:

- [Universalis documentation](https://docs.universalis.app/), [v2 OpenAPI](https://docs.universalis.app/api/schema/v2). Documentation recommends cached aggregate API when individual listings/sales are unnecessary. Published rate limit is 25 req/s, 50 burst, max eight simultaneous connections/IP; Debug Kit is much more conservative.
- [PaissaDB repository/API documentation](https://github.com/zhudotexe/FFXIV_PaissaDB), [live OpenAPI](https://paissadb.zhu.codes/openapi.json). Public district GET does not require Hello/JWT. No separate GET usage restriction was found in these reviewed documents; this is not legal clearance or a service-availability promise. Never use `/hello`, `/ingest`, websocket or authentication.

| Tool | Sole route | Sent / retained |
| --- | --- | --- |
| `network.universalis` v2 | `GET https://universalis.app/api/v2/aggregated/{publicWorldId-or-fixedDc}/{itemId}` | One user-supplied item 1–10,000,000; public world ID 1–65,535 or fixed global DC enum. Project only selected world/DC NQ/HQ minimum/median/average/velocity and qualified upload times/ages. No individual listings, sellers or arbitrary server text. |
| `network.housing` v2 | `GET https://paissadb.zhu.codes/worlds/{publicWorldId}/{fixedDistrictId}` | Supplied public world and Mist/Lavender Beds/Goblet/Shirogane/Empyreum. Project reported open-plot count and oldest/newest observation time/age; verify returned district and each plot's public world/district. No plot identifiers/tenants/names retained. Max 1,800 source plots examined; response cap can reject a larger district before parsing. |

Explicit service-shape requests made during development to Universalis `74/5333` and PaissaDB `74/339` returned valid public data. This checks service contract shape, **not** in-game plugin behavior. Current aggregate response omitted documented `medianListing` and included upload timestamps outside world 74. Median therefore stays unavailable, requested-world upload timestamps are filtered, and DC freshness is explicitly labeled response-level/may include outside-DC worlds. Response item must match request. No authoritative request-scope echo exists in this aggregate response; scope derives from the fixed requested route.

PaissaDB ward/plot numbers are zero-indexed, but this summary does not display them. Reported open plots can include phase-3/unavailable lottery entries: count is never labeled available-to-buy/enter. Ages use per-plot `last_updated_time` (seconds), not `first_seen_time`. Universalis upload timestamps are milliseconds. Missing/zero/future timestamps produce unknown age, not zero-age freshness; no maximum freshness guarantee or current-market claim is made.

### Transport/lifecycle limits

- Consent defaults false and resets after every capture attempt/navigation; no saved preference silently authorizes requests. Startup, metadata/state queries, draw, export and profiling never start HTTP.
- Only frozen numeric/enum public inputs plus a static application User-Agent are sent. Services necessarily see the user's IP; UI discloses this before consent. No identity, game-derived location/world, report, cookies, credentials or upload body.
- Fixed HTTPS hosts/routes; no URL/string endpoint input. Standard TLS validation; no redirects, proxy credentials, cookies, authentication, decompression, retries or websocket.
- One in-flight HTTP request; monotonic global spacing at least 10s across both services, including failures. 429 imposes at least 60s and honors longer Retry-After. No automatic retry after cooldown.
- 10s request timeout includes streaming and parsing cancellation checks; independent 15s monotonic engine watchdog. Streamed and declared response bodies capped at 128KiB; JSON max depth 16. Non-JSON/encoded/malformed/mismatched/oversized data fails with fixed sanitized reasons, not a raw error dump.
- No local cache (zero retained cache entries). Historical captures are visibly timestamped; a failure never silently substitutes an older response. Existing bounded history/preview/export behavior remains.
- Cancel/context replacement/disposal cancels the local token; engine detaches immediately and ignores late results. No framework-thread network wait or worker-thread game/provider access. Synchronous profiling does not attribute worker HTTP/JSON time/allocations.
- Provider/network payloads have distinct source provenance and generated JSON; comparison unsupported rather than claiming stable identities/contracts/freshness across requests. Provider tools are schema v2 as of 0.4.1; network v2 as of 0.4.2; envelope stays 2. Network text always shows frozen captured inputs, even without a payload. Optional numeric result-level `HttpStatusCode` is bounded to 100–599 and omitted from JSON when unknown. No error body or arbitrary server text is retained.

## Live acceptance — pending

1. Reload 0.4.2; confirm 28 tools, unchanged commands/search/navigation/action grouping and existing inspectors.
2. Provider Metadata: capture absent/unloaded/loaded providers. Check versions against Dalamud; endpoint/responding/ready stay unknown. Confirm capture does not load/enable plugins.
3. Provider State Query: select AutoRetainer, Lifestream or vnavmesh already installed; metadata version differences must not block. Verify single expected scalar and unchanged game/provider state; busy/suppressed do not become ready. Repeat with Wrath Combo only if naturally installed. Test absent/unloaded/ambiguous providers, missing/failing endpoints and natural reload. Non-admitted provider families still block; dev/version flags alone do not. Do not enable automation merely to test. Compare reported values against provider UI where possible; one success is not general semantic certification.
4. Network: consent unchecked initially, resets after capture/navigation; no requests before explicit consent+Capture. Read service/IP/input disclosure. Universalis item 5333/world 74 is a public example; choose any intended public scope. Wait at least 10 seconds before another request. Housing world 74/Mist is a public example, not character-derived.
5. Verify display/copied text/JSON/exact exported bytes agree; external provenance/timestamps/unavailable median/freshness caveats remain visible. Captured item/world/DC or world/district must appear on success and failure. HTTP responses should show numeric status (e.g. 200 or 404); local cooldown/no response stays unavailable. Confirm known failed requests preserve but do not replace with last success. Comparisons must block. Do not generate abusive traffic to force service errors.
6. Cancel a pending request; change context or unload while pending. Verify no late result, UI freeze, provider change or background refresh. Exercise timeout/offline/429/large-body behavior where feasible without abusive service traffic.

Offline coverage uses production policies/engine/decoder/transport with fake handlers, not real network in the default test run. It cannot certify provider behavior or live Dalamud/UI/network integration.

Initial 0.4.0: 169/169 offline tests; 0.4.1: 171/171 with advisory-version coverage. Current 0.4.2: 175/175 offline tests; Windows Debug/x64 build with zero warnings/errors; safety checks across 33 production files; locked restore, DLL/Core/manifest 0.4.2.0 / API 15 and local documentation links verified. New reporting tests cover frozen inputs, numeric response status, sanitized errors and exact export bytes. See [validation record](validation.md#network-reporting-cleanup--042).
