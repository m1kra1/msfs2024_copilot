# Voice Co-Pilot

A private spoken second crew member for Microsoft Flight Simulator 2024. The pilot talks aviation phraseology; the Host checks flight state, answers, and moves cockpit controls.

Bounded contexts and relationships: [CONTEXT-MAP.md](CONTEXT-MAP.md).

## Intent

This context exists so a private MSFS 2024 flight can have a co-pilot that hears, judges, speaks, and acts — without putting speech or SimConnect inside the simulator, and without encoding any one aircraft in code.

The job is one Session, one JSON-driven Command Catalog, and many Aircraft Profiles. WASM only marks that the Community Package is loaded. The GUI is a shell over the Session. Setup only delivers the same package into the Community Folder.

## Constraints

- Private Community utility only. Not a Marketplace product, not an aircraft package, not public distribution.
- The Host is the co-pilot. The WASM Marker carries no recognition, catalog, checklist, Learn, or SimConnect logic.
- Recognition, catalog, checklists, Learn, and SimConnect belong to the Session. The GUI and Headless CLI are shells over that Session.
- Aircraft behavior is data: Base Commands merged with exactly one Aircraft Profile. No aircraft-specific Event names in code. Profile `extends` is not inheritance; SimVar aliases are not a live mapping.
- Base Commands are standard Events. Vendor LVars live only on a Study Profile (Dual-write on the same Command id, or new ids).
- The sim is driven only through SimConnect (Events and Set SimVar). No third-party cockpit bridges.
- Pipeline order is binding: Speech Gate → Phrase match → Conditions → Callout → Actions.
- Phrase match is whole-word token sequences (exact, residual-ends-with, or contiguous tokens). Longer phrases win ties. Loose substring match is not a match.
- A Command may be Unable. Conditions and the Positive Climb Gate bind before Actions. Empty Actions is a valid Info-only Command.
- A Checklist is a first-class sequential procedure, not a Command flag or a multi-action Command. Residual containing the whole word *checklist* is intercepted before the Command Catalog.
- Live means Actions reach the sim. Offline still matches and speaks; the aircraft does not move.
- Auto-detect may switch the Aircraft Profile from Aircraft Identity. It does not invent profiles. With Auto-detect off, identity is still observed; the Aircraft Profile does not change.
- Learn Mode observes known Watches. It does not discover unknown LVars. Host-caused echoes are not Detections.
- Apply is in-memory. Save is Apply plus disk. Reload is disk over memory.

## Ubiquitous Language

### Product

**Voice Co-Pilot**:
The product: a spoken second crew member for a private MSFS 2024 session.
_Avoid_: addon, plugin, bot, marketplace app, in-sim tool

**Host**:
The out-of-process desktop process that is the co-pilot (GUI by default, or Headless).
_Avoid_: client, WASM module, in-sim gauge

**Session**:
The running co-pilot instance shared by GUI and Headless: Settings, Catalog, speech, SimConnect, checklists, Learn.
_Avoid_: app state, view-model, window

**Community Package**:
The drop-in MSFS folder (`private-utility-copilot-voice`) that holds the WASM Marker plus extras.
_Avoid_: marketplace package, aircraft package, addon zip

**WASM Marker**:
The empty simulator module that only proves the Community Package is loaded.
_Avoid_: co-pilot module, WASM host, in-sim logic

**extras**:
The Host files inside the Community Package — executable, config, voices — not the WASM Marker.
_Avoid_: assets, content, modules

**GUI**:
The desktop window that is a thin shell over the Session.
_Avoid_: the app (when you mean the Session), cockpit panel

**Headless**:
The same Session without a window, driven by CLI (inject, once, Learn dump).
_Avoid_: server, service, daemon

### Speech

**Phrase**:
A spoken (or injected) utterance the matcher compares to Command and Checklist phrase lists.
_Avoid_: utterance, transcript, query, skill, script

**Residual**:
The Phrase after a leading Wake Word is stripped.
_Avoid_: command text (when you mean the stripped Phrase), raw STT

**Wake Word**:
The leading words that open the Speech Gate (default *Co Pilot*).
_Avoid_: hotword, trigger, prefix (unless you mean Residual stripping)

**Speech Gate**:
Admission of a Phrase: Wake Word, armed PTT (including PTT Grace), Continuous Listen, or Force Gate.
_Avoid_: filter, recognizer, grammar (grammar is how STT is loaded, not the gate)

**PTT**:
A held key that opens the Speech Gate so a bare Phrase is admitted.
_Avoid_: hotkey, shortcut, modifier

**PTT Grace**:
The short window after PTT release during which the gate stays open so recognition can finish.
_Avoid_: debounce, timeout (generic)

**Continuous Listen**:
A Settings mode in which the Speech Gate admits every Phrase without Wake Word or PTT.
_Avoid_: always on, open mic (as a synonym for the setting)

