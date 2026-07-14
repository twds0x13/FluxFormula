using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SourceSerializer.Generator
{
    internal enum CollectionKind { None, List, Array }

    /// <summary>
    /// 编译期字段元数据，由 Roslyn 符号提取。
    /// 用于 SG 管线生成逐字段扫描代码，并追踪哪些字段未来需要 Walk 阶段处理。
    /// </summary>
    internal readonly struct FieldInfo
    {
        public readonly string Name;
        public readonly string TypeName;
        public readonly CollectionKind Kind;
        public readonly string? ElemType;
        /// <summary>
        /// 字段类型是否含 GC 引用。等价于 <c>!ITypeSymbol.IsUnmanagedType</c>。
        /// 供未来 Walk 阶段使用，当前 CodeEmitter 未消费。
        /// </summary>
        public readonly bool NeedsWalkPhase;

        public FieldInfo(string name, string typeName, CollectionKind kind,
            string? elemType, bool needsWalkPhase)
        {
            Name = name;
            TypeName = typeName;
            Kind = kind;
            ElemType = elemType;
            NeedsWalkPhase = needsWalkPhase;
        }
    }

    [Generator]
    public class SerializerGenerator : IIncrementalGenerator
    {
        private static readonly HashSet<string> BuiltinTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "float", "double", "int", "uint", "long", "ulong",
            "short", "ushort", "byte", "sbyte", "bool", "char", "string",
        };

        /// <summary>开放泛型模板镜像（与 SerializerRegistry.GenericTemplates 同步）。键=开放泛型全名，值=模板（T 为类型占位符）。</summary>
        private static readonly Dictionary<string, string> GenericTemplates = new(StringComparer.Ordinal)
        {
            ["System.Collections.Generic.List<>"] = "<first><T1 item></first><body>, <T1 item></body>",
            ["System.Collections.Generic.Dictionary<>"] = "<first><T1 key>: <T2 value></first><body>, <T1 key>: <T2 value></body>",
        };

        private static readonly HashSet<string> _knownEnumTypes = new(StringComparer.Ordinal);

        private static readonly DiagnosticDescriptor CircularDependencyError = new(
            "SSR002", "Circular template dependency",
            "Struct '{0}' has a circular dependency via template field types: {1}",
            "SourceSerializer", DiagnosticSeverity.Error, isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor ReadonlyStructError = new(
            "SSR003", "Readonly struct cannot use [Template]",
            "Struct '{0}' is declared 'readonly'. [Template] requires mutable fields for field assignment. Remove the 'readonly' modifier from the struct or its fields.",
            "SourceSerializer", DiagnosticSeverity.Error, isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor MissingDependencyError = new(
            "SSR004", "Missing template dependency",
            "Template for '{0}' references type '{1}' which has no [Template] and is not a built-in type. Add [Template] or [ExternalTemplate] to the type, use a built-in type, or mark the field with [TemplateIgnore].",
            "SourceSerializer", DiagnosticSeverity.Error, isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor ParseError = new(
            "SSR001", "Template Parse Error",
            "{0}", "SourceSerializer", DiagnosticSeverity.Error, isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor ScalarInRepetitionError = new(
            "SSR005", "Scalar field inside <repetition>",
            "Field '{0}' of struct '{1}' is scalar type '{2}' but appears inside a " +
            "<repetition> block. Use a collection type (List<T>, T[], etc.) for " +
            "fields that receive repeated values.",
            "SourceSerializer", DiagnosticSeverity.Error, isEnabledByDefault: true);

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Pipeline A: [Template] on struct or class
            var structDeclarations = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    "SourceSerializer.TemplateAttribute",
                    predicate: (node, _) => node is StructDeclarationSyntax || node is ClassDeclarationSyntax,
                    transform: (ctx, _) => GetStructInfo(ctx, fromExternal: false))
                .Where(info => info.HasValue)
                .Select((info, _) => info!.Value);

            // Pipeline B: [ExternalTemplate(typeof(X), "...")]
            var externalDeclarations = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    "SourceSerializer.ExternalTemplateAttribute",
                    predicate: (node, _) => true,
                    transform: (ctx, _) => GetExternalInfo(ctx))
                .Where(info => info.HasValue)
                .Select((info, _) => info!.Value);

            // Pipeline C: [TypeAlias("Alias", "float")] — custom aliases
            var typeAliases = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    "SourceSerializer.TypeAliasAttribute",
                    predicate: (node, _) => true,
                    transform: (ctx, _) => GetTypeAlias(ctx))
                .Where(a => a.HasValue)
                .Select((a, _) => a!.Value)
                .Collect();

            // Pipeline D: enum members with [Tag] — auto-generated tag scanners
            var enumTags = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    "SourceSerializer.TagAttribute",
                    predicate: (node, _) => node is EnumMemberDeclarationSyntax,
                    transform: (ctx, _) => GetEnumTagInfo(ctx))
                .Where(info => info.HasValue)
                .Select((info, _) => info!.Value)
                .Collect();

            var combined = structDeclarations.Collect()
                .Combine(externalDeclarations.Collect())
                .Combine(typeAliases)
                .Combine(enumTags);
            context.RegisterSourceOutput(combined, (spc, quad) =>
            {
                var (((builtin, external), aliases), tags) = quad;
                var merged = MergeDeclarations(builtin, external);
                var enumTagMap = BuildEnumTagMap(tags);
                foreach (var k in enumTagMap.Keys) _knownEnumTypes.Add(k);
                GenerateSource(spc, merged, aliases, enumTagMap);
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
            bool isClass = structSymbol.TypeKind == TypeKind.Class;

            string? template = ExtractTemplateArg(structSymbol, "TemplateAttribute");
            if (string.IsNullOrEmpty(template))
                return null;

            var fields = new List<FieldInfo>();
            foreach (var member in structSymbol.GetMembers())
            {
                if (member is IFieldSymbol f && !f.IsStatic && f.DeclaredAccessibility == Accessibility.Public)
                {
                    if (HasTemplateIgnoreAttribute(f))
                        continue;
                    var (kind, elemType) = ClassifyFieldType(f.Type);
                    bool fieldNeedsWalk = !f.Type.IsUnmanagedType;
                    fields.Add(new FieldInfo(f.Name, f.Type.ToDisplayString(), kind, elemType, fieldNeedsWalk));
                }
            }

            return new StructTemplateInfo
            {
                StructName = structName,
                Template = template!,
                IsReadonly = isReadonly,
                NeedsHeapAlloc = isClass,
                NeedsWalkPhase = !structSymbol.IsUnmanagedType,
                TypeKind = isClass ? "class" : "struct",
                Fields = fields,
            };
        }

        /// <summary>
        /// 从 [ExternalTemplate(typeof(X), "...")] 提取（目标类型, 模板）。
        /// attribute 可在 assembly/class/struct 上。
        /// </summary>
        private static StructTemplateInfo? GetExternalInfo(GeneratorAttributeSyntaxContext ctx)
        {
            // ExternalTemplate 的 constructor: (Type targetType, string template)
            foreach (var attr in ctx.Attributes)
            {
                if (attr.AttributeClass == null
                    || attr.AttributeClass.Name != "ExternalTemplateAttribute"
                    || attr.ConstructorArguments.Length < 2)
                    continue;

                // 第一个参数是 typeof(X) → TypedConstant of kind Type
                var typeArg = attr.ConstructorArguments[0];
                if (typeArg.Kind != TypedConstantKind.Type || typeArg.Value == null)
                    continue;

                var targetType = (INamedTypeSymbol)typeArg.Value;

                string? template = attr.ConstructorArguments[1].Value as string;
                if (string.IsNullOrEmpty(template))
                    continue;

                bool isClass = targetType.TypeKind == TypeKind.Class;
                var fields = new List<FieldInfo>();
                foreach (var member in targetType.GetMembers())
                {
                    if (member is IFieldSymbol f && !f.IsStatic && f.DeclaredAccessibility == Accessibility.Public)
                    {
                        if (HasTemplateIgnoreAttribute(f))
                            continue;
                        var (kind, elemType) = ClassifyFieldType(f.Type);
                        bool fieldNeedsWalk = !f.Type.IsUnmanagedType;
                        fields.Add(new FieldInfo(f.Name, f.Type.ToDisplayString(), kind, elemType, fieldNeedsWalk));
                    }
                }

                return new StructTemplateInfo
                {
                    StructName = targetType.Name,
                    Template = template!,
                    IsReadonly = targetType.IsReadOnly,
                    NeedsHeapAlloc = isClass,
                    NeedsWalkPhase = !targetType.IsUnmanagedType,
                    TypeKind = isClass ? "class" : "struct",
                    Fields = fields,
                };
            }

            return null;
        }

        /// <summary>从 [Tag("tag")] 提取枚举标签映射</summary>
        private static (string EnumName, string EnumFullName, string MemberName, string Tag)? GetEnumTagInfo(GeneratorAttributeSyntaxContext ctx)
        {
            var memberSymbol = ctx.TargetSymbol as IFieldSymbol;
            if (memberSymbol == null) return null;

            var enumType = memberSymbol.ContainingType;
            if (enumType == null || enumType.TypeKind != TypeKind.Enum) return null;

            foreach (var attr in ctx.Attributes)
            {
                if (attr.AttributeClass == null
                    || attr.AttributeClass.Name != "TagAttribute"
                    || attr.ConstructorArguments.Length < 1)
                    continue;

                var tag = attr.ConstructorArguments[0].Value as string;
                if (!string.IsNullOrEmpty(tag))
                    return (enumType.Name, enumType.ToDisplayString(), memberSymbol.Name, tag!);
            }
            return null;
        }

        /// <summary>将枚举标签列表按类型分组</summary>
        private static Dictionary<string, List<(string MemberName, string Tag)>> BuildEnumTagMap(
            System.Collections.Immutable.ImmutableArray<(string EnumName, string EnumFullName, string MemberName, string Tag)> tags)
        {
            var map = new Dictionary<string, List<(string, string)>>(StringComparer.Ordinal);
            foreach (var (name, _, member, tag) in tags)
            {
                if (!map.ContainsKey(name))
                    map[name] = new List<(string, string)>();
                map[name].Add((member, tag));
            }
            return map;
        }

        /// <summary>从 [TypeAlias("alias", "type")] 提取别名映射</summary>
        private static (string Alias, string CSharpType)? GetTypeAlias(GeneratorAttributeSyntaxContext ctx)
        {
            foreach (var attr in ctx.Attributes)
            {
                if (attr.AttributeClass == null
                    || attr.AttributeClass.Name != "TypeAliasAttribute"
                    || attr.ConstructorArguments.Length < 2)
                    continue;

                string? alias = attr.ConstructorArguments[0].Value as string;
                string? csharpType = attr.ConstructorArguments[1].Value as string;
                if (!string.IsNullOrEmpty(alias) && !string.IsNullOrEmpty(csharpType))
                    return (alias!, csharpType!);
            }
            return null;
        }

        /// <summary>用 Roslyn 类型系统判定字段的集合分类，同时提取元素类型</summary>
        private static (CollectionKind Kind, string? ElemType) ClassifyFieldType(ITypeSymbol type)
        {
            if (type is IArrayTypeSymbol arr)
                return (CollectionKind.Array, arr.ElementType.ToDisplayString());

            if (type is INamedTypeSymbol named && named.IsGenericType && named.TypeArguments.Length == 1)
            {
                var original = named.OriginalDefinition;
                var fullName = original.ToDisplayString();
                if (fullName == "System.Collections.Generic.List<T>" ||
                    fullName == "System.Collections.Generic.IList<T>" ||
                    fullName == "System.Collections.Generic.ICollection<T>" ||
                    fullName == "System.Collections.Generic.IEnumerable<T>")
                    return (CollectionKind.List, named.TypeArguments[0].ToDisplayString());
            }

            return (CollectionKind.None, null);
        }

        /// <summary>
        /// 判断字段类型是否为 managed（含引用类型字段，需 Walk 阶段处理）。
        /// 直接委托 Roslyn 编译器权威判定 <see cref="ITypeSymbol.IsUnmanagedType"/>，
        /// 零行手动规则——编译器已递归验证所有字段。
        /// </summary>
        private static bool IsManagedFieldType(ITypeSymbol type)
        {
            return !type.IsUnmanagedType;
        }

        /// <summary>检查字段是否标注了 [TemplateIgnore]，若是则跳过不参与序列化。</summary>
        private static bool HasTemplateIgnoreAttribute(IFieldSymbol field)
        {
            foreach (var attr in field.GetAttributes())
            {
                if (attr.AttributeClass != null
                    && attr.AttributeClass.Name == "TemplateIgnoreAttribute"
                    && attr.AttributeClass.ContainingNamespace != null
                    && attr.AttributeClass.ContainingNamespace.ToDisplayString() == "SourceSerializer")
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>从 symbol 的指定 attribute 中提取第一个构造参数（模板字符串）</summary>
        private static string? ExtractTemplateArg(ISymbol symbol, string attrName)
        {
            foreach (var attr in symbol.GetAttributes())
            {
                if (attr.AttributeClass != null
                    && attr.AttributeClass.Name == attrName
                    && attr.AttributeClass.ContainingNamespace != null
                    && attr.AttributeClass.ContainingNamespace.ToDisplayString() == "SourceSerializer")
                {
                    if (attr.ConstructorArguments.Length > 0)
                        return attr.ConstructorArguments[0].Value as string;
                }
            }
            return null;
        }

        private static void GenerateSource(SourceProductionContext context, List<StructTemplateInfo> structs,
            System.Collections.Immutable.ImmutableArray<(string Alias, string CSharpType)> typeAliases,
            Dictionary<string, List<(string MemberName, string Tag)>>? enumTagMap = null)
        {
            if (structs.Count == 0) return;

            // ── Check readonly structs ──
            foreach (var info in structs)
            {
                // SSR003 only applies to structs, not classes
                if (info.IsReadonly && info.TypeKind == "struct")
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

                // ── 1.5 Resolve generic type instances from field references ──
                var generated = ResolveGenericTypeInstances(parsed);
                foreach (var (ginfo, gast) in generated)
                    parsed.Add((ginfo, gast));

                // ── 2. Build dependency graph ──
                var depGraph = BuildDependencyGraph(context, parsed, typeAliases);
                if (depGraph == null) return; // circular dependency detected (diagnostic already reported)

                // ── 2.5 Validate: no scalar fields inside <repetition> ──
                ValidateRepetitionFields(context, parsed);

                // ── 3. Topological sort ──
                var ordered = TopologicalSort(parsed, depGraph);

                // ── 4. Emit code ──
                var emitList = new List<(string StructName, List<TemplateNode> Nodes,
                    Dictionary<string, FieldInfo> FieldTypes, bool NeedsHeapAlloc, bool NeedsWalkPhase, bool IsCollection)>();
                foreach (var (info, ast) in ordered)
                {
                    var fieldTypes = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
                    foreach (var fi in info.Fields)
                        fieldTypes[fi.Name] = fi;
                    emitList.Add((info.StructName, ast, fieldTypes, info.NeedsHeapAlloc, info.NeedsWalkPhase, info.IsCollection));
                }

                var emitDepGraph = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var (info, _) in ordered)
                    emitDepGraph[info.StructName] = CodeEmitter.GetScannerMethodName(info.StructName);

                var aliasMap = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var (alias, csharpType) in typeAliases)
                    aliasMap[alias] = csharpType;

                string source = CodeEmitter.EmitAll(emitList, emitDepGraph, aliasMap, enumTagMap);
                context.AddSource("SerializerScanners.g.cs", source);

                string emitSource = EmitCodeEmitter.EmitAll(emitList, emitDepGraph, aliasMap, enumTagMap);
                context.AddSource("SerializerEmitters.g.cs", emitSource);
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

                    // 枚举标签类型，由 Pipeline D 自动生成
                    if (_knownEnumTypes.Contains(refType))
                        continue;

                    if (allNames.Contains(refType))
                        graph[info.StructName].Add(refType);
                    else
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            MissingDependencyError, Location.None,
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
                else if (node is RepetitionNode rep)
                    foreach (var r in FindFieldTypeReferences(rep.Body))
                        refs.Add(r);
            }
            return refs;
        }

        /// <summary>
        /// 验证 repetition 块内没有标量字段。标量字段在 repetition 中每次迭代覆盖，
        /// 中间值丢失。集合类型（List/Array）已经由 CodeEmitter 处理。
        /// </summary>
        private static void ValidateRepetitionFields(
            SourceProductionContext context,
            List<(StructTemplateInfo Info, List<TemplateNode> Ast)> parsed)
        {
            foreach (var (info, ast) in parsed)
            {
                var fieldKinds = new Dictionary<string, CollectionKind>(StringComparer.Ordinal);
                foreach (var fi in info.Fields)
                    fieldKinds[fi.Name] = fi.Kind;

                ValidateNodes(context, info.StructName, ast, fieldKinds);
            }
        }

        private static void ValidateNodes(
            SourceProductionContext context,
            string structName,
            List<TemplateNode> nodes,
            Dictionary<string, CollectionKind> fieldKinds)
        {
            foreach (var node in nodes)
            {
                if (node is RepetitionNode rep)
                    ValidateRepetitionBody(context, structName, rep.Body, fieldKinds);
                else if (node is OptionalBlockNode opt)
                    ValidateNodes(context, structName, opt.Body, fieldKinds);
            }
        }

        private static void ValidateRepetitionBody(
            SourceProductionContext context,
            string structName,
            List<TemplateNode> nodes,
            Dictionary<string, CollectionKind> fieldKinds)
        {
            foreach (var node in nodes)
            {
                if (node is FieldDirectiveNode field)
                {
                    if (fieldKinds.TryGetValue(field.FieldName, out var kind) && kind == CollectionKind.None)
                    {
                        // Report SSR005: don't have the type name handy here, just report field
                        context.ReportDiagnostic(Diagnostic.Create(
                            ScalarInRepetitionError, Location.None,
                            field.FieldName, structName, "scalar"));
                    }
                }
                else if (node is OptionalBlockNode opt)
                    ValidateRepetitionBody(context, structName, opt.Body, fieldKinds);
                else if (node is RepetitionNode nested)
                    ValidateRepetitionBody(context, structName, nested.Body, fieldKinds);
            }
        }

        /// <summary>
        /// 扫描已解析模板的字段引用，为泛型集合类型（如 List&lt;NamedValue&gt;）
        /// 自动解析开放泛型模板并合成 StructTemplateInfo。
        /// </summary>
        private static List<(StructTemplateInfo Info, List<TemplateNode> Ast)> ResolveGenericTypeInstances(
            List<(StructTemplateInfo Info, List<TemplateNode> Ast)> parsed)
        {
            var result = new List<(StructTemplateInfo, List<TemplateNode>)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var (info, _) in parsed)
                seen.Add(info.StructName);

            foreach (var (info, ast) in parsed)
            {
                foreach (var node in ast)
                    CollectGenericRefs(node, seen, result);
            }

            return result;
        }

        private static void CollectGenericRefs(TemplateNode node, HashSet<string> seen,
            List<(StructTemplateInfo Info, List<TemplateNode> Ast)> result)
        {
            if (node is FieldDirectiveNode field)
            {
                // 尝试将 List<X> / Dict<K,V> 解析为开放泛型实例
                var (openGeneric, elemTypes) = ParseGenericType(field.TypeAlias);
                if (openGeneric != null && elemTypes != null
                    && GenericTemplates.TryGetValue(openGeneric, out var template))
                {
                    // 顺序替换 T1, T2, ... → 具体类型参数
                    string resolved = template;
                    for (int ti = 0; ti < elemTypes.Length; ti++)
                        resolved = resolved.Replace($"T{ti + 1}", elemTypes[ti]);
                    string instanceName = field.TypeAlias; // 保留原始泛型名

                    if (seen.Add(instanceName))
                    {
                        string xml = CompactToXml.IsCompactFormat(resolved)
                            ? CompactToXml.Convert(resolved)
                            : resolved;
                        var ast = XmlTemplateParser.Parse(xml);
                        var synthInfo = new StructTemplateInfo
                        {
                            StructName = instanceName,
                            Template = resolved,
                            IsReadonly = false,
                            NeedsHeapAlloc = true,   // 合成集合类型始终是引用类型 (class)
                            NeedsWalkPhase = true,   // 泛型集合含引用类型元素
                            IsCollection = true,
                            TypeKind = "class",
                            Fields = new List<FieldInfo>
                            {
                                new FieldInfo(field.FieldName, field.TypeAlias, CollectionKind.List, elemTypes[0], needsWalkPhase: true),
                            },
                        };
                        result.Add((synthInfo, ast));
                    }
                }
            }
            else if (node is OptionalBlockNode opt)
            {
                foreach (var child in opt.Body)
                    CollectGenericRefs(child, seen, result);
            }
            else if (node is RepetitionNode rep)
            {
                foreach (var child in rep.Body)
                    CollectGenericRefs(child, seen, result);
            }
        }

        /// <summary>将 "List&lt;NamedValue&gt;" 或 "Dict&lt;string,float&gt;" 解析为 (开放泛型全名, 类型参数[])</summary>
        private static (string? OpenGeneric, string[]? ElementTypes) ParseGenericType(string typeName)
        {
            int lt = typeName.IndexOf('<');
            int gt = typeName.LastIndexOf('>');
            if (lt < 0 || gt < lt) return (null, null);
            string baseName = typeName.Substring(0, lt);
            string argsStr = typeName.Substring(lt + 1, gt - lt - 1);

            // 按逗号拆分类型参数，跟踪嵌套 <> 深度
            var args = new List<string>();
            int depth = 0, lastSplit = 0;
            for (int i = 0; i < argsStr.Length; i++)
            {
                if (argsStr[i] == '<') depth++;
                else if (argsStr[i] == '>') depth--;
                else if (argsStr[i] == ',' && depth == 0)
                {
                    args.Add(argsStr.Substring(lastSplit, i - lastSplit).Trim());
                    lastSplit = i + 1;
                }
            }
            args.Add(argsStr.Substring(lastSplit).Trim());

            var openMap = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["List"] = "System.Collections.Generic.List<>",
                ["Dictionary"] = "System.Collections.Generic.Dictionary<>",
            };
            if (openMap.TryGetValue(baseName, out var fullName))
                return (fullName, args.ToArray());
            return (null, null);
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
            /// <summary>
            /// 类型是否为 class（引用类型）。控制生成代码使用 <c>new T()</c>（堆分配）还是 <c>default</c>（栈）。
            /// struct 始终用 <c>default</c>，不论是否含 managed 字段。
            /// </summary>
            public bool NeedsHeapAlloc;
            /// <summary>
            /// 类型是否不是 unmanaged 类型。等价于 <c>!ITypeSymbol.IsUnmanagedType</c>。
            /// 供未来 Walk 阶段两遍序列化使用。当前传递到 CodeEmitter 但未生成分支代码。
            /// </summary>
            public bool NeedsWalkPhase;
            public bool IsCollection; // true for List<T> etc. — field assignment → .Add()
            public string TypeKind; // "struct" or "class"
            public List<FieldInfo> Fields;
        }
    }
}
