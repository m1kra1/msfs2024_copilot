# Changelog

All notable changes to **msfs2024_copilot** / `private-utility-copilot-voice` are documented in this file.

Format based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).  
Versioning follows package `package_version` where applicable.

---

## [1.1.0] — 2026-08-07

### Fixed

- **Sim events not applied in MSFS** while TTS still answered (e.g. “Landing lights on”).  
  The host often ran in silent **offline/recording** mode when the managed SimConnect assembly was missing, so phrases were recognized and spoken but **no client events** reached the simulator.
- **PTT (F12) only worked once / then only wake word.**  
  PTT was sampled only at speech-recognition completion; by then the key was usually already released, so the gate fell back to requiring the wake word.

### Added

- **Native SimConnect client** (`NativeSimConnectClient`) via P/Invoke to `SimConnect.dll` (no Microsoft managed SDK assembly required).
- Connect order: managed wrapper (if present) → **native DLL** → explicit offline only with `--offline`.
- **`IsLive`** flag and clear console warnings when events are not sent to the sim.
- Console line on successful live transmit: `[SimConnect] LIVE event sent: …`
- **`PttArmService`**: arms on key-down and keeps bare phrases accepted for a grace period after key-up.
- Settings: `speech.ptt_grace_ms` (default **3000**).
- Ship **`SimConnect.dll`** next to `CoPilotVoiceHost.exe` under `PackageSources/extras` (and package output).
- Periodic reconnect attempt when not connected; reconnect before action when possible.
- Unit tests for PTT arming / native search paths.

### Changed

- `ActionExecutor` reports per-action failures instead of failing silently after TTS.
- Offline recording logs `OFFLINE — not sent to sim` so live vs offline is obvious.
- README and package `docs/README.txt` document **IsLive**, SimConnect.dll, and PTT grace behaviour.
- Host CLI: `--allow-offline-fallback` (offline inject still uses `--offline`).

### Notes for testers

1. Start **Free Flight**, then `run_copilot.bat`.
2. Confirm console shows **`IsLive=True`**.
3. Wake word: `Co Pilot landing lights on`.
4. PTT: hold **F12**, say `landing lights on`, release (valid for ~3 s after release).
5. Expect: `[SimConnect] LIVE event sent: LANDING_LIGHTS_ON`.

---

## [1.0.0] — 2026-08-07

### Added

- Initial private MSFS 2024 utility package `private-utility-copilot-voice` (MISC, creator Private).
- Community layout: WASM marker module sources, host under `extras`, JSON command profiles (`base_commands`, aircraft stubs).
- Out-of-process **CoPilotVoiceHost** (.NET 8): speech grammar, condition engine, TTS, SimConnect abstractions.
- Core commands: gear, lights, flaps, FCU/AP, anti-ice, parking brake, spoilers, APU, checklist callouts, etc.
- Context-aware gear-up (`require_positive_climb_for_gear_up`).
- Wake word **Co Pilot**, optional PTT **F12**, `continuous_listen: false`.
- Unit tests for phrase match, conditions, actions, host entry.
- Project README with architecture, install, and build notes.
- Branches: `main` (stable), `dev` (development).

---

## Unreleased

- (nothing yet)
