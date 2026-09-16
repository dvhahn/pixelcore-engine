using System;
using System.Collections.Generic;

namespace PixelCore.Runtime.Core;

public class Blackboard
{
    private readonly Dictionary<string, object> _values = new();

    public event Action<string>? OnChanged;

    public bool GetBool(string key, bool fallback = false)
        => _values.TryGetValue(key, out var v) && v is bool b ? b : fallback;

    public int GetInt(string key, int fallback = 0)
        => _values.TryGetValue(key, out var v) && v is int i ? i : fallback;

    public float GetFloat(string key, float fallback = 0f)
        => _values.TryGetValue(key, out var v) && v is float f ? f : fallback;

    public string GetString(string key, string fallback = "")
        => _values.TryGetValue(key, out var v) && v is string s ? s : fallback;

    public void SetBool(string key, bool value) => Set(key, value);
    public void SetInt(string key, int value) => Set(key, value);
    public void SetFloat(string key, float value) => Set(key, value);
    public void SetString(string key, string value) => Set(key, value);

    public void AddInt(string key, int delta) => SetInt(key, GetInt(key) + delta);

    public void Set(string key, object value)
    {
        _values[key] = value;
        OnChanged?.Invoke(key);
    }

    public bool Has(string key) => _values.ContainsKey(key);

    public void Remove(string key)
    {
        if (_values.Remove(key)) OnChanged?.Invoke(key);
    }

    public void Clear() => _values.Clear();

    public IReadOnlyDictionary<string, object> All => _values;

    public void LoadFrom(IDictionary<string, object> data)
    {
        _values.Clear();
        foreach (var kv in data) _values[kv.Key] = kv.Value;
        OnChanged?.Invoke("*");
    }
}
