using System.Text.Json;
using System.Text.Json.Serialization;
using CoPilotVoiceHost.Models;

namespace CoPilotVoiceHost.Speech;

/// <summary>
/// Loads voices/{pack}/manifest.json and resolves command_id + kind → absolute WAV path.
/// Missing/invalid pack → empty map (safe Hybrid fallback / silent Wav).
/// </summary>
public sealed class VoicePackManifest
{
    private readonly string _packDirectory;
    private readonly Dictionary<(string CommandId, string Kind), string> _files =
        new(CommandKindComparer.Instance);

    public string PackDirectory => _packDirectory;
    public int EntryCount => _files.Count;
    public string? LoadWarning { get; private set; }

    private VoicePackManifest(string packDirectory)
    {
        _packDirectory = packDirectory;
    }

    public static string ResolvePackDirectory(string extrasRoot, string voicePack)
    {
        var pack = string.IsNullOrWhiteSpace(voicePack) ? "austrian_airlines_en_us" : voicePack.Trim();
        // Strip path separators — pack is a single folder name under voices/
        pack = pack.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()
               ?? "austrian_airlines_en_us";
        return Path.GetFullPath(Path.Combine(extrasRoot, "voices", pack));
    }

    public static VoicePackManifest Load(string extrasRoot, string voicePack)
    {
        var packDir = ResolvePackDirectory(extrasRoot, voicePack);
        var manifest = new VoicePackManifest(packDir);

        if (!Directory.Exists(packDir))
        {
            manifest.LoadWarning = $"[TTS][Wav] voice pack folder missing: {packDir}";
            Console.WriteLine(manifest.LoadWarning);
            return manifest;
        }

        var manifestPath = Path.Combine(packDir, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            manifest.LoadWarning = $"[TTS][Wav] manifest.json missing: {manifestPath}";
            Console.WriteLine(manifest.LoadWarning);
            return manifest;
        }

        try
        {
            var json = File.ReadAllText(manifestPath);
            var dto = JsonSerializer.Deserialize<VoicePackManifestDto>(json);
            if (dto?.Entries is null)
            {
                manifest.LoadWarning = $"[TTS][Wav] invalid manifest (no entries): {manifestPath}";
                Console.WriteLine(manifest.LoadWarning);
                return manifest;
            }

            foreach (var entry in dto.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.CommandId) || string.IsNullOrWhiteSpace(entry.Kind)
                    || string.IsNullOrWhiteSpace(entry.File))
                    continue;

                var relative = entry.File.Trim().Replace('\\', '/');
                if (relative.Contains("..", StringComparison.Ordinal)
                    || Path.IsPathRooted(entry.File)
                    || relative.StartsWith('/') || relative.Contains(':'))
                {
                    Console.WriteLine($"[TTS][Wav] rejected unsafe path in manifest: {entry.File}");
                    continue;
                }

                // Only allow file name or simple relative name under pack dir
                var fileName = Path.GetFileName(relative);
                if (string.IsNullOrWhiteSpace(fileName)
                    || !string.Equals(fileName, relative, StringComparison.OrdinalIgnoreCase)
                       && relative.Contains('/'))
                {
                    // Allow single-segment relative only (no subdirs in MVP for safety)
                    if (relative.Contains('/'))
                    {
                        Console.WriteLine($"[TTS][Wav] rejected nested path in manifest: {entry.File}");
                        continue;
                    }
                }

                var abs = Path.GetFullPath(Path.Combine(packDir, fileName));
                if (!abs.StartsWith(packDir, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"[TTS][Wav] rejected path outside pack: {entry.File}");
                    continue;
                }

                var key = (entry.CommandId.Trim(), entry.Kind.Trim());
                manifest._files[key] = abs;
            }
        }
        catch (Exception ex)
        {
            manifest.LoadWarning = $"[TTS][Wav] failed to load manifest: {ex.Message}";
            Console.WriteLine(manifest.LoadWarning);
            manifest._files.Clear();
        }

        return manifest;
    }

    /// <summary>
    /// Resolves a WAV path for commandId + kind. For reject, falls back to command_id "*".
    /// Returns null if no mapping or the file does not exist.
    /// </summary>
    public string? TryResolve(string? commandId, string? responseKind)
    {
        if (string.IsNullOrWhiteSpace(responseKind))
            return null;

        var kind = responseKind.Trim();
        if (!string.IsNullOrWhiteSpace(commandId))
        {
            if (_files.TryGetValue((commandId.Trim(), kind), out var path) && File.Exists(path))
                return path;
        }

        // Wildcard reject (and only for reject kind)
        if (string.Equals(kind, TtsResponseKind.Reject, StringComparison.OrdinalIgnoreCase)
            && _files.TryGetValue(("*", kind), out var wild)
            && File.Exists(wild))
        {
            return wild;
        }

        // Also allow "*" for the requested kind if present (manifest uses * only for reject today)
        if (_files.TryGetValue(("*", kind), out var anyWild) && File.Exists(anyWild))
            return anyWild;

        return null;
    }

    private sealed class CommandKindComparer : IEqualityComparer<(string CommandId, string Kind)>
    {
        public static readonly CommandKindComparer Instance = new();

        public bool Equals((string CommandId, string Kind) x, (string CommandId, string Kind) y) =>
            string.Equals(x.CommandId, y.CommandId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Kind, y.Kind, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string CommandId, string Kind) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.CommandId),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Kind));
    }

    private sealed class VoicePackManifestDto
    {
        [JsonPropertyName("entries")]
        public List<VoicePackEntryDto>? Entries { get; set; }
    }

    private sealed class VoicePackEntryDto
    {
        [JsonPropertyName("command_id")]
        public string? CommandId { get; set; }

        [JsonPropertyName("kind")]
        public string? Kind { get; set; }

        [JsonPropertyName("file")]
        public string? File { get; set; }
    }
}
