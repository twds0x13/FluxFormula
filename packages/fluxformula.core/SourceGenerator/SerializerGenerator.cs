using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SourceSerializer.Generator
{
    internal enum CollectionKind { None, Sequential, Array }

    /// <summary>
    /// 编译期字段元数据，由 Roslyn 符号提取。
    /// 用于 SG 管线生成逐字段扫描/发射代码。
    /// </summary>
    internal readonly struct FieldInfo
    {
        public readonly string Name;
        public readonly string TypeName;
        public readonly CollectionKind Kind;
        public readonly string? ElemType;
        /// <summary>
        /// 字段自身是否声明为 <c>readonly</c>。与 struct 级别的 readonly 不同：
        /// struct 级别 readonly 只影响 <c>this</c> 引用语义，不阻止外部代码对局部变量的字段赋值；
        /// 字段级别 readonly 会阻止任何赋值，需要构造函数初始化。
        /// </summary>
        public readonly bool IsReadonly;

        public FieldInfo(string name, string typeName, CollectionKind kind,
            string? elemType, bool isReadonly = false)
        {
            Name = name;
            TypeName = typeName;
            Kind = kind;
            ElemType = elemType;
            IsReadonly = isReadonly;
        }
    }

    [Generator]
    public class SerializerGenerator : IIncrementalGenerator
    {

        /// <summary>
        /// 内置默认泛型模板，以接口为目标（非具体类）。
        /// 具体类型（List, HashSet, Dictionary 等）通过 Roslyn AllInterfaces 自动匹配。
        /// 用户可通过 [ExternalTemplate(typeof(IList<>), "...")] 或 [ExternalTemplate(typeof(List<>), "...")] 覆盖。
        /// 模板中类型参数占位符使用实际类型参数名（T, TKey, TValue 等），SG 按名称替换。
        /// </summary>
        private static List<StructTemplateInfo> GetDefaultGenericTemplates()
        {
            return new List<StructTemplateInfo>
            {
                new StructTemplateInfo
                {
                    StructName = "IList`1",
                    Template = "<first><T item></first><body>, <T item></body>",
                    NeedsHeapAlloc = true,
                    IsCollection = true,

                    IsOpenGeneric = true,
                    TypeParameterNames = new[] { "T" },
                    ImplementedInterfaces = Array.Empty<string>(),
                    Fields = new List<FieldInfo>(),
                    IsReadonlyStruct = false,
                    MatchedCtorParams = null,
                },
                new StructTemplateInfo
                {
                    StructName = "ISet`1",
                    Template = "<first><T item></first><body>, <T item></body>",
                    NeedsHeapAlloc = true,
                    IsCollection = true,

                    IsOpenGeneric = true,
                    TypeParameterNames = new[] { "T" },
                    ImplementedInterfaces = Array.Empty<string>(),
                    Fields = new List<FieldInfo>(),
                    IsReadonlyStruct = false,
                    MatchedCtorParams = null,
                },
                new StructTemplateInfo
                {
                    StructName = "IReadOnlyList`1",
                    Template = "<first><T item></first><body>, <T item></body>",
                    NeedsHeapAlloc = true,
                    IsCollection = true,

                    IsOpenGeneric = true,
                    TypeParameterNames = new[] { "T" },
                    ImplementedInterfaces = Array.Empty<string>(),
                    Fields = new List<FieldInfo>(),
                    IsReadonlyStruct = false,
                    MatchedCtorParams = null,
                },
                new StructTemplateInfo
                {
                    StructName = "IDictionary`2",
                    Template = "<first><TKey key>: <TValue value></first><body>, <TKey key>: <TValue value></body>",
                    NeedsHeapAlloc = true,
                    IsCollection = true,

                    IsOpenGeneric = true,
                    TypeParameterNames = new[] { "TKey", "TValue" },
                    ImplementedInterfaces = Array.Empty<string>(),
                    Fields = new List<FieldInfo>(),
                    IsReadonlyStruct = false,
                    MatchedCtorParams = null,
                },
                new StructTemplateInfo
                {
                    StructName = "IReadOnlyDictionary`2",
                    Template = "<first><TKey key>: <TValue value></first><body>, <TKey key>: <TValue value></body>",
                    NeedsHeapAlloc = true,
                    IsCollection = true,

                    IsOpenGeneric = true,
                    TypeParameterNames = new[] { "TKey", "TValue" },
                    ImplementedInterfaces = Array.Empty<string>(),
                    Fields = new List<FieldInfo>(),
                    IsReadonlyStruct = false,
                    MatchedCtorParams = null,
                },
            };
        }

        private static readonly HashSet<string> _knownEnumTypes = new(StringComparer.Ordinal);

        private static readonly DiagnosticDescriptor CircularDependencyError = new(
            "SSR002", "Circular template dependency",
            "Struct '{0}' has a circular dependency via template field types: {1}",
            "SourceSerializer", DiagnosticSeverity.Error, isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor ReadonlyFieldError = new(
            "SSR003", "Readonly field cannot be assigned by deserialization",
            "Field '{0}' of '{1}' is declared 'readonly'. Deserialization writes to fields and cannot initialize readonly fields. Remove the 'readonly' modifier from the field, or add a constructor whose parameters match all fields by name and type.",
            "SourceSerializer", DiagnosticSeverity.Error, isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor MissingDependencyError = new(
            "SSR004", "Missing template dependency",
            "Template for '{0}' references type '{1}' which has no [Template] and is not a built-in type. Add [Template] or [ExternalTemplate] to the type, use a built-in type, or mark the field with [TemplateIgnore].",
            "SourceSerializer", DiagnosticSeverity.Error, isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor ParseError = new(
            "SSR001", "Template Parse Error",
            "{0}", "SourceSerializer", DiagnosticSeverity.Error, isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor ScalarInRepetitionError = new(
            "SSR005", "Scalar field inside repetition block",
            "Field '{0}' of struct '{1}' is scalar type '{2}' but appears inside a " +
            "repetition block. Use a collection type (List<T>, T[], etc.) for " +
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
                .Combine(enumTags)
                .Combine(context.CompilationProvider);
            context.RegisterSourceOutput(combined, (spc, quad) =>
            {
                var ((((builtin, external), aliases), tags), compilation) = quad;
                var merged = MergeDeclarations(builtin, external);
                var enumTagMap = BuildEnumTagMap(tags);
                foreach (var k in enumTagMap.Keys) _knownEnumTypes.Add(k);
                GenerateSource(spc, merged, aliases, enumTagMap, compilation);
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

        /// <summary>
        /// 从 INamedTypeSymbol 提取字段元数据、运行构造器匹配、构建 StructTemplateInfo。
        /// GetStructInfo 和 GetExternalInfo 的共享实现。
        /// </summary>
        /// <param name="allowInternalCtors">[Template] 为 true（同程序集），[ExternalTemplate] 为 false</param>
        private static StructTemplateInfo BuildStructTemplateInfo(
            INamedTypeSymbol typeSymbol, string template, bool allowInternalCtors)
        {
            bool isOpenGeneric = typeSymbol.IsGenericType;
            var structName = isOpenGeneric
                ? $"{typeSymbol.Name}`{typeSymbol.TypeParameters.Length}"
                : typeSymbol.Name;
            string[] typeParamNames = isOpenGeneric
                ? typeSymbol.TypeParameters.Select(tp => tp.Name).ToArray()
                : Array.Empty<string>();
            bool isClass = typeSymbol.TypeKind == TypeKind.Class;

            var fields = new List<FieldInfo>();
            var fieldSymbols = new List<IFieldSymbol>();
            foreach (var member in typeSymbol.GetMembers())
            {
                if (member is IFieldSymbol f && !f.IsStatic && f.DeclaredAccessibility == Accessibility.Public)
                {
                    if (HasTemplateIgnoreAttribute(f))
                        continue;
                    if (f.Type is ITypeParameterSymbol tp)
                    {
                        fields.Add(new FieldInfo(f.Name, tp.Name, CollectionKind.None, null));
                        fieldSymbols.Add(f);
                        continue;
                    }
                    var (kind, elemType) = ClassifyFieldType(f.Type);
                    fields.Add(new FieldInfo(f.Name, f.Type.ToDisplayString(), kind, elemType, isReadonly: f.IsReadOnly));
                    fieldSymbols.Add(f);
                }
            }

            // ── 构造器匹配 ──
            bool isReadonlyStruct = typeSymbol.IsReadOnly && !isClass;
            string[]? matchedCtorParams = null;
            if (fieldSymbols.Count > 0)
            {
                foreach (var ctor in typeSymbol.Constructors)
                {
                    if (ctor.IsImplicitlyDeclared) continue;
                    if (allowInternalCtors)
                    {
                        if (!(ctor.DeclaredAccessibility == Accessibility.Public
                           || ctor.DeclaredAccessibility == Accessibility.Internal
                           || ctor.DeclaredAccessibility == Accessibility.ProtectedOrInternal)) continue;
                    }
                    else
                    {
                        if (ctor.DeclaredAccessibility != Accessibility.Public) continue;
                    }
                    if (ctor.Parameters.Length < fieldSymbols.Count) continue;

                    bool allMatch = true;
                    foreach (var fs in fieldSymbols)
                    {
                        var match = ctor.Parameters.FirstOrDefault(p =>
                            string.Equals(p.Name, fs.Name, StringComparison.OrdinalIgnoreCase)
                            && SymbolEqualityComparer.Default.Equals(p.Type, fs.Type));
                        if (match == null) { allMatch = false; break; }
                    }
                    if (allMatch)
                    {
                        matchedCtorParams = ctor.Parameters.Select(p => p.Name).ToArray();
                        break;
                    }
                }
            }

            return new StructTemplateInfo
            {
                StructName = structName,
                Template = template,
                NeedsHeapAlloc = isClass,
                Fields = fields,
                IsOpenGeneric = isOpenGeneric,
                TypeParameterNames = typeParamNames,
                ImplementedInterfaces = typeSymbol.AllInterfaces.Select(i => i.ToDisplayString()).ToArray(),
                IsReadonlyStruct = isReadonlyStruct,
                MatchedCtorParams = matchedCtorParams,
            };
        }

        private static StructTemplateInfo? GetStructInfo(GeneratorAttributeSyntaxContext ctx, bool fromExternal)
        {
            var structSymbol = (INamedTypeSymbol)ctx.TargetSymbol;
            string? template = ExtractTemplateArg(structSymbol, "TemplateAttribute");
            if (string.IsNullOrEmpty(template))
                return null;
            return BuildStructTemplateInfo(structSymbol, template!, allowInternalCtors: true);
        }

        /// <summary>
        /// 从 [ExternalTemplate(typeof(X), "...")] 提取（目标类型, 模板）。
        /// attribute 可在 assembly/class/struct 上。
        /// </summary>
        private static StructTemplateInfo? GetExternalInfo(GeneratorAttributeSyntaxContext ctx)
        {
            foreach (var attr in ctx.Attributes)
            {
                if (attr.AttributeClass == null
                    || attr.AttributeClass.Name != "ExternalTemplateAttribute"
                    || attr.ConstructorArguments.Length < 2)
                    continue;

                var typeArg = attr.ConstructorArguments[0];
                if (typeArg.Kind != TypedConstantKind.Type || typeArg.Value == null)
                    continue;

                var targetType = (INamedTypeSymbol)typeArg.Value;
                string? template = attr.ConstructorArguments[1].Value as string;
                if (string.IsNullOrEmpty(template))
                    continue;

                return BuildStructTemplateInfo(targetType, template!, allowInternalCtors: false);
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
                    fullName == "System.Collections.Generic.IEnumerable<T>" ||
                    fullName == "System.Collections.Generic.ISet<T>" ||
                    fullName == "System.Collections.Generic.IReadOnlyList<T>" ||
                    fullName == "System.Collections.Generic.HashSet<T>")
                    return (CollectionKind.Sequential, named.TypeArguments[0].ToDisplayString());
            }

            return (CollectionKind.None, null);
        }

        /// <summary>
        /// 判断字段类型是否为 managed（含引用类型字段，需 Walk 阶段处理）。
        /// 直接委托 Roslyn 编译器权威判定 <see cref="ITypeSymbol.IsUnmanagedType"/>，
        /// 零行手动规则——编译器已递归验证所有字段。
        /// </summary>
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
            Dictionary<string, List<(string MemberName, string Tag)>>? enumTagMap,
            Compilation compilation)
        {
            if (structs.Count == 0) return;

            var writable = structs.ToList();

            // 注入内置默认泛型模板（用户可通过 [ExternalTemplate] 覆盖）
            foreach (var def in GetDefaultGenericTemplates())
                writable.Insert(0, def);

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
                var generated = ResolveGenericTypeInstances(parsed, compilation);
                foreach (var (ginfo, gast) in generated)
                    parsed.Add((ginfo, gast));

                // ── 1.6 Build interface→concrete dispatch map ──
                // 仅收集用户自定义接口（排除 BCL System.* 接口——不生成 dispatch 方法）
                var interfaceMap = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                foreach (var info in writable)
                {
                    if (info.ImplementedInterfaces == null) continue;
                    foreach (var iface in info.ImplementedInterfaces)
                    {
                        if (iface.StartsWith("System.", StringComparison.Ordinal)) continue;
                        if (!interfaceMap.TryGetValue(iface, out var list))
                            interfaceMap[iface] = list = new List<string>();
                        list.Add(info.StructName);
                    }
                }

                // ── 2. Build dependency graph ──
                var depGraph = BuildDependencyGraph(context, parsed, typeAliases, interfaceMap);
                if (depGraph == null) return; // circular dependency detected (diagnostic already reported)

                // ── 2.5 Validate: no scalar fields inside <repetition> ──
                ValidateRepetitionFields(context, parsed);

                // ── 2.6 Validate: no readonly fields referenced in templates ──
                ValidateFieldMutability(context, parsed);

                // ── 3. Topological sort ──
                var ordered = TopologicalSort(parsed, depGraph);

                // ── 4. Emit code（开放泛型模板不生成代码，仅合成时使用）──
                var emitList = new List<EmitEntry>();
                foreach (var (info, ast) in ordered)
                {
                    if (info.IsOpenGeneric) continue;
                    if (info.IsReadonlyStruct && info.MatchedCtorParams == null) continue;
                    var fieldTypes = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
                    foreach (var fi in info.Fields)
                        fieldTypes[fi.Name] = fi;
                    emitList.Add(new EmitEntry
                    {
                        Common = info.ToCommon(),
                        Nodes = ast,
                        FieldTypes = fieldTypes,
                    });
                }

                var emitDepGraph = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var (info, _) in ordered)
                {
                    if (info.IsOpenGeneric) continue;
                    if (info.IsReadonlyStruct && info.MatchedCtorParams == null) continue;
                    emitDepGraph[info.StructName] = EmitHelpers.GetMethodName("Scan",info.StructName);
                }
                // 接口 dispatch 条目加入依赖图
                foreach (var ifaceName in interfaceMap.Keys)
                    emitDepGraph[ifaceName] = EmitHelpers.GetMethodName("Scan",ifaceName);

                var aliasMap = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var (alias, csharpType) in typeAliases)
                    aliasMap[alias] = csharpType;

                string source = CodeEmitter.EmitAll(emitList, emitDepGraph, aliasMap, enumTagMap, interfaceMap);
                context.AddSource("SerializerScanners.g.cs", source);

                string emitSource = EmitCodeEmitter.EmitAll(emitList, emitDepGraph, aliasMap, enumTagMap, interfaceMap);
                context.AddSource("SerializerEmitters.g.cs", emitSource);

                string blockSource = BlockEmitter.EmitAll(emitList, interfaceMap);
                context.AddSource("SerializerBlocks.g.cs", blockSource);
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
            System.Collections.Immutable.ImmutableArray<(string Alias, string CSharpType)> typeAliases,
            Dictionary<string, List<string>> interfaceMap)
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
                    if (BuiltinTypeNames.All.Contains(refType))
                        continue;

                    // 别名映射到内置类型，不是真正的依赖
                    if (aliasBackingTypes.Contains(refType))
                        continue;

                    // 枚举标签类型，由 Pipeline D 自动生成
                    if (_knownEnumTypes.Contains(refType))
                        continue;

                    // 开放泛型的类型参数不是真正的依赖（合成时替换）
                    if (info.TypeParameterNames != null && info.TypeParameterNames.Contains(refType))
                        continue;

                    // 接口引用：interfaceMap 中有实现 → 合法依赖
                    if (interfaceMap.ContainsKey(refType))
                    {
                        graph[info.StructName].Add(refType);
                        continue;
                    }

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
        /// 验证模板中引用的字段没有标记为 <c>readonly</c>。
        /// struct 级别的 readonly 不影响外部代码对局部变量字段的赋值，但字段级别的 readonly 会阻止赋值。
        /// </summary>
        private static void ValidateFieldMutability(
            SourceProductionContext context,
            List<(StructTemplateInfo Info, List<TemplateNode> Ast)> parsed)
        {
            foreach (var (info, ast) in parsed)
            {
                // 有匹配构造器的类型跳过——构造器会处理字段初始化
                if (info.MatchedCtorParams != null) continue;

                var fieldMap = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
                foreach (var fi in info.Fields)
                    fieldMap[fi.Name] = fi;

                ValidateFieldNodesReadonly(context, info.StructName, ast, fieldMap);
            }
        }

        private static void ValidateFieldNodesReadonly(
            SourceProductionContext context,
            string structName,
            List<TemplateNode> nodes,
            Dictionary<string, FieldInfo> fieldMap)
        {
            foreach (var node in nodes)
            {
                if (node is FieldDirectiveNode field)
                {
                    if (fieldMap.TryGetValue(field.FieldName, out var fi) && fi.IsReadonly)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            ReadonlyFieldError, Location.None,
                            field.FieldName, structName));
                    }
                }
                else if (node is OptionalBlockNode opt)
                {
                    ValidateFieldNodesReadonly(context, structName, opt.Body, fieldMap);
                }
                else if (node is RepetitionNode rep)
                {
                    var allNodes = new List<TemplateNode>();
                    if (rep.First != null) allNodes.AddRange(rep.First);
                    allNodes.AddRange(rep.Body);
                    ValidateFieldNodesReadonly(context, structName, allNodes, fieldMap);
                }
            }
        }

        /// <summary>
        /// 扫描已解析模板的字段引用，为泛型集合类型（如 List&lt;NamedValue&gt;）
        /// 自动解析开放泛型模板并合成 StructTemplateInfo。
        /// 支持硬编码集合泛型（List/Dictionary）和用户定义开放泛型。
        /// 递归发现处理嵌套泛型（如 List&lt;Wrapper&lt;float&gt;&gt;）。
        /// </summary>
        private static List<(StructTemplateInfo Info, List<TemplateNode> Ast)> ResolveGenericTypeInstances(
            List<(StructTemplateInfo Info, List<TemplateNode> Ast)> parsed, Compilation compilation)
        {
            // 构建开放泛型索引：StructName（已含元数后缀如 "IList`1"）→ (Info, AST)
            var openGenerics = new Dictionary<string, (StructTemplateInfo Info, List<TemplateNode> Ast)>(StringComparer.Ordinal);
            foreach (var (info, ast) in parsed)
                if (info.IsOpenGeneric)
                    openGenerics[info.StructName] = (info, ast);

            var result = new List<(StructTemplateInfo, List<TemplateNode>)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (info, _) in parsed)
                seen.Add(info.StructName);

            // 递归发现：对每轮新合成的条目继续扫描字段引用
            CollectAllGenericRefs(parsed, seen, result, openGenerics, compilation);

            return result;
        }

        /// <summary>递归扫描条目列表中的字段引用，发现并合成泛型实例。</summary>
        private static void CollectAllGenericRefs(
            List<(StructTemplateInfo Info, List<TemplateNode> Ast)> entries,
            HashSet<string> seen,
            List<(StructTemplateInfo Info, List<TemplateNode> Ast)> result,
            Dictionary<string, (StructTemplateInfo Info, List<TemplateNode> Ast)> openGenerics,
            Compilation compilation)
        {
            int beforeCount = result.Count;
            foreach (var (info, ast) in entries)
            {
                foreach (var node in ast)
                    CollectGenericRefs(node, seen, result, openGenerics, compilation);
            }

            // 本轮新合成的条目，递归扫描（处理 List&lt;Wrapper&lt;float&gt;&gt; 场景）
            var newEntries = new List<(StructTemplateInfo, List<TemplateNode>)>();
            for (int i = beforeCount; i < result.Count; i++)
                newEntries.Add(result[i]);
            if (newEntries.Count > 0)
                CollectAllGenericRefs(newEntries, seen, result, openGenerics, compilation);
        }

        private static void CollectGenericRefs(TemplateNode node, HashSet<string> seen,
            List<(StructTemplateInfo Info, List<TemplateNode> Ast)> result,
            Dictionary<string, (StructTemplateInfo Info, List<TemplateNode> Ast)> openGenerics,
            Compilation compilation)
        {
            if (node is FieldDirectiveNode field)
            {
                // 解析泛型类型实例：List<float>, Wrapper<int>, Dict<string,float> 等
                var (openGeneric, elemTypes, _) = ParseGenericType(field.TypeAlias, openGenerics);

                if (openGeneric != null && elemTypes != null
                    && TryGetOpenGenericTemplate(openGeneric, elemTypes.Length, openGenerics, compilation,
                        out var openInfo, out var openAst))
                {
                    string instanceName = field.TypeAlias;
                    if (seen.Add(instanceName))
                    {
                        // 1. 模板字符串中替换类型参数（按名称长度降序，避免 T 损坏 TKey）
                        var tpOrder = Enumerable.Range(0, elemTypes.Length)
                            .OrderByDescending(i => openInfo.TypeParameterNames[i].Length)
                            .ToArray();
                        string resolved = openInfo.Template;
                        foreach (var ti in tpOrder)
                            resolved = resolved.Replace(openInfo.TypeParameterNames[ti], elemTypes[ti]);

                        // 2. 重新解析替换后的模板
                        string xml = CompactToXml.IsCompactFormat(resolved)
                            ? CompactToXml.Convert(resolved) : resolved;
                        var ast = XmlTemplateParser.Parse(xml);

                        // 3. 构建具体化 FieldInfo 列表
                        var synthFields = new List<FieldInfo>();
                        foreach (var fi in openInfo.Fields)
                        {
                            string concreteType = fi.TypeName;
                            foreach (var ti in tpOrder)
                                concreteType = concreteType.Replace(openInfo.TypeParameterNames[ti], elemTypes[ti]);
                            var (kind, elemType) = ClassifyFieldTypeByName(concreteType);
                            synthFields.Add(new FieldInfo(fi.Name, concreteType, kind, elemType,
                                isReadonly: fi.IsReadonly));
                        }

                        // 集合检测：开放泛型模板含 <first>/<body> 时自动标记为集合
                        bool synthIsCollection = openInfo.IsCollection
                            || (openInfo.Template.Contains("<first>") && openInfo.Template.Contains("<body>"));

                        var synthInfo = new StructTemplateInfo
                        {
                            StructName = instanceName,
                            Template = resolved,
                            NeedsHeapAlloc = openInfo.NeedsHeapAlloc,
                            IsCollection = synthIsCollection,
                            Fields = synthFields,
                            IsReadonlyStruct = openInfo.IsReadonlyStruct,
                            MatchedCtorParams = openInfo.MatchedCtorParams,
                        };
                        result.Add((synthInfo, ast));
                    }
                }
            }
            else if (node is OptionalBlockNode opt)
            {
                foreach (var child in opt.Body)
                    CollectGenericRefs(child, seen, result, openGenerics, compilation);
            }
            else if (node is RepetitionNode rep)
            {
                foreach (var child in rep.Body)
                    CollectGenericRefs(child, seen, result, openGenerics, compilation);
            }
        }

        /// <summary>
        /// 尝试为泛型字段查找开放泛型模板。先在 openGenerics 精确查找（类级/接口级显式模板），
        /// 失败后通过 Roslyn AllInterfaces 查找匹配的默认接口模板。
        /// 优先级：类级显式模板 &gt; 接口级显式模板 &gt; 默认接口模板。
        /// </summary>
        private static bool TryGetOpenGenericTemplate(
            string baseName, int arity,
            Dictionary<string, (StructTemplateInfo Info, List<TemplateNode> Ast)> openGenerics,
            Compilation compilation,
            out StructTemplateInfo info, out List<TemplateNode> ast)
        {
            info = default;
            ast = default!;

            string key = $"{baseName}`{arity}";

            // 1. 精确匹配（类级 + 接口级显式模板 + 默认接口模板）
            if (openGenerics.TryGetValue(key, out var entry))
            {
                info = entry.Info;
                ast = entry.Ast;
                return true;
            }

            // 2. Roslyn 回退：通过 AllInterfaces 查找匹配的默认接口模板（仅 BCL 类型）
            if (compilation != null)
            {
                var resolved = TryResolveViaInterfaces(baseName, arity, openGenerics, compilation);
                if (resolved != null)
                {
                    info = resolved.Value.Info;
                    ast = resolved.Value.Ast;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 当类型不在 openGenerics 中时，通过 Roslyn 解析 BCL 类型并检查其 AllInterfaces
        /// 是否有匹配的开放泛型接口模板（默认模板均以接口为目标）。
        /// </summary>
        private static (StructTemplateInfo Info, List<TemplateNode> Ast)?
            TryResolveViaInterfaces(
                string typeBaseName, int arity,
                Dictionary<string, (StructTemplateInfo Info, List<TemplateNode> Ast)> openGenerics,
                Compilation compilation)
        {
            // 解析 BCL 类型：System.Collections.Generic.{Name}`{arity}
            string fullName = $"System.Collections.Generic.{typeBaseName}`{arity}";
            var typeSymbol = compilation.GetTypeByMetadataName(fullName);
            if (typeSymbol == null) return null;

            // 收集类型实现的所有开放泛型接口，查找匹配的模板
            var matches = new List<(INamedTypeSymbol Interface, string ArityKey, StructTemplateInfo Info,
                List<TemplateNode> Ast)>();
            foreach (var iface in typeSymbol.AllInterfaces)
            {
                if (!iface.IsGenericType) continue;
                var original = iface.OriginalDefinition;
                string k = $"{original.Name}`{original.TypeParameters.Length}";
                if (openGenerics.TryGetValue(k, out var entry))
                {
                    matches.Add((original, k, entry.Info, entry.Ast));
                }
            }

            if (matches.Count == 0) return null;
            if (matches.Count == 1)
            {
                var m = matches[0];
                return (m.Info, m.Ast);
            }

            // 多重匹配：用 Roslyn 继承关系筛选最派生接口
            var mostDerived = new List<int>();
            for (int a = 0; a < matches.Count; a++)
            {
                bool isExtended = false;
                for (int b = 0; b < matches.Count; b++)
                {
                    if (a == b) continue;
                    foreach (var super in matches[a].Interface.AllInterfaces)
                    {
                        if (SymbolEqualityComparer.Default.Equals(
                            super.OriginalDefinition, matches[b].Interface))
                        {
                            isExtended = true;
                            break;
                        }
                    }
                    if (isExtended) break;
                }
                if (!isExtended) mostDerived.Add(a);
            }

            // 若仍多个平级：按固定优先级
            if (mostDerived.Count == 1)
            {
                var m = matches[mostDerived[0]];
                return (m.Info, m.Ast);
            }

            // 固定优先级：IList > ISet > IReadOnlyList > IDictionary > IReadOnlyDictionary
            var prio = new[] { "IList`1", "ISet`1", "IReadOnlyList`1", "IDictionary`2", "IReadOnlyDictionary`2" };
            foreach (var p in prio)
            {
                foreach (var idx in mostDerived)
                {
                    if (matches[idx].ArityKey == p)
                        return (matches[idx].Info, matches[idx].Ast);
                }
            }
            // 最终 fallback: 第一个
            var first = matches[mostDerived[0]];
            return (first.Info, first.Ast);
        }

        /// <summary>
        /// 将 "List&lt;NamedValue&gt;" / "Wrapper&lt;float&gt;" 解析为泛型实例。
        /// 先在 openGenerics 中精确查找；失败后由调用方通过 TryResolveViaInterfaces 走 Roslyn 回退。
        /// </summary>
        /// <returns>(开放泛型标识, 类型参数[], 是否集合类型)</returns>
        private static (string? OpenGeneric, string[]? ElementTypes, bool IsCollection) ParseGenericType(
            string typeName,
            Dictionary<string, (StructTemplateInfo Info, List<TemplateNode> Ast)> openGenerics)
        {
            int lt = typeName.IndexOf('<');
            int gt = typeName.LastIndexOf('>');
            if (lt < 0 || gt < lt) return (null, null, false);
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

            // 返回解析出的基名和类型参数，查找由调用方 TryGetOpenGenericTemplate 完成
            //（支持 Roslyn 回退查找接口模板）
            return (baseName, args.ToArray(), false);
        }

        /// <summary>字符串版本的类型分类（仅用于合成类型，无 Roslyn 符号可用）。</summary>
        private static (CollectionKind Kind, string? ElemType) ClassifyFieldTypeByName(string typeName)
        {
            // 泛型集合类/接口 → 单参数 Sequential（与 Roslyn ClassifyFieldType 对齐）
            if (typeName.StartsWith("List<") || typeName.StartsWith("System.Collections.Generic.List<") ||
                typeName.StartsWith("IList<") || typeName.StartsWith("System.Collections.Generic.IList<") ||
                typeName.StartsWith("ICollection<") || typeName.StartsWith("System.Collections.Generic.ICollection<") ||
                typeName.StartsWith("IEnumerable<") || typeName.StartsWith("System.Collections.Generic.IEnumerable<") ||
                typeName.StartsWith("ISet<") || typeName.StartsWith("System.Collections.Generic.ISet<") ||
                typeName.StartsWith("HashSet<") || typeName.StartsWith("System.Collections.Generic.HashSet<") ||
                typeName.StartsWith("IReadOnlyList<") || typeName.StartsWith("System.Collections.Generic.IReadOnlyList<"))
            {
                string? elem = ExtractFirstGenericArg(typeName);
                return elem != null ? (CollectionKind.Sequential, elem) : (CollectionKind.None, null);
            }
            if (typeName.EndsWith("[]"))
                return (CollectionKind.Array, typeName.Substring(0, typeName.Length - 2));
            return (CollectionKind.None, null);
        }

        /// <summary>从 "List&lt;float&gt;" 中提取 "float"。</summary>
        private static string? ExtractFirstGenericArg(string typeName)
        {
            int lt = typeName.IndexOf('<');
            int gt = typeName.LastIndexOf('>');
            if (lt < 0 || gt <= lt) return null;
            return typeName.Substring(lt + 1, gt - lt - 1);
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
            /// <summary>
            /// 类型是否为 class（引用类型）。控制生成代码使用 <c>new T()</c>（堆分配）还是 <c>default</c>（栈）。
            /// struct 始终用 <c>default</c>，不论是否含 managed 字段。
            /// </summary>
            public bool NeedsHeapAlloc;
            public bool IsCollection; // true for List<T> etc. — field assignment → .Add()
            public List<FieldInfo> Fields;
            /// <summary>是否为开放泛型类型（如 Wrapper&lt;T&gt;）</summary>
            public bool IsOpenGeneric;
            /// <summary>开放泛型的类型参数名（如 ["T"]）。非泛型时为空数组。</summary>
            public string[] TypeParameterNames;
            /// <summary>此类型实现的所有接口全名。用于编译期接口自动分发。</summary>
            public string[] ImplementedInterfaces;
            /// <summary>结构体是否为 readonly（readonly struct）。C# 强制所有字段为 readonly，需延迟构造。</summary>
            public bool IsReadonlyStruct;
            /// <summary>匹配构造器的参数名列表（按参数顺序）。null 表示无匹配构造器。</summary>
            public string[]? MatchedCtorParams;

            public TemplateCommon ToCommon() => new()
            {
                StructName = StructName,
                NeedsHeapAlloc = NeedsHeapAlloc,
                IsCollection = IsCollection,
                IsReadonlyStruct = IsReadonlyStruct,
                MatchedCtorParams = MatchedCtorParams,
            };
        }
    }

    /// <summary>StructTemplateInfo 与 EmitEntry 的共享字段。</summary>
    internal struct TemplateCommon
    {
        public string StructName;
        public bool NeedsHeapAlloc;
        public bool IsCollection;
        public bool IsReadonlyStruct;
        public string[]? MatchedCtorParams;
    }
}
