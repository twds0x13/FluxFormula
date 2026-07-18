using System;
using System.Collections.Generic;

namespace SourceSerializer.Generator
{
    /// <summary>
    /// 内置类型名称，单一来源。SG 三处管线均引用此集合。
    /// </summary>
    internal static class BuiltinTypeNames
    {
        public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
        {
            "float", "double", "int", "uint", "long", "ulong",
            "short", "ushort", "byte", "sbyte", "bool", "char", "string",
            "decimal", "nint", "nuint", "Half",
        };
    }
}
