namespace Forge.Shared;

/// <summary>
/// smoke-r2 D10: a scoped override of one system variable. The typical use is
/// PROXYNOTICE=0 around a foreign-database load (doc_import, xref reload, dgn_import,
/// template opens), which otherwise raises AutoCAD's modal "Proxy Information" dialog
/// and wedges every later pipe request until a human clicks OK.
/// The original value is restored by <see cref="Dispose"/> — also when the guarded
/// block throws — and the scope is a no-op when the value already equals the override.
/// </summary>
public sealed class ScopedSysvar : IDisposable
{
    private readonly Action<string, object?>? _set;
    private readonly string _name;

    private ScopedSysvar(string name, object? original, bool applied, Action<string, object?>? set)
    {
        _name = name;
        Original = original;
        Applied = applied;
        _set = set;
        // Nothing applied means the current value already equals the override: from the
        // caller's perspective the variable is "restored" from the start.
        Restored = !applied;
    }

    /// <summary>True when the override was actually written (the value differed).</summary>
    public bool Applied { get; }

    /// <summary>The value read before the override (null when <see cref="Applied"/> is false).</summary>
    public object? Original { get; }

    /// <summary>True once the original value is back in place; false only when a restore write threw.</summary>
    public bool Restored { get; private set; }

    /// <summary>True after <see cref="Dispose"/> ran.</summary>
    public bool IsDisposed { get; private set; }

    /// <summary>
    /// Read <paramref name="name"/> through <paramref name="get"/>; when the value differs
    /// from <paramref name="value"/> (numeric-insensitive comparison, so AutoCAD's short/int
    /// sysvar types all compare equal to 0), write the override through <paramref name="set"/>.
    /// Exceptions from the delegates propagate to the caller — a failed read/set means the
    /// caller decides whether to proceed (and warn), not this type.
    /// </summary>
    public static ScopedSysvar Set(string name, object? value, Func<string, object?> get, Action<string, object?> set)
    {
        var original = get(name);
        if (ValuesEqual(original, value))
        {
            return new ScopedSysvar(name, original, applied: false, set: null);
        }

        set(name, value);
        return new ScopedSysvar(name, original, applied: true, set);
    }

    /// <summary>Restore the read-back value exactly once; a restore failure is reported, never thrown.</summary>
    public void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        IsDisposed = true;
        if (!Applied || _set is null)
        {
            Restored = true;
            return;
        }

        try
        {
            _set(_name, Original);
            Restored = true;
        }
        catch
        {
            Restored = false;
        }
    }

    /// <summary>"0" equals 0 and (short)0: AutoCAD sysvar values arrive as various numeric types.</summary>
    private static bool ValuesEqual(object? a, object? b)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }

        try
        {
            return Math.Abs(
                Convert.ToDouble(a, System.Globalization.CultureInfo.InvariantCulture) -
                Convert.ToDouble(b, System.Globalization.CultureInfo.InvariantCulture)) < 1e-9;
        }
        catch
        {
            return string.Equals(a.ToString(), b.ToString(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
