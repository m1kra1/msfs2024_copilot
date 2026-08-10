using System.Text.Json.Serialization;

namespace CoPilotVoiceHost.Models;

public enum LearnSignalKind
{
    SimVar,
    LVar,
    /// <summary>Reserved for future event capture; unused in MVP.</summary>
    Event
}

public enum LearnMappingStatus
{
    Unmapped,
    Mapped,
    /// <summary>More than one command shares the same action signal.</summary>
    Ambiguous
}

public sealed class LearnWatchEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("units")]
    public string Units { get; set; } = "number";

    /// <summary>True when the user added this watch in the current session.</summary>
    [JsonIgnore]
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

    /// <summary>
    /// Shared id for all detections emitted in the same Observe pass (multi-var same tick).
    /// </summary>
    public string? GroupId { get; init; }
}

/// <summary>Optional global learn watchlist (Phase G); types ready for ConfigLoader.</summary>
public sealed class LearnWatchlistConfig
{
    [JsonPropertyName("exclude_names")]
    public List<string> ExcludeNames { get; set; } = new();

    [JsonPropertyName("default_watches")]
    public List<LearnWatchEntry> DefaultWatches { get; set; } = new();
}
