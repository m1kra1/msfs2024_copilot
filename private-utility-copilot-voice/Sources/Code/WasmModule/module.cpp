// private-utility-copilot-voice — Standalone WASM marker module
// Unique module name: private_utility_copilot_voice
// No core co-pilot logic. Host (CoPilotVoiceHost.exe) owns STT/TTS/SimConnect.

#include <MSFS/MSFS.h>
#include <MSFS/MSFS_WindowsTypes.h>
#include <SimConnect.h>

extern "C" MSFS_CALLBACK void module_init(void)
{
    // Marker / logging only. Optional future Custom-Event bridge registration.
}

extern "C" MSFS_CALLBACK void module_deinit(void)
{
}

extern "C" MSFS_CALLBACK void module_update(void)
{
    // Empty — no logic in WASM.
}
