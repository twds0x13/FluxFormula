using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace SourceSerializer.Generator
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

    internal sealed class RepetitionNode : TemplateNode
    {
        /// <summary>首次迭代的模式（无前导分隔符）。null 表示整个 Body 作为通用模式。</summary>
        public List<TemplateNode>? First { get; }
        /// <summary>后续迭代的模式（含前导分隔符）。若 First 为 null，Body 用于所有迭代。</summary>
        public List<TemplateNode> Body { get; }
        public RepetitionNode(List<TemplateNode>? first, List<TemplateNode> body)
            => (First, Body) = (first, body);
        public override string ToString() =>
            $"Repetition(First={(First != null ? First.Count + " nodes" : "none")}, Body={Body.Count} nodes)";
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
        private static readonly XName RootName  = "literal-template";
        private static readonly XName FieldName  = "field";
        private static readonly XName TextName   = "text";
        private static readonly XName OptName    = "optional";
        private static readonly XName RepName    = "repetition";
        private static readonly XName FirstName  = "first";
        private static readonly XName BodyName   = "body";

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
                else if (child.Name == RepName)
                {
                    nodes.Add(ParseRepetition(child));
                }
                else if (child.Name == FirstName)
                {
                    // <first> at root: treat as repetition with First
                    var firstChildren = ParseChildren(child);
                    // Next sibling should be <body>
                    nodes.Add(new RepetitionNode(firstChildren, new List<TemplateNode>()));
                }
                else if (child.Name == BodyName)
                {
                    // <body> after <first>: fill Body of last RepetitionNode
                    if (nodes.Count > 0 && nodes[nodes.Count - 1] is RepetitionNode repNode && repNode.Body.Count == 0)
                    {
                        var bodyChildren = ParseChildren(child);
                        nodes[nodes.Count - 1] = new RepetitionNode(repNode.First, bodyChildren);
                    }
                    else
                    {
                        throw new FormatException(
                            "<body> must immediately follow <first>.");
                    }
                }
                else
                {
                    throw new FormatException(
                        $"Unknown element '<{child.Name}>' in template. " +
                        "Allowed: <field>, <text>, <optional>, <first>, <body>, <repetition>.");
                }
            }

            return nodes;
        }

        private static RepetitionNode ParseRepetition(XElement repEl)
        {
            List<TemplateNode>? first = null;
            List<TemplateNode>? body = null;
            var fallback = new List<TemplateNode>();

            foreach (var child in repEl.Elements())
            {
                if (child.Name == FirstName)
                    first = ParseChildren(child);
                else if (child.Name == BodyName)
                    body = ParseChildren(child);
                else
                    fallback.AddRange(new[] { ParseSingleChild(child) });
            }

            // 向后兼容: 无 <first>/<body> 时全部内容作为通用 Body
            if (first == null && body == null)
                return new RepetitionNode(null, fallback);

            return new RepetitionNode(first, body ?? new List<TemplateNode>());
        }

        private static TemplateNode ParseSingleChild(XElement child)
        {
            if (child.Name == FieldName) return ParseField(child);
            if (child.Name == TextName) return new LiteralTextNode(child.Value);
            if (child.Name == OptName) return new OptionalBlockNode(ParseChildren(child));
            if (child.Name == RepName) return ParseRepetition(child);
            throw new FormatException($"Unexpected element '<{child.Name}>' inside <repetition>.");
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
