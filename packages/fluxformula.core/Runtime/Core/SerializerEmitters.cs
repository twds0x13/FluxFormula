// Source Generator 注入目标，partial class 的生成部分位于 SerializerEmitters.g.cs。
// 每个标记了 [Template] 的 struct 在编译期生成对应的 Emit_Xxx 方法并注册委托。
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace SourceSerializer
{
    /// <summary>
    /// 用户定义 struct 的生成式序列化器注册表。
    /// source generator 在编译期为每个标记 <see cref="TemplateAttribute"/> 的
    /// struct 生成专用 Emit 方法并注册到 <see cref="EmitterRegistry{TData}"/>。
    /// </summary>
    internal static partial class SerializerEmitters
    {
        /// <summary>
        /// 尝试获取 TData 的生成式序列化器。
        /// 若 struct 上存在 [Template] 且 source generator 已生成对应发射器则返回 true；
        /// 否则返回 false。
        /// </summary>
        internal static bool TryGetEmitter<TData>(out EmitterDelegate<TData> emitter)
        {
            if (EmitterRegistry<TData>.Emitter != null)
            {
                emitter = EmitterRegistry<TData>.Emitter;
                return true;
            }
            emitter = null;
            return false;
        }

        /// <summary>
        /// 每个 TData 类型一个注册项。source generator 生成的静态构造器在编译期填充此字段。
        /// </summary>
        [ExcludeFromCodeCoverage]
        private static class EmitterRegistry<TData>
        {
#pragma warning disable CS0649
            public static EmitterDelegate<TData> Emitter;
#pragma warning restore CS0649
        }
    }
}
