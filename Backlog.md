# Backlog — Private Voice Co-Pilot (MSFS 2024)

Offene Features, sinnvolle Erweiterungen und Codebase-Verbesserungen.  
Umgesetzte Funktionalität: [Complete_Features.md](Complete_Features.md).

| | |
|--|--|
| **Stand** | 2026-08-11 · Release **1.6.2** |
| **Branch** | `main` / `dev` |
| **Architektur-Lock** | Siehe [AGENTS.md](AGENTS.md) — Core free of WPF, JSON-first, kein busy Poll, WASM ohne Co-Pilot-Logik |

Jedes Item enthält: **Beschreibung**, **Warum**, **Architektur / Implementierung**, **Priorität**.  
IDs (`A1`, `B2`, …) bleiben stabil; die **Abschnittsreihenfolge folgt der Prio**.

---

## Prioritäten-Übersicht

| Prio | Items |
|------|--------|
| **P0** | A1 Live Fenix Cockpit-Verifikation (Human) |
| **P1** | C1 LVar-Read Conditions · C2 Dynamische Event-Map · C4 `simvar_aliases` · C5 Profile-`extends` |
| **P2** | A4 Fenix Overhead · B5 Dynamic Info-Commands · B6 Action-Delays · B7 Command-Validierung · D1 HostSession · D2 MainWindow · D4 Config-Sync · D5 Tests |
| **P3** | B3 Host Auto-Start · B8 SimConnect Reconnect UX · D3 Logging |
| **Optional** | A2 iniBuilds A350 · B4 list_commands Export · C3 H/B-Event Bridge |
| **Prozess / Policy** | E1 Docs-SSOT · D6 WASM Marker belassen |

---

## Done (recent)

### L0 / L0b / L0c. Learn-Modus (full) — **DONE** in **1.4.0**

MVP + Phase G (watchlists, profile `learn_watch`, debounce/group) + Phase H (action hints, export, headless dump).  
Details: [Complete_Features.md](Complete_Features.md) §5b · [Plans/Plan_LearnMode.md](Plans/Plan_LearnMode.md) · [CHANGELOG.md](CHANGELOG.md).

### B2. WAV-Callouts — **DONE** in **1.5.0**

`tts.engine` Hybrid/Wav/Windows + `tts.voice_pack`; `WavTtsService` / `HybridTtsService` / manifests; Austrian + Lufthansa sample packs under `extras/voices/`.  
Details: [Complete_Features.md](Complete_Features.md) · [Plans/Plan_B2_WavCallouts_with_samples/Plan_B2_WavCallouts.md](Plans/Plan_B2_WavCallouts_with_samples/Plan_B2_WavCallouts.md) · [CHANGELOG.md](CHANGELOG.md).

### Installer (WPF Setup) — **DONE** in **1.6.0** (+ UI **1.6.1** / contrast **1.6.2**)

`CoPilotVoiceSetup`: Community detect/install/uninstall, shortcuts, .NET 8 check, config-preserving upgrade. Pack: `scripts/pack-installer.ps1`.  
UI: dark control templates (1.6.1), contrast redesign / status cards / step pills (1.6.2).  
Human installer + Free Flight abnahme: [Live_Testing.md](Live_Testing.md) (noch offen).  
Host auto-start with MSFS remains **B3** (not done).

---

## P0 — Als Nächstes

### A1. Live Fenix A320 Cockpit-Verifikation (Human)

**Beschreibung:** JSON-Map P0–P3 ist implementiert; es fehlt die menschliche Live-Abnahme in Free Flight (Switch-Animation, LIVE event / LIVE SetSimVar).

**Warum:** Fenix-Updates können LVar-Verhalten ändern; Dual-Write muss im Cockpit sichtbar greifen.

**Architektur / Implementierung:**
- Kein Code nötig für den Test selbst.
- **Vollständige Checklisten (Installer + Host + Fenix):** [Live_Testing.md](Live_Testing.md) §4 (und Gesamtablauf §0–§7).
- Kurz-Checkliste (Free Flight, Status **Live**):
  1. Auto-Detect TITLE enthält `fenix` → Profil `fenix_a320`
  2. Gear up/down: Hebel + `L:S_MIP_GEAR` (0/1)
  3. Landing lights ON/OFF/RETRACT (0/1/2)
  4. Strobe/Beacon/Nav/Taxi/Wing/Logo
  5. Flaps diskret 0–4; Park brake; Speedbrake ARM/RETRACT/DETENT
  6. Overhead: Anti-ice, APU master/start/bleed, BAT, EXT PWR, Fuel, Packs, ADIRS, Seatbelts
  7. Checklists `before start` / `before takeoff` / `after landing` — Multi-Actions
  8. FCU mode holds → TTS **Unable**, keine Events
