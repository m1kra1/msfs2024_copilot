# Plan: LearnMode — Control Capture → Command Mapping

**Feature-ID:** L0  
**Prio:** P0  
**Backlog:** [Backlog.md](Backlog.md)  
**Branch:** `dev`  
**Status:** Phases 1–3 **shipped in 1.4.0** (MVP + watchlists/debounce + action hints/export/headless dump).

This document is the **binding coding-agent specification**. Follow [AGENTS.md](AGENTS.md). Do not invent Marketplace packaging, WASM co-pilot logic, or busy SimVar polling.

---

## 1. Goal

Enable a **Learn Mode** in the WPF host so a pilot can:

1. Toggle **Learn Mode** in the GUI (requires SimConnect **Live** for real capture).
2. Actuate a cockpit control in Free Flight.
3. See the resulting **signal** (watched SimVar / LVar value change) appear in the UI.
4. See whether that signal is already **mapped** to one or more voice commands.
5. **Create** or **edit** a command (phrases, TTS response, conditions, actions) and save it into the **active aircraft profile** JSON.

Result: aircraft controls can be voice-mapped incrementally without hand-editing JSON for every switch.

---

## 2. Non-goals (do not promise / do not build in MVP)

| Non-goal | Reason |
|----------|--------|
| Magically discover **unknown** LVar names for any switch on any aircraft | SimConnect only reports vars the client **subscribes** to |
| Pure “keyboard/joystick binding dump” for all MSFS inputs | Out of scope; different API surface |
| WASM co-pilot / H-Event bridge as dependency for MVP | Optional later (Backlog C3); LVar/`A:` watch first |
| Writing vendor LVars into `base_commands.json` | Study LVars → **active profile only** |
| Busy-polling SimVars every frame | Architecture lock — use period data (SECOND) |
| Full Commands-tab feature parity in v1 editor | Minimal create/edit fields OK; can deep-link to Commands tab later |

**Honest product copy (UI banner):**  
*Learn Mode detects value changes on watched SimVars/LVars. Unknown study switches need a catalog entry, profile watchlist, or a manual watch name.*

---

## 3. User flows

### 3.1 Happy path — unmapped switch

1. Free Flight, host **Live**, correct aircraft profile (auto-detect or Settings).
2. Open tab **Learn** → enable **Learn Mode**.
3. Host builds watch set, registers learn data definition, waits for baseline.
4. User flips a watched control (e.g. landing light).
5. Detection row appears: `LIGHT LANDING  0 → 1  [Unmapped]`.
6. User selects row → **Create Command**.
7. Form prefilled: suggested id, action `set_simvar` (and optional event if known), empty phrases.
8. User enters phrases + TTS response → **Save to profile**.
9. Catalog rebuilds; row badge becomes **Mapped**; Manual tab / voice can use the command.

### 3.2 Happy path — already mapped

1. Same as above; detection shows **Mapped** + command id(s) / primary phrase.
2. **Edit Command** loads that command into the form (profile override path).
3. User adjusts phrases/TTS/conditions/actions → Save.

### 3.3 Manual watch (study aircraft)

1. Learn Mode on.
2. User enters `L:S_OH_…` + units → **Add watch**.
3. Host re-registers learn defs (or adds field).
4. User actuates switch → detection if the LVar updates via SimConnect.

### 3.4 Self-echo suppression

1. User runs Manual/voice command that writes `L:S_MIP_GEAR`.
2. Learn Mode must **not** treat that as a new human learning event (or must suppress ~750 ms for those signal names).

---

## 4. Architecture

```
MSFS
  │  SimConnect SECOND (status + learn defs)
  ▼
ISimConnectClient
  Snapshot (status + learn watches)
  SetLearnWatchDefinitions / ClearLearnWatchDefinitions
  │
  ▼
HostSession (façade, no WPF)
  StartLearnMode / StopLearnMode / AddManualLearnWatch
  poll → LearnCaptureService.Observe(snapshot)
  after actions → SuppressSignals(...)
  │
  ├─ LearnWatchBuilder     (Core)  build watch list
  ├─ LearnCaptureService   (Core)  baseline + diff + ring buffer
  └─ CommandMappingIndex   (Core)  signal → commands
  │
  ▼
Ui/MainWindow Learn tab (thin shell only)
  toggle, list, detail, create/edit form → ApplyCommandSources
```

