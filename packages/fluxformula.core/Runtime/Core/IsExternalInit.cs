// Polyfill for C# 9 init-only setters and record types on .NET Standard 2.0.
// Unity 2021.3–2022.3 run on .NET Standard 2.0 which lacks this type.
// Remove when the project's minimum Unity version ships with .NET 6+.

#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
#endif
