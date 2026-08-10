using CoPilotVoiceHost.Models;
using CoPilotVoiceHost.SimConnect;

namespace CoPilotVoiceHost.Core;

/// <summary>
/// Builds the Learn Mode watch list (union of discrete status vars, catalog signals, manual watches).
/// Pure Core — no WPF / live Sim I/O.
/// </summary>
public static class LearnWatchBuilder
{
    /// <summary>Continuous telemetry vars excluded from default Learn watches (too noisy).</summary>
    public static readonly HashSet<string> DefaultContinuousExcludes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "AIRSPEED INDICATED",
            "PLANE ALTITUDE",
            "VERTICAL SPEED",
            "GROUND VELOCITY",
            "PLANE HEADING DEGREES MAGNETIC"
        };

    /// <summary>
    /// Union of discrete status vars, catalog set_simvar/condition names, optional profile/global
    /// watches (Phase G), and session manual watches. Deduped; capped at <see cref="HostConstants.LearnMaxWatches"/>.
    /// </summary>
    public static IReadOnlyList<LearnWatchEntry> Build(
        CommandCatalog catalog,
        IReadOnlyList<LearnWatchEntry>? manualWatches = null,
        IReadOnlyList<LearnWatchEntry>? profileWatches = null,
        LearnWatchlistConfig? globalWatchlist = null)
    {
        catalog ??= new CommandCatalog();
        var excludes = new HashSet<string>(DefaultContinuousExcludes, StringComparer.OrdinalIgnoreCase);
        if (globalWatchlist?.ExcludeNames is { Count: > 0 })
        {
            foreach (var ex in globalWatchlist.ExcludeNames)
            {
                if (!string.IsNullOrWhiteSpace(ex))
                    excludes.Add(ex.Trim());
            }
        }

        var byName = new Dictionary<string, LearnWatchEntry>(StringComparer.OrdinalIgnoreCase);

        void TryAdd(string? name, string? units, bool isManual)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;
            var n = name.Trim();
            if (excludes.Contains(n))
                return;
            if (byName.ContainsKey(n))
                return;

            byName[n] = new LearnWatchEntry
            {
                Name = n,
                Units = string.IsNullOrWhiteSpace(units) ? "number" : units.Trim(),
                IsManual = isManual
            };
        }

        // 1) Discrete status SimVars (minus continuous excludes)
        foreach (var (name, units) in StatusSimVars.Definitions)
            TryAdd(name, units, isManual: false);

        // 2) Catalog set_simvar / simvar actions
        foreach (var cmd in catalog.Commands)
        {
            if (cmd is null) continue;
            foreach (var action in cmd.Actions)
            {
                if (action is null) continue;
                var type = (action.Type ?? "").Trim().ToLowerInvariant();
                if (type is HostConstants.ActionTypeSetSimVar or HostConstants.ActionTypeSimVar)
                    TryAdd(action.Name, action.Units ?? "number", isManual: false);
            }

            // 3) Condition simvars
            foreach (var cond in cmd.Conditions)
            {
                if (cond is null) continue;
                TryAdd(cond.SimVar, cond.Units ?? "number", isManual: false);
            }
        }

        // 4) Profile learn_watch (Phase G)
        if (profileWatches is not null)
        {
            foreach (var w in profileWatches)
                TryAdd(w?.Name, w?.Units, isManual: false);
        }

        // 5) Global default watches (Phase G)
        if (globalWatchlist?.DefaultWatches is { Count: > 0 })
        {
            foreach (var w in globalWatchlist.DefaultWatches)
                TryAdd(w?.Name, w?.Units, isManual: false);
        }

        // 6) Session manual watches (last so they can re-add excluded names intentionally)
        if (manualWatches is not null)
        {
            foreach (var w in manualWatches)
            {
                if (w is null || string.IsNullOrWhiteSpace(w.Name))
                    continue;
                var n = w.Name.Trim();
                // Manual can override exclude (user explicitly asked to watch).
                if (byName.ContainsKey(n))
                    continue;
                byName[n] = new LearnWatchEntry
                {
                    Name = n,
                    Units = string.IsNullOrWhiteSpace(w.Units) ? "number" : w.Units.Trim(),
                    IsManual = true
                };
            }
        }

        var list = byName.Values.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
        if (list.Count > HostConstants.LearnMaxWatches)
        {
            Console.WriteLine(
                $"[Learn] Watch list truncated from {list.Count} to {HostConstants.LearnMaxWatches}");
            list = list.Take(HostConstants.LearnMaxWatches).ToList();
        }

        return list;
    }
}