### Constraints

- `Config/`, `Core/`, `SimConnect/`, `Speech/`, `Models/`, `Diagnostics/`, `Host/` stay **WPF-free**.
- UI only: layout, binding, click → session APIs.
- Commands remain JSON-driven; save via existing `HostSession.ApplyCommandSources` / profile files under `PackageSources/extras/config` (runtime config root).

---

## 5. Data model

Add to `Models/Models.cs` (or `Models/LearnModels.cs` if cleaner):

```csharp
public enum LearnSignalKind
{
    SimVar,   // A: / unprefixed standard names in snapshot
    LVar,     // names starting with L:
    Event     // reserved for future; MVP may leave unused
}

public enum LearnMappingStatus
{
    Unmapped,
    Mapped,
    Ambiguous   // >1 commands share the same action signal
}

public sealed class LearnWatchEntry
{
    public string Name { get; set; } = "";
    public string Units { get; set; } = "number";
    public bool IsManual { get; set; }
}

public sealed class LearnDetection
{
    public string SignalName { get; init; } = "";
    public LearnSignalKind Kind { get; init; }
    public double? OldValue { get; init; }
    public double? NewValue { get; init; }
    public string Units { get; init; } = "number";
    public DateTimeOffset Utc { get; init; }
    public LearnMappingStatus MappingStatus { get; init; }
    public IReadOnlyList<string> MatchedCommandIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> MatchedCommandLabels { get; init; } = Array.Empty<string>();
    public string? SuggestedCommandId { get; init; }
    public IReadOnlyList<ActionDefinition> SuggestedActions { get; init; } = Array.Empty<ActionDefinition>();
}
```

Optional config models:

```csharp
public sealed class LearnWatchlistConfig
{
    public List<string> ExcludeNames { get; set; } = new();
    public List<LearnWatchEntry> DefaultWatches { get; set; } = new();
}
```

Aircraft profile extension (Phase 2; Phase 1 can skip file field if auto-derived only):

```json
"learn_watch": [
  { "name": "L:S_MIP_GEAR", "units": "number" }
]
```

---

## 6. Core components

### 6.1 `Core/CommandMappingIndex.cs`

- Built from `CommandCatalog`.
- Normalize action names: trim, case-insensitive; treat `L:foo` and `foo` consistently for LVars.
- Index keys = every `actions[].name` for types `event`, `set_simvar`, `simvar`.
- `Lookup(signalName)` → list of `CommandDefinition` (or ids + labels).
- Mapping status: 0 → Unmapped, 1 → Mapped, >1 → Ambiguous.

### 6.2 `Core/LearnWatchBuilder.cs`

Build `IReadOnlyList<LearnWatchEntry>` as **union** of:

1. **Discrete status vars** from `StatusSimVars.Definitions` minus continuous exclude set.  
   Default excludes:  
   `AIRSPEED INDICATED`, `PLANE ALTITUDE`, `VERTICAL SPEED`, `GROUND VELOCITY`, `PLANE HEADING DEGREES MAGNETIC`  
   (and any names from `learn_watchlist.json` exclude list when present).
2. All **set_simvar/simvar** action names from merged catalog (+ units from action if present).
3. All **condition simvar** names from catalog.
4. Profile `learn_watch` (Phase 2).
5. Global `learn_watchlist.json` default watches (Phase 2).
6. Session **manual** watches.

Deduplicate by normalized name. Cap list size (e.g. max **128** watches) with log if truncated.

### 6.3 `Core/LearnCaptureService.cs`

Responsibilities:

