using System.Globalization;
using System.Text;

namespace Forge.Shared;

/// <summary>
/// BCL calls that differ on .NET Framework. net8.0 keeps the framework implementation.
/// </summary>
internal static class BclCompat
{
    public static string[] SplitTrimmed(string text, char separator)
    {
#if NET5_0_OR_GREATER
        return text.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
#else
        var parts = text.Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries);
        var trimmed = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            var value = part.Trim();
            if (value.Length > 0)
            {
                trimmed.Add(value);
            }
        }

        return trimmed.ToArray();
#endif
    }

    public static int Clamp(int value, int min, int max)
    {
#if NET
        return Math.Clamp(value, min, max);
#else
        if (min > max)
        {
            throw new ArgumentException($"'{min}' cannot be greater than {max}.");
        }

        if (value < min)
        {
            return min;
        }

        if (value > max)
        {
            return max;
        }

        return value;
#endif
    }

    public static double Clamp(double value, double min, double max)
    {
#if NET
        return Math.Clamp(value, min, max);
#else
        if (min > max)
        {
            throw new ArgumentException($"'{min}' cannot be greater than {max}.");
        }

        if (value < min)
        {
            return min;
        }

        if (value > max)
        {
            return max;
        }

        return value;
#endif
    }

    public static void ThrowIfNullOrWhiteSpace(string? value, string paramName)
    {
#if NET7_0_OR_GREATER
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
#else
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("The value cannot be an empty string or composed entirely of whitespace.", paramName);
        }
#endif
    }

    public static StringBuilder AppendInvariant(StringBuilder builder, FormattableString value)
    {
        return builder.Append(value.ToString(CultureInfo.InvariantCulture));
    }
}

#if NETFRAMEWORK
internal static class KeyValuePairExtensions
{
    public static void Deconstruct<TKey, TValue>(this KeyValuePair<TKey, TValue> pair, out TKey key, out TValue value)
    {
        key = pair.Key;
        value = pair.Value;
    }
}

public static class FrameworkStringExtensions
{
    public static bool Contains(this string source, string value, StringComparison comparison)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        return source.IndexOf(value, comparison) >= 0;
    }

    public static bool Contains(this string source, char value)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        return source.IndexOf(value) >= 0;
    }

    public static bool StartsWith(this string source, char value)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        return source.Length > 0 && source[0] == value;
    }
}
#endif

#if NETFRAMEWORK && !NET48_OR_GREATER
public static class FrameworkLinqExtensions
{
    public static HashSet<T> ToHashSet<T>(this IEnumerable<T> source)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        return new HashSet<T>(source);
    }

    public static HashSet<T> ToHashSet<T>(this IEnumerable<T> source, IEqualityComparer<T> comparer)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        return new HashSet<T>(source, comparer);
    }
}
#endif
