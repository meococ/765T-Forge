#if NETSTANDARD2_0
// Shim required so record/init-only properties compile on netstandard2.0.
// The runtime type does not exist before net5.0 (and is not in netstandard2.1).
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
#endif
