# Zukünftige Anpassungen

Ideen und geplante Erweiterungen für den Private Voice Co-Pilot  
(`private-utility-copilot-voice` / MSFS 2024).

Stand: 2026-08-07 · Branch: `dev`

---

## 1. Fenix A320 Anpassung

**Ziel:** Zuverlässige Co-Pilot-Steuerung speziell für die **Fenix A320**-Serie  
(nicht nur generische Standard-Events).

### Hintergrund

- Fenix nutzt oft **eigene LVars / H-Events** statt (oder zusätzlich zu) reinen Standard-SimConnect-Events.
- Befehle wie Landing Lights, FCU, Anti-Ice, ECAM-relevante Schalter können am Standard-Airliner  
  „gesprochen und geloggt“ werden, im Fenix-Cockpit aber **keine sichtbare Wirkung** haben.

### Geplante Umsetzung

- Aircraft-Profil erweitern/neu: `config/aircraft/fenix_a320.json` (oder `a320_fenix.json`).
- Mapping von Phrase → **Fenix-spezifische Events / LVars** (soweit über SimConnect erreichbar).
- Optional: Auto-Erkennung des Flugzeugs (Title / ICAO / aircraft.cfg) und automatische Profilwahl.
- Fallback: bei unbekannten Variablen klare TTS-Antwort („Unable – not available on this aircraft“).
- Dokumentation der getesteten Fenix-Version und bekannter Limitierungen.

### Abnahmekriterien (Vorschlag)

- [ ] Profil wählbar über `settings.json` → `aircraft_profile` (z. B. `fenix_a320`).
- [ ] Mindestens: Gear, Lights (Landing/Taxi/Strobe/Beacon/Nav), Flaps, AP Master, Parking Brake.
- [ ] FCU-relevante Befehle soweit mit Fenix kompatibel (SPD/HDG/ALT/VS oder Äquivalente).
- [ ] Live-Test in Free Flight mit Fenix A320 und Host-Log `LIVE event sent` + sichtbare Cockpit-Reaktion.

---

## 2. Anweisungsliste ausgeben

**Ziel:** Der Co-Pilot soll auf Anfrage die **verfügbaren Sprachbefehle** ausgeben  
(und optional in der Konsole / als Datei auflisten).

### Use Cases

- Pilot fragt: *„Co Pilot, what can you do?“* / *„list commands“* / *„command list“*.
- Debugging: vollständige Phrasenliste aus geladenem Base- + Aircraft-Profil.
- Dokumentation: Export der aktuellen Grammar für README oder Training.

### Geplante Umsetzung

- Neuer Befehl in `base_commands.json`, z. B. `id: list_commands` mit Phrasen:
  - `list commands`
  - `what can you do`
  - `command list`
  - `available commands`
- Host baut die Antwort aus dem **aktuell geladenen** Command-Katalog (nach Merge Base + Aircraft).
- Ausgabe-Kanäle:
  1. **TTS** – kurze Zusammenfassung oder gestaffelte Callouts (Chunks, wegen Länge).
  2. **Konsole** – vollständige Liste (id + Phrasen).
  3. Optional später: Schreiben nach `docs/commands_export.txt` oder Clipboard.
- Verhalten ohne SimConnect-Action (reine Info, `actions: []`).

### Abnahmekriterien (Vorschlag)

- [ ] Sprachtrigger lädt die Liste aus dem echten geladenen Katalog (kein Hardcode).
- [ ] Konsole zeigt alle Command-IDs und mindestens eine Phrase pro Command.
- [ ] TTS liefert eine nutzbare Kurzfassung (z. B. Kategorien: Gear, Lights, Flaps, AP, …).
- [ ] Aircraft-Profile-Zusatzbefehle (z. B. Fenix) erscheinen in der Liste, wenn das Profil aktiv ist.

---

## Weitere Ideen (Backlog, ungeordnet)

- Weitere Aircraft-Profile (z. B. PMDG 737, iniBuilds A3xx) analog zu Fenix.
- Checklisten-Sequenzen mit echten Schalter-Aktionen pro Aircraft.
- Deutschsprachige Phraseology (`culture` / zusätzliche JSON-Packs).
- Optionaler Auto-Start des Hosts (externer Launcher / EXE.xml – nur wenn gewünscht).
- WAV-Callouts statt/zusätzlich zu Windows-TTS.

---

## Priorität (Vorschlag)

| Prio | Thema              | Abhängigkeit        |
|------|--------------------|---------------------|
| 1    | Anweisungsliste    | Host + base_commands |
| 2    | Fenix A320 Profil  | Live-Fenix + Mapping |

---

*Dieses File ist die Planungssammlung – Umsetzung erfolgt schrittweise auf `dev`.*
