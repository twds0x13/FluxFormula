using System;
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
        // 零分配 Span 扫描方法 —— 每个内置类型一个
        // 签名: static int Scan_Xxx(ReadOnlySpan<char> src, int pos, out Xxx value)
        // 返回: >pos 表示匹配成功（返回结束位置）；==pos 表示未匹配
        // ═══════════════════════════════════════════════════════

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Scan_Float(ReadOnlySpan<char> src, int pos, out float value)
        {
            value = default;
            if (pos >= src.Length)
                return pos;
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
            if (
                pos < src.Length
                && (src[pos] == 'f' || src[pos] == 'F' || src[pos] == 'd' || src[pos] == 'D')
            )
                pos++;

#if NET6_0_OR_GREATER
            if (
                !float.TryParse(
                    src.Slice(start, parseEnd - start),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value
                )
            )
                return start;
#else
            if (
                !float.TryParse(
                    src.Slice(start, parseEnd - start).ToString(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value
                )
            )
                return start;
#endif
            return pos;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Scan_Double(ReadOnlySpan<char> src, int pos, out double value)
        {
            value = default;
            if (pos >= src.Length)
                return pos;
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
            if (
                !double.TryParse(
                    src.Slice(start, parseEnd - start),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value
                )
            )
                return start;
#else
            if (
                !double.TryParse(
                    src.Slice(start, parseEnd - start).ToString(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value
                )
            )
                return start;
#endif
            return pos;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Scan_Int(ReadOnlySpan<char> src, int pos, out int value)
        {
            value = default;
            if (pos >= src.Length)
                return pos;
            int start = pos;

            if (src[pos] == '+' || src[pos] == '-')
                pos++;
            if (pos >= src.Length || !char.IsDigit(src[pos]))
                return start;
            while (pos < src.Length && char.IsDigit(src[pos]))
                pos++;

#if NET6_0_OR_GREATER
            if (
                !int.TryParse(
                    src.Slice(start, pos - start),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out value
                )
            )
                return start;
#else
            if (
                !int.TryParse(
                    src.Slice(start, pos - start).ToString(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out value
                )
            )
                return start;
#endif
            return pos;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Scan_Uint(ReadOnlySpan<char> src, int pos, out uint value)
        {
            value = default;
            if (pos >= src.Length)
                return pos;
            int start = pos;

            if (!char.IsDigit(src[pos]))
                return start;
            while (pos < src.Length && char.IsDigit(src[pos]))
                pos++;

#if NET6_0_OR_GREATER
            if (
                !uint.TryParse(
                    src.Slice(start, pos - start),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out value
                )
            )
                return start;
#else
            if (
                !uint.TryParse(
                    src.Slice(start, pos - start).ToString(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out value
                )
            )
                return start;
#endif
            return pos;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Scan_Long(ReadOnlySpan<char> src, int pos, out long value)
        {
            value = default;
            if (pos >= src.Length)
                return pos;
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
            if (
                !long.TryParse(
                    src.Slice(start, parseEnd - start),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out value
                )
            )
                return start;
#else
            if (
                !long.TryParse(
                    src.Slice(start, pos - start).ToString(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out value
                )
            )
                return start;
#endif
            return pos;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Scan_Ulong(ReadOnlySpan<char> src, int pos, out ulong value)
        {
            value = default;
            if (pos >= src.Length)
                return pos;
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
            if (
                !ulong.TryParse(
                    src.Slice(start, parseEnd - start),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out value
                )
            )
                return start;
#else
            if (
                !ulong.TryParse(
                    src.Slice(start, pos - start).ToString(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out value
                )
            )
                return start;
#endif
            return pos;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Scan_Short(ReadOnlySpan<char> src, int pos, out short value)
        {
            int result = Scan_Int(src, pos, out int iVal);
            value = result > pos ? (short)iVal : default;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Scan_Ushort(ReadOnlySpan<char> src, int pos, out ushort value)
        {
            int result = Scan_Uint(src, pos, out uint uVal);
            value = result > pos ? (ushort)uVal : default;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Scan_Byte(ReadOnlySpan<char> src, int pos, out byte value)
        {
            int result = Scan_Uint(src, pos, out uint uVal);
            value = result > pos ? (byte)uVal : default;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Scan_Sbyte(ReadOnlySpan<char> src, int pos, out sbyte value)
        {
            int result = Scan_Int(src, pos, out int iVal);
            value = result > pos ? (sbyte)iVal : default;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Scan_Bool(ReadOnlySpan<char> src, int pos, out bool value)
        {
            value = default;
            // 'true'
            if (
                pos + 4 <= src.Length
                && src[pos] == 't'
                && src[pos + 1] == 'r'
                && src[pos + 2] == 'u'
                && src[pos + 3] == 'e'
            )
            {
                value = true;
                return pos + 4;
            }
            // 'false'
            if (
                pos + 5 <= src.Length
                && src[pos] == 'f'
                && src[pos + 1] == 'a'
                && src[pos + 2] == 'l'
                && src[pos + 3] == 's'
                && src[pos + 4] == 'e'
            )
            {
                value = false;
                return pos + 5;
            }
            return pos;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Scan_Char(ReadOnlySpan<char> src, int pos, out char value)
        {
            value = default;
            if (pos >= src.Length)
                return pos;
            value = src[pos];
            return pos + 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Scan_String(ReadOnlySpan<char> src, int pos, out string value)
        {
            value = default!;
            if (pos >= src.Length)
                return pos;
            int start = pos;

            // 仅接受引号字符串——裸字符串在空白符剔除后是无限匹配机
            if (src[pos] != '"')
                return start;

            pos++;
            int contentStart = pos;
            while (pos < src.Length && src[pos] != '"')
                pos++;
            if (pos >= src.Length)
                return start;
            value = src.Slice(contentStart, pos - contentStart).ToString();
            pos++;
            return pos;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Scan_IntPtr(ReadOnlySpan<char> src, int pos, out IntPtr value)
        {
            value = default;
            long result;
            int next = Scan_Long(src, pos, out result);
            if (next == pos)
                return pos;
            value = (IntPtr)result;
            return next;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Scan_UIntPtr(ReadOnlySpan<char> src, int pos, out UIntPtr value)
        {
            value = default;
            ulong result;
            int next = Scan_Ulong(src, pos, out result);
            if (next == pos)
                return pos;
            value = (UIntPtr)result;
            return next;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Scan_Guid(ReadOnlySpan<char> src, int pos, out Guid value)
        {
            value = default;
            if (pos >= src.Length)
                return pos;
            var slice = src.Slice(pos);
#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
            if (!Guid.TryParse(slice, out value))
                return pos;
#else
            if (!Guid.TryParse(slice.ToString(), out value))
                return pos;
#endif
            // Guid.TryParse 不报告消费长度，用标准格式长度 36 字符推进
            return pos + 36;
        }

        /// <summary>
        /// 将字符串追加到 StringBuilder，始终加引号以消除与数值类型的歧义。
        /// </summary>
        public static void Emit_String(System.Text.StringBuilder sb, string value)
        {
            if (value == null)
                return;
            sb.Append('"');
            sb.Append(value);
            sb.Append('"');
        }

        // ═══════════════════════════════════════════════════════
        // Emit 方法 —— 每个内置类型一个
        // 签名: static void Emit_Xxx(StringBuilder sb, Xxx value)
        // ═══════════════════════════════════════════════════════

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Emit_Float(StringBuilder sb, float value)
        {
            sb.Append(value.ToString("G9", CultureInfo.InvariantCulture));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Emit_Double(StringBuilder sb, double value)
        {
            sb.Append(value.ToString("G17", CultureInfo.InvariantCulture));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Emit_Int(StringBuilder sb, int value) => sb.Append(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Emit_Uint(StringBuilder sb, uint value) => sb.Append(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Emit_Long(StringBuilder sb, long value) => sb.Append(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Emit_Ulong(StringBuilder sb, ulong value) => sb.Append(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Emit_Short(StringBuilder sb, short value) => sb.Append(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Emit_Ushort(StringBuilder sb, ushort value) => sb.Append(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Emit_Byte(StringBuilder sb, byte value) => sb.Append(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Emit_Sbyte(StringBuilder sb, sbyte value) => sb.Append(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Emit_Bool(StringBuilder sb, bool value) =>
            sb.Append(value ? "true" : "false");

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Emit_Char(StringBuilder sb, char value) => sb.Append(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Emit_IntPtr(StringBuilder sb, IntPtr value) => sb.Append(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Emit_UIntPtr(StringBuilder sb, UIntPtr value) => sb.Append(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Emit_Guid(StringBuilder sb, Guid value) => sb.Append(value.ToString("D"));
    }
}