**Force Gate**:
An explicit bypass of the Speech Gate (Manual, Debug inject, tests). Conditions still apply.
_Avoid_: cheat, skip, ungated command (Conditions are not skipped)

**Hypothesis**:
One STT candidate (primary or alternate) re-ranked against the loaded Catalog.
_Avoid_: guess, n-best (unless you mean the raw engine list)

### Command Catalog

**Command**:
A catalog entry: stable id, Phrases, Conditions, Actions, Response, optional Reject Response.
_Avoid_: skill, script, binding, hotkey, checklist (a Checklist is not a Command)

**Command Catalog**:
The live merged set of Commands the Session matches and executes.
_Avoid_: command list (the spoken *list commands* readout), profile (the overlay is the Aircraft Profile)

**Base Commands**:
The aircraft-agnostic Command Catalog. Standard Events only; no vendor LVars.
_Avoid_: default profile, generic commands (generic is an Aircraft Profile id)

**Merge**:
How an Aircraft Profile becomes the Catalog: same Command id replaces Base; new ids append; Event Aliases rewrite Event names. Not a chain of `extends`.
_Avoid_: inherit, extends, overlay chain, simvar alias merge

**Event Alias**:
A name rewrite on an Aircraft Profile applied at Merge so an Action Event can keep or change its sim Event name.
_Avoid_: SimVar alias (not live), mapping, remap in code

**Condition**:
A SimVar comparison that must hold against the SimVar Snapshot before Actions run.
_Avoid_: check, assert, gate (reserve Gate for Speech Gate and Positive Climb Gate)

**Action**:
One thing a Command does to the sim: an Event and/or Set SimVar. Empty Actions is allowed.
_Avoid_: event (an Event is one Action type), command (the Command owns Actions)

**Event**:
A named SimConnect client event sent to the sim (gear, lights, flaps, …).
_Avoid_: key event, SimEvent, H-Event, B-Event

**Set SimVar**:
A write of a SimVar or LVar through SimConnect. Live only when the Session is Live.
_Avoid_: LVar inject, AAO, SPAD, calculator code

**Dual-write**:
A Study Profile Command whose Actions include both an Event and one or more Set SimVar writes.
_Avoid_: hybrid command (Hybrid is a TTS engine), Fenix special case in code

**LVar**:
A vendor local variable written only from an Aircraft Profile Action, never from Base Commands.
_Avoid_: SimVar (SimVar is the general snapshot/condition name; LVar is vendor-local)

**Info-only Command**:
A Command with no Actions (and usually no Conditions). Spoken text may be static or derived from the live Catalog.
_Avoid_: help command, hardcoded list

**Response**:
The success Callout for an allowed Command.
_Avoid_: TTS string, message, prompt (a Challenge is a Checklist prompt)

**Reject Response**:
The Callout when Conditions fail. Product idiom is *Unable*.
_Avoid_: error, exception, fail message

**Unable**:
The spoken refusal when a Command is not allowed.
_Avoid_: error, denied, blocked

**Command Result**:
The outcome of one gated Phrase: allowed or Unable, spoken text, Actions that ran.
_Avoid_: return code, status (Status is the live Session readout)

### Aircraft

**Aircraft Profile**:
The per-aircraft overlay merged with Base Commands (ids such as `generic`, `fenix_a320`). The active Settings value.
_Avoid_: profile (alone), aircraft (the Detected Aircraft), livery, title

**Detected Aircraft**:
What the sim currently is, from TITLE and ATC MODEL. Displayed even when Auto-detect is off.
_Avoid_: Aircraft Profile, identity (use Aircraft Identity for the pair of strings)

**Aircraft Identity**:
The pair TITLE + ATC MODEL used for Auto-detect and change detection.
_Avoid_: aircraft name, spawn title

**Auto-detect**:
Rules that may switch the Aircraft Profile from Aircraft Identity while Live (unless the Session is profile-locked).
_Avoid_: autoload, aircraft match (the matcher output is a profile id)

**Detection Rule**:
One pattern + field (title, ATC model, or any) + Aircraft Profile id. First match wins.
_Avoid_: regex rule (match is case-insensitive contains), heuristic

**Fallback Profile**:
The Aircraft Profile used when no Detection Rule matches (shipped: `generic`).
_Avoid_: default settings, base profile (Base Commands are not a profile)

**Study Profile**:
An Aircraft Profile that Dual-writes vendor LVars (and may append new ids) on top of Base Events.
_Avoid_: Fenix code path, study-level C#

### Checklists

**Checklist**:
A named sequential procedure (phrases, items, optional Assigned Profiles). First-class, not a Command.
_Avoid_: command sequence, multi-action command, `checklist` flag on a Command

**Checklist Item**:
One step of a Checklist: Verify or Execute, with a Challenge.
_Avoid_: task, step (unless you mean this item), command

**Challenge**:
The spoken prompt for a Checklist Item.
_Avoid_: response, callout (those are Command/confirm speech)

