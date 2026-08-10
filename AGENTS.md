# AGENTS.md – msfs2024_copilot (private-utility-copilot-voice)

## Project Identity
Private-use utility mod for Microsoft Flight Simulator 2024.
Voice-controlled co-pilot (STT → phrase match → conditions → TTS + SimConnect events).
Package: private-utility-copilot-voice | Creator: Private | Type: MISC (Community only).

## Architecture (locked)
- Community package under Community2024 containing WASM marker + extras/ host files.
- WASM module = marker only (module_init / deinit / update). ZERO co-pilot logic.
- CoPilotVoiceHost.exe = out-of-process .NET 8 WPF host (default) + headless CLI.
- Shared core: HostSession (and everything under Config/, Core/, SimConnect/, Speech/, Models/, Diagnostics/) must remain completely free of WPF/UI dependencies.
- Commands are 100 % JSON-driven: base_commands.json + aircraft/*.json (merge on load). No aircraft-specific C# hardcoding of events.
- UI (`Ui/`, WPF XAML) is a thin shell over HostSession; do not put recognition, catalog, or SimConnect logic in the UI layer.
- **Commands tab** edits working copies of base + active profile in the UI, then calls `HostSession.ApplyCommandSources` (merge + optional disk save). Persistence stays in ConfigLoader/JSON — no command tables hardcoded in C#.
- **Manual tab** builds categorized buttons from the live catalog via Core `CommandCatalogGroups` + `HostSession.RunCatalogCommand` (force-gate inject of first phrase). No command logic in the UI beyond layout/click → session.

## Key Paths
- Host source: `private-utility-copilot-voice/Sources/Host/CoPilotVoiceHost/`
- Solution: `private-utility-copilot-voice/Sources/Host/CoPilotVoiceHost.sln`
- Tests: `private-utility-copilot-voice/Sources/Host/CoPilotVoiceHost.Tests/`
- **Canonical config (edit here):** `private-utility-copilot-voice/PackageSources/extras/config/`
  - `settings.json`, `base_commands.json`, `aircraft/*.json`
- Packaged copy (keep in sync when shipping): `private-utility-copilot-voice/Packages/private-utility-copilot-voice/extras/config/`
- Published host + extras: `private-utility-copilot-voice/PackageSources/extras/`
- Build host: `dotnet publish ... -o ../../PackageSources/extras` (from Host project dir)
- Docs / backlog: repo-root `README.md`, `CHANGELOG.md`, `FUTURE.md`

### Config copy rules
- **Source of truth = PackageSources/extras/config.** The csproj copies those JSON files into build output (`bin/.../config`) via `CopyToOutputDirectory`.
- When changing commands/settings, update **PackageSources** always; also update **Packages/.../extras/config** if the repo ships a built package tree.
- Do **not** hand-edit `bin/` or `obj/` config copies — they are build outputs.

## Design Rules (never break)
1. Utility mod only.
2. Out-of-process host for STT/TTS/SimConnect.
3. No core logic in WASM.
4. SimConnect only (standard events/SimVars; **LVars via JSON** `set_simvar` actions — host writes live with `SetDataOnSimObject`; no SPAD/AAO).
5. Context-aware actions (e.g. gear-up positive-climb / airborne gate; Fenix uses VS + `SIM ON GROUND`).
6. Modular JSON profiles; unique SimConnect app name `"PrivateCoPilotVoice"`.
7. No busy SimVar polling.
8. Private use only.
9. Core free of WPF.

## Runtime pipeline (do not reorder)
1. Speech gate (wake word / PTT / continuous_listen) → phrase text  
   (Windows STT may re-rank **alternates** via `PhraseMatcher.MatchBestHypothesis` before gate)
2. `PhraseMatcher` — **whole-word token sequences** (exact / end / contiguous tokens; no loose substring `Contains`); longer phrases win on ties
3. `ConditionEngine` (SimVars + behavior flags)
4. TTS response (confirm delay when enabled)
5. `ActionExecutor` → SimConnect `event` and/or `set_simvar` / `simvar` (skipped when `actions: []`)

Shared entry points:
- `HostSession.InjectPhrase` / `HandlePhrase` → `CommandProcessor.Process` (GUI Debug inject and headless `--inject`)
- `HostSession.RunCatalogCommand(id)` → Manual tab (force-gate + first phrase / wake-prefixed text)

## Command Schema (current)
Each command in `base_commands.json` / aircraft profiles:
- `id`, `phrases[]`, `response`, `reject_response` (optional)
- `conditions[]` (`simvar` / `op` / `value` / `units`)
- `actions[]`:
  - `{ "type": "event", "name": "GEAR_UP" }`
  - `{ "type": "set_simvar", "name": "L:S_MIP_GEAR", "value": 0, "units": "number" }` (live write when SimConnect IsLive)
- optional flags: `require_positive_climb_flag`, `checklist`

### Merge rules
- Profile commands with the **same id replace** base; **new ids append**.
- Event aliases on the profile rewrite action event names at merge time.
- Catalog rebuild: `ConfigLoader.Merge` via `HostSession.RebuildCatalogFromCurrentSettings` + `RebuildPipelineServices` (matcher/processor rebuilt).

### Info-only / dynamic responses
- Pure info commands use `actions: []` (and usually empty `conditions`) so they work Offline and live without SimConnect events.
- Static JSON `response` is the default spoken text.
- Dynamic spoken text (e.g. **`list_commands`**) is built in Core from the **currently loaded merged catalog** (`CommandListBuilder` + special-case in `CommandProcessor`); full detail goes to log via `CommandResult.DetailLogLines` → console / Debug tab.
- Never hardcode the command list in C# or a fixed TTS string for that feature — always derive from live catalog after profile merge.

## Settings Apply / Save / Reload (regression-critical)
- **Apply (in-memory):** copy UI fields into live `Settings`. Must **not** re-read `settings.json` and wipe edits.
  - Catalog re-merge only when the active **aircraft profile** differs from the last merge (fingerprint thrift).
  - Pipeline services (matcher / PTT / TTS / processor) rebuild only when speech/TTS/behavior fingerprints change.
  - **Speech grammar restart only** when grammar inputs change (wake word, culture/engine, profile/phrase catalog) **and** listening is active. No-op / behavior-only / TTS-only Apply must **not** increment `SpeechStartCount`.
  - Wake-word / profile changes must still restart speech so inject and live mic see the new grammar.
- **Save:** same as Apply + write `settings.json`.
- **Reload:** `LoadAll` from disk (overwrites memory), force rebuild pipeline, restart speech if active.
- After profile change, subsequent commands (including `list_commands`) must see the new merged catalog.

## Performance / resource notes (do not regress)
- No busy SimVar polling; SimConnect status/aircraft data remain event/SECOND-period style. Prefer **native** SimConnect (dispatch thread) so TITLE/status arrive without a HWND; aircraft-identity + Status UI telemetry refresh ~2 Hz.
- `UiLogSink` + Debug TextBox: bounded (~2000 lines); trim oldest under pressure.
- Avoid duplicate background timers for PTT (session poll + HandlePhrase call `PttArmService.Poll`; do not add a second PttArm `StartPolling` on inject/--once paths).

## Speech gate notes
- Default: bare phrases rejected unless wake word present or PTT armed (`ptt_grace_ms` after key release).
- `continuous_listen=true` accepts bare phrases.
- Inject in tests/GUI: include wake word (e.g. `"Co Pilot …"`) or use force-gate / `--ptt` as appropriate.

## Aircraft auto-detect
- **Default ON** in shipped `settings.json`: `auto_detect_aircraft: true`, `announce_profile_switch: true`.
- Rules: `config/aircraft_detection.json` (case-insensitive contains; first match; `fallback_profile`). Fenix rule (`pattern: fenix`) → `fenix_a320` before generic A320.
- Live only: SimConnect TITLE + ATC MODEL (SECOND period). Identity change → `RebuildCatalogFromCurrentSettings` + pipeline/speech rebuild.
- CLI `--profile` locks auto **switching** (title still shown). Offline → title Unknown, no crash.
- Pure matcher: `AircraftProfileMatcher` (no WPF / no Sim I/O).

## Study-level LVar strategy (agent-facing)
- **Base = events.** Never put vendor LVars in `base_commands.json`.
- **Study profiles = dual-write overrides** (`event` + `set_simvar`) by same command `id`; new ids append.
- **Per-aircraft maps only** (Fenix ≠ iniBuilds A350). No C# `if (Fenix)`.
- Prefer **Unable** for unmapped FCU modes; H/B-Event bridge only if live `SetDataOnSimObject` fails.

## Fenix profile notes (agent-facing)
- Gear: `L:S_MIP_GEAR` (0=UP, 1=DOWN). Gate: positive VS + airborne (not `GEAR POSITION == 1`).
- Lights: `L:S_OH_EXT_LT_*` (landing 0/1/2, nose, strobe, beacon, nav/logo, wing).
- Flaps: `L:S_FC_FLAPS` 0–4; incr/decr event-only.
- Park brake: `L:S_MIP_PARKING_BRAKE` 0/1. Speedbrake: `L:A_FC_SPEEDBRAKE` 0=ARM, 1=RETRACT, 2=DETENT.
- Overhead: anti-ice / probe / APU master-start-bleed / BAT / EXT PWR / fuel / packs / ADIRS / seatbelts / dome.
- Checklists (`checklist_*`) include real multi-action sequences on Fenix.
- Prefer JSON profile overrides over C# aircraft branches.

## GUI tabs (thin shell)
| Tab | Role |
|-----|------|
| Status | Live/Offline, detected aircraft, profile, last phrase/action |
| Settings | Apply / Save / Reload; auto-detect; profile combo |
| **Manual** | Categorized fire buttons → `RunCatalogCommand` |
| Commands | Edit base + profile JSON working copies |
| Debug | Log, inject, reconnect, test TTS |

## Current Version & Branch
- Package/app baseline **1.2.x** on **`dev`** (WPF GUI + HostSession + headless). **`main`** = stable baseline.
- Check `CHANGELOG.md` / `manifest.json` for the latest package_version; do not invent version bumps without user intent.

## Docs Workflow
Every meaningful change → update `README.md` + `CHANGELOG.md` + this `AGENTS.md` when architecture/agent conventions change (and `FUTURE.md` when closing/adding planned items), then push to **`dev`** when the user wants it published.

## Testing Expectations
- Unit tests must stay green:  
  `dotnet test private-utility-copilot-voice/Sources/Host/CoPilotVoiceHost.Tests -c Release`
- Prefer tests that drive **shipped** code: real `ConfigLoader` / `HostSession.InjectPhrase` / `CommandProcessor` — not a reimplemented matcher.
- Headless inject path must continue to work (`--headless --offline --inject "…"`).
- GUI Settings Apply/Save/Reload must not regress (in-memory vs disk, speech restart, catalog profile).
- Live path: Free Flight + correct **KittyHawk/MSFS** `SimConnect.dll` (not FSW/Dovetail) → status "Live" + `"[SimConnect] LIVE event sent: …"`.
- Non-regression: existing gear/lights/etc. event commands and offline inject exit codes stay valid.

## Agent do / don't (session hygiene)
**Do**
- Read this file first; keep Core free of WPF.
- Edit config under PackageSources; sync Packages config when needed.
- Put new command logic in JSON first; add C# only for generic pipeline hooks (conditions, dynamic info, SimConnect client).
- After host/config changes: run Release tests; smoke headless inject when relevant.

**Don't**
- Put co-pilot logic in WASM or aircraft-specific event names in C#.
- Busy-poll SimVars; use existing snapshot/event path.
- Re-load settings from disk on Apply (known prior bug).
- Treat `bin/` config or logs as source of truth.
- Add public distribution / marketplace packaging assumptions (private utility only).

## Related planning
- Near-term ideas and done backlog items: `FUTURE.md` (Fenix hybrid P0–P3 LVar map + checklists + list_commands + Manual tab done; live human cockpit re-test open; A350 profile later; H/B bridge optional).
