using System.Security.Cryptography;
using System.Text;

namespace Forge.Shared;

/// <summary>
/// review-wp2-r1 finding 1 (D13): the named-pipe token gate. The caller's token is compared
/// to FORGE_PIPE_TOKEN in fixed time; every dispatch lane — the zero-document lane, the
/// no_active_document / no_command_context refusals, the idle and application marshals and
/// the command context — must pass it before answering. An unauthenticated caller gets
/// `unauthorized`, never `ok`.
/// </summary>
public static class PipeAuth
{
    public const string UnauthorizedCode = "unauthorized";

    /// <summary>True when <paramref name="token"/> equals <paramref name="expected"/> in fixed time; null and empty both mean "".</summary>
    public static bool TokenMatches(string? token, string? expected)
        => CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(token ?? ""),
            Encoding.UTF8.GetBytes(expected ?? ""));
}
