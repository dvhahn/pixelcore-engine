using System;

namespace PixelCore.Runtime.Text;

public readonly struct LocalizedString : IEquatable<LocalizedString>
{
    public string Key { get; }

    public LocalizedString(string key) => Key = key ?? "";

    public bool IsEmpty => string.IsNullOrEmpty(Key);

    public string Value => IsEmpty ? "" : Loc.T(Key);

    public string Format(params (string name, object? value)[] args) =>
        IsEmpty ? "" : Loc.T(Key, args);

    public bool Exists => !IsEmpty && Loc.Has(Key);

    public static implicit operator LocalizedString(string key) => new(key);
    public static implicit operator string(LocalizedString s) => s.Value;

    public override string ToString() => Value;

    public bool Equals(LocalizedString other) => string.Equals(Key, other.Key, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is LocalizedString o && Equals(o);
    public override int GetHashCode() => Key?.GetHashCode(StringComparison.Ordinal) ?? 0;
    public static bool operator ==(LocalizedString a, LocalizedString b) => a.Equals(b);
    public static bool operator !=(LocalizedString a, LocalizedString b) => !a.Equals(b);
}
