# MSFS 2024 Private Voice Co-Pilot

Private-use **utility** mod for Microsoft Flight Simulator 2024. A voice-controlled co-pilot that understands aviation phraseology, checks flight state via SimConnect, speaks a reply, and sends standard control events (gear, lights, flaps, FCU/AP, anti-ice, parking brake, and more).

- **Package name:** `private-utility-copilot-voice`
- **Creator:** Private  
- **Type:** Misc (Community only — **not** Marketplace)
- **Version:** 1.2.0 (see [CHANGELOG.md](CHANGELOG.md))
- **GitHub:** https://github.com/m1kra1/msfs2024_copilot  
- **Planned work:** [FUTURE.md](FUTURE.md)

**Docs workflow:** every meaningful code change updates **README.md** and **CHANGELOG.md**, then is pushed to **`dev`**.

---

## Architecture (important)

| Layer | Role |
|--------|------|
| **Community package** | Drop-in folder under Community2024. Holds WASM marker + host files. |
| **WASM module** | Marker only (`module_init` / `module_deinit` / `module_update`). **No** co-pilot logic, STT, or TTS. |
| **CoPilotVoiceHost.exe** | Out-of-process .NET 8 **WPF** host: GUI + microphone, speech, TTS, SimConnect, JSON commands. Core logic lives in `HostSession` (no WPF deps). |

**Why the host is separate:** Windows speech/TTS and a stable SimConnect client need a normal desktop process. WASM cannot run that stack in an SDK-friendly way. The official pattern for tools like this is an **out-of-process** app.

**Why you start the host yourself:** Free Flight loads the package; it does **not** auto-launch the EXE. There is no in-sim installer or Marketplace auto-start. Start the host after the sim session is running.

```
MSFS Free Flight  →  Community package + WASM marker
        +
run_copilot.bat   →  CoPilotVoiceHost.exe (GUI by default; voice + SimConnect)
```

### Desktop GUI (default)

Starting `CoPilotVoiceHost.exe` without `--headless` opens a **dark cockpit-friendly window**:

| Tab | Contents |
|-----|----------|
| **Status** | SimConnect connected/IsLive/errors, **FLIGHT DATA** (aircraft TITLE, airport, altitude, IAS, V/S, on ground), active profile, last phrase + confidence, last action, mic, versions |
| **Manual** | Categorized buttons for every loaded voice command (active profile). Click to fire (bypasses wake/PTT; conditions still apply). Refresh after profile switch. |
| **Commands** | Browse/filter the merged command catalog; edit phrases, TTS, actions, conditions; Add/Delete; **Apply** (memory) / **Save** (`base_commands.json` + active aircraft profile) |
| **Settings** | Wake word, PTT, confidence, continuous listen, PTT grace, TTS voice, gear-up climb gate, aircraft profile, **auto-detect aircraft** (default on), **announce profile switch**; **Apply / Save / Reload / Open Config Folder** |
| **Debug** | Live log, phrase inject (+ force gate), Force Reconnect, Clear Logs, Test TTS, Reload Config, continuous-listen toggle |

Also: **system tray** (minimize hides to tray; right-click Show / Hide / Reconnect / Exit), **Always on Top**, bottom **status bar** (green/red + Live/Offline), window title `CoPilot Voice Host – [Live|Offline]`.

Theme styles live in `Sources/Host/CoPilotVoiceHost/Themes/DarkCockpit.xaml` (merged from `App.xaml`), including readable **ComboBox** dropdowns (FontSize 14, high contrast, hover/selected states).

#### Settings buttons (important)

| Button | Effect |
|--------|--------|
| **Apply** | Applies edits **in memory only** (does not rewrite `settings.json`). Re-merges the command catalog when the **aircraft profile** changes; rebuilds pipeline services when speech/TTS/behavior inputs change; **restarts speech/grammar only** when grammar inputs change (wake word, culture/engine, profile/phrase set). No-op Apply while listening does not force a speech restart. |
| **Save** | Same as Apply, then **writes** `config/settings.json`. |
| **Reload** | Re-reads `settings.json` + profiles from disk (discards unsaved Apply edits), rebuilds pipeline, restarts speech if it was listening. |
| **Open Config Folder** | Opens the resolved `config` directory in Explorer. |

