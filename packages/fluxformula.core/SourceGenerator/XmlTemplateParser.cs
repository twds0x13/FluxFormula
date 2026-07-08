using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace FluxFormula.LiteralScanner.Generator
{
    // ═══════════════════════════════════════════════════════
    // AST 节点类型
    // ═══════════════════════════════════════════════════════

    internal abstract class TemplateNode { }

    internal sealed class LiteralTextNode : TemplateNode
    {
        public string Text { get; }
        public LiteralTextNode(string text) => Text = text;
        public override string ToString() => $"Literal({Text.Replace("\n", "\\n").Replace("\r", "\\r")})";
    }

    internal sealed class FieldDirectiveNode : TemplateNode
    {
        public string TypeAlias { get; }
        public string FieldName { get; }
        public FieldDirectiveNode(string typeAlias, string fieldName)
            => (TypeAlias, FieldName) = (typeAlias, fieldName);
        public override string ToString() => $"Field({TypeAlias} {FieldName})";
    }

    internal sealed class OptionalBlockNode : TemplateNode
    {
        public List<TemplateNode> Body { get; }
        public OptionalBlockNode(List<TemplateNode> body) => Body = body;
        public override string ToString() => $"Optional({Body.Count} children)";
    }

    // ═══════════════════════════════════════════════════════
    // XML → AST 解析器
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// 将 XML 格式的字面量模板解析为 AST。
    /// 使用 XElement.Parse 处理所有 XML 语法细节。
    /// </summary>
    internal static class XmlTemplateParser
    {
        private static readonly XName RootName = "literal-template";
        private static readonly XName FieldName = "field";
        private static readonly XName TextName  = "text";
        private static readonly XName OptName   = "optional";

        /// <summary>XML 字符串 → AST 节点列表</summary>
        public static List<TemplateNode> Parse(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml))
                return new List<TemplateNode>();

            var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
            var root = doc.Root;
            if (root == null)
                return new List<TemplateNode>();

            if (root.Name != RootName)
                throw new FormatException(
                    $"Root element must be '<literal-template>'. Got '<{root.Name}>'.");

            return ParseChildren(root);
        }

        private static List<TemplateNode> ParseChildren(XElement parent)
        {
            var nodes = new List<TemplateNode>();

            foreach (var child in parent.Elements())
            {
                if (child.Name == FieldName)
                {
                    nodes.Add(ParseField(child));
                }
                else if (child.Name == TextName)
                {
                    nodes.Add(new LiteralTextNode(child.Value));
                }
                else if (child.Name == OptName)
                {
                    nodes.Add(new OptionalBlockNode(ParseChildren(child)));
                }
                else
                {
                    throw new FormatException(
                        $"Unknown element '<{child.Name}>' in template. " +
                        "Allowed: <field>, <text>, <optional>.");
                }
            }

            return nodes;
        }

        private static FieldDirectiveNode ParseField(XElement el)
        {
            var typeAttr = el.Attribute("type");
            var nameAttr = el.Attribute("name");

            if (typeAttr == null)
                throw new FormatException("<field> requires a 'type' attribute.");
            if (nameAttr == null)
                throw new FormatException("<field> requires a 'name' attribute.");

            return new FieldDirectiveNode(typeAttr.Value, nameAttr.Value);
        }
    }
}
