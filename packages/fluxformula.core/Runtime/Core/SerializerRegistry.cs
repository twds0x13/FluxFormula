using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace SourceSerializer
{
    /// <summary>
    /// 内置字面量类型注册表。定义所有 C# 内置 unmanaged 值类型的正则模式、
    /// 解析器委托和零分配 span 扫描方法。
    /// </summary>
    /// <remarks>
    /// <para>内置值类型（float、int、bool 等）有预置的零分配 span 扫描器。</para>
    /// <para>自定义 struct 类型通过 <see cref="TemplateAttribute"/> 或
    /// <see cref="ExternalTemplateAttribute"/> 声明模板后，由 source generator
    /// 编译期生成对应的 <c>Scan_Xxx</c> 方法，递归进入嵌套类型的扫描器。</para>
    /// </remarks>
    public static class SerializerRegistry
    {
        // ═══════════════════════════════════════════════════════
        // 内置类型注册表
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// 内置类型别名字典：alias → (regex_pattern, display_name)。
        /// 用于 source generator 在编译期查找对应类型的扫描方法名。
        /// </summary>
        internal static readonly Dictionary<string, (string Pattern, string DisplayName)> BuiltinTypes = new()
        {
            ["float"]  = (@"-?\d+(?:\.\d+)?[fFdD]?", "float"),
            ["double"] = (@"-?\d+(?:\.\d+)?[dD]?",     "double"),
            ["int"]    = (@"-?\d+",                     "int"),
            ["uint"]   = (@"\d+",                       "uint"),
            ["long"]   = (@"-?\d+[lL]?",                "long"),
            ["ulong"]  = (@"\d+[uU]?[lL]?",             "ulong"),
            ["short"]  = (@"-?\d+",                     "short"),
            ["ushort"] = (@"\d+",                       "ushort"),
            ["byte"]   = (@"\d+",                       "byte"),
            ["sbyte"]  = (@"-?\d+",                     "sbyte"),
            ["bool"]   = (@"true|false",                "bool"),
            ["char"]   = (@".",                         "char"),
            ["string"] = (@"[^\s|>,)}\]]+",             "string"),
        };

        /// <summary>
        /// 返回给定别名是否为内置类型。
        /// </summary>
        // 仅供 source generator 编译期使用，运行时从不调用
        [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
        public static bool IsBuiltinType(string alias) => BuiltinTypes.ContainsKey(alias);

        /// <summary>
        /// 获取内置类型对应的 span 扫描方法名，如 "Scan_Float"、"Scan_Int"。
        /// </summary>
        [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
        internal static string GetScannerMethodName(string alias)
        {
            if (BuiltinTypes.TryGetValue(alias, out var info))
                return $"Scan_{info.DisplayName[0].ToString().ToUpperInvariant()}{info.DisplayName.Substring(1)}";
            return null;
        }

        // ═══════════════════════════════════════════════════════
        // 零分配 Span 扫描方法 —— 每个内置类型一个
        // 签名: static int Scan_Xxx(ReadOnlySpan<char> src, int pos, out Xxx value)
        // 返回: >pos 表示匹配成功（返回结束位置）；==pos 表示未匹配
        // ═══════════════════════════════════════════════════════

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int Scan_Float(ReadOnlySpan<char> src, int pos, out float value)
        {
            value = default;
            if (pos >= src.Length) return pos;
            int start = pos;

            // 可选符号
            if (src[pos] == '+' || src[pos] == '-')
                pos++;

            // 整数部分（必须至少一位数字）
            if (pos >= src.Length || !char.IsDigit(src[pos]))
                return start;
            while (pos < src.Length && char.IsDigit(src[pos]))
                pos++;

            // 可选小数部分
            if (pos < src.Length && src[pos] == '.')
            {
                pos++;
                if (pos >= src.Length || !char.IsDigit(src[pos]))
                    return start; // '.' 之后必须跟数字
                while (pos < src.Length && char.IsDigit(src[pos]))
                    pos++;
            }

            // 可选类型后缀（C# 语法：f/F/d/D）
            int parseEnd = pos;
            if (pos < src.Length && (src[pos] == 'f' || src[pos] == 'F' || src[pos] == 'd' || src[pos] == 'D'))
                pos++;

#if NET6_0_OR_GREATER
            if (!float.TryParse(src.Slice(start, parseEnd - start), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return start;
#else
            if (!float.TryParse(src.Slice(start, parseEnd - start).ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return start;
#endif
            return pos;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int Scan_Double(ReadOnlySpan<char> src, int pos, out double value)
        {
            value = default;
            if (pos >= src.Length) return pos;
            int start = pos;

            if (src[pos] == '+' || src[pos] == '-')
                pos++;
            if (pos >= src.Length || !char.IsDigit(src[pos]))
                return start;
            while (pos < src.Length && char.IsDigit(src[pos]))
                pos++;
            if (pos < src.Length && src[pos] == '.')
            {
                pos++;
                if (pos >= src.Length || !char.IsDigit(src[pos]))
                    return start;
                while (pos < src.Length && char.IsDigit(src[pos]))
                    pos++;
            }
            if (pos < src.Length && (src[pos] == 'e' || src[pos] == 'E'))
            {
                pos++;
                if (pos < src.Length && (src[pos] == '+' || src[pos] == '-'))
                    pos++;
                if (pos >= src.Length || !char.IsDigit(src[pos]))
                    return start;
                while (pos < src.Length && char.IsDigit(src[pos]))
                    pos++;
            }
            int parseEnd = pos;
            if (pos < src.Length && (src[pos] == 'd' || src[pos] == 'D'))
                pos++;

#if NET6_0_OR_GREATER
            if (!double.TryParse(src.Slice(start, parseEnd - start), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return start;
#else
            if (!double.TryParse(src.Slice(start, parseEnd - start).ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return start;
#endif
            return pos;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int Scan_Int(ReadOnlySpan<char> src, int pos, out int value)
        {
            value = default;
            if (pos >= src.Length) return pos;
            int start = pos;

            if (src[pos] == '+' || src[pos] == '-')
                pos++;
            if (pos >= src.Length || !char.IsDigit(src[pos]))
                return start;
            while (pos < src.Length && char.IsDigit(src[pos]))
                pos++;

#if NET6_0_OR_GREATER
            if (!int.TryParse(src.Slice(start, pos - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                return start;
#else
            if (!int.TryParse(src.Slice(start, pos - start).ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                return start;
#endif
            return pos;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int Scan_Uint(ReadOnlySpan<char> src, int pos, out uint value)
        {
            value = default;
            if (pos >= src.Length) return pos;
            int start = pos;

            if (!char.IsDigit(src[pos]))
                return start;
            while (pos < src.Length && char.IsDigit(src[pos]))
                pos++;

#if NET6_0_OR_GREATER
            if (!uint.TryParse(src.Slice(start, pos - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                return start;
#else
            if (!uint.TryParse(src.Slice(start, pos - start).ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                return start;
#endif
            return pos;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int Scan_Long(ReadOnlySpan<char> src, int pos, out long value)
        {
            value = default;
            if (pos >= src.Length) return pos;
            int start = pos;

            if (src[pos] == '+' || src[pos] == '-')
                pos++;
            if (pos >= src.Length || !char.IsDigit(src[pos]))
                return start;
            while (pos < src.Length && char.IsDigit(src[pos]))
                pos++;
            int parseEnd = pos;
            if (pos < src.Length && (src[pos] == 'l' || src[pos] == 'L'))
                pos++;

#if NET6_0_OR_GREATER
            if (!long.TryParse(src.Slice(start, parseEnd - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                return start;
#else
            if (!long.TryParse(src.Slice(start, pos - start).ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                return start;
#endif
            return pos;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int Scan_Ulong(ReadOnlySpan<char> src, int pos, out ulong value)
        {
            value = default;
            if (pos >= src.Length) return pos;
            int start = pos;

            if (!char.IsDigit(src[pos]))
                return start;
            while (pos < src.Length && char.IsDigit(src[pos]))
                pos++;
            int parseEnd = pos;
            if (pos < src.Length && (src[pos] == 'u' || src[pos] == 'U'))
                pos++;
            if (pos < src.Length && (src[pos] == 'l' || src[pos] == 'L'))
                pos++;

#if NET6_0_OR_GREATER
            if (!ulong.TryParse(src.Slice(start, parseEnd - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                return start;
#else
            if (!ulong.TryParse(src.Slice(start, pos - start).ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                return start;
#endif
            return pos;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int Scan_Short(ReadOnlySpan<char> src, int pos, out short value)
        {
            int result = Scan_Int(src, pos, out int iVal);
            value = result > pos ? (short)iVal : default;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int Scan_Ushort(ReadOnlySpan<char> src, int pos, out ushort value)
        {
            int result = Scan_Uint(src, pos, out uint uVal);
            value = result > pos ? (ushort)uVal : default;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int Scan_Byte(ReadOnlySpan<char> src, int pos, out byte value)
        {
            int result = Scan_Uint(src, pos, out uint uVal);
            value = result > pos ? (byte)uVal : default;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int Scan_Sbyte(ReadOnlySpan<char> src, int pos, out sbyte value)
        {
            int result = Scan_Int(src, pos, out int iVal);
            value = result > pos ? (sbyte)iVal : default;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int Scan_Bool(ReadOnlySpan<char> src, int pos, out bool value)
        {
            value = default;
            // 'true'
            if (pos + 4 <= src.Length
                && src[pos] == 't' && src[pos + 1] == 'r'
                && src[pos + 2] == 'u' && src[pos + 3] == 'e')
            {
                value = true;
                return pos + 4;
            }
            // 'false'
            if (pos + 5 <= src.Length
                && src[pos] == 'f' && src[pos + 1] == 'a'
                && src[pos + 2] == 'l' && src[pos + 3] == 's'
                && src[pos + 4] == 'e')
            {
                value = false;
                return pos + 5;
            }
            return pos;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int Scan_Char(ReadOnlySpan<char> src, int pos, out char value)
        {
            value = default;
            if (pos >= src.Length)
                return pos;
            value = src[pos];
            return pos + 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int Scan_String(ReadOnlySpan<char> src, int pos, out string value)
        {
            value = default!;
            if (pos >= src.Length) return pos;
            int start = pos;

            // Quoted: "hello world"
            if (src[pos] == '"')
            {
                pos++;
                int contentStart = pos;
                while (pos < src.Length && src[pos] != '"')
                    pos++;
                if (pos >= src.Length)
                    return start;
#if NET6_0_OR_GREATER
                value = src.Slice(contentStart, pos - contentStart).ToString();
#else
                value = src.Slice(contentStart, pos - contentStart).ToString();
#endif
                pos++;
                return pos;
            }

            // Unquoted: read until whitespace or delimiter
            while (pos < src.Length && !IsStringTerminator(src[pos]))
                pos++;

            if (pos == start) return start;
#if NET6_0_OR_GREATER
            value = src.Slice(start, pos - start).ToString();
#else
            value = src.Slice(start, pos - start).ToString();
#endif
            return pos;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsStringTerminator(char c)
        {
            return char.IsWhiteSpace(c)
                || c == '|' || c == '>' || c == ','
                || c == ')' || c == '}' || c == ']';
        }

        /// <summary>
        /// 将字符串追加到 StringBuilder，必要时加引号转义。
        /// </summary>
        internal static void Emit_String(System.Text.StringBuilder sb, string value)
        {
            bool needsQuote = false;
            for (int i = 0; i < value.Length; i++)
            {
                if (IsStringTerminator(value[i]) || value[i] == '"')
                { needsQuote = true; break; }
            }
            if (needsQuote)
            {
                sb.Append('"');
                sb.Append(value);
                sb.Append('"');
            }
            else
            {
                sb.Append(value);
            }
        }

        // ═══════════════════════════════════════════════════════
        // Emit 方法 —— 每个内置类型一个
        // 签名: static void Emit_Xxx(StringBuilder sb, Xxx value)
        // ═══════════════════════════════════════════════════════

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Emit_Float(StringBuilder sb, float value)
        {
            sb.Append(value.ToString("G9", CultureInfo.InvariantCulture));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Emit_Double(StringBuilder sb, double value)
        {
            sb.Append(value.ToString("G17", CultureInfo.InvariantCulture));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Emit_Int(StringBuilder sb, int value) => sb.Append(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Emit_Uint(StringBuilder sb, uint value) => sb.Append(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Emit_Long(StringBuilder sb, long value) => sb.Append(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Emit_Ulong(StringBuilder sb, ulong value) => sb.Append(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Emit_Short(StringBuilder sb, short value) => sb.Append(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Emit_Ushort(StringBuilder sb, ushort value) => sb.Append(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Emit_Byte(StringBuilder sb, byte value) => sb.Append(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Emit_Sbyte(StringBuilder sb, sbyte value) => sb.Append(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Emit_Bool(StringBuilder sb, bool value) => sb.Append(value ? "true" : "false");

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Emit_Char(StringBuilder sb, char value) => sb.Append(value);
    }
}
