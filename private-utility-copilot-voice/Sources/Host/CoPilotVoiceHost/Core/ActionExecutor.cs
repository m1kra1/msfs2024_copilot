using CoPilotVoiceHost.Models;
using CoPilotVoiceHost.SimConnect;

namespace CoPilotVoiceHost.Core;

public sealed class ActionExecutor
{
    private readonly ISimConnectClient _client;
    private readonly List<ExecutedAction> _history = new();

    public ActionExecutor(ISimConnectClient client)
    {
        _client = client;
    }

    public IReadOnlyList<ExecutedAction> History => _history;

    public IReadOnlyList<ActionDefinition> Execute(IEnumerable<ActionDefinition> actions)
    {
        var done = new List<ActionDefinition>();
        foreach (var action in actions)
        {
            var type = action.Type.Trim().ToLowerInvariant();
            switch (type)
            {
                case "event":
                    _client.TransmitEvent(action.Name, (uint)(action.Value ?? 0));
                    _history.Add(new ExecutedAction("event", action.Name, action.Value));
                    done.Add(action);
                    break;
                case "simvar":
                case "set_simvar":
                    _client.SetSimVar(action.Name, action.Value ?? 0, action.Units ?? "number");
                    _history.Add(new ExecutedAction("simvar", action.Name, action.Value));
                    done.Add(action);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown action type '{action.Type}'");
            }
        }

        return done;
    }

    public void ClearHistory() => _history.Clear();
}

public readonly record struct ExecutedAction(string Type, string Name, double? Value);
