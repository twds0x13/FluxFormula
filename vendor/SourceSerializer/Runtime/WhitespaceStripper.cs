#nullable enable

using System;

namespace SourceSerializer
{
    /// <summary>
    /// 输入预处理：单次扫描剔除字符串外部的全量空白符。
    /// 保护引号字符串内部区域（含 <c>\"</c> 转义），其余空白符全部移除。
    /// </summary>
    /// <remarks>
    /// 两阶段 <see cref="string.Create"/> 实现，零堆分配。
    /// 这是紧凑模板架构的运行时基础——预处理后 Scan 管线对空白符零感知。
    /// </remarks>
    public static class WhitespaceStripper
    {
        /// <summary>剔除给定字符串的字符串外部空白符。</summary>
        public static string Strip(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input ?? string.Empty;
            return Strip(input.AsSpan());
        }

        /// <summary>剔除给定 span 的字符串外部空白符。</summary>
        public static string Strip(ReadOnlySpan<char> input)
        {
            if (input.IsEmpty)
                return string.Empty;

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

            // 无空白符需剔除 → 直接返回原字符串
            if (outputLen == input.Length)
                return input.ToString();

            // 全空白 → 返回空串
            if (outputLen == 0)
                return string.Empty;

            // ── 第二遍：填充输出 ──────────────────────────
            // string.Create 的 TState 不能是 ReadOnlySpan<char>——通过元组绕行
            string inputStr = input.ToString();
            return string.Create(outputLen, (Src: inputStr, Idx: 0), (span, state) =>
            {
                int pos = 0;
                bool inStr = false;
                var src = state.Src.AsSpan();

                for (int i = 0; i < src.Length; i++)
                {
                    char c = src[i];

                    if (inStr)
                    {
                        if (c == '\\' && i + 1 < src.Length && src[i + 1] == '"')
                        {
                            span[pos++] = '\\';
                            span[pos++] = '"';
                            i++;
                        }
                        else if (c == '"')
                        {
                            span[pos++] = c;
                            inStr = false;
                        }
                        else
                        {
                            span[pos++] = c;
                        }
                    }
                    else
                    {
                        if (c == '"')
                        {
                            span[pos++] = c;
                            inStr = true;
                        }
                        else if (!char.IsWhiteSpace(c))
                        {
                            span[pos++] = c;
                        }
                    }
                }
            });
        }
    }
}
