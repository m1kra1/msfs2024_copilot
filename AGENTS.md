# AGENTS.md – msfs2024_copilot (private-utility-copilot-voice)

## Project Identity
Private-use utility mod for Microsoft Flight Simulator 2024.
Voice-controlled co-pilot (STT → phrase match → conditions → TTS + SimConnect events/LVars).
Package: `private-utility-copilot-voice` | Creator: Private | Type: MISC (Community only — not Marketplace).

## Architecture (locked)
- Community package under Community2024: WASM marker + `extras/` host files.
- WASM module = marker only (`module_init` / `deinit` / `update`). **ZERO** co-pilot logic.
- `CoPilotVoiceHost.exe` = out-of-process **.NET 8 WPF** host (GUI default) + headless CLI.
- Shared core: `HostSession` and everything under `Config/`, `Core/`, `SimConnect/`, `Speech/`, `Models/`, `Diagnostics/` must remain **completely free of WPF/UI** dependencies.
- Commands are **100 % JSON-driven**: `base_commands.json` + `aircraft/*.json` (merge on load). No aircraft-specific C# hardcoding of events.
- UI (`Ui/`, WPF XAML) is a **thin shell** over `HostSession`; no recognition, catalog, or SimConnect logic in the UI layer.
- **Commands tab** edits working copies of base + active profile, then `HostSession.ApplyCommandSources` (merge + optional disk save). Persistence = ConfigLoader/JSON only.
- **Manual tab** builds categorized buttons from the live catalog via Core `CommandCatalogGroups` + `HostSession.RunCatalogCommand` (force-gate; first phrase). UI = layout/click → session only.
- **Checklists** are first-class under `config/checklists/*.json` (not inside aircraft profiles). Core: `ChecklistRunner` / `ChecklistValidator` / `ChecklistPhraseIndex`. Host: load on Reload, voice intercept (word **checklist**), poll tick, `ApplyChecklists`. GUI: **Checklists** tab only → session APIs.

## Key Paths
- Host source: `private-utility-copilot-voice/Sources/Host/CoPilotVoiceHost/`
- Solution: `private-utility-copilot-voice/Sources/Host/CoPilotVoiceHost.sln`
- Host project: `private-utility-copilot-voice/Sources/Host/CoPilotVoiceHost/CoPilotVoiceHost.csproj`
- Tests: `private-utility-copilot-voice/Sources/Host/CoPilotVoiceHost.Tests/`
- **Installer (separate WPF app, no co-pilot Core):** `private-utility-copilot-voice/Sources/Installer/CoPilotVoiceSetup/`
  - Solution: `…/Sources/Installer/CoPilotVoiceSetup.sln`
  - Pack distribution: `scripts/pack-installer.ps1` → `dist/CoPilotVoiceSetup/` (Setup + `payload/private-utility-copilot-voice`)
- **Canonical config (edit here):** `private-utility-copilot-voice/PackageSources/extras/config/`
  - `settings.json`, `base_commands.json`, `aircraft_detection.json`, `aircraft/*.json`, `checklists/*.json`
- **Voice packs (WAV callouts):** `private-utility-copilot-voice/PackageSources/extras/voices/{pack}/` (`manifest.json` + `.wav`)
- Packaged copy (keep in sync when shipping): `private-utility-copilot-voice/Packages/private-utility-copilot-voice/extras/config/` (+ `extras/voices/`)
- Published host + extras: `private-utility-copilot-voice/PackageSources/extras/`
- Docs (repo root): `README.md`, `CHANGELOG.md`, `Complete_Features.md`, `Backlog.md`, `Live_Testing.md`
- Feature plans (future + shipped specs): `Plans/` (e.g. `Plans/Plan_LearnMode.md`, `Plans/Plan_B2_WavCallouts_with_samples/`)

### Build / publish / test (explicit)
From repo root (or Host dir as noted):

```bat
dotnet build private-utility-copilot-voice/Sources/Host/CoPilotVoiceHost.sln -c Release
dotnet test private-utility-copilot-voice/Sources/Host/CoPilotVoiceHost.Tests -c Release
dotnet test private-utility-copilot-voice/Sources/Installer/CoPilotVoiceSetup.Tests -c Release
```

Publish host into extras (from Host project directory):

```bat
cd private-utility-copilot-voice\Sources\Host\CoPilotVoiceHost
dotnet publish CoPilotVoiceHost.csproj -c Release -r win-x64 --self-contained false -o ..\..\..\PackageSources\extras
```

Headless smoke (offline inject; wake word or `--ptt` / `--bypass-gate` as needed):

```bat
CoPilotVoiceHost.exe --headless --offline --inject "Co Pilot landing lights on"
```

Pack installer distribution (from repo root):

