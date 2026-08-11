# Complete Features — Private Voice Co-Pilot (MSFS 2024)

Vollständige Übersicht aller **bereits umgesetzten** Funktionalitäten des Projekts  
`private-utility-copilot-voice` / **msfs2024_copilot**.

| | |
|--|--|
| **Stand** | 2026-08-11 |
| **Branch** | `main` / `dev` |
| **Baseline** | Package/App **1.6.2** |
| **Geplante Arbeit** | siehe [Backlog.md](Backlog.md) · Live-Test: [Live_Testing.md](Live_Testing.md) · Learn Mode Spec: [Plans/Plan_LearnMode.md](Plans/Plan_LearnMode.md) |

---

## 1. Projektidentität

| Aspekt | Umsetzung |
|--------|-----------|
| Zweck | Private Utility-Mod: sprachgesteuerter Co-Pilot |
| Simulator | Microsoft Flight Simulator **2024** |
| Package | `private-utility-copilot-voice` |
| Creator | Private |
| Typ | MISC (Community only, kein Marketplace) |
| Pipeline | STT → Phrase-Match → Conditions → TTS + SimConnect Events/LVars |

---

## 2. Architektur (locked & implementiert)

| Schicht | Rolle |
|---------|--------|
| **Community-Package** | Drop-in unter Community2024: WASM-Marker + `extras/` (Host, Config, SimConnect) |
| **WASM-Modul** | Marker only (`module_init` / `module_deinit` / `module_update`). **Keine** Co-Pilot-Logik, kein STT/TTS |
| **CoPilotVoiceHost.exe** | Out-of-process .NET 8 **WPF**-Host (GUI default) + Headless-CLI |
| **HostSession** | Shared Session für GUI und Headless — **keine WPF-Abhängigkeiten** |
| **Core** | `Config/`, `Core/`, `SimConnect/`, `Speech/`, `Models/`, `Diagnostics/` frei von UI |

### Designregeln (umgesetzt)

1. Utility-Mod only (kein Aircraft-Package / keine 3D-Assets)
2. Out-of-process Host für STT / TTS / SimConnect
3. Keine Core-Logik im WASM
4. Nur SimConnect (Standard-Events + `set_simvar` für `A:`/`L:`; kein SPAD/AAO)
5. Context-aware Actions (z. B. Gear-up mit Climb-Gate)
6. Modular JSON-Profile; SimConnect-App-Name **`PrivateCoPilotVoice`**
7. Kein busy SimVar-Polling (Period/Event-Style)
8. Private Use only
9. Core free of WPF

---

## 3. Runtime-Pipeline

Feste Reihenfolge (nicht umordnen):

1. **Speech-Gate** — Wake Word / PTT / `continuous_listen`  
   (Windows STT kann Hypothesen über `PhraseMatcher.MatchBestHypothesis` re-ranken)
2. **PhraseMatcher** — Whole-Word-Token-Sequenzen (exact / end / contiguous; **kein** loses `Contains`); längere Phrasen gewinnen bei Gleichstand
3. **ConditionEngine** — SimVars + Behavior-Flags
4. **TTS-Response** — optional Confirm-Delay (`callout_delay_ms`); engine **Windows** / **Wav** / **Hybrid** with optional voice-pack WAV by `command_id` + kind
5. **ActionExecutor** — `event` und/oder `set_simvar` (übersprungen bei `actions: []`)

### Einstiegspunkte

| API | Verwendung |
|-----|------------|
| `HostSession.InjectPhrase` / `HandlePhrase` | GUI Debug-Inject, Headless `--inject` → `CommandProcessor.Process` |
| `HostSession.RunCatalogCommand(id)` | Manual-Tab: Force-Gate + erste Phrase / Wake-prefixed |

---

## 4. Host-Modi

### 4.1 Desktop-GUI (Default)

Start ohne `--headless` öffnet ein dunkles Cockpit-UI (`Themes/DarkCockpit.xaml`).

