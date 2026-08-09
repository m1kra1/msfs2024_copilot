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
4. SimConnect only (standard events/SimVars; LVars only via JSON profiles).
5. Context-aware actions (e.g. gear-up positive-climb gate).
6. Modular JSON profiles; unique SimConnect app name `"PrivateCoPilotVoice"`.
7. No busy SimVar polling.
8. Private use only.
9. Core free of WPF.

## Runtime pipeline (do not reorder)
1. Speech gate (wake word / PTT / continuous_listen) → phrase text
2. `PhraseMatcher` (longer phrases win)
3. `ConditionEngine` (SimVars + behavior flags)
4. TTS response (confirm delay when enabled)
5. `ActionExecutor` → SimConnect events (skipped when `actions: []`)

Shared entry points: `HostSession.InjectPhrase` / `HandlePhrase` → `CommandProcessor.Process` (GUI Debug inject and headless `--inject` use the same path).

## Command Schema (current)
Each command in `base_commands.json` / aircraft profiles:
- `id`, `phrases[]`, `response`, `reject_response` (optional)
- `conditions[]` (`simvar` / `op` / `value` / `units`)
- `actions[]` (`type=event`, `name=...`)
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
- **Apply (in-memory):** copy UI fields into live `Settings`; optionally rebuild catalog from **current** `AircraftProfile`; rebuild pipeline; restart speech if listening. Must **not** re-read `settings.json` and wipe edits.
- **Save:** same as Apply + write `settings.json`.
- **Reload:** `LoadAll` from disk (overwrites memory), rebuild pipeline, restart speech if active.
- After profile change, subsequent commands (including `list_commands`) must see the new merged catalog.

## Speech gate notes
- Default: bare phrases rejected unless wake word present or PTT armed (`ptt_grace_ms` after key release).
- `continuous_listen=true` accepts bare phrases.
- Inject in tests/GUI: include wake word (e.g. `"Co Pilot …"`) or use force-gate / `--ptt` as appropriate.

## Aircraft auto-detect (optional)
- Settings: `auto_detect_aircraft`, `announce_profile_switch` in `settings.json` / GUI Settings.
- Rules: `config/aircraft_detection.json` (case-insensitive contains; first match; `fallback_profile`).
- Live only: SimConnect TITLE + ATC MODEL (SECOND period). Identity change → `RebuildCatalogFromCurrentSettings` + pipeline/speech rebuild.
- CLI `--profile` locks auto **switching** (title still shown). Offline → title Unknown, no crash.
- Pure matcher: `AircraftProfileMatcher` (no WPF / no Sim I/O).

## Current Version & Branch
- Package/app baseline **1.2.x** on **`dev`** (WPF GUI + HostSession + headless). **`main`** = stable baseline.
- Check `CHANGELOG.md` / `manifest.json` for the latest package_version; do not invent version bumps without user intent.

## Docs Workflow
Every meaningful change → update `README.md` + `CHANGELOG.md` (and `FUTURE.md` when closing/adding planned items), then push to **`dev`** when the user wants it published.
Keep `AGENTS.md` accurate when architecture or agent-facing conventions change.

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
- Near-term ideas and done backlog items: `FUTURE.md` (e.g. Fenix A320 profile next; `list_commands` done).
