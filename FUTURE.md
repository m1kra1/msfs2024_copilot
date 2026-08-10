# Zukünftige Anpassungen

Ideen und geplante Erweiterungen für den Private Voice Co-Pilot  
(`private-utility-copilot-voice` / MSFS 2024).

Stand: 2026-08-09 · Branch: `dev`

---

## 1. Fenix A320 Anpassung — **DONE (profile)** / partial (LVar bridge)

**Status:** Profile `config/aircraft/fenix_a320.json` implemented and selectable (`aircraft_profile: fenix_a320`).  
Gear lever uses Fenix LVar **`L:S_MIP_GEAR`** (from package `Cockpit_Behavior.xml`) plus `GEAR_UP`/`GEAR_DOWN`. Positive-rate phrases + airborne/VS gates. Other core groups still use standard events (lights partially — switch animation needs LVars). Unmapped Airbus FCU mode holds return **Unable**.

**Tested Fenix version:** package LVars verified from installed `fnx-aircraft-320`; live cockpit re-test recommended after host publish.

### Known limitations

- Host can `TransmitEvent` + SimConnect **SetDataOnSimObject** for `L:` / `A:` names (no SPAD/AAO). H-Events/B-Events still not bridged.
- Exterior light **switch** animation still needs `S_OH_EXT_LT_*` LVars (next fix).
- Automatic aircraft detection: **default on** (`auto_detect_aircraft: true`); rules in `aircraft_detection.json` (Fenix → `fenix_a320`).

### Original goal (reference)

- Fenix nutzt oft **eigene LVars / H-Events** statt (oder zusätzlich zu) reinen Standard-SimConnect-Events.
- Fallback Unable TTS: implemented for unmapped FCU modes.

### Abnahmekriterien

- [x] Profil wählbar über `settings.json` → `aircraft_profile` (`fenix_a320`) und GUI.
- [x] Mindestens: Gear, Lights (Landing/Taxi/Strobe/Beacon/Nav), Flaps, AP Master, Parking Brake (+ FD) mapped as events.
- [x] Gear lever LVar `S_MIP_GEAR` + positive-rate phraseology.
- [x] FCU mode holds: Unable; bug/var inc/dec best-effort.
- [ ] Live-Test in Free Flight mit Fenix A320 und Host-Log `LIVE event sent` / `LIVE SetSimVar` + sichtbare Cockpit-Reaktion (human).

### Optional backlog

- Broader Fenix LVar map for overhead (anti-ice, APU, …).
- H-Event / B-Event bridge if SetDataOnSimObject is insufficient on a future Fenix build.

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