```bat
powershell -ExecutionPolicy Bypass -File scripts/pack-installer.ps1
```

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
4. TTS response (confirm delay when enabled) — engine **Windows** / **Wav** / **Hybrid**; Hybrid plays `extras/voices/{voice_pack}` WAV by command id + kind, else Windows SAPI
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
- Pure info commands use `actions: []` (and usually empty `conditions`) so they work offline and live without SimConnect events.
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
- **Settings UI dirty-guard (regression-critical):** Live `StatusChanged` → `MainWindow.RefreshStatus` runs ~2 Hz. While the Settings form is **dirty** (user edited controls, not yet Apply/Save/Reload), **do not** push session values into Settings controls (Auto-Detect checkbox, profile combo, continuous, announce, etc.). Status-tab fields (`TxtProfile`, FLIGHT DATA, …) always stay live. When clean, still sync profile combo from session so Apply cannot clobber an auto-selected profile. `LoadSettingsToUi` clears dirty. Policy helper: `MainWindow.ShouldSyncSettingsControlsFromSession`. Never re-introduce unconditional `SetAutoDetect.IsChecked = Settings…` / force-`SetProfile.Text` on every refresh.

## Performance / resource notes (do not regress)
- No busy SimVar polling; SimConnect status/aircraft data remain event/SECOND-period style. Prefer **native** SimConnect (dispatch thread) so TITLE/status arrive without a HWND; aircraft-identity + Status UI telemetry refresh ~2 Hz.
- `UiLogSink` + Debug TextBox: bounded (~2000 lines); trim oldest under pressure.
- Avoid duplicate background timers for PTT (session poll + HandlePhrase call `PttArmService.Poll`; do not add a second PttArm `StartPolling` on inject/--once paths).

## SimConnect transmit notes (regression-critical)
- Primary live client: **native** `NativeSimConnectClient` (managed is fallback).
- `SimConnect_TransmitClientEvent` must use **`GroupID = SIMCONNECT_GROUP_PRIORITY_HIGHEST` (1)** and **`Flags = SIMCONNECT_EVENT_FLAG_GROUPID_IS_PRIORITY` (0x10)**. Constants: `NativeSimConnectClient.GroupPriorityHighest` / `EventFlagGroupIdIsPriority`. Managed path must stay aligned.
- **Do not** transmit with `Flags = 0` while passing a priority as GroupID — HRESULT can be S_OK while **MSFS never applies** the event (host Live + TTS/actions logged, cockpit dead). Smoke log: `[SimConnect] LIVE event sent: …`.

## Speech gate notes
- Default: bare phrases rejected unless wake word present or PTT armed (`ptt_grace_ms` after key release).
- `continuous_listen=true` accepts bare phrases.
- Inject in tests/GUI: include wake word (e.g. `"Co Pilot …"`) or use force-gate / `--ptt` as appropriate.

## Aircraft auto-detect
- **Default ON** in shipped `settings.json`: `auto_detect_aircraft: true`, `announce_profile_switch: true`.
- Rules: `config/aircraft_detection.json` (case-insensitive contains; first match; `fallback_profile`). Fenix rule (`pattern: fenix`) → `fenix_a320` before generic A320.
- Live only: SimConnect TITLE + ATC MODEL (SECOND period). Identity change → `RebuildCatalogFromCurrentSettings` + pipeline/speech rebuild.
- When **off**: still update detected TITLE/model for display; **do not** switch `Settings.AircraftProfile`. Manual profile via Settings combo + **Apply** (UI dirty-guard must allow unchecking Auto-Detect and changing profile without 2 Hz snap-back).
- false→true Apply re-evaluates current identity once (`_lastDetectedIdentityKey` cleared) so enabling auto switches without needing a new aircraft.
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
- Prefer JSON profile overrides over C# aircraft branches.
- Sequential checklists: `config/checklists/` + `assigned_profiles` (not Fenix-only multi-action commands).

## GUI tabs (thin shell)
| Tab | Role |
|-----|------|
| Status | Live/Offline, **FLIGHT DATA** (~2 Hz), detected aircraft, profile, last phrase/action |
| Settings | Apply / Save / Reload; auto-detect; profile combo (dirty-guard vs live RefreshStatus — see Settings section) |
| **Manual** | Categorized fire buttons → `RunCatalogCommand` |
| **Checklists** | Sequential verify/execute checklists → `StartChecklist` / `ApplyChecklists` |
| **Learn** | Control capture (Live): watches → detections → create/edit → save **active profile only** |
| Commands | Edit base + profile JSON working copies |
| Debug | Log, inject, reconnect, test TTS |

