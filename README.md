# MSFS 2024 Private Voice Co-Pilot

Private-use **utility** mod for Microsoft Flight Simulator 2024. A voice-controlled co-pilot that understands aviation phraseology, checks flight state via SimConnect, speaks a reply, and sends standard control events (gear, lights, flaps, FCU/AP, anti-ice, parking brake, and more).

- **Package name:** `private-utility-copilot-voice`
- **Creator:** Private  
- **Type:** Misc (Community only — **not** Marketplace)
- **GitHub:** https://github.com/m1kra1/msfs2024_copilot

---

## Architecture (important)

| Layer | Role |
|--------|------|
| **Community package** | Drop-in folder under Community2024. Holds WASM marker + host files. |
| **WASM module** | Marker only (`module_init` / `module_deinit` / `module_update`). **No** co-pilot logic, STT, or TTS. |
| **CoPilotVoiceHost.exe** | Out-of-process .NET 8 host: microphone, speech recognition, TTS, SimConnect, JSON commands. |

**Why the host is separate:** Windows speech/TTS and a stable SimConnect client need a normal desktop process. WASM cannot run that stack in an SDK-friendly way. The official pattern for tools like this is an **out-of-process** app.

**Why you start the host yourself:** Free Flight loads the package; it does **not** auto-launch the EXE. There is no in-sim installer or Marketplace auto-start. Start the host after the sim session is running.

```
MSFS Free Flight  →  Community package + WASM marker
        +
run_copilot.bat   →  CoPilotVoiceHost.exe (voice + SimConnect)
```

---

## Requirements

- Microsoft Flight Simulator **2024**
- Windows (speech + TTS APIs)
- **.NET 8** runtime (for the published host), or build from source with the .NET 8 SDK
- Microphone for voice commands
- Optional: Visual Studio 2022 + **MSFS 2024 Platform Toolset** (to compile the WASM `.wasm` binary)

---

## Install (Community2024)

1. Build or use the package under  
   `private-utility-copilot-voice/Packages/private-utility-copilot-voice/`
2. Copy that folder into your MSFS **Community** directory (Community2024).
3. Start MSFS → **Free Flight** (aircraft loaded).
4. From the package’s `extras` folder, run:

   ```bat
   run_copilot.bat
   ```

   or start `CoPilotVoiceHost.exe` directly.

Host console logs config load, SimConnect status, recognized phrases, and transmitted events.

---

## Voice usage

Default settings (`config/settings.json`):

| Setting | Default |
|---------|---------|
| Wake word | `Co Pilot` |
| PTT key | `F12` |
| Continuous listen | `false` (wake word **or** hold PTT required) |
| Confidence threshold | `0.75` |
| TTS voice | Microsoft David (if installed) |
| Gear-up gate | Positive climb required (`require_positive_climb_for_gear_up`) |

**Examples**

- `Co Pilot positive climb gear up` → checks VS & gear → “Checked. Gear up.” → `GEAR_UP`
- Hold **F12** and say `landing lights on` → no wake word needed while PTT is held
- `Co Pilot flaps up` / `autopilot on` / `anti ice on` / `parking brake set` / …

Bare phrases without wake word or PTT are **ignored** when `continuous_listen` is false.

You can also type phrases into the host console (same gate rules apply unless you use CLI flags).

---

## Configuration

All under `PackageSources/extras/config/` (and the published package `extras/config/`):

| File | Purpose |
|------|---------|
| `settings.json` | SimConnect app name, speech, TTS, behavior flags, aircraft profile name |
| `base_commands.json` | Core phrases, conditions, actions (events / SimVars) |
| `aircraft/generic.json` | Default profile |
| `aircraft/a320.json`, `b737.json` | Aircraft-specific extras / overrides |

Commands are JSON-driven — no aircraft-specific hardcoding in C# for events. Profiles merge on top of the base catalog.

**SimConnect app name:** `PrivateCoPilotVoice` (unique).

---

## Repository layout

```
private-utility-copilot-voice/
├── PackageDefinitions/          # MSFS package definition (MISC, modules + extras)
├── PackageSources/
│   ├── modules/                 # WASM artifact path (+ README until .wasm is built)
│   └── extras/                  # Host EXE, config, docs, voices, run_copilot.bat
├── Packages/                    # Built Community package output
├── Sources/
│   ├── Code/WasmModule/         # Standalone WASM sources
│   └── Host/                    # CoPilotVoiceHost + unit tests (.NET 8)
└── private-utility-copilot-voice.xml
```

---

## Build

### Host (.NET 8)

```bat
cd private-utility-copilot-voice\Sources\Host
dotnet build -c Release
dotnet test -c Release
dotnet publish CoPilotVoiceHost\CoPilotVoiceHost.csproj -c Release -r win-x64 --self-contained false -o ..\..\PackageSources\extras
```

### WASM (optional)

Open `Sources/Code/WasmModule/WasmModule.vcxproj` in VS 2022 with the MSFS 2024 toolset. Output should be:

`PackageSources/modules/private-utility-copilot-voice.wasm`

Without the toolset, the package still works for the **host**; the WASM remains a marker/placeholder.

### MSFS Project Editor package

Load `private-utility-copilot-voice.xml` → Build Package → copy `Packages/private-utility-copilot-voice` to Community.

---

## Host CLI (debug / offline)

```bat
CoPilotVoiceHost.exe --config <path-to-config> --offline --no-tts --no-speech --once
CoPilotVoiceHost.exe --config <path> --offline --inject "Co Pilot gear up" --vs 500
CoPilotVoiceHost.exe --inject "landing lights on" --ptt --offline
```

| Flag | Meaning |
|------|---------|
| `--offline` | No live SimConnect DLL; recording client + fixture SimVars |
| `--inject "..."` | One-shot phrase |
| `--ptt` | Treat inject as PTT held (allow bare phrase) |
| `--vs 500` | Fixture vertical speed (fpm) offline |
| `--once` | Load config / connect path and exit |

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

1. Free Flight loaded  
2. `run_copilot.bat` running, console shows config + SimConnect status  
3. Say: **“Co Pilot positive climb gear up”** (with a real climb, gear down)  
4. Expect TTS: “Checked. Gear up.” and gear retract in sim (when SimConnect is live)  
