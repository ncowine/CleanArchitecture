#if NETFRAMEWORK
namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Compiler-only marker that enables <c>init</c> accessors and record types on net472. No runtime
    /// behavior — net10.0 already ships this type in its own BCL; only net472 needs it declared here.
    /// </summary>
    internal static class IsExternalInit
    {
    }
}
#endif