### Learn Mode notes (agent-facing)
- Core: `CommandMappingIndex`, `LearnWatchBuilder`, `LearnCaptureService`, `LearnActionHints` — **no WPF**.
- SimConnect: **isolated** `DEF_LEARN` / `REQ_LEARN` (SECOND). Never add experimental LVars to `DEFINITION_STATUS`.
- Watches: status discrete + catalog + `config/learn_watchlist.json` + profile `learn_watch` + session manual (cap 128).
- HostSession: `StartLearnMode` / `StopLearnMode` / `AddManualLearnWatch` / `ExportLearnDetections`; Observe on existing poll; suppress ~750 ms after actions.
- Save path: load base unchanged + upsert profile command → `ApplyCommandSources(..., saveToDisk: true)`.
- CLI: `--learn-dump`, `--learn-export <path>` (headless).
- Vendor LVars only in aircraft profile, never `base_commands.json`.

## Current Version & Branch
- Package/app baseline **1.7.0** on **`dev`** (first-class Checklist system + prior 1.6.x installer/host fixes).
- Source of version truth: `Packages/.../manifest.json` `package_version` and host csproj `<Version>`.
- Do not invent version bumps without user intent.

## Docs Workflow
Every meaningful change → update `README.md` + `CHANGELOG.md` + this `AGENTS.md` when architecture/agent conventions change.
- Done features → `Complete_Features.md`
- Open work → `Backlog.md` (implementation specs live under `Plans/`, e.g. `Plans/Plan_LearnMode.md`)
- Push to **`dev`** when the user wants it published; releases also go to **`main`** when requested.
- Do **not** revive `FUTURE.md` — replaced by Complete_Features + Backlog.

## Testing Expectations
- Unit tests must stay green:  
  `dotnet test private-utility-copilot-voice/Sources/Host/CoPilotVoiceHost.Tests -c Release`  
  `dotnet test private-utility-copilot-voice/Sources/Installer/CoPilotVoiceSetup.Tests -c Release`
- Prefer tests that drive **shipped** code: real `ConfigLoader` / `HostSession.InjectPhrase` / `CommandProcessor` — not a reimplemented matcher.
- Headless inject path must continue to work (`--headless --offline --inject "…"`).
- GUI Settings Apply/Save/Reload must not regress (in-memory vs disk, speech restart, catalog profile, **Settings dirty-guard** / Auto-Detect uncheck + manual profile).
- Live path: Free Flight + correct **KittyHawk/MSFS** `SimConnect.dll` (not FSW/Dovetail) → status "Live" + `"[SimConnect] LIVE event sent: …"` (native transmit flags 0x10 — see SimConnect transmit notes).
- Human live/installer abnahme: follow root **`Live_Testing.md`** (installer contrast, install/uninstall, Live SimConnect, Fenix A1, Learn, WAV).
- Non-regression: existing gear/lights/etc. event commands and offline inject exit codes stay valid.

## Agent do / don't (session hygiene)
**Do**
- Read this file first; keep Core free of WPF.
- Edit config under PackageSources; sync Packages config when needed.
- Put new command logic in JSON first; add C# only for generic pipeline hooks (conditions, dynamic info, SimConnect client).
- After host/config changes: run Release tests; smoke headless inject when relevant.
- Keep a **single** root `AGENTS.md` only — never add nested `AGENTS.md` / `CLAUDE.md` under subdirs.

**Don't**
- Put co-pilot logic in WASM or aircraft-specific event names in C#.
- Busy-poll SimVars; use existing snapshot/event path.
- Re-load settings from disk on Apply (known prior bug).
- Overwrite Settings controls on every `RefreshStatus` while dirty (breaks Auto-Detect off / manual profile).
- Use `TransmitClientEvent` with Flags=0 + priority GroupID (events appear sent, sim ignores them).
- Treat `bin/` config or logs as source of truth.
- Add public distribution / marketplace packaging assumptions (private utility only).
- Implement Backlog items without an explicit user request.

## Related planning
- **Implemented features (SSOT):** `Complete_Features.md` (incl. Learn Mode §5b)
- **Open work / improvements:** `Backlog.md` (A1 Fenix live re-test P0; later items)
- **Live testing checklist (human):** `Live_Testing.md` (installer + Free Flight; not a substitute for unit tests)
- **Learn Mode coding spec:** `Plans/Plan_LearnMode.md` (Phases 1–3 shipped in 1.4.0)
- **WAV callouts (B2) coding spec:** `Plans/Plan_B2_WavCallouts_with_samples/Plan_B2_WavCallouts.md` (Phase 1 shipped in 1.5.0)
- New feature plans go under **`Plans/`** only (not repo root). Runtime live checklists may live at repo root (`Live_Testing.md`).
- Fenix hybrid + Manual/Commands + Learn Mode full + B2 WAV callouts + Installer (1.6.x) + Checklist system (1.7.0) are **done** in code (see Complete_Features); human live abnahme still open (Live_Testing / A1).