| Tab | Funktion |
|-----|----------|
| **Status** | SimConnect Connected / IsLive / Fehler; **FLIGHT DATA** (Aircraft TITLE, Airport best-effort, Altitude, IAS, V/S, On Ground) ~2 Hz live; aktives Profil; letzte Phrase/Confidence; letzte Action; Mic; Versionen |
| **Manual** | Kategorisierte Buttons für den **aktuell geladenen** Katalog (profilabhängig). Klick → `RunCatalogCommand` (bypassed Wake/PTT; Conditions gelten weiter). Refresh nach Profilwechsel |
| **Learn** | Control Capture (Live): Watches (Status + Katalog + watchlist + profile `learn_watch` + Manual); Debounce/Group; Action-Hints; Export JSON; Create/Edit → Save **aktives Profil**; DEF_LEARN SECOND; Suppress ~750 ms |
| **Commands** | Browse/Filter des gemergten Katalogs; Phrases/TTS/Actions/Conditions editieren; Add/Delete; **Apply** (Memory) / **Save** (`base_commands.json` + aktives Aircraft-Profil) |
| **Settings** | Wake Word, PTT, Confidence, Continuous Listen, PTT Grace, **TTS engine** (Windows/Wav/Hybrid), **voice pack**, Windows TTS Voice, Gear-up Climb Gate, Aircraft Profile, Auto-Detect, Announce Profile Switch; **Apply / Save / Reload / Open Config Folder**. **Dirty-guard:** unapplied edits are not overwritten by live Status refresh (~2 Hz) |
| **Debug** | Live-Log (bounded ~2000 Zeilen), Phrase-Inject (+ Force Gate), Force Reconnect, Clear Logs, Test TTS, Reload Config, Continuous-Listen-Toggle |

**Weitere UI-Features**

- System Tray (Minimize → Tray; Kontextmenü Show / Hide / Reconnect / Exit)
- Always on Top
- Statusleiste grün/rot + Live/Offline
- Fenstertitel `CoPilot Voice Host – [Live|Offline]`
- Schließen (X) beendet Session/SimConnect/Speech und die App vollständig (kein reines Tray-Minimize)

### 4.2 Headless / CLI

```bat
CoPilotVoiceHost.exe --headless
CoPilotVoiceHost.exe --headless --offline --once
CoPilotVoiceHost.exe --headless --offline --inject "Co Pilot landing lights on"
```

| Flag | Bedeutung |
|------|-----------|
| `--headless` | Nur Konsole (kein WPF-Fenster) |
| `--offline` | Recording-Client + Fixture-SimVars |
| `--inject "..."` | One-Shot-Phrase (forciert Headless) |
| `--ptt` | Inject als PTT gehalten (bare Phrase erlaubt) |
| `--vs 500` | Fixture Vertical Speed (fpm) offline |
| `--once` | Config/Connect-Pfad und Exit (forciert Headless) |
| `--config <dir>` | Config-Verzeichnis mit `settings.json` |
| `--profile name` | Aircraft-Profil-Override; sperrt Auto-Switch |
| `--no-tts` / `--no-speech` | Windows TTS / Mic-Recognizer aus |
| `--bypass-gate` | Wake/PTT-Gate bei Inject überspringen |
| `--allow-offline-fallback` | Fallback auf Recording wenn Live-Connect scheitert |

Headless schreibt **`copilot-host-headless.log`** neben die EXE.

---

## 5. Speech & TTS

### Speech (Windows STT)

| Setting | Default (shipped) |
|---------|-------------------|
| Engine | Windows |
| Culture | `en-US` |
| Wake Word | `Co Pilot` |
| PTT Key | `F12` |
| Continuous Listen | `false` (Wake **oder** PTT nötig) |
| Confidence | `0.70` |
| PTT Grace | `ptt_grace_ms` = 3000 ms nach Key-Up |

**Implementierte Komponenten**

