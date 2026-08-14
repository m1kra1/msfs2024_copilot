using CoPilotVoiceHost.Models;

namespace CoPilotVoiceHost.Core;

/// <summary>
/// Tick-based sequential checklist executor (no WPF, no threads).
/// Host poll calls <see cref="Tick"/>; voice/GUI call Start/Stop/RequestContinue.
/// </summary>
public sealed class ChecklistRunner
{
    private readonly Func<IReadOnlyList<CommandDefinition>> _getCatalog;
    private readonly Action<IReadOnlyList<ActionDefinition>> _executeActions;
    private readonly Action<string, int, string?, string?> _speak;
    private readonly object _lock = new();

    private ChecklistDefinition? _active;
    private int _itemIndex;
    private ChecklistRunState _state = ChecklistRunState.Idle;
    private string _statusMessage = "";
    private string _lastResult = "";
    private string _currentChallenge = "";
    private DateTime _delayUntilUtc = DateTime.MinValue;
    private bool _continueRequested;
    private bool _itemChallengeSpoken;
    private bool _itemActionsDone;

    public event Action<ChecklistProgress>? ProgressChanged;

    public ChecklistRunner(
        Func<IReadOnlyList<CommandDefinition>> getCatalog,
        Action<IReadOnlyList<ActionDefinition>> executeActions,
        Action<string, int, string?, string?> speak)
    {
        _getCatalog = getCatalog ?? throw new ArgumentNullException(nameof(getCatalog));
        _executeActions = executeActions ?? throw new ArgumentNullException(nameof(executeActions));
        _speak = speak ?? throw new ArgumentNullException(nameof(speak));
    }

    public bool IsActive
    {
        get
        {
            lock (_lock)
            {
                return _state is ChecklistRunState.Speaking
                    or ChecklistRunState.WaitingVerify
                    or ChecklistRunState.Delaying;
            }
        }
    }

    public ChecklistProgress GetProgress()
    {
        lock (_lock)
            return BuildProgressUnlocked();
    }

