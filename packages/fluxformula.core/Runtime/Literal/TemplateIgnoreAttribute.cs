using System;

namespace SourceSerializer
{
    /// <summary>
    /// 标记该字段不参与序列化与反序列化。
    /// 应用于类型未被 <see cref="TemplateAttribute"/> 注册、但属于内部状态的字段。
    /// 被标记的字段不出现在生成的 scanner/emitter 代码中，也不会触发 SSR004 错误。
    /// </summary>
    /// <remarks>
    /// 被标记的字段不应出现在模板字符串中。
    /// 若模板字符串仍引用该字段的类型，source generator 仍会报告 SSR004 错误。
    /// </remarks>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public sealed class TemplateIgnoreAttribute : Attribute
    {
    }
}