- Ergebnisse in CHANGELOG / ggf. Profil-Notes nachziehen; Live_Testing Sign-off ausfüllen.
- Nach Fenix-Package-Updates erneut smoke-testen.

**Prio:** P0

---

## P1 — Hohe Priorität

### C1. LVar-Read Conditions

**Beschreibung:** Conditions dürfen `L:…` / aircraft-spezifische Vars lesen (nicht nur Standard-Status-SimVars).

**Warum:** Gates wie „Gear already up“ oder „APU available“ brauchen Switch-State bei Study-Aircraft.

**Architektur / Implementierung:**
1. **Kein Busy-Poll.** Profile oder Command-Metadaten listen benötigte LVars; Client registriert sie in einer Data-Definition mit SECOND (oder seltener) Period.
2. `ISimConnectClient`: generisches `RequestNamedVar` / erweiterbare Definition-Liste; Snapshot speichert `L:NAME`.
3. `ConditionEngine` bleibt unverändert (liest Snapshot by name).
4. JSON: `"simvar": "L:S_MIP_GEAR", "op": "==", "value": 1`.
5. Fallback: fehlende Var → Condition fail + klare DenyReason (wie heute).
6. Tests mit Recording-Client, der LVars im Snapshot setzt.

**Prio:** P1

---

### C2. Dynamische SimConnect Event-Map

**Beschreibung:** Unbekannte Event-Namen aus JSON zur Laufzeit mappen (`MapClientEventToSimEvent`), statt nur hardcodierte `StandardEventMap`.

**Warum:** Jedes neue Base-Event erfordert heute C#-Enum + Map-Eintrag — bremst JSON-first.

**Architektur / Implementierung:**
1. Native/Managed Client: wenn `StandardEventMap.TryGet` false → dynamische Event-Id allokieren und `MapClientEventToSimEvent(name)` einmalig.
2. Thread-safe Dictionary `string → EventId`.
3. Logging bei First-Map: `[SimConnect] dynamic event mapped: …`.
4. StandardEventMap behalten für stabile IDs der Kern-Events (optional).
5. Unit-Tests: TransmitEvent mit fiktivem Namen im Recording/Mock-Pfad; Live manuell smoke.

**Prio:** P1

---

### C4. `simvar_aliases` wirklich anwenden (oder entfernen)

**Beschreibung:** `AircraftProfile.SimVarAliases` existiert im Model; Merge wendet primär `EventAliases` an — SimVar-Aliases sind effektiv tot.

**Warum:** Dead API verwirrt Profile-Autoren; nützlich für Condition-SimVar-Umbenennung pro Aircraft.

**Architektur / Implementierung:**
- In `ConfigLoader.Merge` Conditions + set_simvar Names über Aliases rewrite (analog EventAliases).
- Empfohlen: Aliases aktivieren + Unit-Test mit künstlichem Alias.

**Prio:** P1

---

### C5. Profile-`extends` Chain

**Beschreibung:** Feld `extends` steht in Profilen (`"extends": "generic"`), wird beim Load aber nicht als Vererbungskette ausgewertet (Merge = base_commands + ein Profil).

**Warum:** Mehrstufige Profile (generic → airbus → fenix) wären wartbarer; heute ist `extends` irreführend.

**Architektur / Implementierung:**
- `LoadAircraftProfile` löst Chain auf (Depth-Limit, Zyklus-Detect), merged Profile-Commands in extends-Reihenfolge, dann base_commands + final profile.

**Prio:** P1

---

## P2 — Mittel

### A4. Fenix Overhead erweitern

**Beschreibung:** Zusätzliche Fenix-Only Commands (IDG, Hydraulik-Pumpen, Fire Test, Cargo Smoke, Crossbleed, …).

**Warum:** Die wichtigsten Overhead-Gruppen sind abgedeckt; Cockpit-Workflows profitieren von mehr Voice-Coverage.