| Method | Behavior |
|--------|----------|
| `Start(watches)` | `IsActive=true`, store watch names, clear recent, `_baselineReady=false` |
| `Stop()` | inactive, clear baseline |
| `Observe(snapshot)` | if active: first complete pass stores baseline only; later passes emit diffs |
| `SuppressSignals(names, durationMs)` | ignore those names until UTC now + duration |
| `Recent` | bounded ring buffer (~100), newest first |
| `DetectionRaised` / count | for HostSession → UI |

Diff rules:

- Only watched names.
- Epsilon for floats: e.g. `Math.Abs(a-b) > 1e-4` (bool/int switches still fire).
- Skip if suppressed.
- On detection: resolve mapping via `CommandMappingIndex`; fill `SuggestedActions`:

```json
{ "type": "set_simvar", "name": "<SignalName>", "value": <NewValue>, "units": "<units>" }
```

Suggested id: `learn_<sanitized>_<value>` (sanitize to `[a-z0-9_]`).

**Do not** require WPF or SimConnect references inside this class — only `SimVarSnapshot` + pure logic.

### 6.4 Optional `Core/LearnActionHints.cs` (nice-to-have MVP+)

Small static map from common status vars → suggested `event` dual-write (e.g. `LIGHT LANDING` on → `LANDING_LIGHTS_ON`). Not required for MVP acceptance.

---

## 7. SimConnect changes

### 7.1 `ISimConnectClient`

```csharp
/// <summary>
/// Register extra named vars for Learn Mode (SECOND period).
/// Must use a separate data definition from status so failed LVars cannot break the dashboard.
/// No busy polling.
/// </summary>
void SetLearnWatchDefinitions(IReadOnlyList<(string Name, string Units)> vars);

void ClearLearnWatchDefinitions();
```

Default no-op implementations only if interface default methods are avoided — implement on all three clients.

### 7.2 `NativeSimConnectClient` (primary)

- New constants: `DEFINITION_LEARN`, `REQUEST_LEARN` (unique ids in `0xC0010xxx` / `0xC0020xxx` range unused by status/aircraft).
- `SetLearnWatchDefinitions`: ClearDataDefinition; for each var `AddToDataDefinition` FLOAT64; skip failures and log; store ordered field name list; `RequestDataOnSimObject` PERIOD_SECOND.
- On `RECV_SIMOBJECT_DATA` for REQUEST_LEARN: write doubles into `Snapshot` under each name.
- `ClearLearnWatchDefinitions`: clear def + stop request if API allows / re-request empty.

### 7.3 `ManagedSimConnectClient`

Best-effort same behavior (reflection path as status). If too heavy, implement no-op + log once — **Native is preferred** (already primary for Live). Prefer full support if feasible.

### 7.4 `RecordingSimConnectClient`

- Store learn watch list; `Set` on Snapshot remains test-driven.
- `SetLearnWatchDefinitions` records names for tests; no-op connect.

### 7.5 Critical

**Never** add experimental LVars into `DEFINITION_STATUS` / `StatusSimVars` array used by the flight dashboard — invalid names shorten payloads (known past bug class).

---

## 8. HostSession wiring

| API | Notes |
|-----|--------|
| `bool LearnModeActive` | |
| `int LearnWatchCount` | diagnostics |
| `IReadOnlyList<LearnDetection> LearnDetections` | |
| `event Action? LearnChanged` | or piggyback `StatusChanged` — prefer dedicated if cheap |
| `void StartLearnMode()` | no-op/warn if not Live; build watches; client.SetLearnWatch…; capture.Start |
| `void StopLearnMode()` | capture.Stop; ClearLearnWatchDefinitions |
| `void AddManualLearnWatch(string name, string units = "number")` | if active, rebuild watches + re-register |
| Poll path | when learn active, `Observe(_sim.Snapshot)` each identity tick or every poll (Observe must be cheap) |
| After `ActionExecutor.Execute` | `SuppressSignals` for action names ~750 ms |

Profile / catalog rebuild: rebuild `CommandMappingIndex` when catalog changes so badges stay correct.

