using System.Reflection.Emit;
using FluxFormula.Core;

namespace FluxFormula.Compiler
{
    /// <summary>
    /// IL 内联发射接口（Tier B）——允许定义体为特定操作码手写 IL 指令，
    /// 完全跳过 <see cref="IFluxDefinition{TData}.Compute"/> 的方法调用开销。
    /// </summary>
    /// <remarks>
    /// <para>实现此接口后，<see cref="FluxILCompiler{TData, TDef}"/> 优先调用
    /// <see cref="EmitOp"/> 处理每个操作码。返回 true 表示已完成 IL 发射；
    /// 返回 false 表示此操作码不识别，编译器回退到 Tier A 的 <c>Compute</c> 调用。</para>
    ///
    /// <para>Tier B 适合追求极致运行时性能的用户——每个操作零方法调用，完全内联。
    /// 库内置的 <c>FloatMathDef</c> 等提供参考实现。</para>
    ///
    /// <para>此接口仅在支持 <c>DynamicMethod</c> 的平台上可用（Mono/CoreCLR）。
    /// IL2CPP 平台上 <see cref="FluxILCompiler{TData, TDef}"/> 不会被调用，
    /// 因此不会触发对此接口的类型加载。</para>
    /// </remarks>
    public interface IFluxILDefinition<TData> : IFluxDefinition<TData>
        where TData : unmanaged
    {
        /// <summary>
        /// 为指定操作码发射内联 IL 指令到委托体中。
        /// </summary>
        /// <param name="op">操作码</param>
        /// <param name="inst">当前指令（含 Dest / Arg0..5 寄存器索引）</param>
        /// <param name="il">IL 发生器</param>
        /// <param name="regArr">寄存器数组本地变量（<c>TData[]</c>）。
        /// 通过 <c>ldelem</c>/<c>stelem</c> 访问寄存器。</param>
        /// <returns>true 表示已处理此操作码；false 表示不识别，
        /// IL 编译器自动回退到 <see cref="IFluxDefinition{TData}.Compute"/>。</returns>
        bool EmitOp(byte op, Instruction inst, ILGenerator il, LocalBuilder regArr);
    }
}