**Architektur / Implementierung:**
- Nur `fenix_a320.json` (append neue ids oder override).
- LVar-Enums aus Fenix-Quellen verifizieren.
- Manual-Tab: bestehende `CommandCatalogGroups` (Overhead) greift per id-Substring; bei Bedarf Categorize-Regeln erweitern (Core, kein WPF).
- Offline-Inject-Tests für neue Phrases.

**Prio:** P2

---

### B5. Dynamic Info-Commands (Altitude / Speed Callouts)

**Beschreibung:** Sprachbefehle wie „say altitude“, „airspeed check“ mit TTS aus Live-Snapshot.

**Warum:** Co-Pilot-Utility ohne Sim-Events; analog zu `list_commands`.

**Architektur / Implementierung:**
1. Base-Commands mit `actions: []`, speziellen `id`s (z. B. `info_altitude`).
2. `CommandProcessor` Special-Case (wie `CommandListBuilder.IsListCommand`) oder generisches `response_template` später.
3. Snapshot-Werte aus `ISimConnectClient.Snapshot` / HostSession-Properties.
4. Offline: Fixture-Werte oder „Unknown“.
5. Keine Aircraft-Branches in C#.

**Prio:** P2

---

### B6. Action-Delays / Sequencing

**Beschreibung:** Zwischen Checklist-Actions Pausen (`delay_ms`), damit Switches nacheinander greifen.

**Warum:** Multi-Action-Checklists können im Sim überrennen; Fenix-Sequenzen wirken natürlicher mit Delay.

**Architektur / Implementierung:**
- Neuer Action-Typ in JSON: `{ "type": "delay", "value": 200 }` (ms).
- `ActionExecutor`: `Task.Delay` / `Thread.Sleep` mit Cap (z. B. max 2000 ms pro Step, max Gesamtzeit).
- Models + HostConstants; Unit-Test mit Recording-Client.
- UI Commands-Tab: Delay als Action-Typ auswählbar (optional später).

**Prio:** P2

---

### B7. Command-Validierung beim Apply/Save

**Beschreibung:** Schema-/Semantik-Checks im Commands-Tab (fehlende id/phrases, unbekannte Event-Namen, leere Overrides).

**Warum:** Tipfehler in JSON brechen erst zur Laufzeit.

**Architektur / Implementierung:**
- Pure Core-Klasse `CommandCatalogValidator` (keine WPF).
- Checks: non-empty id, ≥1 phrase, action type whitelist, event name gegen `StandardEventMap` **oder** dynamische Map (Warnung, kein Hard-Fail für unbekannte Events wenn C2 umgesetzt).
- `HostSession.ApplyCommandSources` gibt Warnings an Log/UI zurück.
- Unit-Tests mit absichtlich kaputten Commands.

**Prio:** P2

---

### D1. HostSession entflechten

**Beschreibung:** `HostSession.cs` (~950 LOC) bündelt Config-Apply, Identity, Speech, SimConnect, Inject, Headless.

**Warum:** Weniger Regression-Risiko; klarere Unit-Tests pro Concern.

**Architektur / Implementierung:**
- Extrahieren (weiter **ohne WPF**):
  - `AircraftIdentityService` — detection eval + switch hooks
  - `PipelineFingerprint` / Apply-Policy (grammar vs services vs catalog)
  - optional `HeadlessRunner`
- `HostSession` bleibt Fassade für GUI/CLI.
- Bestehende Tests müssen grün bleiben; Fingerprint-Semantik nicht ändern.

**Prio:** P2

---

### D2. MainWindow.xaml.cs verkleinern

**Beschreibung:** UI Code-Behind ~776 LOC (Tabs, Binding, Commands-Editor).

**Warum:** Thin-Shell-Regel: UI soll nur layout/click → session sein.

**Architektur / Implementierung:**
- Leichte Presenter/Helper pro Tab (`StatusTabController`, `CommandsEditorModel`) in `Ui/` — weiterhin nur Session-APIs.
- Keine PhraseMatcher/SimConnect-Logik in UI verschieben (falls vorhanden, zurück in Core/Session).
- XAML-Theme unangetastet lassen wo möglich.

**Prio:** P2

---

### D4. Config-Sync PackageSources ↔ Packages

