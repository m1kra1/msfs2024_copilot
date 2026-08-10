# Plan: B2 — WAV-Callouts statt / zusätzlich zu Windows TTS

**Feature-ID:** B2  
**Prio:** P1  
**Backlog:** [Backlog.md](https://github.com/m1kra1/msfs2024_copilot/blob/dev/Backlog.md)  
**Branch:** `dev`  
**Status:** Spec ready — implement on explicit coding request  

This document is the **binding coding-agent specification**. Follow [AGENTS.md](https://github.com/m1kra1/msfs2024_copilot/blob/dev/AGENTS.md). Do not invent Marketplace packaging, WASM co-pilot logic, or busy SimVar polling.

---

## 1. Goal

Enable short, authentic airline-style WAV callouts from `extras/voices/…` as primary or fallback TTS responses instead of (or in addition to) Windows SAPI TTS.

**User capabilities:**
- Select TTS engine via settings: `"Windows"` | `"Wav"` | `"Hybrid"`.
- Select voice pack via settings (initial packs: `austrian_airlines_en_us`, `lufthansa_en_us`, and the existing placeholder `copilot_en_us`).
- Hear pre-recorded WAVs for known command responses (success + reject) when available.
- Automatic fallback to Windows TTS in Hybrid mode when no matching WAV exists.
- Core remains completely UI-free; HostSession selects the concrete `ITtsService` implementation at pipeline-build time.

**Result:** Authentic co-pilot voice experience with ready-to-use Austrian Airlines and Lufthansa sample packs while preserving full backward compatibility and offline/headless operability.

**Included sample content (must be shipped):**
- Complete minimal voice packs for **Austrian Airlines** and **Lufthansa** (English professional callouts) with matching `manifest.json` already mapping the core commands.
- Files are provided alongside this Plan and must be placed under `PackageSources/extras/voices/`.

---

## 2. Non-goals (do not promise / do not build in MVP)

| Non-goal | Reason |
| --- | --- |
| Full coverage of every command response | Only core high-frequency callouts are pre-mapped; others fall back |
| New NuGet dependencies (NAudio, CSCore, etc.) | Prefer built-in .NET 8 APIs (`System.Media.SoundPlayer` or equivalent) for WAV playback |
| Changing command JSON schema for response text | Mapping is external via manifest (command_id + kind) |
| WASM involvement or in-sim audio | Architecture lock: all TTS lives in the out-of-process host |
| Multi-language packs beyond the two provided | Only the two English packs + placeholder in MVP |
| GUI voice-pack editor or recording UI | Settings selection of engine + pack only; Debug “Test TTS” continues to work |
| Real airline-licensed voice talent | Samples are synthetic professional voices generated for this feature |

**Honest product copy (UI banner / Debug note):**  
*“WAV callouts require matching files under extras/voices/. Missing files fall back to Windows TTS in Hybrid mode. Sample packs: Austrian Airlines & Lufthansa included.”*

---

## 3. User flows

### 3.1 Happy path — Hybrid mode (recommended default)

1. User sets `tts.engine = "Hybrid"` and optionally chooses a voice pack (e.g. `austrian_airlines_en_us`).
2. Command is recognized → ConditionEngine succeeds → response produced.
3. `ITtsService.SpeakAsync` looks up mapping for command `id` + response kind (success / reject).
4. Matching relative WAV path exists → play WAV (non-blocking, respect `callout_delay_ms`).
5. No matching file → fall back to existing Windows TTS with current voice/rate/volume.

### 3.2 Pure Wav mode

1. `tts.engine = "Wav"`.
2. Matching WAV exists → play.
3. No matching WAV → silent (log warning).

### 3.3 Windows mode (legacy)

1. `tts.engine = "Windows"` (or missing key after migration).
2. Behaviour identical to current implementation. Voice pack ignored.

### 3.4 Edge cases

- Headless / `--no-tts`: TTS service is a no-op regardless of engine.
- Missing voices folder or manifest: treat as empty mapping → Hybrid falls back, Wav stays silent.
- Concurrent Speak calls: queue or cancel previous (match existing Windows TTS behaviour).
- Apply/Save/Reload of settings: rebuild pipeline services so new engine/pack takes effect without full process restart.
- Wildcard reject entry (`command_id: "*"`) covers any unmapped reject.

---

## 4. Architecture

```
HostSession.RebuildPipelineServices()
        │
        ▼
Settings.tts.engine + tts.voice_pack ──► factory
        │
        ├── "Windows"  → WindowsTtsService (existing)
        ├── "Wav"      → WavTtsService
        └── "Hybrid"   → HybridTtsService (Wav first, then Windows)
                │
                ▼
ITtsService.SpeakAsync(text, optional commandId, responseKind)
                │
                ▼
Manifest lookup (command_id + kind, with "*" fallback for reject)
                │
                ▼
System.Media.SoundPlayer (or equivalent .NET built-in) + existing delay
```

### Constraints

* Core (`HostSession`, `Speech/`, `Config/`, `Core/`) remains completely free of WPF.
* No changes to WASM module.
* Pipeline rebuild already exists for TTS fingerprint changes — reuse it.
* Mapping is data-driven (JSON manifests), never hardcoded in C#.
* Relative paths only; resolve against the package `extras` root (same resolution used for config).

---

## 5. Data model

Extend existing `tts` section in `settings.json`:

```json
"tts": {
  "engine": "Hybrid",
  "voice": "Microsoft David",
  "rate": 0,
  "volume": 100,
  "voice_pack": "austrian_airlines_en_us"
}
```

- `engine`: `"Windows"` | `"Wav"` | `"Hybrid"` (default after migration: `"Hybrid"`)
- `voice_pack`: folder name under `extras/voices/` (default: `"austrian_airlines_en_us"`)

**Provided manifests (must be copied as-is):**

`extras/voices/austrian_airlines_en_us/manifest.json` and  
`extras/voices/lufthansa_en_us/manifest.json`

Both already contain correct mappings for:

| command_id          | kind     | file                     |
|---------------------|----------|--------------------------|
| gear_up             | success  | gear_up_checked.wav      |
| gear_down           | success  | gear_down_checked.wav    |
| landing_lights_on   | success  | landing_lights_on.wav    |
| flaps_up            | success  | flaps_up.wav             |
| parking_brake_on    | success  | parking_brake_set.wav    |
| autopilot_on        | success  | autopilot_on.wav         |
| anti_ice_on         | success  | anti_ice_on.wav          |
| gear_up             | reject   | unable.wav               |
| *                   | reject   | unable.wav               |

(Additional entries can be added later without code changes.)

---

## 6. Core components

### 6.1 `ITtsService` (existing or introduce if missing)

* Contract must support at least:
  - `Task SpeakAsync(string text, CancellationToken ct = default)`
  - Optional context carrying `commandId` + `responseKind` so Wav/Hybrid can look up files without parsing free text.
* Existing Windows implementation continues to ignore the optional context.

### 6.2 `WavTtsService`

* Resolves `extras/voices/{voice_pack}/` + manifest.
* Plays WAV via built-in .NET API (no new packages).
* Thread-safe / non-blocking relative to the recognition pipeline.
* Supports `"*"` wildcard for reject kind.

### 6.3 `HybridTtsService`

* Tries Wav first; on missing mapping or file error → delegates to WindowsTtsService.
* Single place for fallback policy.

### 6.4 Manifest loader

* Located under Config or Speech.
* Loads once at pipeline build; reloads on Apply/Save/Reload when voice_pack or engine changes.
* Missing or invalid manifest → empty dictionary (safe fallback).

---

## 7. Interface & HostSession wiring

### 7.1 `ITtsService`

Keep or introduce the interface so HostSession can inject any implementation without knowing concrete types.

### 7.2 HostSession

* In `RebuildPipelineServices` (or equivalent factory method):
  - Read `settings.tts.engine` and `settings.tts.voice_pack`.
  - Instantiate Windows / Wav / Hybrid accordingly.
  - Pass resolved extras root and voice_pack name.
* Existing call sites that invoke TTS continue to call the same interface method.
* `--no-tts` continues to install a null/no-op service.

### 7.3 Critical

* Never put WAV path resolution or SoundPlayer calls into UI code or into command catalog logic.
* Do not change the order of the runtime pipeline (Speech gate → PhraseMatcher → ConditionEngine → TTS → ActionExecutor).

---

## 8. HostSession / Settings wiring

| API / Point | Notes |
| --- | --- |
| Settings model | Add `Engine` and `VoicePack` under existing `tts` object. Migration: missing key → `"Hybrid"` / `"austrian_airlines_en_us"`. |
| Apply / Save / Reload | Already rebuilds TTS services on TTS fingerprint change — ensure new engine + voice_pack fields are part of the fingerprint. |
| Debug “Test TTS” | Must exercise the currently selected engine + pack (including Hybrid fallback). |
| Headless | Fully supported; same service selection. |

**Offline testing:** WAV files are local; no SimConnect dependency. Unit tests can supply a temporary voices folder.

---

## 9. UI — Settings & Debug

### 9.1 Placement

Settings tab → existing TTS group:  
- ComboBox for Engine (`Windows` / `Wav` / `Hybrid`)  
- ComboBox for Voice Pack (populated from available folders under voices/ or hard-coded list of known packs for MVP)

### 9.2 Layout (text wireframe)

```
TTS
  Engine:     [ Hybrid                    ▼ ]
  Voice Pack: [ austrian_airlines_en_us   ▼ ]
  Voice:      [ Microsoft David           ▼ ]   (only relevant for Windows/Hybrid)
  Rate / Volume (existing)
```

### 9.3 Thin-shell rules

* UI only reads/writes the settings model and calls `HostSession.Apply` / `Save`.
* No direct file-system or SoundPlayer calls from MainWindow or any XAML code-behind.
* Debug “Test TTS” button continues to call the session’s TTS service.

---

## 10. Config files (Phase 1 vs 2)

| Phase | Config |
| --- | --- |
| **1 MVP** | `settings.json` gains `tts.engine` + `tts.voice_pack`. Two complete voice packs (Austrian + Lufthansa) with WAVs + manifests placed under `PackageSources/extras/voices/`. |
| **2** | Additional packs, more command coverage, optional `voice_key` inside command JSON if needed. |

Do not hand-edit `bin/` or `Packages/` copies. Source of truth remains `PackageSources/extras/`.

**Required copy during implementation:**
```
artifacts/voices/austrian_airlines_en_us/  →  PackageSources/extras/voices/austrian_airlines_en_us/
artifacts/voices/lufthansa_en_us/          →  PackageSources/extras/voices/lufthansa_en_us/
```
(The WAVs and manifests are already correctly assigned.)

---

## 11. File touch list (expected)

| Path | Action |
| --- | --- |
| `Sources/Host/CoPilotVoiceHost/Speech/` (or Core) | Add `WavTtsService.cs`, `HybridTtsService.cs`, manifest loader |
| `Sources/Host/CoPilotVoiceHost/Config/` or Models | Extend settings TTS model |
| `Host/HostSession.cs` | Factory selection of ITtsService |
| `PackageSources/extras/config/settings.json` | Add `engine` + `voice_pack` |
| `PackageSources/extras/voices/austrian_airlines_en_us/` | Copy complete pack (WAVs + manifest) |
| `PackageSources/extras/voices/lufthansa_en_us/` | Copy complete pack (WAVs + manifest) |
| `PackageSources/extras/voices/copilot_en_us/` | Keep existing placeholder (optional empty manifest) |
| `Ui/MainWindow` (Settings) | Engine + Voice Pack ComboBoxes |
| Unit tests | New tests for mapping lookup, Hybrid fallback, missing files, engine/pack selection, wildcard reject |
| `CHANGELOG.md` / `README.md` | Document new settings and the two sample packs |

---

## 12. Implementation phases

### Phase 1 — MVP (required for P1 done)

1. Extend settings model + migration default (`Hybrid` / `austrian_airlines_en_us`).
2. Introduce / confirm `ITtsService` contract with optional context.
3. Implement `WavTtsService` + manifest loader (supports `"*"` wildcard).
4. Implement `HybridTtsService`.
5. Wire factory inside HostSession pipeline rebuild.
6. Copy the two provided voice packs into `PackageSources/extras/voices/`.
7. Settings UI ComboBoxes + Debug Test TTS verification.
8. Unit tests for selection, lookup, fallback, missing files, wildcard.
9. Docs + CHANGELOG entry.

### Phase 2 — Polish

* Volume control for WAV if feasible with chosen playback API.
* More command coverage in the manifests.
* Auto-discovery of available voice packs for the ComboBox.

### Phase 3 — Stretch

* Additional airline packs.
* Simple WAV validation on load (sample rate, channels).

---

## 13. Tests (mandatory)

Project: `CoPilotVoiceHost.Tests`

| Test | Expectation |
| --- | --- |
| Engine selection | `"Windows"` → Windows service; `"Wav"` → Wav; `"Hybrid"` → Hybrid |
| Voice pack selection | Correct folder + manifest loaded |
| Missing manifest | Hybrid falls back; Wav stays silent |
| Matching entry | Correct relative path resolved and playback attempted |
| Wildcard reject (`*`) | Used when no specific reject mapping exists |
| Non-matching command_id | Hybrid falls back to Windows |
| `--no-tts` | No-op service regardless of engine |
| Settings round-trip | `engine` + `voice_pack` survive Save → Reload |
| Pipeline rebuild | Changing engine or pack on Apply rebuilds the live service |

Run:

```
dotnet test private-utility-copilot-voice/Sources/Host/CoPilotVoiceHost.Tests -c Release
```

---

## 14. Acceptance criteria (Phase 1 done)

* `tts.engine` and `tts.voice_pack` readable/writable in settings.json and Settings UI.
* HostSession selects correct concrete service + pack on pipeline build.
* Hybrid mode plays the correct Austrian or Lufthansa WAV when mapping + file exist, otherwise Windows TTS.
* The two sample packs are present under `PackageSources/extras/voices/` with working manifests.
* Wav mode is silent on missing mapping.
* Windows mode unchanged.
* Core remains WPF-free; no WASM changes.
* Existing unit tests + new tests green.
* Debug “Test TTS” exercises the selected engine + pack.
* Documentation updated (README, CHANGELOG).
* No new NuGet packages.

---

## 15. AGENTS.md checklist (agent must verify)

* Core free of WPF
* No logic in WASM
* JSON-first (manifest + settings)
* Pipeline order untouched
* No busy polling
* Settings Apply does not re-read from disk
* Tests added and passing
* PackageSources is source of truth
* Provided Austrian + Lufthansa packs are copied and functional

---

## 16. Suggested commit order

1. `feat(b2): extend tts settings model + migration (engine + voice_pack)`
2. `feat(b2): ITtsService contract + WavTtsService + manifest loader (with * wildcard)`
3. `feat(b2): HybridTtsService + HostSession factory`
4. `feat(b2): add Austrian Airlines + Lufthansa sample voice packs`
5. `feat(b2): Settings UI engine + voice pack selectors`
6. `test(b2): engine/pack selection, lookup, fallback, wildcard`
7. `docs(b2): README, CHANGELOG, voices packs`

---

## 17. Relationship to other backlog items

| Item | Relation |
| --- | --- |
| **L0 Learn-Modus** | Orthogonal; Learn mode may later generate response keys that map into the same manifests |
| **B5 Dynamic Info-Commands** | Dynamic responses will fall back to Windows TTS unless explicit mapping entries are added |
| **D5 Tests** | This feature adds concrete test coverage that D5 can later expand |
| **E1 Docs-SSOT** | Document the new `tts.engine` / `tts.voice_pack` values once implemented |

---

## 18. Open decisions (defaults if unset)

| Decision | Default |
| --- | --- |
| Default value of `tts.engine` after migration | `"Hybrid"` |
| Default `tts.voice_pack` | `"austrian_airlines_en_us"` |
| Mapping key strategy | `command_id` + `kind` (`success`/`reject`) via manifest.json, with `"*"` wildcard for reject |
| Playback API | Built-in .NET (`System.Media.SoundPlayer` or equivalent) — no new dependencies |
| Missing file behaviour in pure Wav mode | Silent (log warning) |
| Sample packs | Austrian Airlines + Lufthansa (already generated and mapped) |

---

*End of Plan_B2_WavCallouts.md — implement only when the user explicitly requests coding for B2.*

**Note for implementer:** The sample WAV files + manifests for both airline packs are already generated and correctly assigned. Copy them from the artifacts/voices/ directory (or the location supplied with this Plan) into `PackageSources/extras/voices/` as the first concrete step of Phase 1.
