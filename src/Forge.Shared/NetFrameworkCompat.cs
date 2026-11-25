#if NETFRAMEWORK
namespace System.Runtime.CompilerServices
{
    /// <summary>Compiler shim required for init-only properties/records on .NET Framework.</summary>
    public static class IsExternalInit
    {
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public sealed class RequiredMemberAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
    public sealed class CompilerFeatureRequiredAttribute : Attribute
    {
        public CompilerFeatureRequiredAttribute(string featureName)
        {
            FeatureName = featureName;
        }

        public string FeatureName { get; }
        public bool IsOptional { get; init; }
    }
}

namespace System.Diagnostics.CodeAnalysis
{
    [AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
    public sealed class SetsRequiredMembersAttribute : Attribute
    {
    }
}

namespace System.Collections.Generic
{
    /// <summary>Minimal net48-compatible equivalent of the .NET 5 read-only set interface.</summary>
    public interface IReadOnlySet<T> : IReadOnlyCollection<T>
    {
        bool Contains(T item);
        bool IsProperSubsetOf(IEnumerable<T> other);
        bool IsProperSupersetOf(IEnumerable<T> other);
        bool IsSubsetOf(IEnumerable<T> other);
        bool IsSupersetOf(IEnumerable<T> other);
        bool Overlaps(IEnumerable<T> other);
        bool SetEquals(IEnumerable<T> other);
    }
}
#endif


#if NETFRAMEWORK
namespace System
{
    public readonly struct Index : IEquatable<Index>
    {
        private readonly int _value;
        public Index(int value, bool fromEnd = false)
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
            _value = fromEnd ? ~value : value;
        }
        private Index(int value) { _value = value; }
        public static Index Start => new Index(0);
        public static Index End => new Index(0, true);
        public int Value => _value < 0 ? ~_value : _value;
        public bool IsFromEnd => _value < 0;
        public int GetOffset(int length)
        {
            var offset = IsFromEnd ? length - Value : Value;
            if ((uint)offset > (uint)length) throw new ArgumentOutOfRangeException(nameof(length));
            return offset;
        }
        public bool Equals(Index other) => _value == other._value;
        public override bool Equals(object? value) => value is Index other && Equals(other);
        public override int GetHashCode() => _value;
        public static implicit operator Index(int value) => new Index(value);
        public override string ToString() => IsFromEnd ? "^" + Value.ToString() : Value.ToString();
    }

    public readonly struct Range : IEquatable<Range>
    {
        public Range(Index start, Index end) { Start = start; End = end; }
        public Index Start { get; }
        public Index End { get; }
        public static Range All => new Range(Index.Start, Index.End);
        public bool Equals(Range other) => Start.Equals(other.Start) && End.Equals(other.End);
        public override bool Equals(object? value) => value is Range other && Equals(other);
        public override int GetHashCode() => Start.GetHashCode() * 31 + End.GetHashCode();
        public (int Offset, int Length) GetOffsetAndLength(int length)
        {
            var start = Start.GetOffset(length);
            var end = End.GetOffset(length);
            if ((uint)end > (uint)length || (uint)start > (uint)end) throw new ArgumentOutOfRangeException(nameof(length));
            return (start, end - start);
        }
        public override string ToString() => Start + ".." + End;
    }
}

namespace System.Runtime.CompilerServices
{
    internal static class RuntimeHelpersCompat
    {
    }

    public static class RuntimeHelpersNetFrameworkRangeShim
    {
    }
}
#endif

namespace Forge.Shared
{
    /// <summary>Small framework-compatible helpers shared by net8 and net48 builds.</summary>
    public static class ForgeCompat
    {
        public static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
        public static double Clamp(double value, double min, double max) => value < min ? min : value > max ? max : value;
        public static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        public const StringSplitOptions TrimEntriesCompat = (StringSplitOptions)2;

        public static bool Contains(this string source, char value) => source.IndexOf(value) >= 0;
        public static bool Contains(this string source, string value, StringComparison comparisonType) => source.IndexOf(value, comparisonType) >= 0;
        public static bool Contains(this string source, char value, StringComparison comparisonType) => source.IndexOf(value.ToString(), comparisonType) >= 0;
        public static int IndexOf(this string source, char value, StringComparison comparisonType) => source.IndexOf(value.ToString(), comparisonType);
        public static bool StartsWith(this string source, char value) => source.Length != 0 && source[0] == value;
        public static bool EndsWith(this string source, char value) => source.Length != 0 && source[source.Length - 1] == value;
        public static System.Text.StringBuilder Append(this System.Text.StringBuilder builder, IFormatProvider provider, FormattableString value) => builder.Append(value.ToString(provider));

