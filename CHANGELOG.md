# Changelog

All notable changes to **msfs2024_copilot** / `private-utility-copilot-voice` are documented in this file.

Format based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).  
Versioning follows package `package_version` where applicable.

**Workflow:** every meaningful change is recorded here and pushed to the `dev` branch.

---

## [Unreleased]

### Fixed

- **Fenix A320 gear up / positive rate:** Airbus callouts (`positive rate`, `positive rate gear up`, …). Gear-up conditions use **airborne + positive VS** (`SIM ON GROUND == 0`, `VERTICAL SPEED > 0`) instead of unreliable `GEAR POSITION == 1`. Actions send `GEAR_UP`/`GEAR_DOWN` **and** write Fenix lever LVar `L:S_MIP_GEAR` (0=UP, 1=DOWN) via SimConnect `SetDataOnSimObject` (no third-party software).
- **Fenix exterior light switches:** landing / taxi (nose) / strobe / beacon / nav also write Fenix overhead LVars (`L:S_OH_EXT_LT_*`) so cockpit switches animate, not only the light effect from standard events.
- **Speech recognition reliability:** `PhraseMatcher` uses whole-word token sequences (no loose substring `Contains`), re-ranks Windows Speech **alternates** against the catalog (e.g. spoilers vs strobes), snappier end-silence timeouts, more distinct spoiler/strobe phrases, default confidence threshold **0.70**.
- **Live `SetSimVar`:** Native (and managed best-effort) SimConnect clients write `A:` / `L:` vars via `SetDataOnSimObject` (was local-snapshot-only).

### Added

- **Manual tab (GUI):** categorized command buttons for the **currently loaded** catalog (profile-aware). One click runs `HostSession.RunCatalogCommand` (force-gate; conditions still apply). Refresh rebuilds after profile change. Grouping: `CommandCatalogGroups` (Core, no WPF).
- **Modernized dark app chrome:** updated palette, chip buttons, stronger ComboBox selected-text contrast (`TextElement.Foreground` on selection presenter).
- **Automatic aircraft profile detection:** when Live, host reads SimConnect `TITLE` / `ATC MODEL`, matches rules from `config/aircraft_detection.json` (first match; else `fallback_profile`), switches profile only on identity change. CLI `--profile` locks auto switching. Status shows detected aircraft + active profile.
- **Fenix A320 aircraft profile (`fenix_a320`):** gear/lights LVars + standard events; unmapped FCU modes return **Unable**. Existing `generic` / `a320` / `b737` profiles unchanged.
- **Commands tab (GUI):** edit merged base + active profile; Apply/Save via `HostSession.ApplyCommandSources`.
- **Dynamic `list_commands` voice command:** TTS summary + full list in Debug log from live catalog.
- **`FUTURE.md`** planning backlog (Fenix LVar map expanded; deeper H/B-Event bridge still optional).

### Changed

- **Default aircraft auto-detect ON** (`auto_detect_aircraft: true`, `announce_profile_switch: true`) so Fenix TITLE → `fenix_a320` applies automatically when Live.
- **Performance / resource pass (CoPilotVoiceHost):** Settings **Apply** rebuilds speech grammar only when grammar inputs change; catalog re-merge only on profile change; identity eval ~2 Hz; bounded Debug log; no duplicate PttArm timer.
- **Maintainability pass:** `HostConstants`, ConfigLoader path helpers, shared SimConnect open/seed helpers.
- **Docs:** README + AGENTS.md + CHANGELOG updated for Manual tab, live SetSimVar/LVars, speech matching, auto-detect defaults.

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
