namespace Forge.Shared;

/// <summary>
/// Deterministic uppercase hex encoding that exists on netstandard2.0.
/// Output is byte-for-byte identical to <c>Convert.ToHexString</c> (uppercase, no separators).
/// </summary>
public static class Hex
{
    public static string Encode(byte[] bytes)
    {
        if (bytes is null)
        {
            throw new ArgumentNullException(nameof(bytes));
        }

        return BitConverter.ToString(bytes).Replace("-", "");
    }
}
