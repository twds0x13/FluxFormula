using System;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace SourceSerializer.Generator
{
    /// <summary>
    /// 将紧凑模板语法翻译为 XML 格式。
    /// 紧凑语法：<c>&lt;float X&gt; &lt;float Y&gt;</c>
    /// XML 输出：<c>&lt;literal-template&gt;&lt;field type="float" name="X"/&gt;&lt;text&gt; &lt;/text&gt;...</c>
    /// </summary>
    // 纯编译期辅助类型，运行时从不实例化
    [ExcludeFromCodeCoverage]
    internal static class CompactToXml
    {
        // 预估的 XML 标签开销（<literal-template>、<text>、<field .../> 等），
        // 用于 StringBuilder 初始容量，避免转换期间扩容
        private const int XmlTagOverheadEstimate = 50;

        // ═══════════════════════════════════════════════════════
        // 公共入口
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// 检测给定字符串是否为紧凑格式（不含 &lt;literal-template&gt; 根元素）。
        /// </summary>
        public static bool IsCompactFormat(string template)
        {
            return !template.TrimStart().StartsWith("<literal-template", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>紧凑格式 → XML 字符串</summary>
        public static string Convert(string compact)
        {
            if (string.IsNullOrEmpty(compact))
                return "<literal-template/>";

            // 预处理：多行 → 规范化空白
            compact = NormalizeWhitespace(compact);

            var sb = new StringBuilder(compact.Length * 2 + XmlTagOverheadEstimate);
            sb.Append("<literal-template>");

            int pos = 0;
            var textBuf = new StringBuilder();

            while (pos < compact.Length)
            {
                if (compact[pos] == '<')
                {
                    FlushText(textBuf, sb);

                    // 嵌套深度跟踪：<List<NamedValue> Mods> 中内层 > 不终止
                    int end = pos + 1;
                    int depth = 1;
                    while (end < compact.Length && depth > 0)
                    {
                        if (compact[end] == '<') depth++;
                        else if (compact[end] == '>') depth--;
                        if (depth > 0) end++;
                    }
                    if (end >= compact.Length)
                        throw new FormatException(
                            $"Unclosed '<' at position {pos} in: \"{compact}\"");

                    string directive = compact.Substring(pos + 1, end - pos - 1);
                    pos = end + 1;

                    string trimmed = directive.Trim();

                    if (trimmed == "optional")
                    {
                        sb.Append("<optional>");
                    }
                    else if (trimmed == "/optional")
                    {
                        sb.Append("</optional>");
                    }
                    else if (trimmed == "repetition")
                    {
                        sb.Append("<repetition>");
                    }
                    else if (trimmed == "/repetition")
                    {
                        sb.Append("</repetition>");
                    }
                    else if (trimmed == "first")
                    {
                        sb.Append("<first>");
                    }
                    else if (trimmed == "/first")
                    {
                        sb.Append("</first>");
                    }
                    else if (trimmed == "body")
                    {
                        sb.Append("<body>");
                    }
                    else if (trimmed == "/body")
                    {
                        sb.Append("</body>");
                    }
                    else if (trimmed == "/repetition")
                    {
                        sb.Append("</repetition>");
                    }
                    else
                    {
                        // <type fieldname> → <field type="type" name="fieldname"/>
                        int spaceIdx = trimmed.IndexOf(' ');
                        if (spaceIdx < 0)
                            throw new FormatException(
                                $"Invalid directive '<{trimmed}>'. Expected '<type fieldname>', '<optional>', or '<repetition>'.");

                        string typeAlias = trimmed.Substring(0, spaceIdx);
                        string fieldName = trimmed.Substring(spaceIdx + 1).Trim();

                        sb.Append($"<field type=\"{EscapeXml(typeAlias)}\" name=\"{EscapeXml(fieldName)}\"/>");
                    }
                }
                else
                {
                    textBuf.Append(compact[pos]);
                    pos++;
                }
            }

            FlushText(textBuf, sb);

            sb.Append("</literal-template>");
            return sb.ToString();
        }

        // ═══════════════════════════════════════════════════════
        // XML 辅助
        // ═══════════════════════════════════════════════════════

        private static void FlushText(StringBuilder buf, StringBuilder output)
        {
            if (buf.Length > 0)
            {
                output.Append("<text>");
                string escaped = EscapeXml(buf.ToString());
                output.Append(escaped);
                output.Append("</text>");
                buf.Clear();
            }
        }

        private static string EscapeXml(string text)
        {
            if (text.IndexOfAny(xmlSpecialChars) < 0)
                return text;

            var sb = new StringBuilder(text.Length + 8);
            foreach (char c in text)
            {
                switch (c)
                {
                    case '&':  sb.Append("&amp;"); break;
                    case '<':  sb.Append("&lt;"); break;
                    case '>':  sb.Append("&gt;"); break;
                    case '"':  sb.Append("&quot;"); break;
                    case '\'': sb.Append("&apos;"); break;
                    default:   sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        private static readonly char[] xmlSpecialChars = { '&', '<', '>', '"', '\'' };

        /// <summary>
        /// 规范化空白：换行→空格，折叠连续空白，去首尾空白。
        /// </summary>
        private static string NormalizeWhitespace(string template)
        {
            var sb = new StringBuilder(template.Length);
            bool lastWasSpace = false;

            for (int i = 0; i < template.Length; i++)
            {
                char c = template[i];
                if (c == '\r' || c == '\n')
                {
                    if (!lastWasSpace && sb.Length > 0)
                    {
                        sb.Append(' ');
                        lastWasSpace = true;
                    }
                    if (c == '\r' && i + 1 < template.Length && template[i + 1] == '\n')
                        i++;
                }
                else if (c == ' ' || c == '\t')
                {
                    if (!lastWasSpace && sb.Length > 0)
                    {
                        sb.Append(' ');
                        lastWasSpace = true;
                    }
                }
                else
                {
                    sb.Append(c);
                    lastWasSpace = false;
                }
            }

            while (sb.Length > 0 && sb[sb.Length - 1] == ' ')
                sb.Length--;

            return sb.ToString();
        }
    }
}
