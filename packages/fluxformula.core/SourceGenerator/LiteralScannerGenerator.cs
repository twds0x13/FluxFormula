using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace FluxFormula.LiteralScanner.Generator
{
    [Generator]
    public class LiteralScannerGenerator : IIncrementalGenerator
    {
        private static readonly HashSet<string> BuiltinTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "float", "double", "int", "uint", "long", "ulong",
            "short", "ushort", "byte", "sbyte", "bool", "char",
        };

        private static readonly DiagnosticDescriptor CircularDependencyError = new(
            "FLX002", "Circular template dependency",
            "Struct '{0}' has a circular dependency via template field types: {1}",
            "FluxFormula", DiagnosticSeverity.Error, isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor ReadonlyStructError = new(
            "FLX003", "Readonly struct cannot use LiteralTemplate",
            "Struct '{0}' is declared 'readonly'. [LiteralTemplate] requires mutable fields for field assignment. Remove the 'readonly' modifier from the struct or its fields.",
            "FluxFormula", DiagnosticSeverity.Error, isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor MissingDependencyWarning = new(
            "FLX004", "Missing template dependency",
            "Template for '{0}' references type '{1}' which has no [LiteralTemplate] and is not a built-in type. The field will be skipped.",
            "FluxFormula", DiagnosticSeverity.Warning, isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor ParseError = new(
            "FLX001", "Literal Template Error",
            "{0}", "FluxFormula", DiagnosticSeverity.Error, isEnabledByDefault: true);

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Pipeline A: struct-level [LiteralTemplate]
            var structDeclarations = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    "FluxFormula.Core.LiteralTemplateAttribute",
                    predicate: (node, _) => node is StructDeclarationSyntax,
                    transform: (ctx, _) => GetStructInfo(ctx, fromExternal: false))
                .Where(info => info.HasValue)
                .Select((info, _) => info!.Value);

            // Pipeline B: [ExternalLiteralTemplate(typeof(X), "...")]
            var externalDeclarations = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    "FluxFormula.Core.ExternalLiteralTemplateAttribute",
                    predicate: (node, _) => true,
                    transform: (ctx, _) => GetExternalInfo(ctx))
                .Where(info => info.HasValue)
                .Select((info, _) => info!.Value);

            // Pipeline C: [LiteralTypeAlias("Alias", "float")] — custom aliases
            var typeAliases = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    "FluxFormula.Core.LiteralTypeAliasAttribute",
                    predicate: (node, _) => true,
                    transform: (ctx, _) => GetTypeAlias(ctx))
                .Where(a => a.HasValue)
                .Select((a, _) => a!.Value)
                .Collect();

            var combined = structDeclarations.Collect()
                .Combine(externalDeclarations.Collect())
                .Combine(typeAliases);
            context.RegisterSourceOutput(combined, (spc, triple) =>
            {
                var ((builtin, external), aliases) = triple;
                var merged = MergeDeclarations(builtin, external);
                // Pass type aliases for the emitter to use
                GenerateSource(spc, merged, aliases);
            });
        }

        /// <summary>
        /// 合并两个来源的模板声明。External 覆盖 struct-level（Priority B 语义）。
        /// </summary>
        private static List<StructTemplateInfo> MergeDeclarations(
            System.Collections.Immutable.ImmutableArray<StructTemplateInfo> builtin,
            System.Collections.Immutable.ImmutableArray<StructTemplateInfo> external)
        {
            var map = new Dictionary<string, StructTemplateInfo>(StringComparer.Ordinal);

            // 先加 struct-level
            foreach (var info in builtin)
                map[info.StructName] = info;

            // 后加 external（覆盖同名）
            foreach (var info in external)
                map[info.StructName] = info;

            return map.Values.ToList();
        }

        private static StructTemplateInfo? GetStructInfo(GeneratorAttributeSyntaxContext ctx, bool fromExternal)
        {
            var structSymbol = (INamedTypeSymbol)ctx.TargetSymbol;
            var structName = structSymbol.Name;
            bool isReadonly = structSymbol.IsReadOnly;

            string? template = ExtractTemplateArg(structSymbol, "LiteralTemplateAttribute");
            if (string.IsNullOrEmpty(template))
                return null;

            var fields = new List<(string Name, string Type)>();
            foreach (var member in structSymbol.GetMembers())
            {
                if (member is IFieldSymbol f && !f.IsStatic && f.DeclaredAccessibility == Accessibility.Public)
                {
                    fields.Add((f.Name, f.Type.Name));
                }
            }

            return new StructTemplateInfo
            {
                StructName = structName,
                Template = template!,
                IsReadonly = isReadonly,
                Fields = fields,
            };
        }

        /// <summary>
        /// 从 [ExternalLiteralTemplate(typeof(X), "...")] 提取（目标类型, 模板）。
        /// attribute 可在 assembly/class/struct 上。
        /// </summary>
        private static StructTemplateInfo? GetExternalInfo(GeneratorAttributeSyntaxContext ctx)
        {
            // ExternalLiteralTemplate 的 constructor: (Type targetType, string template)
            foreach (var attr in ctx.Attributes)
            {
                if (attr.AttributeClass == null
                    || attr.AttributeClass.Name != "ExternalLiteralTemplateAttribute"
                    || attr.ConstructorArguments.Length < 2)
                    continue;

                // 第一个参数是 typeof(X) → TypedConstant of kind Type
                var typeArg = attr.ConstructorArguments[0];
                if (typeArg.Kind != TypedConstantKind.Type || typeArg.Value == null)
                    continue;

                var targetType = (INamedTypeSymbol)typeArg.Value;
                if (targetType.TypeKind != TypeKind.Struct)
                    continue;

                string? template = attr.ConstructorArguments[1].Value as string;
                if (string.IsNullOrEmpty(template))
                    continue;

                var fields = new List<(string Name, string Type)>();
                foreach (var member in targetType.GetMembers())
                {
                    if (member is IFieldSymbol f && !f.IsStatic && f.DeclaredAccessibility == Accessibility.Public)
                        fields.Add((f.Name, f.Type.Name));
                }

                return new StructTemplateInfo
                {
                    StructName = targetType.Name,
                    Template = template!,
                    IsReadonly = targetType.IsReadOnly,
                    Fields = fields,
                };
            }

            return null;
        }

        /// <summary>从 [LiteralTypeAlias("alias", "type")] 提取别名映射</summary>
        private static (string Alias, string CSharpType)? GetTypeAlias(GeneratorAttributeSyntaxContext ctx)
        {
            foreach (var attr in ctx.Attributes)
            {
                if (attr.AttributeClass == null
                    || attr.AttributeClass.Name != "LiteralTypeAliasAttribute"
                    || attr.ConstructorArguments.Length < 2)
                    continue;

                string? alias = attr.ConstructorArguments[0].Value as string;
                string? csharpType = attr.ConstructorArguments[1].Value as string;
                if (!string.IsNullOrEmpty(alias) && !string.IsNullOrEmpty(csharpType))
                    return (alias!, csharpType!);
            }
            return null;
        }

        /// <summary>从 symbol 的指定 attribute 中提取第一个构造参数（模板字符串）</summary>
        private static string? ExtractTemplateArg(ISymbol symbol, string attrName)
        {
            foreach (var attr in symbol.GetAttributes())
            {
                if (attr.AttributeClass != null
                    && attr.AttributeClass.Name == attrName
                    && attr.AttributeClass.ContainingNamespace != null
                    && attr.AttributeClass.ContainingNamespace.ToDisplayString() == "FluxFormula.Core")
                {
                    if (attr.ConstructorArguments.Length > 0)
                        return attr.ConstructorArguments[0].Value as string;
                }
            }
            return null;
        }

        private static void GenerateSource(SourceProductionContext context, List<StructTemplateInfo> structs,
            System.Collections.Immutable.ImmutableArray<(string Alias, string CSharpType)> typeAliases)
        {
            if (structs.Count == 0) return;

            // ── Check readonly structs ──
            foreach (var info in structs)
            {
                if (info.IsReadonly)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        ReadonlyStructError, Location.None, info.StructName));
                }
            }

            // Filter out readonly structs
            var writable = structs.Where(s => !s.IsReadonly).ToList();
            if (writable.Count == 0) return;

            try
            {
                // ── 1. Parse all templates ──
                var parsed = new List<(StructTemplateInfo Info, List<TemplateNode> Ast)>();
                foreach (var info in writable)
                {
                    string xml = CompactToXml.IsCompactFormat(info.Template)
                        ? CompactToXml.Convert(info.Template)
                        : info.Template;
                    var ast = XmlTemplateParser.Parse(xml);
                    parsed.Add((info, ast));
                }

                // ── 2. Build dependency graph ──
                var depGraph = BuildDependencyGraph(context, parsed, typeAliases);
                if (depGraph == null) return; // circular dependency detected (diagnostic already reported)

                // ── 3. Topological sort ──
                var ordered = TopologicalSort(parsed, depGraph);

                // ── 4. Emit code ──
                var emitList = new List<(string StructName, List<TemplateNode> Nodes)>();
                foreach (var (info, ast) in ordered)
                    emitList.Add((info.StructName, ast));

                var emitDepGraph = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var (info, _) in ordered)
                    emitDepGraph[info.StructName] = CodeEmitter.GetScannerMethodName(info.StructName);

                var aliasMap = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var (alias, csharpType) in typeAliases)
                    aliasMap[alias] = csharpType;

                string source = CodeEmitter.EmitAll(emitList, emitDepGraph, aliasMap);
                context.AddSource("LiteralScanners.g.cs", source);
            }
            catch (FormatException ex)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    ParseError, Location.None, ex.Message));
            }
        }

        private static Dictionary<string, HashSet<string>>? BuildDependencyGraph(
            SourceProductionContext context,
            List<(StructTemplateInfo Info, List<TemplateNode> Ast)> parsed,
            System.Collections.Immutable.ImmutableArray<(string Alias, string CSharpType)> typeAliases)
        {
            // 别名映射的后备类型等同于内置类型，不需要依赖边
            var aliasBackingTypes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (alias, _) in typeAliases)
                aliasBackingTypes.Add(alias);

            var graph = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var allNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (info, _) in parsed)
            {
                allNames.Add(info.StructName);
                graph[info.StructName] = new HashSet<string>(StringComparer.Ordinal);
            }

            // Collect dependencies
            foreach (var (info, ast) in parsed)
            {
                var externalRefs = FindFieldTypeReferences(ast);
                foreach (var refType in externalRefs)
                {
                    if (BuiltinTypes.Contains(refType))
                        continue;

                    // 别名映射到内置类型，不是真正的依赖
                    if (aliasBackingTypes.Contains(refType))
                        continue;

                    if (allNames.Contains(refType))
                        graph[info.StructName].Add(refType);
                    else
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            MissingDependencyWarning, Location.None,
                            info.StructName, refType));
                    }
                }
            }

            // Circular dependency check
            foreach (var kv in graph)
            {
                var visited = new HashSet<string>(StringComparer.Ordinal);
                var inStack = new HashSet<string>(StringComparer.Ordinal);
                var cyclePath = new List<string>();
                if (HasCycle(kv.Key, graph, visited, inStack, cyclePath))
                {
                    cyclePath.Add(kv.Key);
                    context.ReportDiagnostic(Diagnostic.Create(
                        CircularDependencyError, Location.None,
                        kv.Key, string.Join(" → ", cyclePath)));
                    return null;
                }
            }

            return graph;
        }

        private static HashSet<string> FindFieldTypeReferences(List<TemplateNode> nodes)
        {
            var refs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in nodes)
            {
                if (node is FieldDirectiveNode field)
                    refs.Add(field.TypeAlias);
                else if (node is OptionalBlockNode opt)
                    foreach (var r in FindFieldTypeReferences(opt.Body))
                        refs.Add(r);
            }
            return refs;
        }

        private static bool HasCycle(
            string node, Dictionary<string, HashSet<string>> graph,
            HashSet<string> visited, HashSet<string> inStack, List<string> cyclePath)
        {
            if (inStack.Contains(node))
            {
                cyclePath.Add(node);
                return true;
            }
            if (visited.Contains(node)) return false;

            visited.Add(node);
            inStack.Add(node);

            if (graph.TryGetValue(node, out var deps))
            {
                foreach (var dep in deps)
                {
                    if (HasCycle(dep, graph, visited, inStack, cyclePath))
                    {
                        cyclePath.Insert(0, dep);
                        return true;
                    }
                }
            }

            inStack.Remove(node);
            return false;
        }

        private static List<(StructTemplateInfo Info, List<TemplateNode> Ast)> TopologicalSort(
            List<(StructTemplateInfo Info, List<TemplateNode> Ast)> parsed,
            Dictionary<string, HashSet<string>> deps)
        {
            var result = new List<(StructTemplateInfo, List<TemplateNode>)>();
            var remaining = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (info, _) in parsed)
                remaining.Add(info.StructName);

            var nameIndex = new Dictionary<string, (StructTemplateInfo, List<TemplateNode>)>(StringComparer.Ordinal);
            foreach (var (info, ast) in parsed)
                nameIndex[info.StructName] = (info, ast);

            while (remaining.Count > 0)
            {
                bool found = false;
                foreach (var name in remaining.ToList())
                {
                    bool hasDep = false;
                    if (deps.TryGetValue(name, out var d))
                    {
                        foreach (var dep in d)
                        {
                            if (remaining.Contains(dep))
                            { hasDep = true; break; }
                        }
                    }

                    if (!hasDep)
                    {
                        result.Add(nameIndex[name]);
                        remaining.Remove(name);
                        found = true;
                    }
                }

                if (!found && remaining.Count > 0)
                {
                    foreach (var name in remaining)
                        result.Add(nameIndex[name]);
                    remaining.Clear();
                }
            }

            return result;
        }

        internal struct StructTemplateInfo
        {
            public string StructName;
            public string Template;
            public bool IsReadonly;
            public List<(string Name, string Type)> Fields;
        }
    }
}
