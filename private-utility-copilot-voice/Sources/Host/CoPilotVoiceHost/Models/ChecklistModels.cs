using System.Text.Json.Serialization;

namespace CoPilotVoiceHost.Models;

/// <summary>Known checklist item modes (JSON <c>mode</c>).</summary>
public static class ChecklistItemMode
{
    public const string Execute = "execute";
    public const string Verify = "verify";
}

/// <summary>One checklist document loaded from <c>config/checklists/{id}.json</c>.</summary>
public sealed class ChecklistDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("phrases")]
    public List<string> Phrases { get; set; } = new();

    [JsonPropertyName("global_delay_ms")]
    public int GlobalDelayMs { get; set; } = HostConstants.ChecklistDefaultGlobalDelayMs;

    /// <summary>Empty = available for all aircraft profiles.</summary>
    [JsonPropertyName("assigned_profiles")]
    public List<string> AssignedProfiles { get; set; } = new();

    [JsonPropertyName("items")]
    public List<ChecklistItem> Items { get; set; } = new();
}

/// <summary>Single sequential checklist step.</summary>
public sealed class ChecklistItem
{
    /// <summary><see cref="ChecklistItemMode.Execute"/> or <see cref="ChecklistItemMode.Verify"/>.</summary>
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = ChecklistItemMode.Verify;

    [JsonPropertyName("challenge")]
    public string Challenge { get; set; } = string.Empty;

    /// <summary>Optional catalog command id (actions for execute, conditions for verify fallback).</summary>
    [JsonPropertyName("command_id")]
    public string? CommandId { get; set; }

    /// <summary>Single free action (execute); preferred over empty <see cref="Actions"/> when set.</summary>
    [JsonPropertyName("action")]
    public ActionDefinition? Action { get; set; }

    [JsonPropertyName("actions")]
    public List<ActionDefinition> Actions { get; set; } = new();

    /// <summary>Single expected condition (verify).</summary>
    [JsonPropertyName("expected")]
    public ConditionDefinition? Expected { get; set; }

    [JsonPropertyName("expected_list")]
    public List<ConditionDefinition> ExpectedList { get; set; } = new();

    [JsonPropertyName("response_ok")]
    public string? ResponseOk { get; set; }

    [JsonPropertyName("delay_after_ms")]
    public int DelayAfterMs { get; set; }
}

public enum ChecklistRunState
{
    Idle = 0,
    Speaking = 1,
    WaitingVerify = 2,
    Delaying = 3,
    Completed = 4,
    Cancelled = 5,
    Failed = 6
}

/// <summary>Live progress snapshot for GUI / diagnostics.</summary>
public sealed class ChecklistProgress
{
    public string ChecklistId { get; init; } = "";
    public string Name { get; init; } = "";
    public ChecklistRunState State { get; init; }
    public int ItemIndex { get; init; }
    public int ItemCount { get; init; }
    public string CurrentChallenge { get; init; } = "";
    public string StatusMessage { get; init; } = "";
    public string LastResult { get; init; } = "";
    public bool IsActive =>
        State is ChecklistRunState.Speaking
            or ChecklistRunState.WaitingVerify
            or ChecklistRunState.Delaying;
}
