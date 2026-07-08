using System;
using System.Text;

namespace FluxFormula.LiteralScanner.Generator
{
    /// <summary>
    /// 将紧凑模板语法翻译为 XML 格式。
    /// 紧凑语法：<c>&lt;float X&gt; &lt;float Y&gt;</c>
    /// XML 输出：<c>&lt;literal-template&gt;&lt;field type="float" name="X"/&gt;&lt;text&gt; &lt;/text&gt;...</c>
    /// </summary>
    internal static class CompactToXml
    {
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

            var sb = new StringBuilder(compact.Length * 2 + 50);
            sb.Append("<literal-template>");

            int pos = 0;
            var textBuf = new StringBuilder();

            while (pos < compact.Length)
            {
                if (compact[pos] == '<')
                {
                    FlushText(textBuf, sb);

                    int end = compact.IndexOf('>', pos + 1);
                    if (end < 0)
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
                    else
                    {
                        // <type fieldname> → <field type="type" name="fieldname"/>
                        int spaceIdx = trimmed.IndexOf(' ');
                        if (spaceIdx < 0)
                            throw new FormatException(
                                $"Invalid directive '<{trimmed}>'. Expected '<type fieldname>' or '<optional>'.");

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