        public static string Replace(this string source, string oldValue, string? newValue, StringComparison comparisonType)
        {
            var replacement = newValue ?? string.Empty;
            var result = new System.Text.StringBuilder();
            var index = 0;
            while (index < source.Length)
            {
                var found = source.IndexOf(oldValue, index, comparisonType);
                if (found < 0) { result.Append(source, index, source.Length - index); break; }
                result.Append(source, index, found - index).Append(replacement);
                index = found + oldValue.Length;
            }
            return result.ToString();
        }

        public static string[] Split(this string source, char separator, StringSplitOptions options) => SplitCompat(source.Split(new[] { separator }, StringSplitOptions.None), options);
        public static string[] Split(this string source, string separator, StringSplitOptions options) => SplitCompat(source.Split(new[] { separator }, StringSplitOptions.None), options);
        private static string[] SplitCompat(string[] raw, StringSplitOptions options)
        {
            var trim = (options & TrimEntriesCompat) != 0;
            var removeEmpty = (options & StringSplitOptions.RemoveEmptyEntries) != 0;
            var values = new List<string>();
            foreach (var item in raw)
            {
                var value = trim ? item.Trim() : item;
                if (!removeEmpty || value.Length != 0) values.Add(value);
            }
            return values.ToArray();
        }

        public static void Write(this Stream stream, ReadOnlySpan<byte> buffer)
        {
            var bytes = buffer.ToArray();
            stream.Write(bytes, 0, bytes.Length);
        }

        public static byte[] Sha256Hash(byte[] bytes)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            return sha.ComputeHash(bytes);
        }

        public static string ToHexString(byte[] bytes)
        {
            var chars = new char[bytes.Length * 2];
            const string hex = "0123456789ABCDEF";
            for (var i = 0; i < bytes.Length; i++) { chars[i * 2] = hex[bytes[i] >> 4]; chars[i * 2 + 1] = hex[bytes[i] & 15]; }
            return new string(chars);
        }

        public static bool FixedTimeEquals(byte[] left, byte[] right)
        {
            if (left.Length != right.Length) return false;
            var diff = 0;
            for (var i = 0; i < left.Length; i++) diff |= left[i] ^ right[i];
            return diff == 0;
        }

        public static bool IsPathFullyQualified(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (path.StartsWith("\\\\", StringComparison.Ordinal)) return true;
            if (path.StartsWith("\\", StringComparison.Ordinal)) return false;
            return Path.IsPathRooted(path);
        }
        public static void MoveFile(string sourceFileName, string destFileName, bool overwrite)
        {
            if (overwrite && File.Exists(destFileName)) File.Delete(destFileName);
            File.Move(sourceFileName, destFileName);
        }

        public static long TickCount64() => System.Diagnostics.Stopwatch.GetTimestamp() * 1000 / System.Diagnostics.Stopwatch.Frequency;

#if NETFRAMEWORK
        public static IEnumerable<T> DistinctBy<T, TKey>(this IEnumerable<T> source, Func<T, TKey> keySelector)
        {
            var seen = new HashSet<TKey>();
            foreach (var item in source)
            {
                if (seen.Add(keySelector(item))) yield return item;
            }
        }
#endif

#if NETFRAMEWORK
        public static bool Remove<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, TKey key, out TValue value)
        {
            if (dictionary.TryGetValue(key, out value))
            {
                dictionary.Remove(key);
                return true;
            }
            return false;
        }
#endif

        public static Task WriteLineAsync(this System.IO.StreamWriter writer, ReadOnlyMemory<char> buffer, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return writer.WriteLineAsync(buffer.ToString());
        }

        public static async Task<int> ReadAsync(this System.IO.StreamReader reader, Memory<char> buffer, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chars = new char[buffer.Length];
            var read = await reader.ReadAsync(chars, 0, chars.Length).ConfigureAwait(false);
            chars.AsSpan(0, read).CopyTo(buffer.Span);
            return read;
        }


        public static void Deconstruct<TKey, TValue>(this KeyValuePair<TKey, TValue> pair, out TKey key, out TValue value)
        {
            key = pair.Key;
            value = pair.Value;
        }
    }
}