**Offline testing:** allow StartLearnMode in offline only if useful for unit tests via session test hooks; GUI may disable toggle when `!IsLive`.

---

## 9. UI — Learn tab

### 9.1 Placement

New `TabItem Header="Learn"` in `MainWindow.xaml` — after **Manual**, before **Commands** (recommended).

### 9.2 Layout (text wireframe)

```
[!] Banner: watched vars only; manual watch for unknown LVars

[x] Learn Mode active     Watches: 42    Profile: fenix_a320    Live: Yes

Manual watch: [ L:........... ] [units: number v] [Add]

[x] Only unmapped   [Clear list]

┌ Recent detections ─────────────────────────────────────┐
│ 12:04:01  LIGHT LANDING     0 → 1    Unmapped          │
│ 12:03:55  L:S_MIP_GEAR      1 → 0    Mapped gear_up    │
└────────────────────────────────────────────────────────┘

Detail:
  Signal: ...
  Matched: ...
  Suggested actions: ...

  [Create Command]  [Edit Mapped]  [Copy name]

── Create / Edit ───────────────────────────────────────
  Id: [          ]
  Phrases (one per line): [                    ]
  Response: [          ]   Reject: [          ]
  Actions (read-only summary + optional raw): ...
  Conditions: simple optional later; Phase 1 can leave empty or single-line JSON
  [Save to active profile]  [Cancel]
```

### 9.3 Thin-shell rules

- No PhraseMatcher / SimConnect / merge logic in code-behind beyond calling session.
- Reuse styles from `DarkCockpit.xaml`.
- Save: construct/update `CommandDefinition` in **profile working copy**, keep base catalog unchanged for LVar commands; call `ApplyCommandSources(base, profile, saveToDisk: true)`.
- If command id exists in base and user edits from Learn, **profile override** (same id replace) — matches existing merge rules.

### 9.4 Commands tab interaction

Phase 1: self-contained mini editor on Learn tab.  
Optional: after save, if Commands tab has working copies, refresh them (same as profile Apply elsewhere).

---

## 10. Config files (Phase 1 vs 2)

| Phase | Config |
|-------|--------|
| **1 MVP** | Auto-derive watches only (status discrete + catalog). Manual watch in session memory. |
| **2** | `PackageSources/extras/config/learn_watchlist.json` + profile `learn_watch`; csproj CopyToOutputDirectory; sync Packages if shipping tree |

Do not hand-edit `bin/` copies.

---

## 11. File touch list (expected)

| Path | Action |
|------|--------|
| `Models/Models.cs` or `Models/LearnModels.cs` | New types |
| `Core/CommandMappingIndex.cs` | **New** |
| `Core/LearnWatchBuilder.cs` | **New** |
| `Core/LearnCaptureService.cs` | **New** |
| `SimConnect/ISimConnectClient.cs` | Extend |
| `SimConnect/NativeSimConnectClient.cs` | Learn DEF |
| `SimConnect/ManagedSimConnectClient.cs` | Best-effort |
| `SimConnect/RecordingSimConnectClient.cs` | Test support |
| `Host/HostSession.cs` | Wire learn |
| `Ui/MainWindow.xaml` | Learn tab |
| `Ui/MainWindow.xaml.cs` | Handlers only |
| `CoPilotVoiceHost.Tests/LearnModeTests.cs` | **New** |
| `README.md` / `CHANGELOG.md` / `Complete_Features.md` | When feature ships |
| `AGENTS.md` | GUI tabs table + learn note when feature ships |

---

## 12. Implementation phases

### Phase 1 — MVP (required for P0 done)

1. Models + CommandMappingIndex + LearnWatchBuilder + LearnCaptureService  
2. SimConnect learn definition (Native + Recording; Managed best-effort)  
3. HostSession API + poll Observe + suppress after actions  
4. Learn tab UI + create/edit + save to profile  
5. Unit tests (below) green  
6. Docs update (README snippet, CHANGELOG Unreleased, Complete_Features, Backlog L0 → done / move)

### Phase 2 — Watchlists & polish

