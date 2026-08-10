using System.Text;
using CoPilotVoiceHost.Models;

namespace CoPilotVoiceHost.Core;

/// <summary>
/// Learn Mode capture: baseline + snapshot diffs + suppress + bounded recent buffer.
/// Pure Core — only uses <see cref="SimVarSnapshot"/> and mapping index (no WPF / SimConnect client).
/// </summary>
public sealed class LearnCaptureService
{
    private readonly object _lock = new();
    private readonly List<LearnDetection> _recent = new();
    private readonly Dictionary<string, double> _baseline = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _suppressUntil = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _watchUnits = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _watchNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _debounceMs;
    private bool _baselineReady;

    /// <param name="debounceMs">
    /// Same-signal re-fire window; 0 disables debounce (useful for capacity tests).
    /// </param>
    public LearnCaptureService(int debounceMs = HostConstants.LearnDebounceMs)
    {
        _debounceMs = Math.Max(0, debounceMs);
    }

    public bool IsActive { get; private set; }
    public int WatchCount
    {
        get { lock (_lock) return _watchNames.Count; }
    }

    /// <summary>Newest first, bounded by <see cref="HostConstants.LearnRecentCapacity"/>.</summary>
    public IReadOnlyList<LearnDetection> Recent
    {
        get
        {
            lock (_lock)
                return _recent.ToList();
        }
    }

    public void Start(IReadOnlyList<LearnWatchEntry> watches)
    {
        lock (_lock)
        {
            IsActive = true;
            _baselineReady = false;
            _baseline.Clear();
            _suppressUntil.Clear();
            _watchNames.Clear();
            _watchUnits.Clear();
            _recent.Clear();

            if (watches is null)
                return;

            foreach (var w in watches)
            {
                if (w is null || string.IsNullOrWhiteSpace(w.Name))
                    continue;
                var name = w.Name.Trim();
                if (!_watchNames.Add(name))
                    continue;
                _watchUnits[name] = string.IsNullOrWhiteSpace(w.Units) ? "number" : w.Units.Trim();
            }
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            IsActive = false;
            _baselineReady = false;
            _baseline.Clear();
            _suppressUntil.Clear();
            _watchNames.Clear();
            _watchUnits.Clear();
        }
    }

    public void ClearRecent()
    {
        lock (_lock)
            _recent.Clear();
    }

