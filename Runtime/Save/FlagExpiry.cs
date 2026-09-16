using System;
using System.Collections.Generic;
using PixelCore.Runtime.Core;

namespace PixelCore.Runtime.Save;

public class FlagExpiry
{
    private readonly Dictionary<string, (FlagLifetime Scope, int Day, string Phase)> _pending = new();

    private Blackboard? _blackboard;
    private Func<(int Day, string Phase)>? _now;

    private bool _expiring;

    public int PendingCount => _pending.Count;

    public void Bind(Blackboard blackboard, Func<(int Day, string Phase)> now)
    {
        if (_blackboard != null) _blackboard.OnChanged -= OnFlagChanged;

        _blackboard = blackboard;
        _now = now;
        blackboard.OnChanged += OnFlagChanged;
    }

    public void Unbind()
    {
        if (_blackboard != null) _blackboard.OnChanged -= OnFlagChanged;
        _blackboard = null;
        _now = null;
        _pending.Clear();
    }

    private void OnFlagChanged(string key)
    {
        if (_expiring || _blackboard == null || _now == null) return;

        if (key == "*") return;

        if (!_blackboard.Has(key))
        {
            _pending.Remove(key);
            return;
        }

        var lifetime = FlagRegistry.LifetimeOf(key);
        if (lifetime == FlagLifetime.Permanent)
        {
            _pending.Remove(key);
            return;
        }

        var (day, phase) = _now();
        _pending[key] = (lifetime, day, phase);
    }

    public List<string> ExpireFor(int day, string phase)
    {
        var expired = new List<string>();
        if (_blackboard == null) return expired;

        foreach (var (key, entry) in _pending)
        {
            bool stale = entry.Scope switch
            {
                FlagLifetime.Day => entry.Day != day,
                FlagLifetime.Phase => entry.Day != day || entry.Phase != phase,
                _ => false,
            };
            if (stale) expired.Add(key);
        }

        if (expired.Count == 0) return expired;

        _expiring = true;
        try
        {
            foreach (var key in expired)
            {
                _blackboard.Remove(key);
                _pending.Remove(key);
            }
        }
        finally { _expiring = false; }

        Console.WriteLine($"[Save] {expired.Count} flag(s) expired (day {day}/{phase}): {string.Join(", ", expired)}");
        return expired;
    }

    public List<ExpiringFlagSave> Capture()
    {
        var list = new List<ExpiringFlagSave>(_pending.Count);
        foreach (var (key, entry) in _pending)
        {
            list.Add(new ExpiringFlagSave
            {
                Key = key,
                Scope = entry.Scope.ToString(),
                Day = entry.Day,
                Phase = entry.Phase,
            });
        }
        list.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
        return list;
    }

    public void LoadFrom(IEnumerable<ExpiringFlagSave> entries, List<string>? problems = null)
    {
        _pending.Clear();

        foreach (var e in entries)
        {
            if (string.IsNullOrEmpty(e.Key)) continue;

            if (!Enum.TryParse<FlagLifetime>(e.Scope, out var scope))
            {
                string note = $"expiry queue: unknown lifetime '{e.Scope}' (key {e.Key}) - discarding this entry";
                problems?.Add(note);
                Console.WriteLine($"[Save] ⚠ {note}");
                continue;
            }
            if (scope == FlagLifetime.Permanent) continue;

            _pending[e.Key] = (scope, e.Day, e.Phase);
        }
    }
}