After Apply/Save, wake word, PTT, continuous listen, and profile phrases take effect on the live mic path without restarting the whole app. The **Debug** log is bounded (~2000 lines in the sink and TextBox) so long sessions do not grow UI memory without limit.

### Headless / CLI mode

Use for automation, scripts, and the old console-style host:

```bat
CoPilotVoiceHost.exe --headless
CoPilotVoiceHost.exe --headless --offline --once
CoPilotVoiceHost.exe --headless --offline --inject "Co Pilot landing lights on"
```

`--once` and `--inject` also force headless (no window). Headless writes **`copilot-host-headless.log`** next to the EXE (WinExe subsystem).

---

## Requirements

- Microsoft Flight Simulator **2024**
- Windows (speech + TTS APIs)
- **.NET 8** runtime (for the published host), or build from source with the .NET 8 SDK
- Microphone for voice commands
- **Microsoft MSFS `SimConnect.dll`** next to the host (KittyHawk client — **not** Flight Sim World / Dovetail). Shipped under `extras` when packaged, plus `SimConnect.cfg` (IPv4 `127.0.0.1:500`).
- Optional: Visual Studio 2022 + **MSFS 2024 Platform Toolset** (to compile the WASM `.wasm` binary)

---

## Install (Community2024)

1. Build or use the package under  
   `private-utility-copilot-voice/Packages/private-utility-copilot-voice/`  
   (or run the published host from `PackageSources/extras/`).
2. Copy the package folder into your MSFS **Community** directory (Community2024), if not already there.
3. Start MSFS → **Free Flight** (aircraft loaded).
4. From the package’s `extras` folder, run:

   ```bat
   run_copilot.bat
   ```

   or start `CoPilotVoiceHost.exe` directly.

5. Check the GUI status bar / Status tab: **Live**. If **Offline**, speech still works but **nothing in the aircraft will move**.  
   Ensure Free Flight is running and the correct **`SimConnect.dll`** + **`SimConnect.cfg`** sit next to `CoPilotVoiceHost.exe`.

Host logs appear in the **Debug** tab (and in headless mode via `copilot-host-headless.log`). Look for `[SimConnect] LIVE event sent: …`.

---

## Voice usage

Default settings (`config/settings.json`):

| Setting | Default |
|---------|---------|
| Wake word | `Co Pilot` |
| PTT key | `F12` |
| Continuous listen | `false` (wake word **or** PTT required) |
| PTT grace | `ptt_grace_ms` = 3000 (bare phrases OK for ~3 s after releasing PTT) |
| Confidence threshold | `0.70` |
| TTS voice | Microsoft David (if installed) |
| Gear-up gate | Positive climb required (`require_positive_climb_for_gear_up`); Fenix: airborne + VS &gt; 0 |
| Auto aircraft detect | **on** (`auto_detect_aircraft`) |
| Live SimConnect | Requires MSFS `SimConnect.dll` + Free Flight; GUI must show **Live** |

**Examples**

- `Co Pilot positive rate gear up` / `positive climb gear up` → conditions → “Checked. Gear up.” → event (+ Fenix lever LVar)
- Hold **F12** and say `landing lights on` → no wake word needed while PTT is held (plus grace after release)
- `Co Pilot arm spoilers` / `flaps up` / `autopilot on` / `parking brake set` / …

Bare phrases without wake word or PTT are **ignored** when `continuous_listen` is false.

Phrase matching uses **whole words** (not loose substrings) and may re-rank Windows Speech **alternates** against the loaded catalog.

In the GUI **Manual** tab you can fire any loaded command by button (bypasses wake/PTT; conditions still apply).  
In the **Debug** tab you can inject phrases (optional **Force** bypasses the wake/PTT gate).

---

## Configuration

All under `PackageSources/extras/config/` (and the published package `extras/config/`):

| File | Purpose |
|------|---------|
| `settings.json` | SimConnect app name, speech, TTS, behavior flags, aircraft profile name, `auto_detect_aircraft`, `announce_profile_switch` |
| `aircraft_detection.json` | Auto-detect rules: case-insensitive contains patterns → profile id + `fallback_profile` |
| `base_commands.json` | Core phrases, conditions, actions (events / SimVars) |
| `aircraft/generic.json` | Default profile |
| `aircraft/a320.json`, `b737.json` | Aircraft-specific extras / overrides |
| `aircraft/fenix_a320.json` | **Fenix A320** — hybrid dual-write (events + Fenix LVars): gear, lights, flaps, park brake, spoilers, anti-ice/APU/overhead, checklists; unmapped FCU modes speak Unable |