    /// <summary>Ignore detections for these signal names until UTC now + duration.</summary>
    public void SuppressSignals(IEnumerable<string>? names, int durationMs = HostConstants.LearnSuppressMs)
    {
        if (names is null)
            return;

        var until = DateTimeOffset.UtcNow.AddMilliseconds(Math.Max(0, durationMs));
        lock (_lock)
        {
            foreach (var raw in names)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;
                foreach (var key in CommandMappingIndex.EnumerateIndexKeys(raw))
                    _suppressUntil[key] = until;
            }
        }
    }

    /// <summary>
    /// First complete pass stores baseline only (no detections). Later passes emit diffs.
    /// Multi-var changes in one call share a <see cref="LearnDetection.GroupId"/>.
    /// Same-signal re-fires within <see cref="HostConstants.LearnDebounceMs"/> update the latest row.
    /// Returns number of new detections added this call (debounce updates count as 0).
    /// </summary>
    public int Observe(SimVarSnapshot snapshot, CommandMappingIndex mappingIndex)
    {
        if (!IsActive || snapshot is null)
            return 0;

        mappingIndex ??= new CommandMappingIndex(new CommandCatalog());
        var now = DateTimeOffset.UtcNow;
        var added = 0;

        lock (_lock)
        {
            if (!IsActive || _watchNames.Count == 0)
                return 0;

            if (!_baselineReady)
            {
                foreach (var name in _watchNames)
                {
                    if (snapshot.TryGet(name, out var v))
                        _baseline[name] = v;
                }

                // Baseline is ready after first observe even if some watches missing —
                // missing values appear when they first arrive (old = null).
                _baselineReady = true;
                return 0;
            }

            var pending = new List<(string Name, double? Old, double New)>();
            foreach (var name in _watchNames)
            {
                if (!snapshot.TryGet(name, out var newVal))
                    continue;

                if (IsSuppressedLocked(name, now))
                {
                    // Keep baseline in sync so post-suppress does not re-fire the same delta.
                    _baseline[name] = newVal;
                    continue;
                }

                if (!_baseline.TryGetValue(name, out var oldVal))
                {
                    _baseline[name] = newVal;
                    pending.Add((name, null, newVal));
                    continue;
                }

                if (Math.Abs(newVal - oldVal) <= HostConstants.LearnDiffEpsilon)
                    continue;

                _baseline[name] = newVal;
                pending.Add((name, oldVal, newVal));
            }

            if (pending.Count == 0)
                return 0;

            var groupId = pending.Count > 1
                ? $"g{now.ToUnixTimeMilliseconds()}"
                : null;

            foreach (var (name, oldVal, newVal) in pending)
            {
                if (TryDebounceUpdateLocked(name, oldVal, newVal, mappingIndex, now, groupId))
                    continue;

                var det = BuildDetection(name, oldVal, newVal, mappingIndex, now, groupId);
                PushRecentLocked(det);
                added++;
            }
        }

        return added;
    }

    private bool IsSuppressedLocked(string name, DateTimeOffset now)
    {
        foreach (var key in CommandMappingIndex.EnumerateIndexKeys(name))
        {
            if (_suppressUntil.TryGetValue(key, out var until) && until > now)
                return true;
        }

        return false;
    }

    /// <summary>
    /// If the newest detection for this signal is within the debounce window, replace it in place.
    /// </summary>
    private bool TryDebounceUpdateLocked(
        string name,
        double? oldVal,
        double newVal,
        CommandMappingIndex mappingIndex,
        DateTimeOffset now,
        string? groupId)
    {
        for (var i = 0; i < _recent.Count; i++)
        {
            var existing = _recent[i];
            if (!existing.SignalName.Equals(name, StringComparison.OrdinalIgnoreCase))
                continue;

            if (_debounceMs <= 0)
                return false;

            var ageMs = (now - existing.Utc).TotalMilliseconds;
            if (ageMs > _debounceMs)
                return false;

            // Keep the original OldValue from the start of the debounce burst.
            var keepOld = existing.OldValue ?? oldVal;
            _recent[i] = BuildDetection(name, keepOld, newVal, mappingIndex, now, groupId ?? existing.GroupId);
            return true;
        }

        return false;
    }

    private LearnDetection BuildDetection(
        string name,
        double? oldVal,
        double newVal,
        CommandMappingIndex mappingIndex,
        DateTimeOffset utc,
        string? groupId)
    {
        var matches = mappingIndex.Lookup(name);
        var status = matches.Count switch
        {
            0 => LearnMappingStatus.Unmapped,
            1 => LearnMappingStatus.Mapped,
            _ => LearnMappingStatus.Ambiguous
        };

        _watchUnits.TryGetValue(name, out var units);
        units ??= "number";

        var kind = name.StartsWith("L:", StringComparison.OrdinalIgnoreCase)
            ? LearnSignalKind.LVar
            : LearnSignalKind.SimVar;

        return new LearnDetection
        {
            SignalName = name,
            Kind = kind,
            OldValue = oldVal,
            NewValue = newVal,
            Units = units,
            Utc = utc,
            MappingStatus = status,
            MatchedCommandIds = matches.Select(c => c.Id).ToList(),
            MatchedCommandLabels = matches.Select(CommandMappingIndex.FormatLabel).ToList(),
            SuggestedCommandId = SuggestCommandId(name, newVal),
            SuggestedActions = LearnActionHints.Suggest(name, newVal, units),
            GroupId = groupId
        };
    }

    private void PushRecentLocked(LearnDetection detection)
    {
        _recent.Insert(0, detection);
        while (_recent.Count > HostConstants.LearnRecentCapacity)
            _recent.RemoveAt(_recent.Count - 1);
    }

    /// <summary>Serialize recent detections to a JSON array string (export helper).</summary>
    public string ExportRecentJson()
    {
        lock (_lock)
        {
            var rows = _recent.Select(d => new
            {
                utc = d.Utc.ToString("o"),
                signal = d.SignalName,
                kind = d.Kind.ToString(),
                old_value = d.OldValue,
                new_value = d.NewValue,
                units = d.Units,
                mapping = d.MappingStatus.ToString(),
                matched_ids = d.MatchedCommandIds,
                suggested_id = d.SuggestedCommandId,
                group_id = d.GroupId,
                suggested_actions = d.SuggestedActions.Select(a => new
                {
                    type = a.Type,
                    name = a.Name,
                    value = a.Value,
                    units = a.Units
                })
            }).ToList();
            return System.Text.Json.JsonSerializer.Serialize(
                rows,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }
    }

    /// <summary>Suggested id: <c>learn_&lt;sanitized&gt;_&lt;value&gt;</c> with [a-z0-9_].</summary>
    public static string SuggestCommandId(string signalName, double value)
    {
        var sb = new StringBuilder("learn_");
        foreach (var ch in signalName.Trim().ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
                sb.Append(ch);
            else if (ch is ' ' or '-' or ':' or '/' or '.')
                sb.Append('_');
        }

        // Collapse repeated underscores
        var raw = sb.ToString().Trim('_');
        while (raw.Contains("__", StringComparison.Ordinal))
            raw = raw.Replace("__", "_", StringComparison.Ordinal);

        var valPart = FormatValueToken(value);
        return $"{raw}_{valPart}";
    }

    private static string FormatValueToken(double value)
    {
        if (Math.Abs(value - Math.Round(value)) < HostConstants.LearnDiffEpsilon)
            return ((long)Math.Round(value)).ToString(System.Globalization.CultureInfo.InvariantCulture);

        return value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)
            .Replace('.', '_')
            .Replace('-', 'm');
    }
}
