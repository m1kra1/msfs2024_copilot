using CoPilotVoiceHost.Models;

namespace CoPilotVoiceHost.Core;

public sealed class ConditionEngine
{
    /// <summary>
    /// Evaluates command conditions against a SimVar snapshot.
    /// When requirePositiveClimbForGearUp is true and the command is gear_up (or flagged),
    /// VERTICAL SPEED &gt; 100 fpm is enforced even if conditions list is altered.
    /// </summary>
    public ConditionEvaluationResult Evaluate(
        CommandDefinition command,
        SimVarSnapshot snapshot,
        BehaviorSettings behavior)
    {
        var conditions = command.Conditions.ToList();

        if (behavior.RequirePositiveClimbForGearUp
            && (command.RequirePositiveClimbFlag
                || command.Id.Equals("gear_up", StringComparison.OrdinalIgnoreCase)))
        {
            var hasVs = conditions.Any(c =>
                c.SimVar.Equals("VERTICAL SPEED", StringComparison.OrdinalIgnoreCase));
            if (!hasVs)
            {
                conditions.Insert(0, new ConditionDefinition
                {
                    SimVar = "VERTICAL SPEED",
                    Op = ">",
                    Value = 100,
                    Units = "feet per minute"
                });
            }
        }
        else if (!behavior.RequirePositiveClimbForGearUp
                 && command.RequirePositiveClimbFlag)
        {
            // Setting disabled: drop VS gate from gear_up evaluation
            conditions = conditions
                .Where(c => !c.SimVar.Equals("VERTICAL SPEED", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        foreach (var condition in conditions)
        {
            if (!snapshot.TryGet(condition.SimVar, out var actual))
            {
                return new ConditionEvaluationResult(
                    false,
                    $"Missing SimVar '{condition.SimVar}'");
            }

            if (!Compare(actual, condition.Op, condition.Value))
            {
                return new ConditionEvaluationResult(
                    false,
                    $"{condition.SimVar}={actual} does not satisfy {condition.Op} {condition.Value}");
            }
        }

        return new ConditionEvaluationResult(true, null);
    }

    public static bool Compare(double actual, string op, double expected)
    {
        const double eps = 1e-6;
        return op.Trim() switch
        {
            "==" or "=" => Math.Abs(actual - expected) <= eps,
            "!=" or "<>" => Math.Abs(actual - expected) > eps,
            ">" => actual > expected,
            ">=" => actual >= expected - eps,
            "<" => actual < expected,
            "<=" => actual <= expected + eps,
            _ => throw new ArgumentException($"Unknown condition operator '{op}'")
        };
    }
}

public readonly record struct ConditionEvaluationResult(bool Allowed, string? DenyReason);
