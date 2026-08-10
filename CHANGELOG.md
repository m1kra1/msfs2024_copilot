# Changelog

All notable changes to **msfs2024_copilot** / `private-utility-copilot-voice` are documented in this file.

Format based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).  
Versioning follows package `package_version` where applicable.

**Workflow:** every meaningful change is recorded here and pushed to **`dev`**. Releases are also merged/pushed to **`main`**.

---

## [Unreleased]

---

## [1.5.0] - 2026-08-10

### Added

- **B2 WAV callouts:** `tts.engine` = `Windows` | `Wav` | `Hybrid` (default **Hybrid**) and `tts.voice_pack` (default `austrian_airlines_en_us`).
  - `WavTtsService` + `HybridTtsService` + `VoicePackManifest` under `Speech/`; playback via built-in `System.Media.SoundPlayer` (no new NuGet).
  - Mapping by `command_id` + kind (`success`/`reject`) from `extras/voices/{pack}/manifest.json`, with `*` reject wildcard.
  - Sample packs: **Austrian Airlines** and **Lufthansa** (`en_us`) with core callouts (gear, lights, flaps, park brake, AP, anti-ice, unable).
  - Settings UI: TTS engine + voice pack ComboBoxes; Debug **Test TTS** exercises pack sample (`gear_up` success) with Windows fallback in Hybrid.
  - `CommandProcessor` passes command id + response kind into TTS; pipeline fingerprint includes engine + pack.

### Changed

- **Package/app version 1.5.0** (`manifest.json`, AssetPackage, host csproj).
- Docs: README / AGENTS / Complete_Features / Backlog aligned to B2 WAV callouts + 1.5.0.

---

## [1.4.0] - 2026-08-10

### Added

- **Learn Mode (L0) complete:**
  - **MVP:** GUI **Learn** tab — watches (status + catalog + manual), detections, Mapped/Unmapped/Ambiguous, create/edit → **Save to active profile**, isolated SimConnect DEF_LEARN (SECOND), self-echo suppress (~750 ms).
  - **Phase G:** `config/learn_watchlist.json` (exclude + default watches), profile `learn_watch` (Fenix seed), debounce (400 ms same-signal), multi-var `GroupId` per Observe pass.
  - **Phase H:** `LearnActionHints` dual-write suggestions (event + set_simvar), **Export JSON** (GUI + `ExportLearnDetections`), headless `--learn-dump` / `--learn-export <path>`.
  - Core: `CommandMappingIndex`, `LearnWatchBuilder`, `LearnCaptureService`, `LearnActionHints`. Spec: [Plan_LearnMode.md](Plan_LearnMode.md).

### Changed

- **Package/app version 1.4.0** (`manifest.json`, AssetPackage, host csproj).
- Docs: README / AGENTS / Complete_Features / Backlog aligned to Learn Mode shipped + 1.4.0.

---

## [1.3.0] - 2026-08-10

### Added

- **Status FLIGHT DATA dashboard:** Aircraft, Airport (best-effort GPS approach id), Altitude, IAS, Vertical speed, On ground — refreshed ~2 Hz while Live.
- **Fenix A320 hybrid profile (`fenix_a320`):** dual-write events + LVars for gear, exterior lights (incl. landing retract 0/1/2), flaps, parking brake, speedbrake, anti-ice/probe, APU, batteries, EXT PWR, fuel, packs, ADIRS, seatbelts, wing/logo/dome; unmapped FCU modes return **Unable**.
- **Fenix checklists with actions:** `before start` / `before takeoff` / `after landing` run multi-step event + LVar sequences (not TTS-only).
- **Manual tab (GUI):** categorized command buttons for the currently loaded catalog → `HostSession.RunCatalogCommand`.
- **Commands tab (GUI):** edit base + active profile; Apply/Save via `HostSession.ApplyCommandSources`.
- **Automatic aircraft profile detection:** `aircraft_detection.json`; default ON; Fenix TITLE → `fenix_a320` before generic A320.
- **Dynamic `list_commands` voice command:** TTS summary + full Debug list from live merged catalog.
- **Live `SetSimVar`:** `A:` / `L:` writes via SimConnect `SetDataOnSimObject` (Native + managed best-effort).
- **Docs SSOT:** `Complete_Features.md` (implemented), `Backlog.md` (open work), `Plan_LearnMode.md` (Learn Mode coding spec); removed `FUTURE.md`.

### Changed

- **Package/app version 1.3.0** (`manifest.json`, AssetPackage, host csproj assembly version).
- **Default auto-detect ON** (`auto_detect_aircraft`, `announce_profile_switch`).
- **Performance thrift:** Apply rebuilds speech grammar only when grammar inputs change; catalog re-merge on profile change; identity ~2 Hz; bounded Debug log.
- **Maintainability:** `HostConstants`, ConfigLoader path helpers, shared SimConnect open/seed helpers.
- **Speech matching:** whole-word token sequences + STT alternate re-rank; default confidence **0.70**.
- **Docs:** README / AGENTS.md / Complete_Features aligned to 1.3.0.

### Fixed

- **Live SimConnect flight data:** Prefer native SimConnect (dispatch thread); fixed invalid status SimVar `ANTITOCK` → `ANTISKID BRAKES ACTIVE`; robust field-count handling.
- **Close exits fully:** Window close disposes session and shuts down (not tray-only).
- **Fenix gear-up gate:** airborne + positive VS (not unreliable `GEAR POSITION == 1`); lever LVar dual-write.
- **Fenix exterior light switch animation** via overhead LVars.
- **Commands tab ListView** dark theme contrast.

---

## [1.2.1] - 2026-08-09