- `learn_watchlist.json`, profile `learn_watch`  
- Debounce / group multi-var same second  
- Copy name; filter only-unmapped  
- Seed Fenix extras if needed  

### Phase 3 — Stretch

- True event notifications if practical  
- Action hints dual-write  
- Export detections  
- Headless learn dump  

---

## 13. Tests (mandatory)

Project: `CoPilotVoiceHost.Tests` — prefer real Core types, not reimplemented matcher.

| Test | Expectation |
|------|-------------|
| `CommandMappingIndex_Finds_SetSimVar_From_Catalog` | Catalog with `L:S_MIP_GEAR` → matched command id |
| `CommandMappingIndex_Unmapped_When_Missing` | Unknown signal → empty |
| `LearnWatchBuilder_Excludes_Continuous` | IAS not in watch set by default |
| `LearnWatchBuilder_Includes_Catalog_LVars` | set_simvar names present |
| `LearnCapture_Baseline_Does_Not_Emit` | first Observe no detection |
| `LearnCapture_Emits_On_Change` | 0→1 emits one detection |
| `LearnCapture_Suppress_Works` | suppressed name ignored |
| `LearnCapture_Recent_Bounded` | >100 inserts still cap |
| `RecordingClient_SetLearnWatch_DoesNotThrow` | interface completeness |

Run:

```bat
dotnet test private-utility-copilot-voice/Sources/Host/CoPilotVoiceHost.Tests -c Release
```

---

## 14. Acceptance criteria (Phase 1 done)

- [ ] Learn tab visible; Learn Mode toggle works when Live  
- [ ] Flipping a **watched** control produces a detection with old/new values  
- [ ] Mapped vs Unmapped badge correct vs current catalog  
- [ ] Create Command → Save writes active profile JSON + live catalog updates  
- [ ] Edit Mapped updates existing profile override  
- [ ] Manual watch can add `L:…` name  
- [ ] Self-triggered actions suppressed briefly  
- [ ] Status dashboard still receives TITLE/status (learn DEF isolated)  
- [ ] No busy poll; Core free of WPF  
- [ ] Release unit tests green  
- [ ] Docs: CHANGELOG + Complete_Features + Backlog status  

---

## 15. AGENTS.md checklist (agent must verify)

- [ ] Utility mod only  
- [ ] Out-of-process host  
- [ ] No co-pilot logic in WASM  
- [ ] SimConnect only  
- [ ] JSON profiles; no `if (Fenix)` for mapping  
- [ ] No busy SimVar polling  
- [ ] Core free of WPF  
- [ ] Config source of truth = PackageSources  
- [ ] Vendor LVars only in aircraft profile, not base  

---

## 16. Suggested commit order

1. `feat(learn): Core capture + mapping index + watch builder`  
2. `feat(learn): SimConnect learn data definition`  
3. `feat(learn): HostSession Start/Stop/Observe/Suppress`  
4. `feat(learn): GUI Learn tab create/edit/save`  
5. `test(learn): unit coverage`  
6. `docs(learn): README, CHANGELOG, Complete_Features, Backlog`  

---

## 17. Relationship to other backlog items

| Item | Relation |
|------|----------|
| **A1** Fenix live verify | Can use Learn Mode to confirm LVars change; still human checklist |
| **C1** LVar-read conditions | Learn registration path may share `SetLearnWatchDefinitions` patterns |
| **C2** Dynamic event map | Orthogonal (send path); not required for Learn MVP |
| **C3** H/B bridge | Only if SetSimVar write fails; not for capture MVP |

---

## 18. Open decisions (defaults if unset)

| Decision | Default |
|----------|---------|
| Learn while Offline | GUI disabled; tests use Recording client |
| Save target | **Active aircraft profile** always for Learn-created LVar commands |
| Max watches | 128 |
| Suppress window | 750 ms |
| Detection buffer | 100 |
| Period | SECOND (1 Hz) |

---

*End of Plan_LearnMode.md — implement only when the user explicitly requests coding for L0.*
