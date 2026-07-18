using System;
using System.Diagnostics.CodeAnalysis;

namespace SourceSerializer
{
    /// <summary>
    /// 为无法直接修改源码的第三方 struct 类型声明字面量模板。
    /// 可置于 assembly、class 或 struct 上，每个 attribute 声明一个类型的模板映射。
    /// </summary>
    /// <remarks>
    /// <para><b>多行写法：</b>模板支持 C# raw string literal 多行格式。
    /// 换行在解析时被规范化为空格，因此以下两种写法等价：</para>
    /// <code>
    /// // 单行
    /// [ExternalTemplate(typeof(Vector3), "&lt;float x&gt; &lt;float y&gt; &lt;float z&gt;")]
    ///
    /// // 多行（推荐）
    /// [ExternalTemplate(typeof(Vector3), """
    ///     &lt;float x&gt;
    ///     &lt;float y&gt;
    ///     &lt;float z&gt;
    ///     """)]
    /// </code>
    /// </remarks>
    /// <example>
    /// <code>
    /// // 注册 Unity Vector3 的模板
    /// [assembly: ExternalLiteralTemplate(typeof(UnityEngine.Vector3),
    ///     "&lt;float x&gt; &lt;float y&gt; &lt;float z&gt;")]
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true)]
    // 纯声明式 attribute，无运行时逻辑
    [ExcludeFromCodeCoverage]
    public sealed class ExternalTemplateAttribute : Attribute
    {
        /// <summary>目标 struct 类型</summary>
        public Type TargetType { get; }

        /// <summary>模板字符串（支持多行，换行等同于空格）</summary>
        public string Template { get; }

        /// <summary>
        /// 为给定类型注册字面量模板。
        /// </summary>
        /// <param name="targetType">目标 struct 类型（不可为 null）</param>
        /// <param name="template">模板字符串</param>
        public ExternalTemplateAttribute(Type targetType, string template)
        {
            TargetType = targetType ?? throw new ArgumentNullException(nameof(targetType));
            Template = template ?? throw new ArgumentNullException(nameof(template));
        }
    }
}
