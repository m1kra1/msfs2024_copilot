Private Voice Co-Pilot (private-utility-copilot-voice)
=====================================================
Private-use utility mod for Microsoft Flight Simulator 2024.
Creator: Private | Content type: MISC | Not for Marketplace.

WHAT IT IS
----------
Out-of-process voice co-pilot host that listens for aviation phraseology,
checks flight state via SimConnect, speaks a response, and sends standard
SimConnect events (gear, lights, flaps, FCU/AP, anti-ice, parking brake, etc.).

ARCHITECTURE
------------
- WASM module (modules/): marker only (module_init/deinit/update). No core logic.
- Host EXE (extras/): CoPilotVoiceHost.exe — STT, TTS, SimConnect, JSON profiles.

INSTALL
-------
1. Copy the built package folder "private-utility-copilot-voice" into your
   Community2024 folder (e.g. ...\Microsoft Flight Simulator 2024\Community\).
2. Start MSFS 2024 and load a Free Flight session.
3. Start the host manually (it does not auto-start from the sim):

     run_copilot.bat

   or double-click CoPilotVoiceHost.exe from this extras folder.

USAGE
-----
- Default wake word: "Co Pilot" (see config/settings.json → speech.wake_word)
- Optional PTT key: F12 (speech.ptt_key)
- continuous_listen is false by default (wake word or PTT required)
- Example: "Positive Climb Gear Up" → host checks vertical speed & gear,
  replies "Checked. Gear up.", then sends GEAR_UP if conditions pass.

CONFIGURATION
-------------
- config/settings.json     — SimConnect app name, speech, TTS, behavior flags
- config/base_commands.json — Core phrases, conditions, actions
- config/aircraft/*.json  — Aircraft profile overrides (generic, a320, b737)
- Set behavior.require_positive_climb_for_gear_up to gate gear-up calls

VOICES
------
Windows TTS by default (tts.voice). Optional WAV packs can live under
voices/copilot_en_us/ for future pre-recorded callouts.

BUILD NOTES
-----------
- WASM: VS 2022 + MSFS 2024 Platform Toolset → Sources/Code/WasmModule
- Host: .NET 8 → Sources/Host/CoPilotVoiceHost (publish into extras/)
- Package: MSFS Project Editor → load project → Build Package

PRIVATE USE
-----------
No Content Manager / Marketplace validation. Package name and creator
reflect private character. Do not redistribute without your own review.

TROUBLESHOOTING
---------------
- Host console logs SimConnect connect/fail and recognized phrases.
- Ensure MSFS is running before starting the host for live control.
- Unique SimConnect app name: PrivateCoPilotVoice