**Auto aircraft detection (default on):** when SimConnect is **Live** and `auto_detect_aircraft` is true, the host reads aircraft **TITLE** / **ATC MODEL** (period SECOND, not a busy poll) and applies the first matching rule in `aircraft_detection.json` (e.g. title contains `fenix` → `fenix_a320`). Profile switches only when the detected identity changes. CLI `--profile` locks the profile for that process (auto still shows the title but does not switch). Offline/Unknown → no crash; keep configured profile.

### Study-level LVar strategy

Study aircraft (Fenix A320 today; iniBuilds A350 later) use **profile overrides**, not a global LVar rewrite of `base_commands.json`:

| Rule | Detail |
|------|--------|
| Base catalog | Standard SimConnect **events** for generic / Asobo |
| Study profile | Same command `id` **replaces** base with dual-write: `event` + `set_simvar` (`L:` / `A:`) |
| New profile-only ids | Append (e.g. Fenix `fuel_pumps_on`, `adirs_nav`, `seatbelt_signs_on`) |
| Gaps | Prefer **Unable** TTS over guessed mappings (FCU modes) |
| Cross-vendor | Separate LVar maps per aircraft — Fenix names must not be reused for A350 |

**Fenix A320 profile:** auto when TITLE matches `fenix`, or select in Settings. Mapped groups include gear (`L:S_MIP_GEAR`), exterior lights (`L:S_OH_EXT_LT_*`), flaps (`L:S_FC_FLAPS` 0–4), parking brake (`L:S_MIP_PARKING_BRAKE`), speedbrake (`L:A_FC_SPEEDBRAKE`), anti-ice / probe / APU / batteries / fuel / packs / ADIRS / seatbelts, and **checklists with real actions**. Writes go through SimConnect `SetDataOnSimObject` (no SPAD/AAO). H/B-Events are not bridged. Live Free Flight re-test after Fenix updates.

**Action types in JSON:**

| `type` | Meaning |
|--------|---------|
| `event` | `TransmitClientEvent` (e.g. `GEAR_UP`, `LANDING_LIGHTS_ON`) |
| `set_simvar` / `simvar` | Live write of `A:` or `L:` name when connected (`SetDataOnSimObject`) |

Next to the host EXE (also under `extras/`):

| File | Purpose |
|------|---------|
| `SimConnect.dll` | Microsoft MSFS client (KittyHawk) |
| `SimConnect.cfg` | Client config (default IPv4 127.0.0.1 Port **500**) |
| `Microsoft.FlightSimulator.SimConnect.dll` | Optional managed wrapper |

Commands are JSON-driven — no aircraft-specific hardcoding in C# for events. Profiles merge on top of the base catalog.

**List available commands:** say *“Co Pilot what can you do”* / *“list commands”* / *“command list”* / *“available commands”*. The host builds the list from the **currently loaded** catalog (base + active aircraft profile): TTS gives a short summary; the full list (id + phrase) is written to the console / Debug log. Works Offline and Live; changes with profile Apply/Reload.

**SimConnect app name:** `PrivateCoPilotVoice` (unique).

Server-side listen ports come from  
`%APPDATA%\Microsoft Flight Simulator 2024\SimConnect.xml` (typically Port 500 IPv4).

---

## Repository layout

```
CO_Pilot_msfs2024/
├── README.md
├── CHANGELOG.md
├── FUTURE.md
└── private-utility-copilot-voice/
    ├── PackageDefinitions/          # MSFS package definition (MISC, modules + extras)
    ├── PackageSources/
    │   ├── modules/                 # WASM artifact path (+ README until .wasm is built)
    │   └── extras/                  # Host EXE, config, SimConnect, docs, run_copilot.bat
    ├── Packages/                    # Built Community package output
    ├── Sources/
    │   ├── Code/WasmModule/         # Standalone WASM sources
    │   └── Host/                    # CoPilotVoiceHost (WPF) + unit tests (.NET 8)
    └── private-utility-copilot-voice.xml
```

Host source highlights:

- `Host/HostSession.cs` — session shared by GUI and headless (settings, speech, SimConnect, inject, `RunCatalogCommand`)
- `Ui/MainWindow.xaml` — Status / Manual / Settings / Commands / Debug
- `Core/CommandCatalogGroups.cs` — Manual-tab categories (no WPF)
- `Core/PhraseMatcher.cs` — whole-word match + STT alternate re-rank
- `Config/`, `Core/`, `SimConnect/`, `Speech/` — no WPF dependencies

---

## Build

### Host (.NET 8 / WPF)

```bat
cd private-utility-copilot-voice\Sources\Host
dotnet build -c Release
dotnet test -c Release
dotnet publish CoPilotVoiceHost\CoPilotVoiceHost.csproj -c Release -r win-x64 --self-contained false -o ..\..\PackageSources\extras
```

Project: `OutputType=WinExe`, `TargetFramework=net8.0-windows`, `UseWPF=true`.

### WASM (optional)

Open `Sources/Code/WasmModule/WasmModule.vcxproj` in VS 2022 with the MSFS 2024 toolset. Output should be:

`PackageSources/modules/private-utility-copilot-voice.wasm`

Without the toolset, the package still works for the **host**; the WASM remains a marker/placeholder.

### MSFS Project Editor package

Load `private-utility-copilot-voice.xml` → Build Package → copy `Packages/private-utility-copilot-voice` to Community.

---

## Host CLI (debug / offline)

```bat
CoPilotVoiceHost.exe --headless --config <path-to-config> --offline --no-tts --no-speech --once
CoPilotVoiceHost.exe --headless --config <path> --offline --inject "Co Pilot gear up" --vs 500
CoPilotVoiceHost.exe --headless --inject "landing lights on" --ptt --offline
```

| Flag | Meaning |
|------|---------|
| `--headless` | Console-only (no WPF window) |
| `--offline` | Recording client + fixture SimVars (no live sim events) |
| `--inject "..."` | One-shot phrase (forces headless) |
| `--ptt` | Treat inject as PTT held (allow bare phrase) |
| `--vs 500` | Fixture vertical speed (fpm) offline |
| `--once` | Load config / connect path and exit (forces headless) |
| `--config <dir>` | Config directory containing `settings.json` |
| `--profile name` | Aircraft profile override (`generic`, `a320`, `b737`, …) |
| `--no-tts` / `--no-speech` | Disable Windows TTS / mic recognizer |
| `--bypass-gate` | Skip wake-word/PTT gate on inject |
| `--allow-offline-fallback` | Fall back to recording if live connect fails |

---

## Design rules (do not break)

1. **Utility mod**, not aircraft package — no 3D/LODs/Marketplace flow  
2. **Out-of-process host** for STT/TTS/SimConnect  
3. **No core logic in WASM**  
4. **SimConnect only** (standard events/SimVars; aircraft LVars/H-events only via JSON profiles)  
5. **Context-aware** actions (e.g. gear up needs positive climb when enabled)  
6. **Modular JSON** profiles; unique SimConnect IDs  
7. **No busy SimVar polling** — period/event style (status ~1 Hz / ≤ 5 Hz intent)  
8. **Private use** — Community copy only  
9. **Core free of WPF** — UI only in `Ui/` + bootstrap  

---

## Branches

| Branch | Use |
|--------|-----|
| `main` | Stable baseline |
| `dev` | Active development (default working branch) |

---

## License / use

Private personal project. Creator field is **Private**. Not intended for Marketplace distribution or Content Manager validation. Review and use at your own risk.

---

## Quick test checklist

### GUI

1. Free Flight loaded  
2. `run_copilot.bat` → window opens (dark theme)  
3. Status bar shows **Live** (or Offline if SimConnect not connected)  
4. Settings → change wake word → **Apply** → still shows new value; **Save** writes `settings.json`  
5. Debug → inject `Co Pilot landing lights on` → last action / log updates  
6. Minimize → tray icon keeps host running  

### Voice / live sim

1. Free Flight + **Live** in status bar  
2. Say: **“Co Pilot positive climb gear up”** (real climb, gear down)  
3. Expect TTS + `[SimConnect] LIVE event sent: GEAR_UP` and gear retract  

### Headless

```bat
CoPilotVoiceHost.exe --headless --offline --inject "Co Pilot landing lights on"
```

Expect exit 0 and `LANDING_LIGHTS_ON` in `copilot-host-headless.log`.
