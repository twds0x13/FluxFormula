using System;
using System.Diagnostics.CodeAnalysis;

namespace SourceSerializer
{
    /// <summary>
    /// 声明一个模板类型别名，映射到内置 C# 值类型。
    /// 例如 <c>[assembly: TypeAlias("Distance", "float")]</c> 允许模板中写 <c>&lt;Distance range&gt;</c>，
    /// 生成的扫描代码与 <c>&lt;float range&gt;</c> 完全一致。
    /// </summary>
    /// <remarks>
    /// 此 attribute 仅改变模板中的<b>类型名称</b>，不改变解析逻辑。
    /// 若需要自定义解析行为（如识别 0x 前缀），使用 <see cref="ExternalTemplateAttribute"/> 注册手写模板。
    /// </remarks>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    // 纯声明式 attribute，无运行时逻辑
    [ExcludeFromCodeCoverage]
    public sealed class TypeAliasAttribute : Attribute
    {
        /// <summary>模板中使用的别名（如 "Distance"）</summary>
        public string Alias { get; }

        /// <summary>映射到的 C# 值类型名（如 "float"、"int"）</summary>
        public string CSharpType { get; }

        /// <summary>注册类型别名。</summary>
        public TypeAliasAttribute(string alias, string csharpType)
        {
            Alias = alias ?? throw new ArgumentNullException(nameof(alias));
            CSharpType = csharpType ?? throw new ArgumentNullException(nameof(csharpType));
        }
    }
}