### Changed

- **Visual modernization (dark cockpit theme):** centralized `Themes/DarkCockpit.xaml` ResourceDictionary with polished styles for Window, TabControl/TabItem, Button (incl. primary), TextBox, CheckBox, GroupBox, StatusBar, and full **ComboBox / ComboBoxItem** templates.
- **ComboBox readability:** FontSize 14, MinHeight 36, padding, high-contrast popup (dark panel + light text), clear hover/selected states, wrap/ellipsis for long items; editable text stays high-contrast.
- App.xaml merges the theme dictionary; MainWindow spacing/hierarchy tightened for a calmer sim-side look.

### Fixed

- **Debug log scrollbars:** TextBox template binds `PART_ContentHost` scroll visibility via TemplateBinding (LogTextBox Auto works again).
- **Horizontal ScrollBar height:** orientation-aware ScrollBar style (vertical width / horizontal height) so long NoWrap log lines keep usable bars.

---

## [1.2.0] - 2026-08-09

### Added

- **WPF desktop GUI** for `CoPilotVoiceHost` (default launch):
  - Tabs: **Status**, **Settings**, **Debug**
  - System tray (Show / Hide / Reconnect / Exit), Always on Top, dark theme
  - Status bar + window title Live/Offline
  - Settings Apply / Save / Reload / Open Config Folder
  - Debug: live log, inject phrase, Force Reconnect, Test TTS, continuous-listen toggle
- **`--headless`** CLI flag; `--once` / `--inject` still run console-only pipeline
- Shared **`HostSession`** (no WPF deps) for GUI + headless
- Headless log file `copilot-host-headless.log` next to the EXE
- Unit tests for WinExe/WPF csproj, settings save round-trip, session inject, XAML shell, Apply/Save/Reload speech restart

### Fixed

- **Settings Apply** no longer re-reads `settings.json` and wiping in-memory edits; catalog rebuild uses the selected profile while keeping live Settings.
- **Speech grammar / wake word** rebuild on Apply, Save, and Reload when listening is active (`StartSpeechListening` after pipeline rebuild).

### Changed

- Project: `OutputType=WinExe`, `UseWPF=true`, `UseWindowsForms=true` (tray)
- README documents GUI + headless usage

---

## [1.1.1] - 2026-08-07

### Fixed

- **`SimConnect_Open` HRESULT 0x80004005 while Free Flight is active.**  
  The packaged `SimConnect.dll` was a **Flight Sim World (Dovetail 2018)** client and cannot open against MSFS 2024.  
  Replaced with a **Microsoft/KittyHawk** SimConnect client DLL.
- Added client **`SimConnect.cfg`** targeting IPv4 `127.0.0.1:500` (matches typical local `SimConnect.xml`).
- Multi-index open attempts: settings index, LOCAL (`0xFFFFFFFF`), Pipe, IPv6, Auto.
- Reject known FSW/Dovetail `SimConnect.dll` files so they are not loaded by mistake.
- Pin process working directory to the EXE folder so `SimConnect.cfg` is found.
- Fix managed SimConnect constructor argument types (`uint` vs `int`) when the managed assembly is present.

### Changed

- Ship next to the host: `SimConnect.dll` (MSFS), `SimConnect.cfg`, `Microsoft.FlightSimulator.SimConnect.dll`.
- Clearer console diagnostics for each open strategy.
- Package docs (`docs/README.txt`) note MSFS DLL requirement and Port 500.
- Package version **1.1.1**.

### Notes for testers

1. Fully quit the old host process.
2. Free Flight in cockpit, then start `PackageSources\extras\run_copilot.bat` (or package `extras`).
3. Expect **`IsLive=True`** / `Open OK via ...`.
4. Command e.g. `Co Pilot landing lights on` → `[SimConnect] LIVE event sent: LANDING_LIGHTS_ON`.

---

## [1.1.0] - 2026-08-07

### Fixed

- **Sim events not applied in MSFS** while TTS still answered (e.g. "Landing lights on").  
  Silent offline/recording mode when the managed SimConnect assembly was missing.
- **PTT (F12) only worked once / then only wake word.**  
  PTT was sampled only at recognition completion after key release.

### Added

- **Native SimConnect client** (`NativeSimConnectClient`) via P/Invoke.
- Connect order: managed wrapper (if present) → native DLL → offline only with `--offline`.
- **`IsLive`** flag and console warnings when events are not sent to the sim.
- Live transmit log: `[SimConnect] LIVE event sent: ...`
- **`PttArmService`**: arm on key-down + grace after key-up.
- Settings: `speech.ptt_grace_ms` (default **3000**).
- Unit tests for PTT arming / native search / FSW rejection.

### Changed

- `ActionExecutor` reports per-action failures.
- Offline recording logs `OFFLINE — not sent to sim`.
- README / package docs for IsLive, SimConnect.dll, PTT grace.
- CLI: `--allow-offline-fallback`.

---

## [1.0.0] - 2026-08-07

### Added

- Initial private MSFS 2024 utility package `private-utility-copilot-voice` (MISC, creator Private).
- Community layout: WASM marker module sources, host under `extras`, JSON command profiles.
- Out-of-process **CoPilotVoiceHost** (.NET 8): speech, conditions, TTS, SimConnect abstractions.
- Core commands: gear, lights, flaps, FCU/AP, anti-ice, parking brake, spoilers, APU, checklist callouts.
- Context-aware gear-up (`require_positive_climb_for_gear_up`).
- Wake word **Co Pilot**, optional PTT **F12**, `continuous_listen: false`.
- Unit tests, project README, branches `main` / `dev`.
