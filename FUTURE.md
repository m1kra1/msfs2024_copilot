# Zukünftige Anpassungen

Ideen und geplante Erweiterungen für den Private Voice Co-Pilot  
(`private-utility-copilot-voice` / MSFS 2024).

Stand: 2026-08-09 · Branch: `dev`

---

## 1. Fenix A320 Anpassung — **DONE (profile)** / partial (LVar bridge)

**Status:** Profile `config/aircraft/fenix_a320.json` implemented and selectable (`aircraft_profile: fenix_a320`).  
Core groups map via standard SimConnect **events** (gear, external lights, flaps, parking brake, AP master, flight director toggle). Unmapped Airbus FCU mode holds return **Unable – not available on this aircraft**. FCU bug/var knobs kept best-effort.

**Tested Fenix version:** *unverified in this development/CI environment* (no Free Flight + Fenix available for agent verification). Live Free Flight with a current Fenix A320 MSFS 2024 build is still recommended once.

### Known limitations

- Host action pipeline = SimConnect `TransmitEvent` / local SetSimVar only — **no LVar / H-Event / B-Event write bridge**.
- True Fenix-only overhead/FCU systems that require custom variables remain Unable or base best-effort until a future bridge exists.
- Automatic aircraft detection: **done** (`auto_detect_aircraft` + `aircraft_detection.json`; live TITLE/ATC MODEL).

### Original goal (reference)

- Fenix nutzt oft **eigene LVars / H-Events** statt (oder zusätzlich zu) reinen Standard-SimConnect-Events.
- Fallback Unable TTS: implemented for unmapped FCU modes.

### Abnahmekriterien

- [x] Profil wählbar über `settings.json` → `aircraft_profile` (`fenix_a320`) und GUI.
- [x] Mindestens: Gear, Lights (Landing/Taxi/Strobe/Beacon/Nav), Flaps, AP Master, Parking Brake (+ FD) mapped as events.
- [x] FCU mode holds: Unable; bug/var inc/dec best-effort.
- [ ] Live-Test in Free Flight mit Fenix A320 und Host-Log `LIVE event sent` + sichtbare Cockpit-Reaktion (human / sim environment).

### Optional backlog

- LVar/H-Event bridge (architecture extension — out of current non-goals).
- Auto profile switch by aircraft title.

---

## 2. Anweisungsliste ausgeben — **DONE** (Unreleased / `list_commands`)

**Status:** Implemented. Voice command `list_commands` builds the list from the live merged catalog; TTS short summary + full `[CommandList]` lines in console/Debug log. File export / clipboard remain optional backlog.

### Use Cases

- Pilot fragt: *„Co Pilot, what can you do?“* / *„list commands“* / *„command list“*.
- Debugging: vollständige Phrasenliste aus geladenem Base- + Aircraft-Profil.
- Dokumentation: Export der aktuellen Grammar für README oder Training (optional, not yet).

### Implemented

- `base_commands.json` → `id: list_commands` with required phrases; `actions: []`.
- `CommandListBuilder` + `CommandProcessor` dynamic response from current catalog.
- Profile Apply/Reload updates subsequent list output.
- Optional later: `docs/commands_export.txt` or Clipboard.

### Abnahmekriterien

- [x] Sprachtrigger lädt die Liste aus dem echten geladenen Katalog (kein Hardcode).
- [x] Konsole zeigt alle Command-IDs und mindestens eine Phrase pro Command.
- [x] TTS liefert eine nutzbare Kurzfassung (count + example phrases).
- [x] Aircraft-Profile-Zusatzbefehle erscheinen in der Liste, wenn das Profil aktiv ist.

---

## Weitere Ideen (Backlog, ungeordnet)

- Weitere Aircraft-Profile (z. B. PMDG 737, iniBuilds A3xx) analog zu Fenix.
- Checklisten-Sequenzen mit echten Schalter-Aktionen pro Aircraft.
- Deutschsprachige Phraseology (`culture` / zusätzliche JSON-Packs).
- Optionaler Auto-Start des Hosts (externer Launcher / EXE.xml – nur wenn gewünscht).
- WAV-Callouts statt/zusätzlich zu Windows-TTS.
- Fenix LVar/H-Event bridge for systems that ignore standard events.

---

## Priorität (Vorschlag)

| Prio | Thema              | Abhängigkeit        |
|------|--------------------|---------------------|
| —    | ~~Fenix A320 Profil~~ | Done (`fenix_a320`; live cockpit check still human) |
| —    | ~~Anweisungsliste~~ | Done (`list_commands`) |
| next | Fenix LVar bridge (optional) | Architecture extension |

---

*Dieses File ist die Planungssammlung – Umsetzung erfolgt schrittweise auf `dev`.*
