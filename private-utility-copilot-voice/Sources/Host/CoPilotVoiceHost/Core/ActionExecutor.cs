using CoPilotVoiceHost.Diagnostics;
using CoPilotVoiceHost.Models;
using CoPilotVoiceHost.SimConnect;

namespace CoPilotVoiceHost.Core;

public sealed class ActionExecutor
{
    private readonly ISimConnectClient _client;
    private readonly ILogSink? _log;
    private readonly List<ExecutedAction> _history = new();
    private readonly List<string> _errors = new();

    public ActionExecutor(ISimConnectClient client, ILogSink? log = null)
    {
        _client = client;
        _log = log;
    }

    public IReadOnlyList<ExecutedAction> History => _history;
    public IReadOnlyList<string> Errors => _errors;
    public bool LastExecuteHadErrors => _errors.Count > 0;

    public IReadOnlyList<ActionDefinition> Execute(IEnumerable<ActionDefinition> actions)
    {
        _errors.Clear();
        var done = new List<ActionDefinition>();
        foreach (var action in actions)
        {
            var type = action.Type.Trim().ToLowerInvariant();
            try
            {
                switch (type)
                {
                    case HostConstants.ActionTypeEvent:
                        if (!_client.IsConnected)
                        {
                            _errors.Add($"Not connected — cannot send event {action.Name}");
                            Write($"[Action] FAIL event:{action.Name} — SimConnect not connected");
                            break;
                        }

                        _client.TransmitEvent(action.Name, (uint)(action.Value ?? 0));
                        AddHistory(new ExecutedAction(HostConstants.ActionTypeEvent, action.Name, action.Value));
                        done.Add(action);
                        if (!_client.IsLive)
                            Write($"[Action] event:{action.Name} recorded but NOT live");
                        break;
                    case HostConstants.ActionTypeSimVar:
                    case HostConstants.ActionTypeSetSimVar:
                        _client.SetSimVar(action.Name, action.Value ?? 0, action.Units ?? "number");
                        AddHistory(new ExecutedAction(HostConstants.ActionTypeSimVar, action.Name, action.Value));
                        done.Add(action);
                        break;
                    default:
                        _errors.Add($"Unknown action type '{action.Type}'");
                        break;
                }
            }
            catch (Exception ex)
            {
                _errors.Add($"{action.Type}:{action.Name} → {ex.Message}");
                Write($"[Action] FAIL {action.Type}:{action.Name} — {ex.Message}");
            }
        }

        return done;
    }

    public void ClearHistory() => _history.Clear();

    private void AddHistory(ExecutedAction item)
    {
        _history.Add(item);
        var overflow = _history.Count - HostConstants.ActionHistoryMax;
        if (overflow > 0)
            _history.RemoveRange(0, overflow);
    }

    private void Write(string message)
    {
        if (_log is not null)
            _log.Info(message);
        else
            Console.WriteLine(message);
    }
}

public readonly record struct ExecutedAction(string Type, string Name, double? Value);
