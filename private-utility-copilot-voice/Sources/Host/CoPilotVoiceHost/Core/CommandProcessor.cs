using CoPilotVoiceHost.Models;

namespace CoPilotVoiceHost.Core;

/// <summary>
/// Orchestrates phrase match → condition check → TTS response → action execution.
/// Order is binding: Condition-Check → TTS-Response (with callout delay when confirming) → Event.
/// </summary>
public sealed class CommandProcessor
{
    private readonly PhraseMatcher _matcher;
    private readonly ConditionEngine _conditions;
    private readonly ActionExecutor _executor;
    private readonly BehaviorSettings _behavior;
    private readonly IReadOnlyList<CommandDefinition> _catalogCommands;

    public CommandProcessor(
        PhraseMatcher matcher,
        ConditionEngine conditions,
        ActionExecutor executor,
        BehaviorSettings behavior,
        IReadOnlyList<CommandDefinition>? catalogCommands = null)
    {
        _matcher = matcher;
        _conditions = conditions;
        _executor = executor;
        _behavior = behavior;
        _catalogCommands = catalogCommands ?? Array.Empty<CommandDefinition>();
    }

    /// <param name="speakWithDelay">
    /// Invoked before actions on allow, and on deny for reject response.
    /// Signature: (text, delayMs). When null, TTS is skipped but actions still run after the speak slot.
    /// </param>
    public CommandResult? Process(
        string recognizedText,
        SimVarSnapshot snapshot,
        string? wakeWord = null,
        Action<string, int>? speakWithDelay = null)
    {
        var (command, matchedPhrase, _) = _matcher.Match(recognizedText, wakeWord);
        if (command is null)
            return null;

        var eval = _conditions.Evaluate(command, snapshot, _behavior);
        if (!eval.Allowed)
        {
            var reject = string.IsNullOrWhiteSpace(command.RejectResponse)
                ? $"Unable. {eval.DenyReason}"
                : command.RejectResponse!;

            // Speak reject before any action (none on deny)
            SpeakBeforeActions(speakWithDelay, reject);

            return new CommandResult
            {
                CommandId = command.Id,
                MatchedPhrase = matchedPhrase,
                Allowed = false,
                SpokenResponse = reject,
                ActionsExecuted = Array.Empty<ActionDefinition>(),
                DenyReason = eval.DenyReason
            };
        }

        // Dynamic response for list_commands from the live catalog; otherwise static JSON response.
        string spoken;
        IReadOnlyList<string> detailLines = Array.Empty<string>();
        if (CommandListBuilder.IsListCommand(command.Id))
        {
            var source = _catalogCommands.Count > 0 ? _catalogCommands : new[] { command };
            spoken = CommandListBuilder.BuildSpokenSummary(source);
            detailLines = CommandListBuilder.BuildFullLogLines(source);
        }
        else
        {
            spoken = command.Response;
        }

        // Allowed: TTS first (confirm + delay), then transmit events
        SpeakBeforeActions(speakWithDelay, spoken);

        var executed = _executor.Execute(command.Actions);

        return new CommandResult
        {
            CommandId = command.Id,
            MatchedPhrase = matchedPhrase,
            Allowed = true,
            SpokenResponse = spoken,
            ActionsExecuted = executed,
            DenyReason = null,
            DetailLogLines = detailLines
        };
    }

    private void SpeakBeforeActions(Action<string, int>? speakWithDelay, string text)
    {
        if (speakWithDelay is null)
            return;

        // confirm_before_action: delay then speak before actions; otherwise speak with zero delay still before actions
        var delayMs = _behavior.ConfirmBeforeAction
            ? Math.Max(0, _behavior.CalloutDelayMs)
            : 0;

        speakWithDelay(text, delayMs);
    }
}
