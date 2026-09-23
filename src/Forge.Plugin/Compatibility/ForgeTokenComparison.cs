using System.Security.Cryptography;
using System.Text;

namespace Forge.Plugin;

/// <summary>
/// Constant-time comparison for the named-pipe bearer token.
/// net8 uses <c>CryptographicOperations.FixedTimeEquals</c>; net462 has no such API, so both
/// operands are SHA-256 hashed and compared with a byte-wise XOR accumulator and one final
/// equality test (no early exit, no branch on content).
/// </summary>
internal static class ForgeTokenComparison
{
    public static bool Equals(string? left, string? right)
    {
        if (left is null || right is null)
        {
            return false;
        }

#if NETFRAMEWORK
        using var sha256 = SHA256.Create();
        var leftHash = sha256.ComputeHash(Encoding.UTF8.GetBytes(left));
        var rightHash = sha256.ComputeHash(Encoding.UTF8.GetBytes(right));
        var difference = 0;
        for (var index = 0; index < leftHash.Length; index++)
        {
            difference |= leftHash[index] ^ rightHash[index];
        }

        return difference == 0;
#else
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(left),
            Encoding.UTF8.GetBytes(right));
#endif
    }
}