**Beschreibung:** Zwei Config-Trees können driften (PackageSources vs Packages shipped).

**Warum:** Source of Truth ist PackageSources; Community-Package kann veraltete JSON haben.

**Architektur / Implementierung:**
- Script `sync-config.ps1`: kopiert `PackageSources/extras/config` → `Packages/.../extras/config`.
- README/AGENTS: „nach Config-Edit Script laufen lassen“.
- Optional: Test der prüft, dass kritische Files hash-gleich sind (nur in CI/dev).

**Prio:** P2

---

### D5. Test-Coverage Lücken

**Beschreibung:** Ergänzende Tests für riskante Pfade.

**Vorschläge:**
- Native/Managed `SetSimVar` mit Mock/Recording (IsLive-Pfad logging)
- Commands-Tab Save round-trip (Base + Profile Dateien)
- Auto-Detect: auto-off → auto-on Re-Eval ohne Identity-Key-Stuck
- ActionExecutor unknown type + delay (wenn B6)
- Validator (wenn B7)

**Prio:** P2

---

## P3 — Niedrig

### B3. Host Auto-Start (EXE.xml / Launcher)

**Beschreibung:** Optionalen Start des Hosts mit MSFS dokumentieren/skripten (SimConnect EXE.xml oder externes Launcher-Script).

**Warum:** Manueller Start nach Free Flight ist fehleranfällig.

**Architektur / Implementierung:**
- **Nicht** in WASM-Co-Pilot-Logik.
- Docs + optionales Script unter `extras/docs/` oder Repo-Root:
  - Vorlage für `%APPDATA%\…\exe.xml` / SimConnect auto-start (MSFS-Version prüfen)
  - oder Task/Shortcut der auf `run_copilot.bat` zeigt
- Host muss tolerant sein wenn Sim noch nicht Live ist (Reconnect-Timer existiert).
- Keine Marketplace-Annahmen.

**Prio:** P3

---

### B8. SimConnect Reconnect UX

**Beschreibung:** Sichtbarere Reconnect-Zustände, konfigurierbarer Backoff, optional Auto-Reconnect-Toggle in Settings.

**Warum:** Free Flight Load/Reload trennt oft; Force Reconnect existiert, UX ist spartanisch.

**Architektur / Implementierung:**
- Settings: `simconnect.auto_reconnect`, `reconnect_interval_ms` (bestehenden Timer nutzen/erweitern).
- Status-Tab: Last Error, Next Retry, Attempt Count.
- Keine Busy-Loops; Timer-basiert wie heute.

**Prio:** P3

---

### D3. Logging vereinheitlichen

**Beschreibung:** `ActionExecutor` und Teile von SimConnect schreiben `Console.WriteLine`; GUI nutzt `UiLogSink`.

**Warum:** Headless + GUI Logs driften; schwerer zu filtern.

**Architektur / Implementierung:**
- Optionales `ILogSink` in ActionExecutor/SimConnect-Konstruktoren (Default: Console + Session.Log).
- LogLevel bereits in Diagnostics vorhanden — durchgängig nutzen.
- Keine Abhängigkeit auf WPF in Core.

**Prio:** P3

---

## Optional / Later

### A2. iniBuilds A350 Profile

**Beschreibung:** Neues Aircraft-Profil (z. B. `inibuilds_a350.json`) mit **eigener** LVar-Map (nicht Fenix-Namen kopieren).

**Warum:** Study-Level-Flugzeuge brauchen dual-write für Switch-Animation; gleiches Hybrid-Muster wie Fenix.

**Architektur / Implementierung:**
1. LVar-Discovery (Aircraft XML / Community-Maps / Live-Debug) — eigene Namen pro Vendor.
2. Datei `PackageSources/extras/config/aircraft/inibuilds_a350.json`:
   - `profile_id`, Notes, Limitations
   - Zuerst: Gear + Exterior Lights (event + `set_simvar`)
   - Danach: Park brake, Spoilers, Flaps
   - Unmapped FCU → Unable (`actions: []`)
3. `aircraft_detection.json`: Rule **vor** generischen A3xx-Patterns (`pattern` z. B. `inibuilds` / `a350` — so dass Fenix/A320 nicht greifen).
4. Packages-Config sync; Unit-Tests analog `FenixA320ProfileTests` (Merge, dual-write, detection order).
5. **Kein** `if (A350)` in C# — nur JSON + Detection-Rule.

