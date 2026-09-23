#if NETFRAMEWORK
// Shim required so record/init-only properties compile on net462.
// The runtime type does not exist before net5.0 (and is not in netstandard2.1).
// Forge.Shared carries the equivalent netstandard2.0 shim for the shared library.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
#endif
