# Zukünftige Anpassungen

Ideen und geplante Erweiterungen für den Private Voice Co-Pilot  
(`private-utility-copilot-voice` / MSFS 2024).

Stand: 2026-08-10 · Branch: `dev`

---

## Study-Level LVar strategy (locked)

**Ja zum LVar-System — nein zu „alles generisch auf LVars“.**

| Layer | Policy |
|-------|--------|
| `base_commands.json` | **Event-first** (Asobo / generic). Never put vendor LVars here. |
| Study profiles (`fenix_a320`, later `inibuilds_a350`, …) | **Override by command id** with dual-write: `event` + `set_simvar` (`L:` / `A:`). New ids append. |
| Host | `set_simvar` → SimConnect `SetDataOnSimObject`. No SPAD/AAO. H/B-Events only if LVar write fails live. |
| Cross-vendor | **Separate LVar maps** per aircraft. Same *mechanism*, not shared names (Fenix ≠ A350). |
| Gaps | Prefer **Unable** TTS over guessed mappings (FCU modes). |

### Priority order (per profile)

1. **P0** Gear + exterior lights (switch animation)
2. **P1** Parking brake, spoilers/speedbrake, flaps discrete
3. **P2** Overhead / systems voice commands (anti-ice, APU, fuel, packs, ADIRS, signs, …)
4. **P3** Checklists with real multi-action sequences
5. Optional: H/B-Event bridge; LVar *read* conditions

### Anti-patterns

- LVars in base catalog  
- One shared “Airbus LVar” profile for Fenix + iniBuilds  
- Aircraft-specific `if` branches in C#  
- Dropping events before dual-write is live-verified  

---

## 1. Fenix A320 — **DONE (expanded LVar map)** / live human re-test open

**Status:** Profile `config/aircraft/fenix_a320.json` is the study-level hybrid map.

| Area | Mapping |
|------|---------|
| Gear | `GEAR_*` + `L:S_MIP_GEAR` (0=UP, 1=DOWN); airborne + VS gate |
| Exterior lights | Events + `L:S_OH_EXT_LT_*` (landing 0/1/2, nose, strobe, beacon, nav/logo, wing) |
| Flaps | Events + `L:S_FC_FLAPS` (0–4); incr/decr event-only |
| Parking brake | Event + `L:S_MIP_PARKING_BRAKE` (0/1) |
| Speedbrake / spoilers | Events + `L:A_FC_SPEEDBRAKE` (0=ARM, 1=RETRACT, 2=DETENT) |
| Anti-ice / probe | Events + `L:S_OH_PNEUMATIC_*_ANTI_ICE`, `L:S_OH_PROBE_HEAT` |
| APU | Events + MASTER/START/BLEED LVars; extra `apu_master_*` / `apu_bleed_*` |
| Overhead extras | Batteries, EXT PWR, fuel pumps, packs, ADIRS NAV, seatbelt signs, dome |
| Checklists | `checklist_*` fire real multi-action sequences (not TTS-only) |
| FCU mode holds | Unable (empty actions) |

**Sources for LVar names/enums:** Fenix `Cockpit_Behavior.xml` naming + community AAO/YourControls maps. Live cockpit re-test after each Fenix package update.

### Abnahmekriterien

- [x] Profile selectable + auto-detect (`fenix` → `fenix_a320`)
- [x] P0 gear + exterior lights dual-write
- [x] P1 parking / spoilers / flaps dual-write
- [x] P2 overhead + P3 checklists with actions
- [ ] Human Live Free Flight: `LIVE event` / `LIVE SetSimVar` + visible switches

### Optional backlog

- More overhead (IDG, hyd pumps, fire test, …)
- H/B-Event bridge if SetDataOnSimObject insufficient on a future build
- LVar-backed conditions (read switch state)

---

## 2. Anweisungsliste — **DONE** (`list_commands`)

Live catalog → TTS summary + Debug/console full list. File export / clipboard optional.

---

## 3. iniBuilds A350 (planned)

- New profile (e.g. `inibuilds_a350.json`) with **its own** LVars — do not copy Fenix names.
- Auto-detect rule **before** generic A3xx patterns.
- Same hybrid dual-write + Unable for unmapped FCU.
- Start with P0/P1 groups after A350 LVar discovery.

---

## Weitere Ideen (Backlog)

- PMDG 737 / other study profiles  
- Deutschsprachige Phraseology  
- Optional host auto-start (EXE.xml / external launcher)  
- WAV callouts vs Windows TTS  

---

## Priorität

| Prio | Thema | Status |
|------|--------|--------|
| — | Fenix hybrid LVar map (P0–P3) | Done (JSON); live human check open |
| — | `list_commands` | Done |
| next | Live Fenix cockpit verification | Human |
| later | iniBuilds A350 profile | When flying the aircraft |
| optional | H/B-Event bridge | Only if LVar write fails |

---

*Dieses File ist die Planungssammlung – Umsetzung erfolgt schrittweise auf `dev`.*
