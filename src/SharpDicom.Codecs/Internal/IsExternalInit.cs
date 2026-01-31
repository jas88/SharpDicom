// ReSharper disable once CheckNamespace
namespace System.Runtime.CompilerServices
{
#if NETSTANDARD2_0
    /// <summary>
    /// Polyfill for init-only setters in C# 9.0 on netstandard2.0.
    /// </summary>
    /// <remarks>
    /// This type is required by the compiler to support init-only properties
    /// on target frameworks that don't have it built-in (netstandard2.0, net472, etc.).
    /// It is only used at compile time and has no runtime impact.
    /// </remarks>
    internal static class IsExternalInit
    {
    }
#endif
}