- `SpeechInputGate` — Wake / PTT / continuous
- `PttArmService` — Arm on key-down + Grace nach Release (kein „nur einmal“-Bug)
- `PttKeyboard` — Win32 `GetAsyncKeyState`
- `WindowsSpeechRecognitionService` — Grammar aus Katalog-Phrasen; Alternate-Hypothesen
- `PhraseMatcher` — Normalisierung, Token-Match, Score, `MatchBestHypothesis`

### TTS (Windows)

- `ITtsService` / Windows-TTS
- Voice (Default: Microsoft David), Rate, Volume aus `settings.json`
- Confirm-before-action + `callout_delay_ms` (Default 400)
- Test-TTS aus Debug-Tab und Session-API

---

## 5b. Learn Mode (L0 — full 1.4.0)

| Aspekt | Umsetzung |
|--------|-----------|
| UI | Tab **Learn** (nach Manual): Toggle, Manual Watch, Detection-Liste, Only-unmapped, Create/Edit, Save to profile, **Export JSON** |
| Core | `CommandMappingIndex`, `LearnWatchBuilder`, `LearnCaptureService`, `LearnActionHints` (kein WPF) |
| SimConnect | Isolierte DEF_LEARN / REQ_LEARN (SECOND); Native voll; Recording für Tests; Managed no-op |
| Watches | Diskrete Status-SimVars + Catalog set_simvar/conditions + **`learn_watchlist.json`** + profile **`learn_watch`** + Manual; Cap 128 |
| Debounce | Same-signal within 400 ms updates latest row; multi-var same Observe → shared `GroupId` |
| Mapping | 0 → Unmapped, 1 → Mapped, >1 → Ambiguous |
| Suggestions | `set_simvar` + optional dual-write **event** via `LearnActionHints` (lights, gear, park brake, anti-ice) |
| Save | Nur **aktives Aircraft-Profil** (Base unverändert); `ApplyCommandSources` |
| Self-echo | Suppress ~750 ms nach host-executed Action-Namen |
| Headless | `--learn-dump` (Watch-Liste), `--learn-export <path>` (Detections JSON) |
| Non-Goal | Keine magische Discovery unbekannter LVars |

Spec: [Plans/Plan_LearnMode.md](Plans/Plan_LearnMode.md).

---

## 6. Command-System (JSON-driven)

### 6.1 Schema

Jedes Command in `base_commands.json` / Aircraft-Profilen:

| Feld | Bedeutung |
|------|-----------|
| `id` | Eindeutige ID |
| `phrases[]` | Erkannte Sprachphrasen |
| `response` | Gesprochene Bestätigung |
| `reject_response` | Optional bei Condition-Fail |
| `conditions[]` | `simvar` / `op` / `value` / `units` |
| `actions[]` | Siehe Action-Typen |
| `require_positive_climb_flag` | Gear-up Climb-Gate |
| `checklist` | Checklist-Markierung (Kategorie Manual-Tab) |

### 6.2 Action-Typen

| `type` | Verhalten |
|--------|-----------|
| `event` | `TransmitClientEvent` (z. B. `GEAR_UP`) |
| `set_simvar` / `simvar` | Live-Write `A:`/`L:` via `SetDataOnSimObject` wenn IsLive |

### 6.3 Merge-Regeln

- Profil-Commands mit **gleicher id ersetzen** Base
- **Neue ids** werden angehängt
- `event_aliases` auf dem Profil schreiben Event-Namen zur Merge-Zeit um
- Catalog-Rebuild: `ConfigLoader.Merge` über `HostSession.RebuildCatalogFromCurrentSettings` + Pipeline-Rebuild

### 6.4 Info-only / dynamische Antworten

- Reine Info-Commands: `actions: []` (oft leere Conditions) — offline und live nutzbar
- **`list_commands`**: dynamische TTS-Zusammenfassung aus dem **aktuell gemergten** Katalog (`CommandListBuilder` + Special-Case in `CommandProcessor`); volle Liste → `CommandResult.DetailLogLines` → Console / Debug-Tab
- Phrases u. a.: *what can you do*, *list commands*, *command list*, *available commands*