    /// <summary>Start (or replace) a checklist run. Speaks intro and begins item 0 on next ticks.</summary>
    public void Start(ChecklistDefinition checklist)
    {
        if (checklist is null) throw new ArgumentNullException(nameof(checklist));
        lock (_lock)
        {
            if (_state is ChecklistRunState.Speaking or ChecklistRunState.WaitingVerify or ChecklistRunState.Delaying)
            {
                _statusMessage = "Replaced active checklist";
                _lastResult = "replaced";
            }

            _active = CloneShallow(checklist);
            _itemIndex = 0;
            _continueRequested = false;
            _itemChallengeSpoken = false;
            _itemActionsDone = false;
            _delayUntilUtc = DateTime.MinValue;
            _currentChallenge = "";
            _lastResult = "";
            _state = ChecklistRunState.Speaking;
            _statusMessage = "Starting";
            RaiseProgressUnlocked();
        }

        var name = string.IsNullOrWhiteSpace(checklist.Name) ? checklist.Id : checklist.Name;
        _speak($"{name} checklist.", 0, checklist.Id, TtsResponseKind.Success);
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (_active is null && _state == ChecklistRunState.Idle)
                return;

            _state = ChecklistRunState.Cancelled;
            _statusMessage = "Cancelled";
            _lastResult = "cancelled";
            _continueRequested = false;
            RaiseProgressUnlocked();
            _active = null;
            _state = ChecklistRunState.Idle;
        }
    }

    public void RequestContinue()
    {
        lock (_lock)
        {
            if (_state == ChecklistRunState.WaitingVerify)
                _continueRequested = true;
        }
    }

    /// <summary>Advance state machine; safe to call every poll (~50 ms).</summary>
    public void Tick(DateTime utcNow, SimVarSnapshot snapshot)
    {
        ChecklistDefinition? active;
        int itemIndex;
        ChecklistRunState state;
        bool continueRequested;
        bool challengeSpoken;
        bool actionsDone;
        DateTime delayUntil;

        lock (_lock)
        {
            active = _active;
            itemIndex = _itemIndex;
            state = _state;
            continueRequested = _continueRequested;
            challengeSpoken = _itemChallengeSpoken;
            actionsDone = _itemActionsDone;
            delayUntil = _delayUntilUtc;
        }

        if (active is null
            || state is ChecklistRunState.Idle or ChecklistRunState.Completed
                or ChecklistRunState.Cancelled or ChecklistRunState.Failed)
            return;

        if (state == ChecklistRunState.Delaying)
        {
            if (utcNow < delayUntil)
                return;

            AdvanceToNextItem(utcNow);
            return;
        }

        if (itemIndex < 0 || itemIndex >= active.Items.Count)
        {
            CompleteSuccess();
            return;
        }

        var item = active.Items[itemIndex];
        var mode = (item.Mode ?? ChecklistItemMode.Verify).Trim().ToLowerInvariant();

        if (!challengeSpoken)
        {
            var challenge = item.Challenge?.Trim() ?? "";
            lock (_lock)
            {
                _currentChallenge = challenge;
                _itemChallengeSpoken = true;
                _state = ChecklistRunState.Speaking;
                _statusMessage = mode == ChecklistItemMode.Execute ? "Executing" : "Challenge";
                RaiseProgressUnlocked();
            }

            var delay = CapDelay(active.GlobalDelayMs);
            _speak(challenge, delay, active.Id, TtsResponseKind.Success);

            if (mode == ChecklistItemMode.Execute)
            {
                // Fall through same tick after speak scheduled (TTS may block briefly).
            }
            else
            {
                lock (_lock)
                {
                    _state = ChecklistRunState.WaitingVerify;
                    _statusMessage = "Waiting for verify";
                    RaiseProgressUnlocked();
                }

                return;
            }
        }

        if (mode == ChecklistItemMode.Execute)
        {
            if (!actionsDone)
            {
                var actions = ResolveExecuteActions(item);
                if (actions is null)
                {
                    Fail("Unable — no actions for execute item.");
                    return;
                }

                try
                {
                    _executeActions(actions);
                }
                catch (Exception ex)
                {
                    Fail($"Unable — action failed: {ex.Message}");
                    return;
                }

                var ok = string.IsNullOrWhiteSpace(item.ResponseOk)
                    ? HostConstants.ChecklistDefaultResponseOk
                    : item.ResponseOk!.Trim();
                _speak(ok, 0, active.Id, TtsResponseKind.Success);

                lock (_lock)
                {
                    _itemActionsDone = true;
                    _lastResult = "executed";
                }

                EnterDelay(utcNow, active, item);
            }

            return;
        }

        // verify
        if (state != ChecklistRunState.WaitingVerify && challengeSpoken)
        {
            lock (_lock)
            {
                if (_state == ChecklistRunState.Speaking)
                {
                    _state = ChecklistRunState.WaitingVerify;
                    _statusMessage = "Waiting for verify";
                    RaiseProgressUnlocked();
                }
            }
        }

        var conditions = ResolveVerifyConditions(item);
        if (conditions is null || conditions.Count == 0)
        {
            Fail("Unable — no expected state for verify item.");
            return;
        }

        bool pass;
        lock (_lock)
            continueRequested = _continueRequested;

        if (continueRequested)
        {
            _speak("Continuing.", 0, active.Id, TtsResponseKind.Success);
            lock (_lock)
            {
                _continueRequested = false;
                _lastResult = "continued";
            }

            EnterDelay(utcNow, active, item);
            return;
        }

        pass = EvaluateConditions(conditions, snapshot);
        if (!pass)
        {
            lock (_lock)
            {
                if (_state == ChecklistRunState.WaitingVerify)
                {
                    _statusMessage = "Waiting — set control or say Continue";
                    // Avoid spamming ProgressChanged every 50ms: only if message changed
                }
            }

            return;
        }

        var responseOk = string.IsNullOrWhiteSpace(item.ResponseOk)
            ? HostConstants.ChecklistDefaultResponseOk
            : item.ResponseOk!.Trim();
        _speak(responseOk, 0, active.Id, TtsResponseKind.Success);
        lock (_lock)
            _lastResult = "verified";
        EnterDelay(utcNow, active, item);
    }

    private void EnterDelay(DateTime utcNow, ChecklistDefinition active, ChecklistItem item)
    {
        var ms = CapDelay(item.DelayAfterMs) + CapDelay(active.GlobalDelayMs);
        // After success we already applied global_delay on challenge speak for execute;
        // delay_after is the inter-item pause. Use delay_after only if > 0, else a small global.
        ms = CapDelay(item.DelayAfterMs);
        if (ms <= 0)
            ms = Math.Min(100, CapDelay(active.GlobalDelayMs)); // brief yield between items

        lock (_lock)
        {
            _state = ChecklistRunState.Delaying;
            _delayUntilUtc = utcNow.AddMilliseconds(ms);
            _statusMessage = ms > 0 ? $"Delay {ms} ms" : "Next";
            RaiseProgressUnlocked();
        }

        if (ms <= 0)
            AdvanceToNextItem(utcNow);
    }

    private void AdvanceToNextItem(DateTime utcNow)
    {
        string? speakId = null;
        string? speakName = null;
        lock (_lock)
        {
            if (_active is null) return;
            _itemIndex++;
            _itemChallengeSpoken = false;
            _itemActionsDone = false;
            _continueRequested = false;
            _delayUntilUtc = DateTime.MinValue;

            if (_itemIndex >= _active.Items.Count)
            {
                _state = ChecklistRunState.Completed;
                _statusMessage = "Complete";
                _currentChallenge = "";
                _lastResult = "completed";
                RaiseProgressUnlocked();
                speakId = _active.Id;
                speakName = _active.Name;
                _active = null;
                _state = ChecklistRunState.Idle;
            }
            else
            {
                _state = ChecklistRunState.Speaking;
                _statusMessage = "Next item";
                _currentChallenge = _active.Items[_itemIndex].Challenge ?? "";
                RaiseProgressUnlocked();
            }
        }

        if (speakId is not null)
            _speak($"{speakName} checklist complete.", 0, speakId, TtsResponseKind.Success);
    }

    private void CompleteSuccess()
    {
        string? id = null;
        string? name = null;
        lock (_lock)
        {
            if (_active is null) return;
            id = _active.Id;
            name = _active.Name;
            _state = ChecklistRunState.Completed;
            _statusMessage = "Complete";
            _lastResult = "completed";
            RaiseProgressUnlocked();
            _active = null;
            _state = ChecklistRunState.Idle;
        }

        _speak($"{name} checklist complete.", 0, id, TtsResponseKind.Success);
    }

    private void Fail(string message)
    {
        string? id;
        lock (_lock)
        {
            id = _active?.Id;
            _state = ChecklistRunState.Failed;
            _statusMessage = message;
            _lastResult = "failed";
            RaiseProgressUnlocked();
            _active = null;
            _state = ChecklistRunState.Idle;
        }

        _speak(message, 0, id, TtsResponseKind.Reject);
    }

    private List<ActionDefinition>? ResolveExecuteActions(ChecklistItem item)
    {
        if (item.Actions is { Count: > 0 })
            return item.Actions.Select(CloneAction).ToList();

        if (item.Action is not null && !string.IsNullOrWhiteSpace(item.Action.Name))
            return new List<ActionDefinition> { CloneAction(item.Action) };

        if (!string.IsNullOrWhiteSpace(item.CommandId))
        {
            var cmd = FindCommand(item.CommandId!);
            if (cmd is null || cmd.Actions.Count == 0)
                return null;
            return cmd.Actions.Select(CloneAction).ToList();
        }

        return null;
    }

    private List<ConditionDefinition>? ResolveVerifyConditions(ChecklistItem item)
    {
        if (item.ExpectedList is { Count: > 0 })
            return item.ExpectedList.Select(CloneCondition).ToList();

        if (item.Expected is not null && !string.IsNullOrWhiteSpace(item.Expected.SimVar))
            return new List<ConditionDefinition> { CloneCondition(item.Expected) };

        if (!string.IsNullOrWhiteSpace(item.CommandId))
        {
            var cmd = FindCommand(item.CommandId!);
            if (cmd is null || cmd.Conditions.Count == 0)
                return null;
            return cmd.Conditions.Select(CloneCondition).ToList();
        }

        return null;
    }

    private CommandDefinition? FindCommand(string id)
    {
        var catalog = _getCatalog() ?? Array.Empty<CommandDefinition>();
        return catalog.FirstOrDefault(c =>
            c.Id.Equals(id.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static bool EvaluateConditions(IReadOnlyList<ConditionDefinition> conditions, SimVarSnapshot snapshot)
    {
        foreach (var c in conditions)
        {
            if (!snapshot.TryGet(c.SimVar, out var actual))
                return false;
            if (!ConditionEngine.Compare(actual, c.Op, c.Value))
                return false;
        }

        return true;
    }

    private static int CapDelay(int ms) =>
        Math.Clamp(ms, 0, HostConstants.ChecklistMaxDelayMs);

    private ChecklistProgress BuildProgressUnlocked() => new()
    {
        ChecklistId = _active?.Id ?? "",
        Name = _active?.Name ?? "",
        State = _state,
        ItemIndex = _itemIndex,
        ItemCount = _active?.Items.Count ?? 0,
        CurrentChallenge = _currentChallenge,
        StatusMessage = _statusMessage,
        LastResult = _lastResult
    };

    private void RaiseProgressUnlocked() =>
        ProgressChanged?.Invoke(BuildProgressUnlocked());

    private static ChecklistDefinition CloneShallow(ChecklistDefinition src) => new()
    {
        Id = src.Id,
        Name = src.Name,
        Phrases = src.Phrases?.ToList() ?? new List<string>(),
        GlobalDelayMs = src.GlobalDelayMs,
        AssignedProfiles = src.AssignedProfiles?.ToList() ?? new List<string>(),
        Items = src.Items?.ToList() ?? new List<ChecklistItem>()
    };

    private static ActionDefinition CloneAction(ActionDefinition a) => new()
    {
        Type = a.Type,
        Name = a.Name,
        Value = a.Value,
        Units = a.Units
    };

    private static ConditionDefinition CloneCondition(ConditionDefinition c) => new()
    {
        SimVar = c.SimVar,
        Op = c.Op,
        Value = c.Value,
        Units = c.Units
    };
}
