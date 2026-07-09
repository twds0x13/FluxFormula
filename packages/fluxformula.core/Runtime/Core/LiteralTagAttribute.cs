using System;

namespace FluxFormula.Core
{
    /// <summary>
    /// 标记枚举成员的字面量标签，供 Source Generator 在编译期生成 switch-on-string 扫描器。
    /// </summary>
    /// <example>
    /// <code>
    /// public enum Element : byte
    /// {
    ///     Physical = 0,
    ///     [LiteralTag("fire")]  Fire,
    ///     [LiteralTag("ice")]   Ice,
    ///     [LiteralTag("magic")] Magic,
    /// }
    /// </code>
    /// 模板中写 <c>&lt;Element tag&gt;</c>，生成器自动识别枚举类型并生成标签扫描代��。
    /// </example>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public sealed class LiteralTagAttribute : Attribute
    {
        /// <summary>标签字符串（如 "fire"、"ice"）</summary>
        public string Tag { get; }

        /// <summary>为枚举成员声明字面量标签。</summary>
        public LiteralTagAttribute(string tag)
        {
            Tag = tag ?? throw new ArgumentNullException(nameof(tag));
        }
    }
}
