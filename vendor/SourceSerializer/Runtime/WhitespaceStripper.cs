#nullable enable

using System;
using System.Runtime.InteropServices;

namespace SourceSerializer
{
    /// <summary>
    /// 输入预处理：单次扫描剔除字符串外部的全量空白符。
    /// 保护引号字符串内部区域（含 <c>\"</c> 转义），其余空白符全部移除。
    /// </summary>
    /// <remarks>
    /// 两遍扫描：第一遍计数，第二遍填充。无空白符时 Span 直接回指原串（零分配），
    /// 有空白符时通过 <see cref="Marshal.AllocHGlobal"/> 分配 native memory 存储结果。
    /// 这是紧凑模板架构的运行时基础——预处理后 Scan 管线对空白符零感知。
    /// <para>通过 duck-typed <c>Dispose()</c> 支持 <c>using var</c> 模式，兼容 C# 8+。</para>
    /// </remarks>
    public readonly unsafe ref struct WhitespaceStripper
    {
        private readonly char* _buffer;
        /// <summary>已剥离空白符的结果 span。无空白符时回指原串，全空白时为 Empty。</summary>
        public readonly ReadOnlySpan<char> Span;

        /// <summary>构造即完成剥离。两遍扫描，仅在有空白符需剔除时分配 native memory。</summary>
        public WhitespaceStripper(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                _buffer = null;
                Span = text ?? string.Empty;
                return;
            }

            ReadOnlySpan<char> input = text.AsSpan();

            // ── 第一遍：计算输出长度 ──────────────────────
            int outputLen = 0;
            bool inString = false;

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];

                if (inString)
                {
                    if (c == '\\' && i + 1 < input.Length && input[i + 1] == '"')
                    {
                        outputLen += 2; // 保留 \" 两个字符
                        i++;
                    }
                    else if (c == '"')
                    {
                        outputLen++; // 保留闭合引号
                        inString = false;
                    }
                    else
                    {
                        outputLen++; // 保留字符串内容
                    }
                }
                else
                {
                    if (c == '"')
                    {
                        outputLen++; // 保留开放引号
                        inString = true;
                    }
                    else if (!char.IsWhiteSpace(c))
                    {
                        outputLen++; // 保留非空白符
                    }
                    // 空白符在字符串外部：跳过（不增加 outputLen）
                }
            }

            // 无空白符需剔除 → 直接回指原串
            if (outputLen == input.Length)
            {
                _buffer = null;
                Span = input;
                return;
            }

            // 全空白 → 返回空 span
            if (outputLen == 0)
            {
                _buffer = null;
                Span = ReadOnlySpan<char>.Empty;
                return;
            }

            // ── 第二遍：填充 native buffer ──────────────────
            _buffer = (char*)Marshal.AllocHGlobal(outputLen * sizeof(char));
            var dest = new Span<char>(_buffer, outputLen);

            int pos = 0;
            bool inStr = false;

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];

                if (inStr)
                {
                    if (c == '\\' && i + 1 < input.Length && input[i + 1] == '"')
                    {
                        dest[pos++] = '\\';
                        dest[pos++] = '"';
                        i++;
                    }
                    else if (c == '"')
                    {
                        dest[pos++] = c;
                        inStr = false;
                    }
                    else
                    {
                        dest[pos++] = c;
                    }
                }
                else
                {
                    if (c == '"')
                    {
                        dest[pos++] = c;
                        inStr = true;
                    }
                    else if (!char.IsWhiteSpace(c))
                    {
                        dest[pos++] = c;
                    }
                }
            }

            Span = dest;
        }

        /// <summary>释放 native memory（如有分配）。支持 duck-typed <c>using var</c> 模式。</summary>
        public void Dispose()
        {
            if (_buffer != null)
            {
                Marshal.FreeHGlobal((IntPtr)_buffer);
            }
        }
    }
}
