# Changelog

All notable changes to **msfs2024_copilot** / `private-utility-copilot-voice` are documented in this file.

Format based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).  
Versioning follows package `package_version` where applicable.

**Workflow:** every meaningful change is recorded here and pushed to the `dev` branch.

---

## [Unreleased]

### Changed

- **README.md** fully refreshed for 1.2.0: GUI Settings Apply/Save/Reload semantics, speech restart, SimConnect files, CLI flags, quick-test checklist; docs workflow note (README + CHANGELOG stay current on `dev`).

### Added

- **Automatic aircraft profile detection:** when Live, host reads SimConnect `TITLE` / `ATC MODEL`, matches case-insensitive contains rules from `config/aircraft_detection.json` (first match wins; else `fallback_profile`), and switches profile only when the detected identity changes (catalog + speech grammar rebuild). Enable via `settings.json` `auto_detect_aircraft` or GUI Settings checkbox; optional `announce_profile_switch` TTS. CLI `--profile` locks auto switching for the session. Offline/unknown title stays on configured/fallback profile without crashing. Status tab shows detected aircraft (or Unknown) + active profile. Clear `[AircraftDetect] title=… → profile '…'` log lines. GUI Apply uses a detached settings snapshot so false→true auto-detect re-evaluates an already-observed aircraft; Status/Settings profile combo syncs after auto-switch so Apply cannot clobber with a stale selection.
- **Fenix A320 aircraft profile (`fenix_a320`):** selectable via `settings.json` `aircraft_profile` or GUI profile list. Maps core gear / external lights / flaps / parking brake / AP / FD commands to standard SimConnect events Fenix typically honors for hardware; Airbus FCU mode holds (HDG/ALT/SPD/VS/NAV/APP/LOC) return clear **"Unable - not available on this aircraft"** (empty actions) instead of silent no-ops. FCU bug/var knobs kept best-effort. No LVar/H-Event bridge (host limitation). Profile notes document untested-in-CI Fenix version + known limits. Existing `generic` / `a320` / `b737` profiles unchanged.
- **Commands tab (GUI):** view and edit the voice command catalog (merged base + active aircraft profile). Filter, add/delete, edit id/phrases/response/reject/actions/conditions/flags. **Apply** updates the live pipeline in memory; **Save** writes `base_commands.json` + active `aircraft/*.json` and applies. Core stays free of WPF (`HostSession.ApplyCommandSources` / ConfigLoader save).
- **Dynamic `list_commands` voice command:** ask the co-pilot what it can do (`list commands`, `what can you do`, `command list`, `available commands`, …). Builds the list from the **currently loaded** merged catalog (base + active aircraft profile). TTS speaks a short summary (count + example phrases); the full list (command id + phrase per entry) is written to the console / debug log. Empty actions — works Offline and Live; profile Apply/Reload updates the list. No file export.
- **`FUTURE.md`** – Planung für kommende Anpassungen:
  1. ~~Fenix A320-Anpassung~~ -> profile `fenix_a320` (SimConnect events + Unable; LVar bridge still backlog)
  2. ~~Anweisungsliste ausgeben (Sprachbefehl + Konsole/TTS)~~ → implemented as `list_commands`

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