**Verify**:
A Checklist Item that waits until Expected Conditions match or the pilot says Continue.
_Avoid_: check, condition-only command

**Execute**:
A Checklist Item that runs a catalog Command or free Actions, then confirms.
_Avoid_: command (Execute may *use* a Command id)

**Continue**:
Pilot speech that skips the wait on the current Verify item (*continue*, *go on*, *next*).
_Avoid_: skip checklist, next checklist (that would start another Checklist)

**Stop Checklist**:
Pilot speech that cancels the Checklist Run (*stop checklist*, *cancel checklist*, *abort checklist*).
_Avoid_: abort (alone), cancel command

**Assigned Profiles**:
Which Aircraft Profile ids may use a Checklist. Empty means every profile.
_Avoid_: aircraft filter, visibility (GUI filter is not the assignment)

**Checklist Run**:
The live sequential execution of one Checklist, one item at a time.
_Avoid_: job, workflow, command processor

### Learn

**Learn Mode**:
A Live capture mode that watches known signals and records Detections for mapping onto the active Aircraft Profile.
_Avoid_: recorder, spy, auto-discover, LVar scanner

**Watch**:
A named SimVar or LVar Learn Mode observes (status discretes, catalog, watchlist, profile, or session-manual).
_Avoid_: subscription, probe, sensor

**Detection**:
An observed value change on a Watch, grouped with same-tick changes and classified Mapped, Unmapped, or Ambiguous.
_Avoid_: event (an Event is a sim Action), hit, change log

**Mapped**:
A Detection whose signal already appears as an Action on exactly one Command.
_Avoid_: known, bound

**Unmapped**:
A Detection whose signal is not an Action on any Command.
_Avoid_: new LVar, discovered variable

**Ambiguous**:
A Detection whose signal appears as an Action on more than one Command.
_Avoid_: conflict, duplicate

### Flight State

**Live**:
The Session is connected such that Events and Set SimVar actually reach MSFS.
_Avoid_: connected (Connected is the session; Live is that the sim applies it), online

**Offline**:
The Session runs without a live sim. Matching and speech still work; the aircraft does not move.
_Avoid_: disconnected (you can be Connected and still not Live), mock

**Connected**:
A SimConnect session is open. Not sufficient for the aircraft to move.
_Avoid_: Live, ready

**SimVar Snapshot**:
The current flight-state values used by Conditions, Flight Data, checklists, and Learn.
_Avoid_: telemetry dump, poll result (there is no busy poll)

**Flight Data**:
The live readout of identity and flight state (title, airport, altitude, IAS, vertical speed, on ground).
_Avoid_: HUD, instruments (those are in the sim)

**Positive Climb Gate**:
The extra vertical-speed Condition for gear-up (or flagged Commands) when that behavior is on.
_Avoid_: gear condition (the Command may list more Conditions), safety check

### Settings and Voice

**Settings**:
The Session preferences: speech, TTS, behavior, active Aircraft Profile, Auto-detect.
_Avoid_: config (the whole on-disk set includes Catalog, profiles, checklists), options

**Apply**:
Copy edits into the live Session only. Does not re-read disk.
_Avoid_: save, reload, commit

**Save**:
Apply, then write Settings (or command / checklist sources) to disk.
_Avoid_: apply (Apply is memory-only)

**Reload**:
Replace Session memory from disk. Discards unsaved Apply edits.
_Avoid_: refresh, sync (sync is the repo config copy rule, not this)

**Voice Pack**:
A named set of WAV Callouts keyed by Command id and kind (success or reject).
_Avoid_: voice (the Windows SAPI voice), sound pack, airline pack (the pack is data, not the airline)

**Callout**:
Spoken confirmation (Response or Reject Response) that precedes Actions when confirmation is on.
_Avoid_: beep, announcement (profile-switch announce is separate)

**Hybrid TTS**:
Voice Pack WAV when mapped for that Command and kind; otherwise Windows SAPI.
_Avoid_: dual-write (that is Actions), mixed engine (the setting name is Hybrid)

**Manual**:
Firing a Catalog Command by id without the Speech Gate. Conditions still apply.
_Avoid_: debug click, bypass (Force Gate only; not a Conditions bypass)

**Inject**:
Feeding a Phrase into the Session as if it were spoken (Debug, Headless, tests).
_Avoid_: simulate, fake STT

### Delivery

**Community Folder**:
The MSFS Community directory where the Community Package is installed.
_Avoid_: install dir (Setup also writes shortcuts and install state), AppData (state only)

**Setup**:
The installer that copies the Payload into the Community Folder and creates Host shortcuts.
_Avoid_: in-sim installer, Marketplace install, Host (the Host is the co-pilot)

**Payload**:
The packaged Community Package tree Setup copies.
_Avoid_: extras (extras is inside the package), dist (a build output, not the domain object)