**Prio:** Optional / later

---

### B4. list_commands Export (Clipboard / Datei)

**Beschreibung:** Volle Command-Liste zusätzlich in Zwischenablage oder Textdatei exportieren.

**Warum:** Debug/Training; TTS-Summary ist kurz, Detail nur im Log.

**Architektur / Implementierung:**
- Core: `CommandListBuilder` liefert bereits Full-Log-Lines — wiederverwenden.
- HostSession-Methode `ExportCommandList(path?)` oder GUI-Button im Debug/Manual-Tab (nur UI-Shell).
- Optional Flag in settings; Headless: `--export-commands path`.

**Prio:** Optional

---

### C3. Optional H/B-Event Bridge

**Beschreibung:** Falls `SetDataOnSimObject` für bestimmte Fenix/Study-LVars live nicht greift: H-Events / B-Events über WASM-Custom-Event oder Calculator-Code.

**Warum:** Letzte Eskalation für Switch-Animation; aktuell dual-write ohne Bridge.

**Architektur / Implementierung:**
- **Nur wenn Live-Test scheitert** (A1).
- JSON Action-Typ z. B. `{ "type": "h_event", "name": "…" }` oder Bridge-Name in profile notes.
- WASM: minimale Registrierung Custom Event → Execute Calculator / H-Event (weiter **keine** STT/TTS/Command-Logik).
- Host sendet Bridge-Payload über SimConnect Client Event.
- AGENTS.md: Prefer Unable / LVar write first; Bridge last resort.
- Separate Feature-Flags; dual-write behalten wo möglich.

**Prio:** Optional

---

## Prozess / Policy

### E1. Feature-SSOT & Planungs-SSOT

**Beschreibung:**  
- **Complete_Features.md** = was gebaut ist  
- **Backlog.md** = was als Nächstes kommt  
- CHANGELOG für Releases; AGENTS für Agent-Konventionen

**Architektur / Implementierung:** Bei Feature-Done: Item aus Backlog streichen, Complete_Features + CHANGELOG updaten. Kein paralleles drittes Planungsfile.

**Prio:** Prozess (laufend)

---

### D6. WASM Marker belassen (bis Bridge nötig)

**Beschreibung:** WASM bleibt leerer Marker; nur bei C3 erweitern.

**Warum:** Architektur-Lock — keine Co-Pilot-Logik im Modul.

**Architektur / Implementierung:** Status quo; `module.cpp` Kommentar verweist auf optionale Bridge. Kein STT/Command-Code.

**Prio:** — (Policy)

---

## Explizit **nicht** geplant

| Thema | Grund |
|-------|--------|
| Marketplace / Content Manager Packaging | Private Utility only |
| Co-Pilot-Logik im WASM | Architektur-Lock |
| SPAD / AAO Abhängigkeit | SimConnect-only |
| Busy SimVar-Polling | Performance-Lock |
| Aircraft-spezifische `if` in C# | JSON-Profile only |
| Shared „Airbus LVar“-Profil Fenix+iniBuilds | Cross-vendor Anti-Pattern |

---

## Empfohlene nächste Schritte (an Prios ausgerichtet)

1. **L0 (P0)** — Learn-Modus implementieren nach [Plans/Plan_LearnMode.md](Plans/Plan_LearnMode.md)
2. **A1 (P0)** — Live Free Flight Fenix abhaken (Human; optional mit Learn-Tab)
3. **B2 (P1)** — WAV-Callouts / Hybrid-TTS
4. **C2 + C4 + C5 (P1)** — dynamische Events, SimVar-Aliases, Profile-`extends`
5. **C1 (P1)** — LVar-Read Conditions (kann Learn-Watch-Infrastruktur teilen)
6. **B6 / B7 / A4 (P2)** — Delays, Validator, mehr Fenix-Overhead
7. **D1 / D2 / D4 / D5 (P2)** — Maintainability & Tests parallel zur Feature-Welle
8. **P3 / Optional** — Auto-Start, Reconnect-UX, Logging; A350 / Export / H/B nur bei Bedarf

---

*Backlog ist lebendig: Einträge nach Umsetzung nach Complete_Features verschieben und hier entfernen oder als „Done“ markieren.*
