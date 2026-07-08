using System;

namespace FluxFormula.Core
{
    /// <summary>
    /// 标记 struct 的字面量模板，供 source generator 在编译期生成 span 扫描代码。
    /// 模板字符串中的 <c>&lt;type fieldname&gt;</c> 指令映射到内置类型或已注册结构体的扫描器；
    /// <c>&lt;optional&gt;...&lt;/optional&gt;</c> 包裹可选块；其余裸文字按字面量精确匹配。
    /// </summary>
    /// <example>
    /// <code>
    /// [LiteralTemplate("&lt;float damage&gt;|&lt;optional&gt;draw &lt;int draw&gt;&lt;/optional&gt;|idx:&lt;int index&gt;")]
    /// public struct SpellContext { public float Damage; public byte DrawsProvide; public byte StartIndex; }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public sealed class LiteralTemplateAttribute : Attribute
    {
        /// <summary>模板字符串</summary>
        public string Template { get; }

        /// <summary>
        /// 使用模板字符串标记 struct。
        /// </summary>
        /// <param name="template">模板字符串，如 <c>"&lt;float X&gt;, &lt;float Y&gt;, &lt;float Z&gt;"</c></param>
        public LiteralTemplateAttribute(string template)
        {
            Template = template ?? throw new ArgumentNullException(nameof(template));
        }
    }
}
