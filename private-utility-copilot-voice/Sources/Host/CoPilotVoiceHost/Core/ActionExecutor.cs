using CoPilotVoiceHost.Models;
using CoPilotVoiceHost.SimConnect;

namespace CoPilotVoiceHost.Core;

public sealed class ActionExecutor
{
    private readonly ISimConnectClient _client;
    private readonly List<ExecutedAction> _history = new();
    private readonly List<string> _errors = new();

    public ActionExecutor(ISimConnectClient client)
    {
        _client = client;
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
                    case "event":
                        if (!_client.IsConnected)
                        {
                            _errors.Add($"Not connected — cannot send event {action.Name}");
                            Console.WriteLine($"[Action] FAIL event:{action.Name} — SimConnect not connected");
                            break;
                        }

                        _client.TransmitEvent(action.Name, (uint)(action.Value ?? 0));
                        _history.Add(new ExecutedAction("event", action.Name, action.Value));
                        done.Add(action);
                        if (!_client.IsLive)
                            Console.WriteLine($"[Action] event:{action.Name} recorded but NOT live");
                        break;
                    case "simvar":
                    case "set_simvar":
                        _client.SetSimVar(action.Name, action.Value ?? 0, action.Units ?? "number");
                        _history.Add(new ExecutedAction("simvar", action.Name, action.Value));
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
                Console.WriteLine($"[Action] FAIL {action.Type}:{action.Name} — {ex.Message}");
            }
        }

        return done;
    }

    public void ClearHistory() => _history.Clear();
}

public readonly record struct ExecutedAction(string Type, string Name, double? Value);
