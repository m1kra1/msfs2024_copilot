# Live-Testing — Private Voice Co-Pilot (1.7.0)

Menschliche Abnahme in Free Flight / am Installer.  
**Status (Stand 2026-08-11):** Live testing aktuell **noch nicht** durchgeführt.

| | |
|--|--|
| **Baseline** | Package/App **1.7.0** (`dev` / `main`) |
| **Dist** | `dist/CoPilotVoiceSetup/` (nach `scripts/pack-installer.ps1`) |
| **Unit tests** | Host 129 + Installer 9 (Release) — grün vor diesem Release |
| **Verwandt** | Fenix P0 Backlog **A1** · [Backlog.md](Backlog.md) · Features: [Complete_Features.md](Complete_Features.md) |

Ergebnis je Block mit **Pass / Fail / N/A** und kurzer Notiz (Datum, Aircraft, Build-Pfad) unten oder in einem eigenen Log abhaken.

---

## 0. Vorbereitung

### 0.1 Build / Distribution

- [ ] Repo auf **1.7.0** (`manifest.json` / Host-Version / Setup-Version)
- [ ] Optional frisch packen:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/pack-installer.ps1
```

- [ ] Test-Lauf aus: `dist\CoPilotVoiceSetup\CoPilotVoiceSetup.exe`
- [ ] Payload vorhanden: `dist\CoPilotVoiceSetup\payload\private-utility-copilot-voice\`
- [ ] **.NET 8 Desktop Runtime** installiert ([Download](https://dotnet.microsoft.com/download/dotnet/8.0))

### 0.2 MSFS / SimConnect

- [ ] MSFS **2024**, Free Flight (nicht nur Menü)
- [ ] Neben dem Host: **KittyHawk/MSFS** `SimConnect.dll` + `SimConnect.cfg` (IPv4 `127.0.0.1:500`) — **nicht** FSW/Dovetail
- [ ] Community-Pfad bekannt (Steam/MS Store/Custom)

### 0.3 Offline-Smoke (ohne Sim, optional vor Live)

```bat
cd dist\CoPilotVoiceSetup\payload\private-utility-copilot-voice\extras
CoPilotVoiceHost.exe --headless --offline --inject "Co Pilot landing lights on"
```

- [ ] Exit ohne Crash; Log/`copilot-host-headless.log` zeigt Match / Action (offline: kein LIVE send)

---

## 1. Installer UI & Kontrast (1.6.2)

**Ziel:** Kein heller Text auf hellem Grund; Wizard nutzbar unter **Windows Light Theme**.

### 1.1 Visual / Lesbarkeit

- [ ] Window-Hintergrund dunkel; Body-Text hell und lesbar
- [ ] **Step-Pills** (1–4) sichtbar; Active = Accent + weißer Text; Upcoming = muted
- [ ] Status-Cards (Info / Success / Warning / Error) mit getöntem dunklem Hintergrund
- [ ] Buttons: Primary (blau, weißes Label), Ghost (Back/Cancel), Secondary (Browse/Detect)
- [ ] Progress-Log: mono, heller Text auf dunklem Input; dunkle Scrollbar

### 1.2 Location ComboBox (kritisch)

- [ ] Community-Pfad im **editierbaren** Feld hell auf **dunklem** Input (nicht weiß)
- [ ] Tippen/Paste lesbar; Caret sichtbar
- [ ] Dropdown-Items hell auf dunkel; Hover/Selected dunkelblau, Text lesbar
- [ ] Browse… / Detect funktionieren

### 1.3 Wizard-Flow (Neuinstallation)

| Step | Prüfen | Pass? |
|------|--------|-------|
| Welcome | Version Package + Setup; Payload-Pfad oder klare Warnung | ☐ |
| Runtime | .NET found → grüne Card; missing → rote Card + Download-Button | ☐ |
| Location | Pfad wählen; Ready / Upgrade-Warnung / Error Card korrekt | ☐ |
| Options | CheckBoxes Labels lesbar; Keep config / Shortcuts / Launch | ☐ |
| Progress | Log-Zeilen wachsen; Cancel deaktiviert während Busy | ☐ |
| Finish | Success- oder Error-Card; Launch Host / Open folder | ☐ |

### 1.4 Install-Ergebnis

- [ ] Package unter `Community\private-utility-copilot-voice\`
- [ ] Desktop-Shortcut (falls gewählt) startet Host mit WD = `extras`
- [ ] Start-Menü-Eintrag (+ Uninstall-Eintrag, falls gewählt)
- [ ] `%LocalAppData%\PrivateCoPilotVoice\install.json` geschrieben
- [ ] Bei Upgrade: Config-Backup unter `%LocalAppData%\PrivateCoPilotVoice\backup\`
- [ ] Keep-config: `settings.json` / aircraft-Profile erhalten; `base_commands` / voices aus Package aktualisiert

### 1.5 Uninstall

- [ ] Welcome → „Uninstall existing…“ **oder** Setup mit `--uninstall`
- [ ] Uninstall-Button **rot (Destructive)**; Warn-Card zu Config-Backup
- [ ] Package-Ordner entfernt; Shortcuts weg; `install.json` bereinigt
- [ ] Config-Backup vor Löschung vorhanden

**Installer-Notizen:**  
_Datum: ________  OS-Theme: Light / Dark  Ergebnis: _________

---

## 2. Host GUI — Offline / Start

Ohne oder vor Free Flight:

- [ ] Host startet (Shortcut oder `extras\CoPilotVoiceHost.exe`)
- [ ] Statusleiste **Offline** (ohne Sim) — kein Crash
- [ ] Tabs: Status, Manual, Checklists, Learn, Commands, Settings, Debug erreichbar
- [ ] Settings: TTS Engine + Voice Pack Combo lesbar (Light OS Theme)
- [ ] Debug: Inject `"Co Pilot landing lights on"` (oder Force gate) → Match + TTS
- [ ] Debug: **Test TTS** (Hybrid/WAV-Pack Sample)
- [ ] Manual-Tab: Button feuert Command (force-gate)
- [ ] Commands-Tab: Browse/Filter; Apply/Save ohne Crash

**Offline-Notizen:** _________________________________

---

## 3. Host — Live SimConnect (Free Flight)

### 3.1 Verbindung

- [ ] Free Flight, Aircraft geladen
- [ ] Host starten **nach** Session
- [ ] Status **Live** (Title bar + Status-Bar grün)
- [ ] **FLIGHT DATA** ~2 Hz: TITLE, Alt, IAS, V/S, On Ground (sinnvolle Werte)
- [ ] Debug-Log: keine dauerhaften SimConnect-Errors; kein FSW-DLL

### 3.2 Auto-Detect / Profil

- [ ] Auto-detect an (Default)
- [ ] TITLE/ATC MODEL → erwartetes Profil (z. B. Fenix → `fenix_a320`)
- [ ] Optional: Announce profile switch hörbar
- [ ] Profile manuell wechseln + Apply → Manual/Commands/list_commands folgen dem Merge

### 3.3 Speech Gate

- [ ] Bare Phrase **ohne** Wake/PTT → abgelehnt (Default)
- [ ] `"Co Pilot …"` → akzeptiert
- [ ] PTT (Default F12): halten/loslassen + Phrase in Grace-Fenster
- [ ] Optional Continuous listen: bare phrases OK

### 3.4 Standard-Events (Generic / Airliner-Base)

Im Cockpit **sichtbar** + Log `[SimConnect] LIVE event sent: …` wo Events greifen:

| Command (Beispiel-Phrase) | Erwartung | Pass? |
|---------------------------|-----------|-------|
| Landing lights on/off | Licht / Event | ☐ |
| Gear down | Gear down (Boden ok) | ☐ |
| Gear up | Gate: positiv climb / airborne (sonst Unable) | ☐ |
| Flaps up / down / set | Flaps bewegen | ☐ |
| Parking brake set/release | Brake | ☐ |
| Strobe / Beacon / Nav | Lights | ☐ |
| Autopilot on/off (falls gemappt) | AP / Unable | ☐ |

### 3.5 Info / TTS

- [ ] `list commands` / Info-Commands: TTS + Log-Detail, offline/live ohne Event-Zwang
- [ ] Hybrid: gemappte Commands spielen WAV aus Voice-Pack; unmapped → Windows SAPI
- [ ] Reject / Unable: passende Ablehnung + optional reject-WAV

**Live-Generic-Notizen:**  
_Aircraft: ________  TITLE: _________

---

## 4. Fenix A320 Hybrid (Backlog A1 — P0)

Nur mit Fenix A320 in Free Flight, Status **Live**, Profil **`fenix_a320`**.

| # | Check | Pass? | Notiz |
|---|--------|-------|-------|
| 1 | Auto-Detect TITLE enthält `fenix` → `fenix_a320` | ☐ | |
| 2 | Gear up/down: Hebel + `L:S_MIP_GEAR` (0/1) | ☐ | |
| 3 | Landing lights ON/OFF/RETRACT (0/1/2) | ☐ | |
| 4 | Strobe / Beacon / Nav / Taxi / Wing / Logo | ☐ | |
| 5 | Flaps diskret 0–4; Park brake; Speedbrake ARM/RETRACT/DETENT | ☐ | |
| 6 | Overhead: Anti-ice, APU master/start/bleed, BAT, EXT PWR, Fuel, Packs, ADIRS, Seatbelts | ☐ | |
| 7 | Checklists tab / voice: `before start checklist` etc. (sequential verify/execute from `config/checklists/`) | ☐ | |
| 8 | FCU mode holds → TTS **Unable**, keine falschen Events | ☐ | |
| 9 | Log: `LIVE event sent` und/oder live `SetSimVar` / SetDataOnSimObject | ☐ | |

**Fenix-Notizen / Fenix-Build-Version:** _________

---

## 5. Checklists tab / voice (1.7.0)

- [ ] Tab **Checklists**: Samples geladen (`before_start`, `before_takeoff`, `after_landing`)
- [ ] Start/Stop + Progress; Apply/Save/Reload
- [ ] Voice (Live oder inject): `Co Pilot before takeoff checklist` startet Runner
- [ ] Verify-Item wartet bis Zustand passt oder Pilot sagt **continue**
- [ ] **stop checklist** bricht ab
- [ ] Execute-Items senden Events/LVars (bei Live sichtbar)

**Checklist-Notizen:** _________

---

## 5b. Learn Mode (Live)

- [ ] Learn nur sinnvoll bei **Live**
- [ ] Start Learn → Watches / Detections erscheinen bei Cockpit-Schaltern
- [ ] Mapped vs Unmapped erkennbar
- [ ] Create/Edit Command → **Save to active profile** (nicht base)
- [ ] Export JSON funktioniert
- [ ] Nach Save: Manual/Commands/Inject sehen neuen Command
- [ ] Keine Vendor-LVars in `base_commands.json` gelandet

**Learn-Notizen:** _________

---

## 6. Upgrade / Regression Installer (optional, wenn Alt-Install vorhanden)

- [ ] Alte Config bewusst belassen → Upgrade mit Keep config
- [ ] Backup unter LocalAppData
- [ ] Host startet; settings/profile erhalten
- [ ] Neue base_commands / voices greifen

---

## 7. Abschluss / Sign-off

| Bereich | Ergebnis | Tester | Datum |
|---------|----------|--------|-------|
| Installer UI (contrast 1.6.2+) | Pass / Fail / Skip | | |
| Install / Uninstall | Pass / Fail / Skip | | |
| Host Offline | Pass / Fail / Skip | | |
| Host Live Generic | Pass / Fail / Skip | | |
| Fenix A1 | Pass / Fail / Skip | | |
| Checklists 1.7.0 | Pass / Fail / Skip | | |
| Learn Mode | Pass / Fail / Skip | | |
| WAV / Hybrid TTS | Pass / Fail / Skip | | |

**Gesamtergebnis:** ☐ Freigabe für Alltag  ☐ Nacharbeit nötig  

**Bekannte Issues (nach Live-Test hier eintragen):**

1. …
2. …

Nach erfolgreichem Live-Test:

1. Diese Datei Status-Zeile oben auf **durchgeführt** setzen (Datum + Kurzfazit).
2. Optional [CHANGELOG.md](CHANGELOG.md) / [Backlog.md](Backlog.md) A1 abhaken oder Notes ergänzen.
3. Profil-Fixes nur in **aircraft JSON** (PackageSources), nie Hardcode in C#.

---

## Schnellreferenz

| Aktion | Befehl / Ort |
|--------|----------------|
| Dist bauen | `powershell -ExecutionPolicy Bypass -File scripts/pack-installer.ps1` |
| Setup | `dist\CoPilotVoiceSetup\CoPilotVoiceSetup.exe` |
| Uninstall CLI | `CoPilotVoiceSetup.exe --uninstall` |
| Host headless inject | `CoPilotVoiceHost.exe --headless --offline --inject "Co Pilot …"` |
| Host tests | `dotnet test private-utility-copilot-voice/Sources/Host/CoPilotVoiceHost.Tests -c Release` |
| Installer tests | `dotnet test private-utility-copilot-voice/Sources/Installer/CoPilotVoiceSetup.Tests -c Release` |
| Config SSOT | `PackageSources/extras/config/` |
| Install state | `%LocalAppData%\PrivateCoPilotVoice\` |
| SimConnect-OK | Status **Live** + `[SimConnect] LIVE event sent: …` |
