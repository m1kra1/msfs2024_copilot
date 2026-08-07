# Changelog

All notable changes to **msfs2024_copilot** / `private-utility-copilot-voice` are documented in this file.

Format based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).  
Versioning follows package `package_version` where applicable.

**Workflow:** every meaningful change is recorded here and pushed to the `dev` branch.

---

## [Unreleased]

- (nothing yet)

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