### 6.5 Manual-Tab Kategorien (`CommandCatalogGroups`)

Gear · Lights · Flaps · Autopilot / FCU · Anti-ice · Brakes / Spoilers · APU / Systems · Overhead · Checklists · Info · Other

### 6.6a Installer (WPF Setup)

| Aspekt | Umsetzung |
|--------|-----------|
| App | `CoPilotVoiceSetup` under `Sources/Installer/` (separate from HostSession) |
| Pack | `scripts/pack-installer.ps1` → `dist/CoPilotVoiceSetup/` + `payload/private-utility-copilot-voice` |
| Features | Community auto-detect (`UserCfg.opt`) + Browse; copy package; Desktop + Start Menu shortcuts; .NET 8 Desktop Runtime check; upgrade keep-config; uninstall (`--uninstall`); launch after install |
| UI | Dark theme (`Themes/InstallerTheme.xaml`): contrast-safe controls (no light-on-light), status cards, step pills, Host-aligned (1.6.2) |
| State | `%LocalAppData%\PrivateCoPilotVoice\install.json` + config backups under `backup\` |

### 6.6 TTS / WAV-Callouts (B2)

| Aspekt | Umsetzung |
|--------|-----------|
| Settings | `tts.engine` = `Windows` \| `Wav` \| `Hybrid` (Default **Hybrid**); `tts.voice_pack` (Default `austrian_airlines_en_us`) |
| Services | `WindowsTtsService`, `WavTtsService`, `HybridTtsService` (WAV first → Windows fallback); `--no-tts` → `ConsoleTtsService` |
| Mapping | `extras/voices/{pack}/manifest.json`: `command_id` + `kind` (`success`/`reject`); `*` wildcard for reject |
| Playback | Built-in `System.Media.SoundPlayer` via `IWavPlayer` — no extra NuGet |
| Context | `CommandProcessor` passes command id + response kind into `ITtsService.Speak` |
| Sample packs | `austrian_airlines_en_us`, `lufthansa_en_us` (core gear/lights/flaps/park brake/AP/anti-ice + unable) |
| UI | Settings: Engine + Voice Pack ComboBoxes; Debug Test TTS uses `gear_up` success sample when pack maps it |

---

## 7. Base-Command-Katalog (58 Commands)

Quelle: `PackageSources/extras/config/base_commands.json` (Source of Truth).

### Gear
- `gear_up` (Conditions: VS > 100 fpm + GEAR POSITION; `require_positive_climb_flag`)
- `gear_down`

### Lights
- Landing / Taxi / Strobe / Beacon / Nav on+off
- Cabin on+off, Logo on, Wing on

### Flaps
- `flaps_up`, `flaps_down`, `flaps_1`–`flaps_3`, `flaps_incr`, `flaps_decr`

### Autopilot / FCU
- AP on/off, Flight Director on/off
- Heading / Altitude / Speed / VS hold
- NAV / Approach / LOC
- Heading bug inc/dec, Alt/Spd/VS var inc/dec

### Anti-ice / Probe
- Anti-ice on/off, Pitot heat on/off

### Brakes / Spoilers
- Parking brake on/off
- Spoilers arm/disarm, spoilers on/off

### APU
- `apu_start`, `apu_off`

### Checklists (Base: primär TTS-Callouts)
- `checklist_before_start`
- `checklist_before_takeoff`
- `checklist_after_landing`

### Info
- `list_commands`

---

## 8. Aircraft-Profile

| Profil | Inhalt |
|--------|--------|
| `generic` | Leere Overrides; Default-Fallback |
| `a320` | Stub: `a320_managed_speed` |
| `b737` | Stub: `b737_n1_mode` |
| `fenix_a320` | **Study-level Hybrid** (Events + LVars), 76 Command-Einträge (Replace + Append) |

### Auto-Detect (Default **ON**)

- Settings: `auto_detect_aircraft: true`, `announce_profile_switch: true`
- Regeln: `config/aircraft_detection.json` (case-insensitive contains, first match, `fallback_profile`)
- **Fenix** (`pattern: fenix`) → `fenix_a320` **vor** generic A320
- Weitere Regeln: A320/A319/A321 → `a320`; 737 → `b737`
- Live: SimConnect TITLE + ATC MODEL (SECOND period)
- Identity-Change → Catalog + Pipeline/Speech rebuild
- **Aus:** TITLE/ATC MODEL weiter anzeigen; **kein** Profil-Switch. Manuelles Profil: Settings-Combo + **Apply** (UI Dirty-Guard hält Checkbox/Combo bis Apply, trotz ~2 Hz Status-Refresh)
- **false→true** Apply: einmalige Re-Eval der aktuellen Identity (Profil springt auf Match ohne neues Aircraft)
- CLI `--profile` sperrt Auto-**Switch** (Title wird weiterhin angezeigt)
- Offline → Title Unknown, kein Crash
- Matcher: `AircraftProfileMatcher` (pure, kein Sim-I/O)

### Study-Level LVar-Strategie (implementiert)

| Layer | Policy |
|-------|--------|
| `base_commands.json` | **Event-first** — keine Vendor-LVars |
| Study-Profile (Fenix) | Override by id: dual-write `event` + `set_simvar` |
| Host | `SetDataOnSimObject`; kein SPAD/AAO |
| Cross-vendor | Getrennte LVar-Maps pro Aircraft |

---

## 9. Fenix A320 Profile (DONE — JSON)

Datei: `config/aircraft/fenix_a320.json`

| Bereich | Mapping |
|---------|---------|
| Gear | `GEAR_*` + `L:S_MIP_GEAR` (0=UP, 1=DOWN); Gate: airborne + VS > 0 |
| Exterior Lights | Events + `L:S_OH_EXT_LT_*` (Landing 0/1/2, Nose, Strobe, Beacon, Nav/Logo, Wing) |
| Landing retract | `landing_lights_retract` (LVar = 0) |
| Flaps | Events + `L:S_FC_FLAPS` 0–4; incr/decr event-only |
| Parking Brake | Event + `L:S_MIP_PARKING_BRAKE` 0/1 |
| Speedbrake | Events + `L:A_FC_SPEEDBRAKE` (0=ARM, 1=RETRACT, 2=DETENT) |
| Anti-ice / Probe | Events + Fenix Overhead LVars |
| APU | Events + MASTER / START / BLEED LVars; extra `apu_master_*` / `apu_bleed_*` |
| Overhead | Batteries, EXT PWR, Fuel Pumps, Packs, ADIRS NAV, Seatbelt Signs, Dome |
| Checklists | Multi-Action-Sequenzen (nicht nur TTS) |
| FCU Mode Holds | **Unable** (leere Actions) |

Airbus-Callouts für Gear-up: *positive rate*, *positive rate gear up*, …

---

## 10. SimConnect

### Clients

| Client | Rolle |
|--------|-------|
| **NativeSimConnectClient** | Preferiert (Dispatch-Thread; TITLE/Status ohne HWND) |
| **ManagedSimConnectClient** | Optional wenn managed Assembly vorhanden |
| **RecordingSimConnectClient** | Offline / Fixture / Tests |

### Connect-Robustheit

- KittyHawk/MSFS-`SimConnect.dll` (FSW/Dovetail wird abgelehnt)
- `SimConnect.cfg` IPv4 `127.0.0.1:500`
- Multi-Index-Open (Settings-Index, LOCAL, Pipe, IPv6, Auto)
- Working Directory = EXE-Ordner
- `IsLive` + Log `[SimConnect] LIVE event sent: …` / LIVE SetSimVar
- Offline-Recording: Events nicht an die Sim

### Daten & Actions

- Status-SimVars (SECOND period): Gear, VS, Flaps, AP, Lights, Park Brake, Anti-Ice, IAS, Altitude, On Ground, Ground Velocity, Heading, …
- Aircraft Identity: TITLE, ATC MODEL; Airport Ident best-effort
- Live **TransmitEvent** für Standard-Events (`StandardEventMap`) und dynamisch gemappte Namen
- **Transmit-Flags (kritisch):** `SimConnect_TransmitClientEvent` mit `GroupID = HIGHEST (1)` und **`Flags = GROUPID_IS_PRIORITY (0x10)`** — Native + Managed aligned (`NativeSimConnectClient.EventFlagGroupIdIsPriority`). Ohne Flag kann der Host Live/TTS zeigen, während der Sim keine Events anwendet
- Live **SetSimVar** für `A:`/`L:` via `SetDataOnSimObject`
- Message Pump ~50 ms; Identity-Eval ~2 Hz (kein Busy-Poll)

---

## 11. Settings: Apply / Save / Reload

| Aktion | Verhalten |
|--------|-----------|
| **Apply** | UI → Memory; **kein** Re-Read von `settings.json`. Catalog-Re-Merge nur bei Profil-Wechsel. Pipeline nur bei Speech/TTS/Behavior-Fingerprint-Change. Speech-Restart nur bei Grammar-Change **und** aktivem Listening |
| **Save** | Apply + Write `settings.json` |
| **Reload** | `LoadAll` von Disk (überschreibt Memory), Force Pipeline-Rebuild, Speech-Restart wenn aktiv |

Nach Profilwechsel sieht `list_commands` den neuen gemergten Katalog.

### Settings-UI Dirty-Guard (implementiert)

- Live `StatusChanged` refresht Status-Tab und Flight Data ~2 Hz
- Solange Settings-Controls **dirty** sind (User editiert, noch kein Apply/Save/Reload): **kein** Zurückschreiben von Session → Auto-Detect-Checkbox, Profil-Combo, Continuous/Announce usw.
- Form **clean**: Sync erlaubt (verhindert Apply-Clobber nach Auto-Profil-Switch)
- `LoadSettingsToUi` setzt Dirty zurück; Policy: `MainWindow.ShouldSyncSettingsControlsFromSession`

---

## 12. Config & Packaging

### Source of Truth

`private-utility-copilot-voice/PackageSources/extras/config/`

| Datei | Zweck |
|-------|--------|
| `settings.json` | SimConnect, Speech, TTS, Behavior, Profile, Auto-Detect |
| `base_commands.json` | Core-Command-Katalog |
| `aircraft_detection.json` | Auto-Detect-Regeln |
| `aircraft/*.json` | Profile (generic, a320, b737, fenix_a320) |

csproj kopiert Config nach Build-Output. `Packages/.../extras/config` = packaged Copy.

### Host-Nebenfiles (`extras/`)

- `CoPilotVoiceHost.exe` (+ deps)
- `SimConnect.dll`, `SimConnect.cfg`, optional managed wrapper
- `run_copilot.bat`
- `docs/README.txt`
- `voices/copilot_en_us/` (Platzhalter für künftige WAV-Callouts)

### Config-Resolve (`ConfigLoader.ResolveConfigRoot`)

1. Expliziter Pfad
2. `config/` neben EXE
3. Repo `PackageSources/extras/config` (Suche aufwärts)

### Persistenz-APIs

- Save Settings / Base Commands / Aircraft Profile
- Clone Catalog/Profile/Command für UI-Working-Copies
- `ApplyCommandSources` (merge + optional disk save)

---

## 13. Performance & Ressourcen

- Kein busy SimVar-Polling
- Apply-Fingerprint-Thrift (weniger unnötige Grammar-Restarts)
- Bounded Debug-Log (~2000 Zeilen, älteste trimmen)
- Kein doppelter PTT-Timer (Session-Poll + HandlePhrase teilen `PttArmService.Poll`)
- Identity + Status-UI ~2 Hz

---

## 14. Tests

Projekt: `CoPilotVoiceHost.Tests` (~90 Tests)

| Suite | Abdeckung (Auszug) |
|-------|---------------------|
| `CommandPipelineTests` | Match, Conditions, Processor, Inject |
| `FenixA320ProfileTests` | Merge, dual-write, Unable FCU, Checklists |
| `AircraftDetectionTests` | Rules, Fenix vor A320, CLI lock |
| `ListCommandsTests` | Dynamische Liste aus Live-Katalog |
| `GuiHostTests` | WinExe/WPF, Apply/Save/Reload Speech-Restart, Settings dirty-guard policy |
| `PerformanceThriftTests` | No-op Apply ohne Speech-Restart |
| `CommandEditorTests` | Commands-Tab Apply/Save-Pfad |
| `SkepticFixTests` / `HostEntryTests` / `MaintainabilityConstantsTests` | Regressionen, Entry, Constants |

```bat
dotnet test private-utility-copilot-voice/Sources/Host/CoPilotVoiceHost.Tests -c Release
```

---

## 15. Build & Install (umgesetzt)

- .NET 8 Host: build / test / publish nach `PackageSources/extras`
- WASM optional (VS + MSFS Toolset); ohne Toolset bleibt Marker/Placeholder
- Community-Install: Package-Ordner kopieren, Free Flight starten, `run_copilot.bat`
- Erwartung: Status **Live** bei korrekter KittyHawk-DLL + laufendem Free Flight

---

## 16. Dokumentation (Bestand)

| Datei | Rolle |
|-------|--------|
| [README.md](README.md) | User-facing Overview, Install, Usage |
| [CHANGELOG.md](CHANGELOG.md) | Versionshistorie |
| [AGENTS.md](AGENTS.md) | Agent/Developer-Konventionen |
| **Complete_Features.md** (diese Datei) | Feature-SSOT (umgesetzt) |
| [Backlog.md](Backlog.md) | Offene Features & Verbesserungen |

---

## 17. Versionshistorie (Feature-Meilensteine)

| Version | Wesentliche Features |
|---------|----------------------|
| **1.0.0** | Initial: Host, JSON-Commands, Wake/PTT, Core-Events, Gear-Climb-Gate, Tests |
| **1.1.0** | Native SimConnect, IsLive, PTT-Grace/Arm, Offline-Transparenz |
| **1.1.1** | KittyHawk DLL, SimConnect.cfg, Multi-Open, FSW-Reject |
| **1.2.0** | WPF GUI, HostSession, Headless-Flag, Settings Apply/Save/Reload |
| **1.2.1** | Dark Cockpit Theme, ComboBox/Scrollbar-Fixes |
| **1.3.0** | Auto-Detect, Fenix hybrid LVar P0–P3, Manual/Commands-Tabs, list_commands, live SetSimVar, Flight Data Dashboard, Speech whole-word + alternates, Performance thrift, Docs SSOT (Complete_Features / Backlog) |
| **1.4.0** | Learn Mode full (MVP + watchlists, profile learn_watch, debounce/group, action hints, export, headless dump) |
| **1.5.0** | B2 WAV callouts: Hybrid/Wav/Windows TTS engines, voice packs + manifests, Austrian Airlines & Lufthansa samples |
| **1.6.0** | WPF Installer (`CoPilotVoiceSetup`): Community detect/install/uninstall, shortcuts, .NET 8 check, config-preserving upgrade |
| **1.6.1** | Installer dark-theme control templates (readable ComboBox/Button/CheckBox); docs plans under `Plans/` |
| **1.6.2** | Installer contrast redesign (status cards, step pills, SystemColors/ScrollBar, editable Combo chrome-free); host ComboBox fix |
| **Unreleased (post-1.6.2)** | Live events: native `TransmitClientEvent` + `GROUPID_IS_PRIORITY`; Settings dirty-guard (Auto-Detect abschaltbar / manuelles Profil während Live-Refresh) |

---

*Dieses Dokument beschreibt nur implementierte Funktionalität. Offene Punkte und Verbesserungen: [Backlog.md](Backlog.md).*
